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

### ① 契约:IDevice + DataEventArgs + DeviceBase

> 📂 `src/DaqMonitor.Core/Devices/IDevice.cs` · namespace `DaqMonitor.Core.Devices`(一个文件三个类型,与参考工程一字不差)
> 🔧 无 NuGet
> 💡 用到 R1 的 `DeviceState`(`DaqMonitor.Core.Models`)

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

### ② 模拟设备 SimulatedDevice

> 📂 `src/DaqMonitor.Core/Devices/SimulatedDevice.cs` · namespace `DaqMonitor.Core.Devices`
> 🔧 无 NuGet
> 💡 "同步门面 + 内部异步"的样板:Connect() 立即返回,采集在 [Task.Run](kp:taskrun) 的后台循环里

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

### ③ 第一批测试(设备流程 + 时间戳铁律)

> 📂 `src/DaqMonitor.Tests/DeviceFlowTests.cs` · namespace `DaqMonitor.Tests`
> 🔧 无需装包(R1 的 xunit 模板自带)
> 💡 这是本项目的"测试样板":事件异步到达,用 `ManualResetEventSlim`/超时等待断言

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
