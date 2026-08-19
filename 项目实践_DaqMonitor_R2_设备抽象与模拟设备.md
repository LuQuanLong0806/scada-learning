# R2 · 设备抽象 + 模拟设备(面向接口,零硬件开工)

> **定位**:定义全项目的设备契约 [IDevice](kp:idevice),再写一个"假设备"让你没硬件也能跑通全链路。
> **前置**:R1 全绿。**预计敲码**:60 分钟。
> **产出**:IDevice/DataEventArgs/DeviceBase/SimulatedDevice + 第一批测试,`dotnet test` 全绿。

---

## 🎯 本篇交付物

```
src/DaqMonitor.Core/Devices/
├─ IDevice.cs            # 契约:接口 + 事件参数 + 基类(一个文件三个类型)
└─ SimulatedDevice.cs    # 模拟设备:后台线程周期产生随机值
src/DaqMonitor.Tests/
└─ DeviceFlowTests.cs    # 验证:订阅设备事件 → 收到带时间戳的数据
```

## 📋 需求单(先自己设计接口,再对照)

| # | 需求 | 验收 |
|---|---|---|
| FR2-1 | 统一设备契约:任何设备(串口/PLC/TCP/模拟)都能 `Connect/Disconnect/Read/Write`,并对外报 `Id/Name/State` | 换设备实现类,上层代码零改动 |
| FR2-2 | 数据到达走**事件推送**([DataReceived](kp:event-delegate)),采集层被动收,不轮询 | 订阅方能收到 (PointId, Value, Timestamp) |
| FR2-3 | 时间戳由**事件发送方统一打**,默认 `DateTime.Now` | 收到的 Timestamp ≠ 0001-01-01 |
| FR2-4 | 模拟设备:Connect 走 离线→连接中→在线 状态机;Start(interval) 后台循环每 tick 给每个点位发随机值,约 10% 概率冲到 95~120(方便后面看报警) | 测试 3 秒内收到数据 |
| FR2-5 | 基类复用:状态字段 + 触发事件的 `RaiseData` 收进抽象基类,子类不重复写 | SimulatedDevice 里没有一行事件订阅代码 |

**自己先想 5 分钟**:接口方法要不要做成 async([提示](kp:sync-facade))?事件参数为什么不用 SensorPoint 而单独造 DataEventArgs?

## 📚 本篇知识点

- [IDevice 统一抽象](kp:idevice) · [为什么是同步门面](kp:sync-facade) · [event 事件机制](kp:event-delegate) · [struct vs class](kp:struct-vs-class) · [xUnit 单元测试](kp:unit-test)

## 🛠️ 参考实现

### ① 契约:IDevice + DataEventArgs + DeviceBase(一个文件,分 3 步搭)

> 📂 `src/DaqMonitor.Core/Devices/IDevice.cs` · namespace `DaqMonitor.Core.Devices`(与参考工程一字不差)
> 🔧 无 NuGet · 💡 用到 R1 的 `DeviceState`(`DaqMonitor.Core.Models`)
> 这个文件是"三个类型平铺"的结构,天然适合搭积木:**每一步追加一个类型,贴完就能编译**。跟敲完 3 步,你的文件就和折叠块里的完整版一模一样。
> 🗺️ **前端类比**:IDevice ≈ interface,DeviceBase ≈ 抽象基类组件,DataReceived ≈ 事件的 on/emit

**第 1 步 · DataEventArgs —— 一次采样的"快递盒"**(新建文件,贴入下面全部)

```csharp
using DaqMonitor.Core.Models;
using System;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// 数据到达事件参数。注意:载荷不是 SensorPoint——
/// 事件只报"哪个点、什么值、何时",转成 SensorPoint 是采集管道的事(职责分离)。
/// </summary>
public class DataEventArgs : EventArgs
{
    public int PointId { get; init; }
    public double Value { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;   // 发送方统一打时间戳
}
```

📚 **知识点**
- **为什么载荷不是 SensorPoint?** 设备层只报事实("哪个点、什么值、何时"),组装成领域模型是采集管道的职责——设备不该知道领域模型长什么样,这样换领域模型不动设备层。前端类比:`CustomEvent` 的 `detail` 只放最小事实,组件自己决定怎么用它渲染。
- **`{ get; init; }`** = 只读属性 + 只能在对象初始化器里赋值(C# 9)。比 `set` 安全:事件参数发出去之后没人能改——≈ TS 的 `readonly`。
- **时间戳默认 `DateTime.Now`**:谁发数据谁打时间,下游不许自己补——"单一时间源"是分布式采集的铁律,否则各盖各的章,历史回放时序全乱。

**第 2 步 · IDevice —— 所有设备的"合同"**(追加到同一文件末尾)

```csharp
/// <summary>设备统一接口:UI/采集层只认它,不关心底层是串口/网口/PLC</summary>
public interface IDevice
{
    int Id { get; }
    string Name { get; }
    DeviceState State { get; }

    void Connect();
    void Disconnect();
    double Read(int addr);
    void Write(int addr, double value);

    /// <summary>采集层拿到数据后单向通知订阅方(UI 刷新用)</summary>
    event EventHandler<DataEventArgs>? DataReceived;
}
```

📚 **知识点**
- **接口 = 可插拔的合同**:上层只 `using IDevice`,底层是串口/网口/PLC 无所谓。R4 接入真实设备时你会体会:换设备只改 `new` 的那一行,管道/UI/报警零改动——这就是"面向接口编程"值钱的地方。
- **`Read` 返回 `double` 不是 `byte[]`**:设备层交出来的已经是工程量(25.3 ℃),协议字节、CRC、字节序是设备内部的事(见 R3)。**原始值与工程量的分层**,后面每一层都在受益。
- **接口里能声明 event**:C# 接口成员不止方法,事件/属性都行。`EventHandler<T>?` 的 `?` 表示"可以没人订阅"——调用前必须判空,所以 DeviceBase 里用 `?.Invoke`。

**第 3 步 · DeviceBase —— 把重复的部分先写好**(追加到文件末尾)

```csharp
/// <summary>设备基类:复用通用状态与事件触发逻辑(FR2-5)</summary>
public abstract class DeviceBase : IDevice
{
    public int Id { get; }
    public string Name { get; }
    public DeviceState State { get; protected set; } = DeviceState.Offline;

    public event EventHandler<DataEventArgs>? DataReceived;

    protected DeviceBase(int id, string name) { Id = id; Name = name; }

    public abstract void Connect();
    public abstract void Disconnect();
    public abstract double Read(int addr);
    public abstract void Write(int addr, double value);

    /// <summary>子类采集到数据后调用:触发 DataReceived 事件(自动打时间戳)</summary>
    protected void RaiseData(int pointId, double value)
        => DataReceived?.Invoke(this, new DataEventArgs { PointId = pointId, Value = value, Timestamp = DateTime.Now });
}
```

📚 **知识点**
- **为什么接口之外还要一个抽象基类?** 接口定合同,基类写"合同里所有设备都要重复写的那部分":Id/Name/State 字段、事件字段、发射器。子类只写差异(怎么连、怎么读)——模板方法思想,前端类比:抽象基类组件(HOC)把"订阅/卸载"包好,子组件只写渲染。
- **`protected set`**:状态只有设备自己(和子类)能改,外面只读——防止 UI 层手滑把设备改成 Online。状态机转换权收在设备内部。
- **`RaiseData` 是唯一的发射口**:所有子类报数必须走它 → 时间戳统一在这里盖 → 想加"事件计数/日志"只改一处。**一个口子进出,是留扩展点的基本功**。

<details markdown="1">
<summary>📄 完整文件 IDevice.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Models;
using System;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// 数据到达事件参数。注意:载荷不是 SensorPoint——
/// 事件只报"哪个点、什么值、何时",转成 SensorPoint 是采集管道的事(职责分离)。
/// </summary>
public class DataEventArgs : EventArgs
{
    public int PointId { get; init; }
    public double Value { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;   // 发送方统一打时间戳
}

/// <summary>设备统一接口:UI/采集层只认它,不关心底层是串口/网口/PLC</summary>
public interface IDevice
{
    int Id { get; }
    string Name { get; }
    DeviceState State { get; }

    void Connect();
    void Disconnect();
    double Read(int addr);
    void Write(int addr, double value);

    /// <summary>采集层拿到数据后单向通知订阅方(UI 刷新用)</summary>
    event EventHandler<DataEventArgs>? DataReceived;
}

/// <summary>设备基类:复用通用状态与事件触发逻辑(FR2-5)</summary>
public abstract class DeviceBase : IDevice
{
    public int Id { get; }
    public string Name { get; }
    public DeviceState State { get; protected set; } = DeviceState.Offline;

    public event EventHandler<DataEventArgs>? DataReceived;

    protected DeviceBase(int id, string name) { Id = id; Name = name; }

    public abstract void Connect();
    public abstract void Disconnect();
    public abstract double Read(int addr);
    public abstract void Write(int addr, double value);

    /// <summary>子类采集到数据后调用:触发 DataReceived 事件(自动打时间戳)</summary>
    protected void RaiseData(int pointId, double value)
        => DataReceived?.Invoke(this, new DataEventArgs { PointId = pointId, Value = value, Timestamp = DateTime.Now });
}
```

</details>

### ② 模拟设备 SimulatedDevice(单类文件:先整体贴入,再分 3 步读懂)

> 📂 `src/DaqMonitor.Core/Devices/SimulatedDevice.cs` · namespace `DaqMonitor.Core.Devices`
> 🔧 无 NuGet
> 💡 "同步门面 + 内部异步"的样板:Connect() 立即返回,采集在 [Task.Run](kp:taskrun) 的后台循环里
>
> 这个文件是**一个类、成员互相调用**(Disconnect 调 Stop、Start 用字段),拆成几段分开编译过不了——所以玩法是:**先展开折叠块把完整文件贴进去,然后按下面 3 步逐块读懂**。每一步都标了它在文件里的位置。

**第 1 步 · 类骨架:字段 + 构造 + 四个接口方法**(文件开头到 `Write` 为止)

```csharp
using DaqMonitor.Core.Models;
using System.Threading;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// 模拟设备:实现 IDevice,用后台线程周期性产生随机点位。
/// 没有真实硬件也能跑通整个采集链路——本地演示 / 单元测试 / 面试 demo 都用它。
/// 它和真实串口/PLC 设备暴露同一接口,上层(管道/UI/报警)完全无感——可插拔。
/// </summary>
public class SimulatedDevice : DeviceBase
{
    private readonly int[] _pointIds;
    private readonly Random _rnd = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public SimulatedDevice(int id, string name, params int[] pointIds)
        : base(id, name)
        => _pointIds = pointIds.Length > 0 ? pointIds : new[] { 1 };

    public override void Connect()
    {
        State = DeviceState.Connecting;
        Thread.Sleep(50); // 模拟握手耗时
        State = DeviceState.Online;
    }

    public override void Disconnect()
    {
        Stop();
        State = DeviceState.Offline;
    }

    public override double Read(int addr) => Math.Round(_rnd.NextDouble() * 100, 2);

    public override void Write(int addr, double value) { /* 模拟设备只读,忽略写 */ }
```

📚 **知识点**
- **`params int[]` 构造**:`new SimulatedDevice(1, "Sim-01", 1, 2, 3)` 最后三个参数自动收进数组——调用方写起来像可变参数,和 `console.log(...)` 的观感一致。
- **`Connect()` 为什么睡 50ms?** 假装握手耗时,让 `Connecting` 状态肉眼可见(真实设备握手要几百毫秒)。注意它**只改状态、不做采集**——采集是 `Start()` 的事,所以 Connect 立即返回,不卡调用方。
- **状态机的三个态都在这了**:Connecting(握着)→ Online(通了)→ Disconnect 时 Offline。UI 的状态灯(绿/黄/灰)绑的就是这个属性。

**第 2 步 · Start:Task.Run 后台采集循环**(`Write` 之后,类的中间)

```csharp
    /// <summary>
    /// 开始模拟采集:每隔 interval 给每个点位发一个随机值。
    /// 约 10% 概率冲到 95~120 区间,从而越过 100 的报警阈值,方便看到报警效果。
    /// </summary>
    public void Start(TimeSpan interval)
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    foreach (var pid in _pointIds)
                    {
                        var v = _rnd.NextDouble() < 0.1
                            ? 95 + _rnd.NextDouble() * 25   // 可能越界,触发报警
                            : 20 + _rnd.NextDouble() * 70;  // 正常区间
                        RaiseData(pid, Math.Round(v, 2));
                    }
                    await Task.Delay(interval, token);
                }
            }
            catch (OperationCanceledException) { /* 正常退出 */ }
        }, token);
    }
```

📚 **知识点**
- **[同步门面](kp:sync-facade) + 内部异步**:`Start()` 是同步方法,内部 `Task.Run` 起后台循环——≈ 前端 `setInterval(fn, ms)` 定时 emit,区别是这里用 `Task.Delay + while`,因为要能**取消**。
- **CancellationToken 是优雅停机的钥匙**:`Task.Delay(interval, token)` 一被取消就抛 `OperationCanceledException`,`while` 循环自然散架——catch 它是**正常退出**,不是 bug。这套"令牌取消"模式在 MC 运控项目里还管着两轴电机急停,是工业软件的标准动作(详见 [取消令牌](kp:cancel-token))。
- **`if (_loop is not null) return;`**:防重复启动——连点两次"启动采集"不会起两个循环、数据翻倍。UI 按钮灰亮之外的第二道保险。
- **10% 越限概率不是随便写的**:让 R5 的报警引擎有事干、R8 的表盘会变红——模拟数据的形状要**服务于下游演示**,这是做 demo 设备的门道。

**第 3 步 · Stop:取消 + 等收尾 + 释放**(`Start` 之后,类的末尾)

```csharp
    /// <summary>停止模拟采集并释放后台任务。</summary>
    public void Stop()
    {
        _cts?.Cancel();
        try { _loop?.Wait(500); } catch { /* 忽略 */ }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }
}
```

📚 **知识点**
- **停机的三步舞**:`Cancel()`(发停止信号)→ `Wait(500)`(等后台循环退出,**带超时**,防止设备卡死拖死 UI)→ 清空字段(允许再次 Start)。顺序不能乱:先 Dispose 再 Wait 的话,循环里还在用的令牌就没了。
- **`catch { /* 忽略 */ }`**:等 500ms 超时会抛异常,这里吞掉是**故意的**——停机路径上任何异常都不该往上冒(冒上去的就是"点停止按钮崩溃")。工业软件里"关闭/停止"代码路径的容错标准比"运行"路径高一档。

<details markdown="1">
<summary>📄 完整文件 SimulatedDevice.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Models;
using System.Threading;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// 模拟设备:实现 IDevice,用后台线程周期性产生随机点位。
/// 没有真实硬件也能跑通整个采集链路——本地演示 / 单元测试 / 面试 demo 都用它。
/// 它和真实串口/PLC 设备暴露同一接口,上层(管道/UI/报警)完全无感——可插拔。
/// </summary>
public class SimulatedDevice : DeviceBase
{
    private readonly int[] _pointIds;
    private readonly Random _rnd = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public SimulatedDevice(int id, string name, params int[] pointIds)
        : base(id, name)
        => _pointIds = pointIds.Length > 0 ? pointIds : new[] { 1 };

    public override void Connect()
    {
        State = DeviceState.Connecting;
        Thread.Sleep(50); // 模拟握手耗时
        State = DeviceState.Online;
    }

    public override void Disconnect()
    {
        Stop();
        State = DeviceState.Offline;
    }

    public override double Read(int addr) => Math.Round(_rnd.NextDouble() * 100, 2);

    public override void Write(int addr, double value) { /* 模拟设备只读,忽略写 */ }

    /// <summary>
    /// 开始模拟采集:每隔 interval 给每个点位发一个随机值。
    /// 约 10% 概率冲到 95~120 区间,从而越过 100 的报警阈值,方便看到报警效果。
    /// </summary>
    public void Start(TimeSpan interval)
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    foreach (var pid in _pointIds)
                    {
                        var v = _rnd.NextDouble() < 0.1
                            ? 95 + _rnd.NextDouble() * 25   // 可能越界,触发报警
                            : 20 + _rnd.NextDouble() * 70;  // 正常区间
                        RaiseData(pid, Math.Round(v, 2));
                    }
                    await Task.Delay(interval, token);
                }
            }
            catch (OperationCanceledException) { /* 正常退出 */ }
        }, token);
    }

    /// <summary>停止模拟采集并释放后台任务。</summary>
    public void Stop()
    {
        _cts?.Cancel();
        try { _loop?.Wait(500); } catch { /* 忽略 */ }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }
}
```

</details>

### ③ 第一批测试(设备流程 + 时间戳铁律)

> 📂 `src/DaqMonitor.Tests/DeviceFlowTests.cs` · namespace `DaqMonitor.Tests`
> 🔧 无需装包(R1 的 xunit 模板自带)
> 💡 这是本项目的"测试样板":事件异步到达,用 `ManualResetEventSlim`/超时等待断言
>
> 测试文件天然适合搭积木:先立一个空测试类(能编译),再把两个测试方法一块块贴进去,每步都编译通过。

**第 1 步 · 空测试类骨架**(整个文件先建出来)

```csharp
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Models;
using Xunit;

namespace DaqMonitor.Tests;

// 设备抽象的教学测试:接口可用 + 事件推送 + 时间戳铁律
public class DeviceFlowTests
{
}
```

📚 **知识点**
- **三个 using 各管一摊**:`Devices`(被测的 SimulatedDevice)、`Models`(SensorPoint 等领域模型)、`Xunit`([Fact] 标记 + Assert 断言库)。
- **空类也能过 `dotnet test`**:显示"总计: 0"——先让骨架编译通过,再加测试,和前端先写空 `describe` 再填 `it` 一个套路。
- **测试文件按功能命名**:`DeviceFlowTests` 测的是"设备流程"(连上→启动→收数→停机),以后每篇新模块都会有自己的 `XxxTests` 文件,见名知义。

**第 2 步 · 测试 1:事件推送 + 时间戳**(贴进类里,最后一个 `}` 之前)

```csharp
    [Fact]
    public void SimulatedDevice_RaisesData_WithTimestamp()
    {
        IDevice dev = new SimulatedDevice(1, "sim", 7);   // 故意用接口类型声明——上层只认 IDevice
        DataEventArgs? got = null;
        var mre = new ManualResetEventSlim();

        dev.DataReceived += (s, e) => { got = e; mre.Set(); };

        dev.Connect();
        ((SimulatedDevice)dev).Start(TimeSpan.FromMilliseconds(20));

        Assert.True(mre.Wait(3000), "3 秒内应收到至少一条数据");
        Assert.NotNull(got);
        Assert.Equal(7, got!.PointId);
        Assert.InRange(got.Value, 0, 200);
        Assert.True(got.Timestamp > new DateTime(2000, 1, 1), "Timestamp 应被采集源打上");

        ((SimulatedDevice)dev).Stop();
        dev.Disconnect();
        Assert.Equal(DeviceState.Offline, dev.State);
    }
```

📚 **知识点**
- **事件是后台线程发的,不能直接断言**:`RaiseData` 在 Task.Run 循环里触发,主测试线程立刻读 `got` 大概率还是 null——和测前端 `setTimeout` 回调一个道理,必须等它发生。`ManualResetEventSlim` 就是等待信号:事件处理器里 `Set()`(发信号),主线程 `Wait(3000)`(最多等 3 秒)。所以第一行 Assert 断的不是数据,而是**"事件确实发生了"**——信号没来,后面全免谈。
- **`IDevice dev = new SimulatedDevice(...)` 故意用接口类型声明**:整个测试只通过合同访问设备;`Start` 是 SimulatedDevice 特有的,才 `((SimulatedDevice)dev)` 强转一下。体会这里:以后换成 `SerialDevice`,除强转外一字不改——可插拔不是嘴上说的,是测试替你锁死的。
- **断言断不变量,不断偶然值**:`Timestamp > 2000-01-01` 只验证"被采集源打上了",不断言精确时刻(时钟不可预测);`InRange(Value, 0, 200)` 断的是模拟数据的合理区间——比 `NotNull` 有内容得多。

**第 3 步 · 测试 2:时间戳铁律 + 反面教材**(同样贴进类里,最后一个 `}` 之前)

```csharp
    [Fact]
    public void EventToSensorPoint_Conversion_MustCopyTimestamp()
    {
        // R1 说过的翻车点:转 SensorPoint 必须抄 e.Timestamp
        var e = new DataEventArgs { PointId = 3, Value = 42.0, Timestamp = DateTime.Now };

        var p = new SensorPoint { Id = e.PointId, Value = e.Value, Timestamp = e.Timestamp };

        Assert.Equal(3, p.Id);
        Assert.Equal(42.0, p.Value);
        Assert.Equal(e.Timestamp, p.Timestamp);

        var bad = new SensorPoint { Id = e.PointId, Value = e.Value };   // 反面教材
        Assert.Equal(new DateTime(1, 1, 1), bad.Timestamp);              // 不抄 = 0001-01-01
    }
```

📚 **知识点**
- **这个测试锁的是 R1 说的翻车点**:事件 → SensorPoint 转换必须显式抄 `e.Timestamp`,不抄就是 `0001-01-01`(C# struct 的 default 值)。代码里专门写了反面教材变量 `bad`,把"错误的写法长什么样"也变成断言——以后谁改坏这条链路,测试立刻红给你看。
- **用测试锁约定 = 防御性文档**:比注释强一个量级——注释会过时,测试每次 CI 都重新验证一遍。工业软件的时间戳牵着历史曲线排序、报警先后,错一位全乱。

<details markdown="1">
<summary>📄 完整文件 DeviceFlowTests.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Models;
using Xunit;

namespace DaqMonitor.Tests;

// 设备抽象的教学测试:接口可用 + 事件推送 + 时间戳铁律
public class DeviceFlowTests
{
    [Fact]
    public void SimulatedDevice_RaisesData_WithTimestamp()
    {
        IDevice dev = new SimulatedDevice(1, "sim", 7);   // 故意用接口类型声明——上层只认 IDevice
        DataEventArgs? got = null;
        var mre = new ManualResetEventSlim();

        dev.DataReceived += (s, e) => { got = e; mre.Set(); };

        dev.Connect();
        ((SimulatedDevice)dev).Start(TimeSpan.FromMilliseconds(20));

        Assert.True(mre.Wait(3000), "3 秒内应收到至少一条数据");
        Assert.NotNull(got);
        Assert.Equal(7, got!.PointId);
        Assert.InRange(got.Value, 0, 200);
        Assert.True(got.Timestamp > new DateTime(2000, 1, 1), "Timestamp 应被采集源打上");

        ((SimulatedDevice)dev).Stop();
        dev.Disconnect();
        Assert.Equal(DeviceState.Offline, dev.State);
    }

    [Fact]
    public void EventToSensorPoint_Conversion_MustCopyTimestamp()
    {
        // R1 说过的翻车点:转 SensorPoint 必须抄 e.Timestamp
        var e = new DataEventArgs { PointId = 3, Value = 42.0, Timestamp = DateTime.Now };

        var p = new SensorPoint { Id = e.PointId, Value = e.Value, Timestamp = e.Timestamp };

        Assert.Equal(3, p.Id);
        Assert.Equal(42.0, p.Value);
        Assert.Equal(e.Timestamp, p.Timestamp);

        var bad = new SensorPoint { Id = e.PointId, Value = e.Value };   // 反面教材
        Assert.Equal(new DateTime(1, 1, 1), bad.Timestamp);              // 不抄 = 0001-01-01
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
已通过! - 失败: 0, 通过: 2 ... DeviceFlowTests
```

## ✅ 验收清单

- [ ] build 0 错 0 警,test 2/2 绿
- [ ] 能回答:为什么事件载荷是 DataEventArgs 而不是 SensorPoint?(设备只报事实,组装领域模型是管道的职责)
- [ ] 能回答:Connect() 里为什么没有 async/await?([同步门面](kp:sync-facade),内部 Task.Run)
- [ ] 把 `IDevice dev = new SimulatedDevice(...)` 改成未来的 `SerialDevice`,测试逻辑不用改——体会可插拔
- [ ] git commit -m "R2: IDevice 抽象+模拟设备+流程测试"

## 🎤 面试怎么讲这一篇

> "我定义了统一设备接口 IDevice:同步的连接/读写方法加一个 DataReceived 事件。选同步门面是因为工业设备驱动普遍是短指令-快返回的模型,同步语义让上层调用简单;耗时轮询在设备内部用 Task.Run 起,不阻塞 UI。事件参数只带 PointId/Value/Timestamp,由发送方统一打时间戳——下游转领域模型时必须显式抄,否则落 default,这是我们测试里专门锁死的行为。模拟设备实现了同一接口,整个项目零硬件也能开发和测试。"

**✅ 打卡[ ]**
