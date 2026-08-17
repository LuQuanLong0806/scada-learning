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

> 📂 `src/DaqMonitor.Core/Alarms/AlarmEngine.cs`
> 💡 三个生产级特性:线程安全(规则运行时增删)、边沿触发(不刷屏)、回滞(防抖)

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

### ③ EngineeringConverter —— 工程量转换

> 📂 `src/DaqMonitor.Core/Engineering/EngineeringConverter.cs` · 🔧 无 NuGet

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

### ④ 三个测试文件(15 个测试)

> 📂 `src/DaqMonitor.Tests/AcquisitionPipelineTests.cs`
> 💡 测试替身 FakeDevice 继承 DeviceBase——比 Moq 更轻,教学先手写替身,R7 再上 Moq

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

> 📂 `src/DaqMonitor.Tests/AlarmEngineTests.cs`

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

> 📂 `src/DaqMonitor.Tests/EngineeringConverterTests.cs`

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
