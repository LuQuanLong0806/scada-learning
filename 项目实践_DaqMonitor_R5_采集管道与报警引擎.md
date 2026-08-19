# R5 · 采集管道 + 报警引擎 + 工程量转换(系统心脏)

> **定位**:R4 设备会吐数据了,但 100Hz × 10 台设备直接刷 UI 必卡死。这一篇造**管道**(Channel 缓冲 + 批量)——它是 15K 岗位架构题的标准答案,也是整个系统的心脏。顺带把报警引擎和工程量转换做了。
> **前置**:R4 全绿。**预计敲码**:90 分钟。
> **产出**:AcquisitionPipeline + 报警三件套 + EngineeringConverter,15 个新测试(累计 42)。

---

## 🎯 本篇交付物

```
src/DaqMonitor.Core/
├─ Acquisition/AcquisitionPipeline.cs   # 心脏:事件→Channel→定时批量→BatchReady
├─ Alarms/
│  ├─ AlarmRule.cs                      # 规则:阈值+方向+级别+回滞
│  ├─ AlarmEvent.cs                     # 报警事件参数
│  └─ AlarmEngine.cs                    # 引擎:边沿触发+回滞+线程安全
└─ Engineering/EngineeringConverter.cs  # 量程标定/查表插值/字节序
src/DaqMonitor.Tests/
├─ AcquisitionPipelineTests.cs          # 1 测试(FakeDevice 喂数)
├─ AlarmEngineTests.cs                  # 2 测试(边沿/回滞)
└─ EngineeringConverterTests.cs         # 12 测试(线性/查表/字节序)
```

## 📋 需求单(先自己设计,再对照)

| # | 需求 | 验收 |
|---|---|---|
| FR5-1 | 采集管道:设备 `DataReceived` 只做**入队**一件极轻的事;后台消费 + 定时(默认 200ms 级)`Flush` 批量出队,`BatchReady` 一次性给订阅方 | 3 个点进,1 批 3 个点出 |
| FR5-2 | 批量双触发:凑满 maxBatch(默认 500)**或**定时器到点,谁先谁触发 | 高频时不等定时器 |
| FR5-3 | **构造即启动**(new 完就开始消费,没有 Start/Stop 方法);Dispose 才停 | 构造后无需任何调用即工作 |
| FR5-4 | 报警规则与数据分离:AlarmRule = 点位+阈值+方向(IsHigh)+级别+**回滞带宽**;运行时可增删 | 改配置即可加报警点 |
| FR5-5 | 报警引擎**边沿触发**:只在"未报警→报警"上升沿发 `AlarmTriggered`,持续越界不重复报;回到正常发 `AlarmCleared` | 同点连超 2 次只报 1 次 |
| FR5-6 | [回滞](kp:hysteresis):值在 Threshold±Hysteresis 带内不触发/不恢复,防阈值附近抖动狂报 | 120→102(带内)→90→120 只报 2 次 |
| FR5-7 | 工程量转换:线性标定 raw→物理量(除零返回 engMin 不抛异常)、[查表插值](kp:eng-scale)(PT100 分度表)、32 位浮点 4 种[字节序](kp:byte-order)重排 | 4-20mA→0-100℃ 全对 |

**自己先想 15 分钟**(这一篇是架构核心,值得多想):
1. 事件处理器里直接 `Dispatcher.Invoke` 刷 UI,10 台设备 100Hz 会发生什么?(每秒 1000 次 UI 线程争抢,界面冻结)
2. 为什么用 `Channel<T>` 而不是 `ConcurrentQueue<T>`?(异步 ReadAllAsync 天然背压/完成语义;前端类比:rx buffer vs 裸回调)
3. 报警为什么不"每条越界数据都报"?(100Hz 下 1 秒 100 条报警刷屏——所以边沿+回滞)
4. 阈值 100、回滞 5:值 103 报不报?为什么带内的越界值要忽略?(刚触发过,带内视为"还没恢复",这是防抖的灵魂)

## 📚 本篇知识点

- [Channel<T>](kp:channel) · [批量处理/200ms 一批](kp:batching) · [回滞](kp:hysteresis) · [报警边沿触发](kp:alarm-edge) · [量程标定](kp:eng-scale) · [字节序](kp:byte-order)

## 🛠️ 参考实现

### ① AcquisitionPipeline —— 系统心脏

> 📂 `src/DaqMonitor.Core/Acquisition/AcquisitionPipeline.cs` · namespace `DaqMonitor.Core.Acquisition`
> 🔧 无 NuGet(System.Threading.Channels 在 BCL 里)
> 💡 **构造即启动**:new 的时候定时器和消费循环就位,没有 Start/Stop——这是本工程的既定契约,后面 R7 的 DI 组装、R8 的 UI 都按这个语义用
> 🗺️ **新手读码地图**(5 步看懂,这是全项目最核心的 40 行):1. 它解决的问题:100Hz × 多设备 = 每秒几百次事件,如果每次都直接刷 UI,界面必卡死——所以要"攒一批、一次刷" 2. `Register(dev)` 把管道挂到设备事件上;`OnPoint` 是事件入口,只干一件极轻的事:把 DataEventArgs 转成 SensorPoint **塞进 Channel**(注意抄了 e.Timestamp)就返回 3. `Channel<T>` 是线程安全的传送带:任意线程往里塞,`ConsumeAsync` 用 `await foreach` 在后台一条条取,取出来先攒进 `_pending` 4. 什么时候"一批就绪"?两个出口任一满足:`_pending` 攒满 maxBatch(量大不等定时器)或 `_flushTimer` 到点 Flush(量少也别憋太久)——两处都在 `lock (_gate)` 里把 `_pending` 整包换新再在锁外触发 BatchReady,**锁外触发**是避免订阅方在锁里干慢活把别人卡住 5. UI 订阅 BatchReady 拿到一批,一次 Dispatcher.Invoke 刷 N 个点。**前端类比**:Channel ≈ 无限长的事件队列,整个模式 ≈ 前端性能优化里的"rAF 节流批量 setState"——高频事件先入队,定时批量消化。

#### 🏗️ 为什么这样设计:缓冲为什么用 Channel<T>,而不是 ConcurrentQueue 或直接 ObservableCollection?

**当时面临的选择**:

| 方案 | 优点 | 代价 |
|---|---|---|
| 直接 ObservableCollection.Add | 最直观 | **跨线程改绑定集合直接 NotSupportedException**;每条一次 UI 通知,100Hz 必卡 |
| ConcurrentQueue + 轮询出队 | 线程安全 | 没有异步等待:没数据时消费线程要么空转要么 Sleep,完成/取消语义全要自己搭 |
| `Channel<T>` + `await foreach`(选定) | 多学一个 API | — |

**为什么选它**:Channel 是 .NET 官方为"**生产者-消费者**"造的传送带,恰好就是采集管道的形状:①线程安全入队/出队开箱即用;②`ReadAllAsync` 没数据时**异步挂起不占线程**,有数据立刻醒——比轮询省一个线程的空转;③带"写端关闭"(Complete)和取消(CancellationToken)语义,优雅停机不用自己发明。前端类比:RxJS 的 Subject/缓冲操作符,但语言级原生、零依赖。

**不这样会怎样**:ObservableCollection 跨线程加元素当场崩(这是 WPF 新手必踩的第一坑);ConcurrentQueue 能跑,但消费循环要自己写"空了睡多久"的节拍——睡短了空转、睡长了延迟,而 Channel 的异步唤醒天然没有这个两难。

**🎤 面试一句话**:"管道缓冲我用 Channel 不用 ConcurrentQueue:它是官方生产者-消费者原语,线程安全之外还有异步挂起唤醒、Complete 完成语义和取消配合——await foreach 没数据时零线程开销。ObservableCollection 更不行,跨线程 Add 直接异常。"

#### 🏗️ 为什么这样设计:管道为什么"构造即启动",不给 Start/Stop 方法?

**当时面临的选择**:

| 方案 | 优点 | 代价 |
|---|---|---|
| 显式 Start()/Stop() | 生命周期"显式" | 忘调 Start 系统静默没数据(最难查);每个用它的地方都要多一步 |
| 构造即启动,Dispose 即停(选定) | 语义 = "存在即工作" | 无法"建好但暂停" |

**为什么选它**:管道是**常驻基础设施**,不是按需开关的业务对象——系统跑起来它就该在转,生命周期 == 容器/应用的生命周期。构造即启动把"忘了启动"这类错误**从运行期消灭在编译期思维里**:拿到管道引用,它一定在消费。C# 生态同款哲学:`Channel` 写端没有 Start、`Timer` new 出来就走。真正的开关(暂停采集)放在设备层 Start/Stop——每层管自己该管的生命周期。

**不这样会怎样**:显式 Start 的版本,R7 组装时忘了调,系统"看起来正常"但曲线永远空白——这种"无报错但不工作"的 bug 比崩溃难查一个量级。

**🎤 面试一句话**:"管道是常驻服务,我做成构造即启动、Dispose 即停——引用在手它必在消费,把'忘启动'这类静默错误直接消灭;按需的启停在设备层做,每层管自己的生命周期。"

**第 1 步 · 骨架 + 构造即启动 + 消费循环**(整个文件先建出来;构造函数和 ConsumeAsync 互相绑定,一起贴)

```csharp
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Models;
using System.Threading.Channels;

namespace DaqMonitor.Core.Acquisition;

/// <summary>
/// 统一采集管道:所有设备事件 → 入 Channel 缓冲 → 后台消费 + 定时批量出队 → 一次性推给 UI。
/// 解决"逐事件 Dispatcher.Invoke"在 100Hz×多设备下冲垮 UI 的问题。
/// 关键点:事件只做"入队"这一件极轻的事,重活(刷新/上云)由定时器批量处理。
/// </summary>
public sealed class AcquisitionPipeline : IDisposable
{
    private readonly Channel<SensorPoint> _channel = Channel.CreateUnbounded<SensorPoint>();
    private readonly List<IDevice> _devices = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Timer _flushTimer;
    private readonly object _gate = new();
    private List<SensorPoint> _pending = new();
    private readonly int _maxBatch;

    /// <summary>批量就绪事件:在后台线程触发,UI 订阅方需自行 Dispatcher 回 UI 线程再改界面。</summary>
    public event EventHandler<IReadOnlyList<SensorPoint>>? BatchReady;
    public event EventHandler<Exception>? Error;

    public AcquisitionPipeline(TimeSpan flushInterval, int maxBatch = 500)
    {
        _maxBatch = maxBatch;
        _flushTimer = new Timer(_ => Flush(), null, flushInterval, flushInterval);
        _ = ConsumeAsync();
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var p in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                List<SensorPoint>? batch = null;
                lock (_gate)
                {
                    _pending.Add(p);
                    if (_pending.Count >= _maxBatch) { batch = _pending; _pending = new(); }
                }
                if (batch is not null) BatchReady?.Invoke(this, batch);
            }
        }
        catch (OperationCanceledException) { /* 正常退出 */ }
        catch (Exception ex) { Error?.Invoke(this, ex); }
    }
}
```

📚 **知识点**
- **`Channel.CreateUnbounded<SensorPoint>()` = 一条无限长的传送带**:`System.Threading.Channels` 是 BCL 自带的内存队列,任意线程 `Writer.TryWrite` 塞、一个后台任务 `Reader.ReadAllAsync` 取——**生产者消费者的全部线程安全问题它包了**(前端类比:一个永不背压的 Subject,但带 await 的天然流控)。
- **构造函数最后两行就是"构造即启动"契约的全部**:`new Timer(...)`(定时出口就位)+ `_ = ConsumeAsync()`(消费循环点火)——**new 完就开始工作,没有 Start 方法**。这是本工程铁律,R7 的 DI 和 R8 的 UI 都按这个语义用。`_ =` 丢弃是故意告诉编译器"我知道我没 await 它"。
- **`await foreach` + `ReadAllAsync(token)`**:没有数据就异步挂起(不占线程),来了就醒——**比轮询队列优雅一个量级**,这就是选 Channel 不选 ConcurrentQueue 的理由。
- **"锁内换包、锁外触发"**:`lock (_gate)` 里只做 `_pending` 整包换新(纳秒级),`BatchReady?.Invoke` 放锁外——**订阅方在事件里干多慢的活都不挡别人进锁**。这个模式在 Flush 里还会再出现一次。
- **`catch (Exception ex) { Error?.Invoke(...) }` 消费循环兜底**:循环挂了管道就"死"了,把异常发给 Error 事件让上层诊断——**长活任务必须有自己的尸检通道**。

**第 2 步 · 设备注册 + 事件入口 `OnPoint`**(贴进类里,最后一个 `}` 之前)

```csharp
    /// <summary>注册一个设备:自动订阅它的 DataReceived,把点塞进缓冲。</summary>
    public void Register(IDevice device)
    {
        device.DataReceived += OnPoint;
        _devices.Add(device);
    }

    private void OnPoint(object? sender, DataEventArgs e)
        => _channel.Writer.TryWrite(new SensorPoint { Id = e.PointId, Value = e.Value, Timestamp = e.Timestamp });
```

📚 **知识点**
- **`OnPoint` 全身只有一行 TryWrite——这是整个架构的性能支点**:100Hz × 10 台 = 每秒 1000 次进来,每次只花"转对象 + 入队"的纳秒级成本;**如果没有这层缓冲,每次都 Dispatcher.Invoke 刷 UI,界面直接冻结**。事件回调的铁律:越短越好,重活交给下游。
- **`Timestamp = e.Timestamp` 又抄了一遍时间戳**(R2 铁律第三次出现)——漏抄就 0001-01-01,曲线全歪;类型系统不帮你查这个,只能靠纪律 + 测试。
- **`Register` 顺手 `_devices.Add`**:记下注册过谁,Dispose 时才知道要退订谁——**订阅了就要记得退订路径**,否则设备销毁了事件还在往死对象上打。

**第 3 步 · 定时出口 `Flush`**(贴进类里)

```csharp
    private void Flush()
    {
        List<SensorPoint>? batch = null;
        lock (_gate)
        {
            if (_pending.Count > 0) { batch = _pending; _pending = new(); }
        }
        if (batch is not null) BatchReady?.Invoke(this, batch);
    }
```

📚 **知识点**
- **Flush 是低频场景的出口**:数据稀稀拉拉凑不满 500 条,定时器(默认 200ms)到点强制交货——**满批触发照顾吞吐,定时触发照顾延迟**,双出口合起来"高频不堵、低频不憋"(前端类比:滚动加载的"满屏加载 + 兜底定时刷新"双条件)。
- **`batch = _pending; _pending = new();` 整包换新**:把攒好的列表引用交出去、立刻换一个空列表继续攒——**交接零拷贝**,比一条条 CopyTo 高效得多,也是并发代码里"快照交接"的惯用法。

**第 4 步 · `Dispose`:五步有序拆除**(贴进类里,收尾)

```csharp
    public void Dispose()
    {
        _cts.Cancel();
        _flushTimer.Dispose();
        _channel.Writer.TryComplete();
        foreach (var d in _devices) d.DataReceived -= OnPoint;
        _cts.Dispose();
    }
}
```

📚 **知识点**
- **拆除顺序有讲究**:Cancel(叫停消费)→ Dispose 定时器(不再产生新 Flush)→ TryComplete(告诉 Channel"不会再有数据",`await foreach` 自然走到尽头)→ 逐个退订设备事件(入口封死)→ 最后 Dispose 令牌。**先停上游、再停下游、最后放资源**——反向操作会出现"退订了还在写"的竞态。

<details markdown="1">
<summary>📄 完整文件 AcquisitionPipeline.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Models;
using System.Threading.Channels;

namespace DaqMonitor.Core.Acquisition;

/// <summary>
/// 统一采集管道:所有设备事件 → 入 Channel 缓冲 → 后台消费 + 定时批量出队 → 一次性推给 UI。
/// 解决"逐事件 Dispatcher.Invoke"在 100Hz×多设备下冲垮 UI 的问题。
/// 关键点:事件只做"入队"这一件极轻的事,重活(刷新/上云)由定时器批量处理。
/// </summary>
public sealed class AcquisitionPipeline : IDisposable
{
    private readonly Channel<SensorPoint> _channel = Channel.CreateUnbounded<SensorPoint>();
    private readonly List<IDevice> _devices = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Timer _flushTimer;
    private readonly object _gate = new();
    private List<SensorPoint> _pending = new();
    private readonly int _maxBatch;

    /// <summary>批量就绪事件:在后台线程触发,UI 订阅方需自行 Dispatcher 回 UI 线程再改界面。</summary>
    public event EventHandler<IReadOnlyList<SensorPoint>>? BatchReady;
    public event EventHandler<Exception>? Error;

    public AcquisitionPipeline(TimeSpan flushInterval, int maxBatch = 500)
    {
        _maxBatch = maxBatch;
        _flushTimer = new Timer(_ => Flush(), null, flushInterval, flushInterval);
        _ = ConsumeAsync();
    }

    /// <summary>注册一个设备:自动订阅它的 DataReceived,把点塞进缓冲。</summary>
    public void Register(IDevice device)
    {
        device.DataReceived += OnPoint;
        _devices.Add(device);
    }

    private void OnPoint(object? sender, DataEventArgs e)
        => _channel.Writer.TryWrite(new SensorPoint { Id = e.PointId, Value = e.Value, Timestamp = e.Timestamp });

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var p in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                List<SensorPoint>? batch = null;
                lock (_gate)
                {
                    _pending.Add(p);
                    if (_pending.Count >= _maxBatch) { batch = _pending; _pending = new(); }
                }
                if (batch is not null) BatchReady?.Invoke(this, batch);
            }
        }
        catch (OperationCanceledException) { /* 正常退出 */ }
        catch (Exception ex) { Error?.Invoke(this, ex); }
    }

    private void Flush()
    {
        List<SensorPoint>? batch = null;
        lock (_gate)
        {
            if (_pending.Count > 0) { batch = _pending; _pending = new(); }
        }
        if (batch is not null) BatchReady?.Invoke(this, batch);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _flushTimer.Dispose();
        _channel.Writer.TryComplete();
        foreach (var d in _devices) d.DataReceived -= OnPoint;
        _cts.Dispose();
    }
}
```

</details>

> ⚠️ **注意 OnPoint 里那行**:转 SensorPoint 时**抄了 e.Timestamp**——R2 立的时间戳铁律,不抄就落 0001-01-01,后面曲线全歪。

### ② 报警三件套(规则 / 事件 / 引擎)

> 📂 `src/DaqMonitor.Core/Alarms/AlarmRule.cs`

```csharp
using DaqMonitor.Core.Models;

namespace DaqMonitor.Core.Alarms;

/// <summary>报警规则:点位 + 阈值 + 级别 + 方向(超上限 / 低于下限)。规则与数据分离,改配置即可加报警点。</summary>
public class AlarmRule
{
    public int PointId { get; set; }
    public double Threshold { get; set; }
    public AlarmLevel Level { get; set; }
    public bool IsHigh { get; set; } = true;     // true: 超过阈值报警;false: 低于阈值报警
    /// <summary>回滞带宽:值在 [Threshold-带宽, Threshold+带宽] 内视为"已恢复",不再反复触发(生产必做,防止阈值附近抖动狂报)。</summary>
    public double Hysteresis { get; set; }
}
```

📚 **知识点**
- **`class` + `get; set;` 而不是 record**:规则是**运行时可改配置**(现场调阈值直接 `rule.Threshold = 105`),要可变性;而 R4 的 RegisterMap 是建好就不动的映射,用 record——**可变/不可变的选择跟着使用场景走**。
- **五个字段回答"谁、多高、多严重、哪个方向、多迟钝"**:`IsHigh` 区分超上限(温度)与低于下限(压力/液位),`Hysteresis` 是防抖带宽——**规则对象是报警业务的全部配置面**,UI 配置界面照着它长。

#### 🏗️ 为什么这样设计:报警规则为什么是"四个数据字段",而不是一个 Func<SensorPoint, bool> 谓词?为什么必须带回滞?

**当时面临的选择(规则的形态)**:

| 方案 | 优点 | 代价 |
|---|---|---|
| `Func<double,bool>` 谓词(代码即规则) | 任意逻辑都能写 | **没法序列化/没法做配置界面**;改阈值=改代码重新发版;现场工程师看不懂 |
| Threshold + IsHigh + Hysteresis 数据字段(选定) | 多打几行 | 只覆盖"单阈值+方向"一种规则形态 |

**为什么选它**:报警阈值是**现场调试出来的参数**,不是开发期定死的逻辑——温控设备的合理阈值要试产才知道,现场工程师要在 UI 上改、要存盘、要下次开机还在。数据字段天然可 JSON 序列化、可生成表格编辑界面(R8 的报警配置就照着它长);谓词函数这三样全做不到。**"配置数据化"是工程软件和 demo 的分水岭**。真遇到复杂规则再扩展字段(如窗口期),但那是需求推着走,不是现在猜。

**为什么必须带回滞**:阈值附近信号必然抖动——温度 100 上下飘 0.5,纯阈值判断会 100.2 报、99.8 恢复、100.3 又报……1 秒报 50 条,报警列表被刷成垃圾,真报警反而被淹没。回滞把"恢复"的门槛拉到 Threshold−Hysteresis,带内一律视为"还没好",物理层抖动被吸收。配合**边沿触发**(只在"正常→报警"跃迁那一刻报一次),这就是温度控制器、烟雾报警器通用的工业做法。

**🎤 面试一句话**:"报警规则我做成纯数据字段不带谓词:阈值是现场调出来的配置,要能 UI 编辑、序列化存盘;谓词函数改一次阈值就得改代码发版。判断上加回滞+边沿——阈值附近信号必抖,纯阈值一秒能报几十条,回滞把恢复门槛拉低、边沿只在跃迁时报,这是工业报警的标配防抖。"

> 📂 `src/DaqMonitor.Core/Alarms/AlarmEvent.cs`

```csharp
using DaqMonitor.Core.Models;

namespace DaqMonitor.Core.Alarms;

/// <summary>报警触发事件参数。</summary>
public class AlarmEvent : EventArgs
{
    public int PointId { get; init; }
    public AlarmLevel Level { get; init; }
    public double Value { get; init; }
}
```

📚 **知识点**
- **`{ get; init; }` 只读创建**:事件参数是"已发生事实的快照",发出去就不许改——init 限定"只能在构造时赋值",比 get; set; 语义更诚实(R2 的 DataEventArgs 同款)。

> 📂 `src/DaqMonitor.Core/Alarms/AlarmEngine.cs`
> 💡 三个生产级特性:线程安全(规则运行时增删)、边沿触发(不刷屏)、回滞(防抖)
> 🗺️ **新手读码地图**(按 Evaluate 一条数据的旅程看):1. 先给规则列表拍快照(`_rules.ToList()`),因为规则可能被别的线程运行时增删,遍历快照不怕中途被改 2. 找到管这个点位的规则,算两个布尔值:`breach` = 越限了吗(IsHigh 决定是"超上限"还是"低于下限");`inBand` = 是不是落在阈值 ± 回滞带宽里 3. **边沿触发**靠 `_active` 这个 HashSet:它记着"哪些点当前正在报警"。`_active.Add` 返回 false 说明本来就在集合里(早报过了)→ 不重复报;只有"从没报→报"这个**上升沿**才触发 AlarmTriggered——不然 100Hz 的越限数据每条都弹通知,界面直接刷屏 4. **回滞**防的是另一种抖:值在阈值附近 99↔101 来回横跳。有了带宽,101 触发后跌回 99(还在带宽内)不算恢复,必须跌出 [Threshold−带宽] 才发 AlarmCleared——温度控制器的"不灵敏区"就是这个思想 5. 触发/恢复是成对的两个事件,UI 各挂一个:变红 + 复位。**前端类比**:_active ≈ 组件里的 state,边沿触发 ≈ 只在 state 翻转时才 useEffect,回滞 ≈ 防抖 debounce 的硬件版。

**第 1 步 · 骨架:规则表 + 活动集 + 事件**(整个文件先建出来)

```csharp
using DaqMonitor.Core.Models;
using System.Collections.Generic;

namespace DaqMonitor.Core.Alarms;

/// <summary>
/// 报警引擎:规则与数据分离。数据流入逐条匹配,命中即发 AlarmTriggered。
/// 生产级特性:
/// ① 线程安全——规则可在运行时增删;
/// ② 边沿触发——只在"未报警→报警"的上升沿通知,避免每条越界数据都刷屏;
/// ③ 回滞(hysteresis)——值在阈值附近抖动时不反复触发/恢复。
/// </summary>
public class AlarmEngine
{
    private readonly List<AlarmRule> _rules = new();
    private readonly HashSet<int> _active = new();   // 当前已处于报警状态的点位
    private readonly object _gate = new();

    public event EventHandler<AlarmEvent>? AlarmTriggered;
    /// <summary>报警恢复(下降沿):点位从报警区间回到正常区间时触发,UI 据此把表盘颜色复位。</summary>
    public event EventHandler<AlarmEvent>? AlarmCleared;

    public void Add(AlarmRule r) { lock (_gate) _rules.Add(r); }
    public void Clear() { lock (_gate) { _rules.Clear(); _active.Clear(); } }
}
```

📚 **知识点**
- **`_active` HashSet 是"边沿触发"的全部状态**:记着"哪些点正在报警"——Add 返回 false = 早在集合里(报过了),Remove 返回 true = 刚从集合出来(该恢复了)。**状态最小化**:一个集合把"重复报"和"漏恢复"两个 bug 一起防了。
- **Add/Clear 都锁,规则运行时可增删**:UI 线程加规则、采集线程 Evaluate 同时发生也不炸——**读写同一份状态就必须同一把锁**,引擎类从第一天就按多线程设计。

**第 2 步 · `Evaluate`:一条数据的判决之旅**(贴进类里,最后一个 `}` 之前)

```csharp
    public void Evaluate(SensorPoint p)
    {
        List<AlarmRule> snapshot;
        lock (_gate) snapshot = _rules.ToList();

        foreach (var r in snapshot)
        {
            if (r.PointId != p.Id) continue;
            bool breach = r.IsHigh ? p.Value > r.Threshold : p.Value < r.Threshold;
            bool inBand = r.Hysteresis > 0 && Math.Abs(p.Value - r.Threshold) <= r.Hysteresis;

            if (breach && !inBand)
            {
                bool wasActive;
                lock (_gate) wasActive = !_active.Add(p.Id);
                if (!wasActive)   // 仅上升沿触发
                    AlarmTriggered?.Invoke(this, new AlarmEvent { PointId = p.Id, Level = r.Level, Value = p.Value });
            }
            else if (!breach && r.Hysteresis > 0)
            {
                bool wasActive;
                lock (_gate) wasActive = _active.Remove(p.Id);   // 回到正常区间,复位,下次越界再报
                if (wasActive)   // 仅下降沿通知 UI 复位表盘
                    AlarmCleared?.Invoke(this, new AlarmEvent { PointId = p.Id, Level = r.Level, Value = p.Value });
            }
        }
    }
}
```

📚 **知识点**
- **开头 `_rules.ToList()` 拍快照**:遍历期间别的线程 Add/Clear 规则,快照纹丝不动——**"锁内拷贝、锁外遍历"**与管道的"锁内换包、锁外触发"同一门派。
- **两个布尔定乾坤**:`breach`(越限了吗,IsHigh 分方向)+ `inBand`(落在回滞带内吗)——**真报警 = breach && !inBand**:越界但还在带内 = "刚触发过、还没走远",视为维持现状。这就是回滞的灵魂:阈值 100、带宽 5 时,103 虽然越界但**不报**(带内),90 才算真恢复。
- **`!_active.Add(p.Id)` 一行两用**:Add 成功(返回 true)= 第一次进报警 → wasActive=false → 触发;Add 失败 = 早在集合里 → 不重复报。**用集合 API 的返回值当"边沿检测器"**,比维护 Dictionary<pointId, bool> 状态机简洁十倍。
- **成对事件 AlarmTriggered / AlarmCleared**:触发(上升沿)让 UI 表盘变红,恢复(下降沿)让它复位——**UI 状态跟着事件走,不需要自己判断**,MVVM 联动的标准接法。
- **回滞只在 `r.Hysteresis > 0` 时生效**:没配带宽就退化为纯边沿触发——**可配置降级,默认行为保持简单**。

<details markdown="1">
<summary>📄 完整文件 AlarmEngine.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Models;
using System.Collections.Generic;

namespace DaqMonitor.Core.Alarms;

/// <summary>
/// 报警引擎:规则与数据分离。数据流入逐条匹配,命中即发 AlarmTriggered。
/// 生产级特性:
/// ① 线程安全——规则可在运行时增删;
/// ② 边沿触发——只在"未报警→报警"的上升沿通知,避免每条越界数据都刷屏;
/// ③ 回滞(hysteresis)——值在阈值附近抖动时不反复触发/恢复。
/// </summary>
public class AlarmEngine
{
    private readonly List<AlarmRule> _rules = new();
    private readonly HashSet<int> _active = new();   // 当前已处于报警状态的点位
    private readonly object _gate = new();

    public event EventHandler<AlarmEvent>? AlarmTriggered;
    /// <summary>报警恢复(下降沿):点位从报警区间回到正常区间时触发,UI 据此把表盘颜色复位。</summary>
    public event EventHandler<AlarmEvent>? AlarmCleared;

    public void Add(AlarmRule r) { lock (_gate) _rules.Add(r); }
    public void Clear() { lock (_gate) { _rules.Clear(); _active.Clear(); } }

    public void Evaluate(SensorPoint p)
    {
        List<AlarmRule> snapshot;
        lock (_gate) snapshot = _rules.ToList();

        foreach (var r in snapshot)
        {
            if (r.PointId != p.Id) continue;
            bool breach = r.IsHigh ? p.Value > r.Threshold : p.Value < r.Threshold;
            bool inBand = r.Hysteresis > 0 && Math.Abs(p.Value - r.Threshold) <= r.Hysteresis;

            if (breach && !inBand)
            {
                bool wasActive;
                lock (_gate) wasActive = !_active.Add(p.Id);
                if (!wasActive)   // 仅上升沿触发
                    AlarmTriggered?.Invoke(this, new AlarmEvent { PointId = p.Id, Level = r.Level, Value = p.Value });
            }
            else if (!breach && r.Hysteresis > 0)
            {
                bool wasActive;
                lock (_gate) wasActive = _active.Remove(p.Id);   // 回到正常区间,复位,下次越界再报
                if (wasActive)   // 仅下降沿通知 UI 复位表盘
                    AlarmCleared?.Invoke(this, new AlarmEvent { PointId = p.Id, Level = r.Level, Value = p.Value });
            }
        }
    }
}
```

</details>

### ③ EngineeringConverter —— 工程量转换

> 📂 `src/DaqMonitor.Core/Engineering/EngineeringConverter.cs` · 🔧 无 NuGet

**第 1 步 · 骨架 + 线性标定 `Linear`**(整个文件先建出来)

```csharp
namespace DaqMonitor.Core.Engineering;

/// <summary>
/// 工程量转换:把 AD 原始码 / 模数比例还原成现场熟悉的物理量,
/// 同时处理 32 位浮点的 4 种字节序排列(Modbus / OPC UA 现场常见坑)。
/// 全部静态 + 扩展方法,零依赖,便于单测。
/// </summary>
public static class EngineeringConverter
{
    /// <summary>
    /// 线性标定:把 raw ∈ [rawMin, rawMax] 线性映射到 [engMin, engMax]。
    /// 例:4-20mA → 0-100℃,AD 0-65535 → -50~150℃。
    /// 注意 rawMax == rawMin 时不能除零,按现场惯例返回 engMin(也避免抛异常打断采集)。
    /// </summary>
    public static double Linear(double raw, double rawMin, double rawMax, double engMin, double engMax)
    {
        double span = rawMax - rawMin;
        if (Math.Abs(span) < double.Epsilon) return engMin;
        double ratio = (raw - rawMin) / span;
        return engMin + ratio * (engMax - engMin);
    }
}
```

📚 **知识点**
- **线性标定就是初中的一次函数**:`ratio = (raw - rawMin) / span` 算"走了量程的百分之几",再映射到工程量区间——4-20mA 变送器、0-65535 AD 码全用这一个公式。**工业软件的数学其实很朴素,难在工程细节**。
- **`Math.Abs(span) < double.Epsilon` 防除零**:rawMax == rawMin(配置错了)不抛异常,返回 engMin——**采集链路上的转换函数不许抛异常打断流水线**,坏配置给个安全值、记条日志,流水线继续走。这是工业代码和互联网代码口味不同的典型点。

**第 2 步 · 查表插值 `Lookup` + `Bisect`**(贴进类里,两个方法一起贴——Lookup 调 Bisect)

```csharp
    /// <summary>
    /// 非线性查表(PT100 / 热电偶分度表用):
    /// 在 table(key=raw 升序,value=eng)中插值。
    /// raw 低于最小 key 返回表首;高于最大 key 返回表尾;中间做线性插值。
    /// </summary>
    /// <exception cref="ArgumentException">table 为 null 或空。</exception>
    public static double Lookup(double raw, SortedList<double, double> table)
    {
        if (table is null || table.Count == 0)
            throw new ArgumentException("查表失败:分度表为空", nameof(table));
        if (raw <= table.Keys[0]) return table.Values[0];
        if (raw >= table.Keys[^1]) return table.Values[^1];

        // 二分找到第一个 key > raw 的位置,与前一档做线性插值
        int idx = Bisect(table.Keys, raw);
        double x0 = table.Keys[idx - 1];
        double x1 = table.Keys[idx];
        double y0 = table.Values[idx - 1];
        double y1 = table.Values[idx];
        double span = x1 - x0;
        if (Math.Abs(span) < double.Epsilon) return y0;
        return y0 + (raw - x0) / span * (y1 - y0);
    }

    /// <summary>在升序只读 keys 中找到第一个 > target 的索引(标准 bisect_right 语义)。</summary>
    private static int Bisect(IList<double> keys, double target)
    {
        int lo = 0, hi = keys.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (keys[mid] <= target) lo = mid + 1; else hi = mid;
        }
        return lo;
    }
```

📚 **知识点**
- **传感器不是线性的,所以查表**:PT100 铂电阻的电阻→温度曲线略有弯度,厂家给**分度表**(几百行"多少欧姆对应多少度")——代码在相邻两档之间做线性插值,**表越密插值越准**,精度靠数据不靠公式。
- **`Bisect` 是手写二分查找**:找"第一个大于 target 的位置"(Python 的 bisect_right 语义)——分度表几百行,二分 9 次内定位,比线性扫快一个量级。`>> 1` 是除 2 的位运算写法。
- **两端 clamp**:低于表首返回表首值、高于表尾返回表尾值——**超量程给边界值而不是外推**(温度不可能 -9999℃,给边界更安全;Linear 允许外推、Lookup 钳边界,是两种现场口味,注释都写明了)。

**第 3 步 · 字节序枚举 + `Swap`**(贴进类里)

```csharp
    /// <summary>32 位浮点跨寄存器时的字节序排列(与 ModbusFrameParser.ByteOrder 同义,这里独立定义避免协议层耦合)。</summary>
    public enum ByteOrder { ABCD, CDAB, BADC, DCBA }

    /// <summary>
    /// 字节序交换:把 4 字节 [a,b,c,d] 按指定顺序重排。常用于 Modbus 浮点 / OPC UA / CAN 信号解析。
    /// ABCD=不动;CDAB=字交换(现场最常见);BADC=字节交换;DCBA=全反序。
    /// </summary>
    /// <exception cref="ArgumentException">输入长度不是 4。</exception>
    public static byte[] Swap(byte[] abcd, ByteOrder order)
    {
        if (abcd is null || abcd.Length != 4)
            throw new ArgumentException("字节序交换需要恰好 4 字节", nameof(abcd));
        return order switch
        {
            ByteOrder.ABCD => new[] { abcd[0], abcd[1], abcd[2], abcd[3] },
            ByteOrder.CDAB => new[] { abcd[2], abcd[3], abcd[0], abcd[1] },
            ByteOrder.BADC => new[] { abcd[1], abcd[0], abcd[3], abcd[2] },
            ByteOrder.DCBA => new[] { abcd[3], abcd[2], abcd[1], abcd[0] },
            _ => throw new ArgumentOutOfRangeException(nameof(order))
        };
    }
```

📚 **知识点**
- **为什么这里再定义一个 ByteOrder 而不复用 ModbusFrameParser 的?** 注释说明白了:**避免工程层反向耦合协议层**——EngineeringConverter 是纯数学工具,不该 using 协议命名空间。两个同名枚举代价很小,耦合的代价很大(前端类比:utils 里再写一个 Status 枚举,别 import 组件目录的)。
- **Swap 是"排列组合器"**:四种顺序就是四个 new 数组的重排——和 R3 `ToFloatModbus` 的 switch 殊途同归,但那边边排边算、这边排完给 ToFloat 用——**重排和解读分离**,Swap 可独立测试。

**第 4 步 · `ToFloat`:按序还原浮点**(贴进类里,收尾)

```csharp
    /// <summary>把按指定字节序排列的 4 字节解为 float(默认按大端 ABCD 还原)。</summary>
    public static float ToFloat(byte[] bytes, ByteOrder order = ByteOrder.ABCD)
    {
        var ordered = Swap(bytes, order);
        // 本机小端,把 ABCD(大端)逆转一次再 ToSingle
        if (BitConverter.IsLittleEndian) Array.Reverse(ordered);
        return BitConverter.ToSingle(ordered, 0);
    }
}
```

📚 **知识点**
- **`BitConverter.IsLittleEndian` 运行时探测端序**:不像 R3 硬编码"x86 小端"——工具类追求可移植(万一跑在 ARM 大端模式),**查一下再 Reverse 是更严谨的写法**。
- **组合拳:`Swap 归一 + Reverse 适配本机 + ToSingle 落地`**——每步只干一件事,合起来四种字节序全通吃。

<details markdown="1">
<summary>📄 完整文件 EngineeringConverter.cs(对答案 / 整体粘贴用)</summary>

```csharp
namespace DaqMonitor.Core.Engineering;

/// <summary>
/// 工程量转换:把 AD 原始码 / 模数比例还原成现场熟悉的物理量,
/// 同时处理 32 位浮点的 4 种字节序排列(Modbus / OPC UA 现场常见坑)。
/// 全部静态 + 扩展方法,零依赖,便于单测。
/// </summary>
public static class EngineeringConverter
{
    /// <summary>
    /// 线性标定:把 raw ∈ [rawMin, rawMax] 线性映射到 [engMin, engMax]。
    /// 例:4-20mA → 0-100℃,AD 0-65535 → -50~150℃。
    /// 注意 rawMax == rawMin 时不能除零,按现场惯例返回 engMin(也避免抛异常打断采集)。
    /// </summary>
    public static double Linear(double raw, double rawMin, double rawMax, double engMin, double engMax)
    {
        double span = rawMax - rawMin;
        if (Math.Abs(span) < double.Epsilon) return engMin;
        double ratio = (raw - rawMin) / span;
        return engMin + ratio * (engMax - engMin);
    }

    /// <summary>
    /// 非线性查表(PT100 / 热电偶分度表用):
    /// 在 table(key=raw 升序,value=eng)中插值。
    /// raw 低于最小 key 返回表首;高于最大 key 返回表尾;中间做线性插值。
    /// </summary>
    /// <exception cref="ArgumentException">table 为 null 或空。</exception>
    public static double Lookup(double raw, SortedList<double, double> table)
    {
        if (table is null || table.Count == 0)
            throw new ArgumentException("查表失败:分度表为空", nameof(table));
        if (raw <= table.Keys[0]) return table.Values[0];
        if (raw >= table.Keys[^1]) return table.Values[^1];

        // 二分找到第一个 key > raw 的位置,与前一档做线性插值
        int idx = Bisect(table.Keys, raw);
        double x0 = table.Keys[idx - 1];
        double x1 = table.Keys[idx];
        double y0 = table.Values[idx - 1];
        double y1 = table.Values[idx];
        double span = x1 - x0;
        if (Math.Abs(span) < double.Epsilon) return y0;
        return y0 + (raw - x0) / span * (y1 - y0);
    }

    /// <summary>在升序只读 keys 中找到第一个 > target 的索引(标准 bisect_right 语义)。</summary>
    private static int Bisect(IList<double> keys, double target)
    {
        int lo = 0, hi = keys.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (keys[mid] <= target) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    /// <summary>32 位浮点跨寄存器时的字节序排列(与 ModbusFrameParser.ByteOrder 同义,这里独立定义避免协议层耦合)。</summary>
    public enum ByteOrder { ABCD, CDAB, BADC, DCBA }

    /// <summary>
    /// 字节序交换:把 4 字节 [a,b,c,d] 按指定顺序重排。常用于 Modbus 浮点 / OPC UA / CAN 信号解析。
    /// ABCD=不动;CDAB=字交换(现场最常见);BADC=字节交换;DCBA=全反序。
    /// </summary>
    /// <exception cref="ArgumentException">输入长度不是 4。</exception>
    public static byte[] Swap(byte[] abcd, ByteOrder order)
    {
        if (abcd is null || abcd.Length != 4)
            throw new ArgumentException("字节序交换需要恰好 4 字节", nameof(abcd));
        return order switch
        {
            ByteOrder.ABCD => new[] { abcd[0], abcd[1], abcd[2], abcd[3] },
            ByteOrder.CDAB => new[] { abcd[2], abcd[3], abcd[0], abcd[1] },
            ByteOrder.BADC => new[] { abcd[1], abcd[0], abcd[3], abcd[2] },
            ByteOrder.DCBA => new[] { abcd[3], abcd[2], abcd[1], abcd[0] },
            _ => throw new ArgumentOutOfRangeException(nameof(order))
        };
    }

    /// <summary>把按指定字节序排列的 4 字节解为 float(默认按大端 ABCD 还原)。</summary>
    public static float ToFloat(byte[] bytes, ByteOrder order = ByteOrder.ABCD)
    {
        var ordered = Swap(bytes, order);
        // 本机小端,把 ABCD(大端)逆转一次再 ToSingle
        if (BitConverter.IsLittleEndian) Array.Reverse(ordered);
        return BitConverter.ToSingle(ordered, 0);
    }
}
```

</details>

### ④ 三个测试文件(15 个测试)

> 📂 `src/DaqMonitor.Tests/AcquisitionPipelineTests.cs`
> 💡 测试替身 FakeDevice 继承 DeviceBase——比 Moq 更轻,教学先手写替身,R7 再上 Moq

搭积木:第 1 步骨架 + FakeDevice 替身,第 2 步贴入唯一的管道测试。

**第 1 步 · 骨架 + FakeDevice 替身**(整个文件先建出来)

```csharp
using DaqMonitor.Core.Acquisition;
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Models;
using Xunit;

namespace DaqMonitor.Tests;

public class AcquisitionPipelineTests
{
    private sealed class FakeDevice : DeviceBase
    {
        public FakeDevice() : base(1, "fake") { }
        public override void Connect() { }
        public override void Disconnect() { }
        public override double Read(int addr) => 0;
        public override void Write(int addr, double value) { }
        public void Emit(int pointId, double value) => RaiseData(pointId, value);
    }
}
```

📚 **知识点**
- **手写替身比 Moq 更适合教学**:`FakeDevice` 继承 DeviceBase,四个抽象方法全给空实现,再加一个 `Emit` 主动发数据——**9 行代码 = 一个可控的假设备**。想 mock 抽象类,Moq 要写 `Mock.Of` + Setup 一堆,手写反而清楚(前端类比:手写一个 dummy 组件比配 mock 库快)。
- **`Emit` 直通 `RaiseData`**:测试想什么时候发数据就什么时候发——**替身的本质是"把异步的不可控变成同步的可控"**。

**第 2 步 · 管道批量测试(异步等待版)**(贴进类里,最后一个 `}` 之前)

```csharp
    [Fact]
    public async Task Pipeline_BatchesPoints_IntoBatchReady()
    {
        using var pipeline = new AcquisitionPipeline(TimeSpan.FromMilliseconds(50));
        var received = new List<SensorPoint>();
        var done = new TaskCompletionSource();

        pipeline.BatchReady += (s, batch) =>
        {
            received.AddRange(batch);
            if (received.Count >= 3) done.TrySetResult();
        };

        var dev = new FakeDevice();
        pipeline.Register(dev);
        dev.Emit(1, 10);
        dev.Emit(2, 20);
        dev.Emit(3, 30);

        await Task.WhenAny(done.Task, Task.Delay(2000));
        Assert.Equal(3, received.Count);
    }
}
```

📚 **知识点**
- **`TaskCompletionSource` 是异步测试的信号枪**:R2 用 ManualResetEventSlim(同步等),这里用 TCS(async 等)——**事件到达时 TrySetResult,await 立刻续跑**,比 Sleep 精准比轮询优雅。`Task.WhenAny(done.Task, Task.Delay(2000))` 再兜一层超时:事件不来最多等 2 秒就往下走,断言 3≠counted 失败——**测试永不悬挂**的标配写法。
- **`Assert.Equal(3, received.Count)` 顺带验证"合批"**:3 个点先后 Emit,收到的不是 3 次单点事件而是凑成一批(50ms 定时窗内)——一个断言验证了"缓冲 + 批量 + 不丢"三件事。

<details markdown="1">
<summary>📄 完整文件 AcquisitionPipelineTests.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Acquisition;
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Models;
using Xunit;

namespace DaqMonitor.Tests;

public class AcquisitionPipelineTests
{
    private sealed class FakeDevice : DeviceBase
    {
        public FakeDevice() : base(1, "fake") { }
        public override void Connect() { }
        public override void Disconnect() { }
        public override double Read(int addr) => 0;
        public override void Write(int addr, double value) { }
        public void Emit(int pointId, double value) => RaiseData(pointId, value);
    }

    [Fact]
    public async Task Pipeline_BatchesPoints_IntoBatchReady()
    {
        using var pipeline = new AcquisitionPipeline(TimeSpan.FromMilliseconds(50));
        var received = new List<SensorPoint>();
        var done = new TaskCompletionSource();

        pipeline.BatchReady += (s, batch) =>
        {
            received.AddRange(batch);
            if (received.Count >= 3) done.TrySetResult();
        };

        var dev = new FakeDevice();
        pipeline.Register(dev);
        dev.Emit(1, 10);
        dev.Emit(2, 20);
        dev.Emit(3, 30);

        await Task.WhenAny(done.Task, Task.Delay(2000));
        Assert.Equal(3, received.Count);
    }
}
```

</details>

> 📂 `src/DaqMonitor.Tests/AlarmEngineTests.cs`

搭积木:第 1 步骨架 + 边沿测试,第 2 步贴入回滞测试。

**第 1 步 · 骨架 + 边沿触发测试**(整个文件先建出来)

```csharp
using DaqMonitor.Core.Alarms;
using DaqMonitor.Core.Models;
using Xunit;

namespace DaqMonitor.Tests;

public class AlarmEngineTests
{
    [Fact]
    public void Evaluate_FiresOnlyOnRisingEdge()
    {
        var engine = new AlarmEngine();
        engine.Add(new AlarmRule { PointId = 3, Threshold = 100, Level = AlarmLevel.Critical, IsHigh = true });
        int count = 0;
        engine.AlarmTriggered += (s, e) => count++;

        engine.Evaluate(new SensorPoint { Id = 3, Value = 200 });
        engine.Evaluate(new SensorPoint { Id = 3, Value = 200 });   // 同点仍超阈值:不应重复报
        Assert.Equal(1, count);
    }
}
```

📚 **知识点**
- **两行 Evaluate 一模一样,断言只报 1 次**:这就是"边沿触发"最短证明——**持续越界不重复报**。100Hz 数据流里越限状态会持续几十秒,没有边沿检测就是每秒 100 条通知刷屏。
- **`count++` 闭包计数器**:事件回调往闭包变量里累加——测事件触发次数的标准姿势(前端:fireEvent 后查调用次数,同款)。

**第 2 步 · 回滞防抖测试(FR5-6 的验收脚本)**(贴进类里,最后一个 `}` 之前)

```csharp
    [Fact]
    public void Evaluate_WithHysteresis_DoesNotChatter()
    {
        var engine = new AlarmEngine();
        engine.Add(new AlarmRule { PointId = 3, Threshold = 100, Hysteresis = 5, Level = AlarmLevel.Warning, IsHigh = true });
        int count = 0;
        engine.AlarmTriggered += (s, e) => count++;

        engine.Evaluate(new SensorPoint { Id = 3, Value = 120 });   // 越界,触发
        engine.Evaluate(new SensorPoint { Id = 3, Value = 102 });   // 仍在回滞带(95~105)内:不报
        engine.Evaluate(new SensorPoint { Id = 3, Value = 90 });    // 回到正常区:复位
        engine.Evaluate(new SensorPoint { Id = 3, Value = 120 });   // 再次越界:再报
        Assert.Equal(2, count);
    }
}
```

📚 **知识点**
- **四个值 120→102→90→120 是一条"抖动剧情"**:触发 → 掉回带内(不许恢复)→ 真正出带(恢复,虽然这测试只数触发)→ 再触发——**总数 2 恰好对应两次"上升沿"**。如果引擎没做回滞,102 会触发一次恢复、120 又触发一次报警,count 会变成 3——**测试的期望值本身就是防抖语义的数学表达**。
- **`Hysteresis = 5` → 回滞带 [95, 105]**:注释里算给你看——读报警测试先算带,再看每个值落带的哪一侧,整个断言就透明了。

<details markdown="1">
<summary>📄 完整文件 AlarmEngineTests.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Alarms;
using DaqMonitor.Core.Models;
using Xunit;

namespace DaqMonitor.Tests;

public class AlarmEngineTests
{
    [Fact]
    public void Evaluate_FiresOnlyOnRisingEdge()
    {
        var engine = new AlarmEngine();
        engine.Add(new AlarmRule { PointId = 3, Threshold = 100, Level = AlarmLevel.Critical, IsHigh = true });
        int count = 0;
        engine.AlarmTriggered += (s, e) => count++;

        engine.Evaluate(new SensorPoint { Id = 3, Value = 200 });
        engine.Evaluate(new SensorPoint { Id = 3, Value = 200 });   // 同点仍超阈值:不应重复报
        Assert.Equal(1, count);
    }

    [Fact]
    public void Evaluate_WithHysteresis_DoesNotChatter()
    {
        var engine = new AlarmEngine();
        engine.Add(new AlarmRule { PointId = 3, Threshold = 100, Hysteresis = 5, Level = AlarmLevel.Warning, IsHigh = true });
        int count = 0;
        engine.AlarmTriggered += (s, e) => count++;

        engine.Evaluate(new SensorPoint { Id = 3, Value = 120 });   // 越界,触发
        engine.Evaluate(new SensorPoint { Id = 3, Value = 102 });   // 仍在回滞带(95~105)内:不报
        engine.Evaluate(new SensorPoint { Id = 3, Value = 90 });    // 回到正常区:复位
        engine.Evaluate(new SensorPoint { Id = 3, Value = 120 });   // 再次越界:再报
        Assert.Equal(2, count);
    }
}
```

</details>

> 📂 `src/DaqMonitor.Tests/EngineeringConverterTests.cs`

搭积木:第 1 步骨架 + 线性标定 3 测试,第 2 步查表 2 测试,第 3 步字节序 4 测试(含 Theory)。

**第 1 步 · 骨架 + 线性标定测试**(整个文件先建出来)

```csharp
using DaqMonitor.Core.Engineering;
using Xunit;

namespace DaqMonitor.Tests;

public class EngineeringConverterTests
{
    [Fact]
    public void Linear_Maps4to20mA_To0to100()
    {
        // 4mA → 0℃,20mA → 100℃,12mA → 50℃
        Assert.Equal(0, EngineeringConverter.Linear(4, 4, 20, 0, 100));
        Assert.Equal(100, EngineeringConverter.Linear(20, 4, 20, 0, 100));
        Assert.Equal(50, EngineeringConverter.Linear(12, 4, 20, 0, 100));
    }

    [Fact]
    public void Linear_OutsideRange_ClampsToExtrapolation()
    {
        // 不做硬限幅(工业现场常允许外推少量超量程),按线性公式延伸
        // 0mA ∈ [4,20] 外推:(0-4)/16 * 100 = -25.0
        Assert.Equal(-25.0, EngineeringConverter.Linear(0, 4, 20, 0, 100), 2);
    }

    [Fact]
    public void Linear_DivZero_ReturnsEngMin()
    {
        // rawMax == rawMin 不能除零,按现场惯例返回 engMin
        Assert.Equal(42.0, EngineeringConverter.Linear(123, 100, 100, 42.0, 99.0));
    }
}
```

📚 **知识点**
- **三个测试 = 线性标定的三段人生**:量程内(正常)、量程外(外推)、量程塌缩(除零)——**正常/边界/畸形各一个测试**,API 的行为面才算锁全。
- **`Assert.Equal(expected, actual, 2)` 第三个参数是精度**:小数点后 2 位内相等即过——浮点断言永远带精度,前面说过,这里再见。
- **4-20mA 是工业模拟量的"世界语"**:4 对应量程下限、20 对应上限(0mA 是断线故障,不是 0℃)——**"为什么从 4 开始"面试可能问:为了把断线和真 0 区分开**。

**第 2 步 · 查表插值测试**(贴进类里,最后一个 `}` 之前)

```csharp
    [Fact]
    public void Lookup_InterpolatesBetweenTableEntries()
    {
        // PT100 简化分度表:0Ω→0℃,100Ω→100℃,200Ω→200℃ 线性区外
        var table = new SortedList<double, double> { [0] = 0, [100] = 100, [200] = 200 };
        Assert.Equal(50, EngineeringConverter.Lookup(50, table));
        // 边界 clamp
        Assert.Equal(0, EngineeringConverter.Lookup(-10, table));
        Assert.Equal(200, EngineeringConverter.Lookup(999, table));
    }

    [Fact]
    public void Lookup_ThrowsOnEmptyTable()
    {
        Assert.Throws<ArgumentException>(() => EngineeringConverter.Lookup(1, new SortedList<double, double>()));
    }
```

📚 **知识点**
- **`SortedList` 的索引初始化器 `{ [0] = 0, [100] = 100 }`**:三行造一张迷你分度表——**测试数据结构越轻,读测试越快**。
- **一个测试覆盖"插值 + 两端钳位"三种情况**:50 插值出 50、-10 钳在 0、999 钳在 200——三组断言把 Lookup 的三种返回路径全走完。

**第 3 步 · 字节序测试(Theory 参数化登场)**(贴进类里,收尾)

```csharp
    [Theory]
    [InlineData(EngineeringConverter.ByteOrder.ABCD, new byte[] { 0x42, 0x48, 0x00, 0x00 })]   // 50.0f 大端
    [InlineData(EngineeringConverter.ByteOrder.CDAB, new byte[] { 0x00, 0x00, 0x42, 0x48 })]   // 字交换后还原
    [InlineData(EngineeringConverter.ByteOrder.BADC, new byte[] { 0x48, 0x42, 0x00, 0x00 })]   // 字节交换后还原
    [InlineData(EngineeringConverter.ByteOrder.DCBA, new byte[] { 0x00, 0x00, 0x48, 0x42 })]   // 全反序后还原
    public void Swap_And_ToFloat_AllOrdersDecodeToSameValue(EngineeringConverter.ByteOrder order, byte[] bytes)
    {
        // 无论字节序如何,所有 4 种排列如果原始语义是 50.0f 大端 ABCD,解码后都应是 50.0f
        float v = EngineeringConverter.ToFloat(bytes, order);
        Assert.Equal(50.0f, v, 1);
    }

    [Fact]
    public void Swap_ThrowsOnWrongLength()
    {
        Assert.Throws<ArgumentException>(() => EngineeringConverter.Swap(new byte[] { 1, 2, 3 }, EngineeringConverter.ByteOrder.ABCD));
    }

    [Fact]
    public void Swap_IdempotentForABCD()
    {
        var src = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        var dst = EngineeringConverter.Swap(src, EngineeringConverter.ByteOrder.ABCD);
        Assert.Equal(src, dst);
    }

    [Fact]
    public void Swap_WordSwap_RearrangesCorrectly()
    {
        var src = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        var dst = EngineeringConverter.Swap(src, EngineeringConverter.ByteOrder.CDAB);
        Assert.Equal(new byte[] { 0x33, 0x44, 0x11, 0x22 }, dst);
    }
}
```

📚 **知识点**
- **`[Theory]` + `[InlineData]` = 参数化测试**:同一个测试逻辑跑 4 组数据(每种字节序一组)——**四个测试报告项,一份代码**,前端 vitest 的 `test.each` 同款。每组 InlineData 的字节数组都是"50.0f 按该序排列后的样子",解码回来必须都是 50.0——**四条路殊途同归,这才是"字节序重排"的可证正确**。
- **`Swap_IdempotentForABCD`:ABCD 是"恒等变换"**——不动就是对的;**`Swap_WordSwap_RearrangesCorrectly`:CDAB 必须精确重排**——`11 22 33 44 → 33 44 11 22`。一个测"不变",一个测"变对",对称锁定。

<details markdown="1">
<summary>📄 完整文件 EngineeringConverterTests.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Engineering;
using Xunit;

namespace DaqMonitor.Tests;

public class EngineeringConverterTests
{
    [Fact]
    public void Linear_Maps4to20mA_To0to100()
    {
        // 4mA → 0℃,20mA → 100℃,12mA → 50℃
        Assert.Equal(0, EngineeringConverter.Linear(4, 4, 20, 0, 100));
        Assert.Equal(100, EngineeringConverter.Linear(20, 4, 20, 0, 100));
        Assert.Equal(50, EngineeringConverter.Linear(12, 4, 20, 0, 100));
    }

    [Fact]
    public void Linear_OutsideRange_ClampsToExtrapolation()
    {
        // 不做硬限幅(工业现场常允许外推少量超量程),按线性公式延伸
        // 0mA ∈ [4,20] 外推:(0-4)/16 * 100 = -25.0
        Assert.Equal(-25.0, EngineeringConverter.Linear(0, 4, 20, 0, 100), 2);
    }

    [Fact]
    public void Linear_DivZero_ReturnsEngMin()
    {
        // rawMax == rawMin 不能除零,按现场惯例返回 engMin
        Assert.Equal(42.0, EngineeringConverter.Linear(123, 100, 100, 42.0, 99.0));
    }

    [Fact]
    public void Lookup_InterpolatesBetweenTableEntries()
    {
        // PT100 简化分度表:0Ω→0℃,100Ω→100℃,200Ω→200℃ 线性区外
        var table = new SortedList<double, double> { [0] = 0, [100] = 100, [200] = 200 };
        Assert.Equal(50, EngineeringConverter.Lookup(50, table));
        // 边界 clamp
        Assert.Equal(0, EngineeringConverter.Lookup(-10, table));
        Assert.Equal(200, EngineeringConverter.Lookup(999, table));
    }

    [Fact]
    public void Lookup_ThrowsOnEmptyTable()
    {
        Assert.Throws<ArgumentException>(() => EngineeringConverter.Lookup(1, new SortedList<double, double>()));
    }

    [Theory]
    [InlineData(EngineeringConverter.ByteOrder.ABCD, new byte[] { 0x42, 0x48, 0x00, 0x00 })]   // 50.0f 大端
    [InlineData(EngineeringConverter.ByteOrder.CDAB, new byte[] { 0x00, 0x00, 0x42, 0x48 })]   // 字交换后还原
    [InlineData(EngineeringConverter.ByteOrder.BADC, new byte[] { 0x48, 0x42, 0x00, 0x00 })]   // 字节交换后还原
    [InlineData(EngineeringConverter.ByteOrder.DCBA, new byte[] { 0x00, 0x00, 0x48, 0x42 })]   // 全反序后还原
    public void Swap_And_ToFloat_AllOrdersDecodeToSameValue(EngineeringConverter.ByteOrder order, byte[] bytes)
    {
        // 无论字节序如何,所有 4 种排列如果原始语义是 50.0f 大端 ABCD,解码后都应是 50.0f
        float v = EngineeringConverter.ToFloat(bytes, order);
        Assert.Equal(50.0f, v, 1);
    }

    [Fact]
    public void Swap_ThrowsOnWrongLength()
    {
        Assert.Throws<ArgumentException>(() => EngineeringConverter.Swap(new byte[] { 1, 2, 3 }, EngineeringConverter.ByteOrder.ABCD));
    }

    [Fact]
    public void Swap_IdempotentForABCD()
    {
        var src = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        var dst = EngineeringConverter.Swap(src, EngineeringConverter.ByteOrder.ABCD);
        Assert.Equal(src, dst);
    }

    [Fact]
    public void Swap_WordSwap_RearrangesCorrectly()
    {
        var src = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        var dst = EngineeringConverter.Swap(src, EngineeringConverter.ByteOrder.CDAB);
        Assert.Equal(new byte[] { 0x33, 0x44, 0x11, 0x22 }, dst);
    }
}
```

</details>

## ✅ 验证(必做)

```bash
dotnet build
dotnet test
```
**期望输出(关键行)**:
```
已成功生成。 → 0 个警告 0 个错误
已通过! - 失败: 0,通过: 42 ... DaqMonitor.Tests.dll
```
(42 = 之前 27 + 本篇 15)

## ✅ 验收清单

- [ ] build 0 错 0 警,test 42/42 绿
- [ ] 能回答:为什么事件回调里只做 `TryWrite` 一件事?(1000 事件/秒时,回调越短,设备线程越不被拖住)
- [ ] 能回答:BatchReady 双触发(满 500 条 / 定时到)分别照顾什么场景?(高频吞吐 / 低频延迟)
- [ ] 能回答:回滞带宽 5、阈值 100:值 103 时处于什么状态?值 90 呢?(带内=维持现状;90=出带恢复)
- [ ] 亲手算:Linear(12, 4, 20, 0, 100) = ?(50.0——4-20mA 中点对应量程中点)
- [ ] git commit -m "R5: 采集管道+报警引擎+工程量转换+15测试"

## 🎤 面试怎么讲这一篇

> "采集链路的核心是个管道:设备事件回调只做 Channel 入队,后台任务消费,200 毫秒定时或攒满 500 条就触发 BatchReady 批量事件——UI 一次刷一批,100Hz 乘 10 台设备也不卡界面。报警引擎三个生产特性:规则运行时增删要线程安全、只在上升沿触发避免越界期间每条数据都报警、回滞带宽防阈值附近抖动狂报——120 触发后掉到 102 还在带内不恢复,掉到 90 才恢复。工程量转换支持线性标定和分度表插值,除零退化返回量程下限而不是抛异常,因为采集链路上一个坏点不该打断整条流水线。这套东西全部纯内存可测,15 个测试不碰硬件。"

**✅ 打卡[ ]**
