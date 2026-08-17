# R7 · DI 组装 + 诊断 + 容错(零件装箱,一键点火)

> **定位**:R1-R6 造好了所有零件,散装零件不是系统。这一篇写**组合根 Bootstrapper**(DI 一处注册全站注入)、诊断服务、重试工具、设备健康监测——最后用**冒烟测试**一键证明全链路真的能跑。
> **前置**:R6 全绿。**预计敲码**:80 分钟。
> **产出**:Retry/DeviceHealthMonitor/DiagnosticsService/Bootstrapper + 6 个新测试(累计 56)。

---

## 🎯 本篇交付物

```
src/DaqMonitor.Core/
├─ Resilience/Retry.cs              # 指数退避+抖动重试(不依赖 Polly)
├─ Health/DeviceHealthMonitor.cs    # 心跳探活:连续超时判掉线→自动重连(R4 挪来)
├─ Diagnostics/DiagnosticsService.cs # 指标计数+环形日志
└─ AppServices/Bootstrapper.cs      # 组合根:DI 全量注册(R7 版;R9+ 加认证/配方/运控)
src/DaqMonitor.Tests/
├─ RetryTests.cs                    # 2 测试
├─ DeviceHealthMonitorTests.cs      # 2 测试
├─ DeviceMoqTests.cs                # 1 测试(Moq 替身设备,验证管道)
└─ CompositionSmokeTests.cs         # 1 冒烟:Bootstrapper 装配→采集→存储
```

## 📋 需求单(先自己设计,再对照)

| # | 需求 | 验收 |
|---|---|---|
| FR7-1 | [Retry](kp:retry-backoff):指数退避(200/400/800ms…)+ 随机抖动;maxRetries=3 表示首试+3 重试;耗尽后抛原异常 | 失败 2 次第 3 次成功 → 调用数=3 |
| FR7-2 | DeviceHealthMonitor:注入心跳委托 + IDevice;连续 missThreshold 次探活失败判掉线(Disconnect);链路恢复自动[指数退避重连](kp:retry-backoff)回 Online;`TickOnceAsync` 可单步驱动(测试不用真等定时器) | 2 次失败掉线→恢复→回 Online |
| FR7-3 | DiagnosticsService:线程安全计数(样本/报警/批量/耗时/uptime)+ 环形日志(上限 200 条,最新在上) | 计数准确,日志不无限涨 |
| FR7-4 | [Bootstrapper 组合根](kp:di):DI 注册 DbContextFactory/PointStore/AlarmEngine/DiagnosticsService/AcquisitionPipeline/IDevice;数据库放 LocalApplicationData;启动 EnsureCreated + 预置 2 条报警规则 | GetRequiredService 全部可解析 |
| FR7-5 | 冒烟测试:Bootstrapper.Build() → 挂设备 → 起管道 → 3 秒内收到批次且入库 | CompositionSmokeTests 绿 |
| FR7-6 | [Moq](kp:moq) 替身设备:Mock<IDevice> + Raise 模拟 DataReceived,验证管道与具体设备实现解耦 | DeviceMoqTests 绿 |

**自己先想 10 分钟**:
1. 组合根为什么放 Core 而不是 UI?(UI/测试/未来服务复用同一套装配;放 UI 则换个壳就要重新接线)
2. 重试为什么要加**随机抖动**?(多客户端同频重试会"共振"打死服务端——打散重试时刻)
3. DeviceHealthMonitor 为什么把心跳做成**委托注入**而不是内部写死"读寄存器"?(不同设备探活方式不同;解耦后单测可控)
4. 冒烟测试和单元测试的区别?(整链路真装配真跑通 vs 单个类逻辑正确——发布前冒烟兜底)

## 📚 本篇知识点

- [DI 依赖注入](kp:di) · [指数退避重试](kp:retry-backoff) · [Moq 替身](kp:moq) · [xUnit 单元测试](kp:unit-test) · [Task/async](kp:taskrun)

## 🛠️ 参考实现

### ⓪ 装包

```bash
dotnet add src/DaqMonitor.Core package Microsoft.Extensions.DependencyInjection --version 8.0.1
dotnet add src/DaqMonitor.Tests package Moq --version 4.20.72
dotnet add src/DaqMonitor.Tests package Microsoft.Extensions.DependencyInjection --version 8.0.1
```

### ① Retry —— 指数退避重试

> 📂 `src/DaqMonitor.Core/Resilience/Retry.cs` · namespace `DaqMonitor.Core.Resilience`
> 🔧 无 NuGet
> 💡 面试常问"通信断了怎么办"——答案:重试+退避+超时+重连,不是裸 try-catch

```csharp
namespace DaqMonitor.Core.Resilience;

/// <summary>
/// 生产级重试:指数退避 + 随机抖动。无需 Polly,手撸即可。
/// 用途:串口 / Modbus / PLC / 网络 通信偶发失败,不应直接抛给用户,应重试。
/// </summary>
public static class Retry
{
    /// <summary>无返回值的重试。maxRetries=3 表示最多试 4 次(首试 + 3 次重试)。</summary>
    public static async Task ExecuteAsync(Func<Task> action, int maxRetries = 3, int baseDelayMs = 200, CancellationToken ct = default)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception) when (attempt < maxRetries && !ct.IsCancellationRequested)
            {
                attempt++;
                var delay = (int)(baseDelayMs * Math.Pow(2, attempt - 1)) + Random.Shared.Next(0, baseDelayMs);
                await Task.Delay(delay, ct);
            }
        }
    }

    /// <summary>带返回值的重试。</summary>
    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> action, int maxRetries = 3, int baseDelayMs = 200, CancellationToken ct = default)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                return await action();
            }
            catch (Exception) when (attempt < maxRetries && !ct.IsCancellationRequested)
            {
                attempt++;
                var delay = (int)(baseDelayMs * Math.Pow(2, attempt - 1)) + Random.Shared.Next(0, baseDelayMs);
                await Task.Delay(delay, ct);
            }
        }
    }
}
```

### ② DeviceHealthMonitor —— 心跳探活 + 自动重连

> 📂 `src/DaqMonitor.Core/Health/DeviceHealthMonitor.cs` · namespace `DaqMonitor.Core.Health`
> 💡 解决"IsConnected 不可信、设备悄悄掉线、数据突然不动"——工业现场真实痛点;测试用 `TickOnceAsync` 单步驱动,不真等定时器

```csharp
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Resilience;

namespace DaqMonitor.Core.Health;

/// <summary>
/// 设备健康监测:周期性发心跳探活,连续多次超时判掉线 → 指数退避重连;设备恢复正常后自动回 Online。
///
/// 设计要点:
///   - 探活动作 heartbeat 由外部注入(读一个寄存器 / 发心跳包 / Ping),不直接耦合具体协议;
///   - 重连复用已有的 Retry(指数退避 + 随机抖动),不重复造轮子;
///   - 状态变化通过 StateChanged 广播,UI / 日志可订阅;
///   - 全程可单测:heartbeat 用 delegate 控制、IDevice 用替身,无需真实硬件。
///
/// 用法(通常在组合根里包住设备):
///   var dev = new CanDevice(2, "CAN", new SimulatedCanChannel());
///   var health = new DeviceHealthMonitor(dev, heartbeat: () => Task.Run(() => dev.Read(1)),
///                                        heartbeatIntervalMs: 5000, missThreshold: 2,
///                                        log: m => Console.WriteLine("[health] " + m));
///   health.Start();   // 后台每 5s 探活一次
/// </summary>
public sealed class DeviceHealthMonitor : IDisposable
{
    private readonly IDevice _device;
    private readonly Func<Task> _heartbeat;
    private readonly int _intervalMs;
    private readonly int _missThreshold;
    private readonly Action<string>? _log;
    private readonly CancellationTokenSource _cts = new();
    private int _consecutiveMisses;
    private bool _running;

    /// <summary>状态变化通知:掉线时发 Offline,重连成功发 Online。</summary>
    public event Action<DeviceState>? StateChanged;

    public DeviceHealthMonitor(IDevice device, Func<Task> heartbeat,
        int heartbeatIntervalMs = 5000, int missThreshold = 2, Action<string>? log = null)
    {
        _device = device;
        _heartbeat = heartbeat;
        _intervalMs = heartbeatIntervalMs;
        _missThreshold = missThreshold;
        _log = log;
    }

    /// <summary>启动后台心跳循环(真实运行调用)。测试请用 TickOnceAsync 单步验证。</summary>
    public void Start()
    {
        if (_running) return;
        _running = true;
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try { await Task.Delay(_intervalMs, _cts.Token); }
                catch (OperationCanceledException) { break; }
                if (_cts.IsCancellationRequested) break;
                try { await TickOnceAsync(_cts.Token); }
                catch (OperationCanceledException) { break; }
                catch { /* 单步异常不终止循环 */ }
            }
        });
    }

    /// <summary>执行一次探活 + 掉线判定 + 重连(可单测入口)。</summary>
    public async Task TickOnceAsync(CancellationToken ct = default)
    {
        bool alive = await ProbeAsync(ct);

        if (alive)
        {
            _consecutiveMisses = 0;
            if (_device.State == DeviceState.Offline)
            {
                // 心跳已恢复但链路还断着 → 重连
                await ReconnectAsync(ct);
            }
            return;
        }

        _consecutiveMisses++;
        if (_consecutiveMisses >= _missThreshold && _device.State == DeviceState.Online)
        {
            // 判掉线:用 Disconnect 把状态切到 Offline(DeviceBase 内部置位)
            _device.Disconnect();
            _log?.Invoke("连续心跳超时,判定掉线");
            StateChanged?.Invoke(DeviceState.Offline);
        }
    }

    private async Task<bool> ProbeAsync(CancellationToken ct)
    {
        try
        {
            // maxRetries:1 即"探一次,失败再试一次就放弃",模拟一次心跳往返
            await Retry.ExecuteAsync(_heartbeat, maxRetries: 1, baseDelayMs: 0, ct: ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ReconnectAsync(CancellationToken ct)
    {
        _log?.Invoke("开始指数退避重连...");
        try
        {
            await Retry.ExecuteAsync(() => Task.Run(() => _device.Connect()),
                maxRetries: 5, baseDelayMs: 500, ct: ct);
            _consecutiveMisses = 0;
            _log?.Invoke("重连成功");
            StateChanged?.Invoke(DeviceState.Online);
        }
        catch
        {
            _log?.Invoke("重连失败,等待下次心跳");
        }
    }

    public void Dispose() => _cts.Cancel();
}
```

### ③ DiagnosticsService —— 指标 + 环形日志

> 📂 `src/DaqMonitor.Core/Diagnostics/DiagnosticsService.cs` · namespace `DaqMonitor.Core.Diagnostics`
> 💡 工业现场 80% 的时间在排查"为什么没数据"——指标和日志是第一手证据;放 Core 与 UI 无关,R8 的诊断面板直接绑它

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace DaqMonitor.Core.Diagnostics;

/// <summary>
/// 诊断 / 调试服务(放在 Core,与 UI 无关,纯逻辑)。
///
/// 设计要点:
/// ① 全部用 lock 保护计数,线程安全(后台采集线程 / UI 线程都会调);
/// ② 日志用环形缓冲(最多 200 条),避免内存无限增长;
/// ③ 暴露 ReadOnlyObservableCollection,UI 直接绑。
/// </summary>
public class DiagnosticsService
{
    private readonly object _gate = new();
    private int _totalSamples;
    private int _alarmCount;
    private int _batchCount;
    private long _lastBatchMs;
    private readonly DateTime _startTime = DateTime.Now;
    private readonly ObservableCollection<string> _log = new();
    private const int MaxLog = 200;

    /// <summary>累计采样点数(每批累加)。</summary>
    public int TotalSamples => _totalSamples;
    /// <summary>累计报警触发次数(上升沿计)。</summary>
    public int AlarmCount => _alarmCount;
    /// <summary>累计批量次数。</summary>
    public int BatchCount => _batchCount;
    /// <summary>最近一批的处理耗时(毫秒),排查"卡顿/丢点"看它。</summary>
    public long LastBatchMs => _lastBatchMs;
    /// <summary>已运行时长。</summary>
    public TimeSpan Uptime => DateTime.Now - _startTime;
    /// <summary>对外只读日志视图,UI 直接绑。</summary>
    public ReadOnlyObservableCollection<string> Log { get; }

    public DiagnosticsService() => Log = new ReadOnlyObservableCollection<string>(_log);

    /// <summary>记录一次批量采集:累加点数/批次数,并写一条 INFO 日志。</summary>
    public void RecordBatch(int sampleCount, long elapsedMs)
    {
        lock (_gate)
        {
            _totalSamples += sampleCount;
            _batchCount++;
            _lastBatchMs = elapsedMs;
        }
        Append("INFO", $"批量 #{_batchCount}: {sampleCount} 点, 耗时 {elapsedMs}ms");
    }

    /// <summary>记录一次报警触发(上升沿)。</summary>
    public void RecordAlarm(int pointId, string level, double value)
    {
        lock (_gate) _alarmCount++;
        Append("WARN", $"报警 点位{pointId} → {level}, 值={value}");
    }

    /// <summary>通用 INFO 记录(如设备连接/断开)。</summary>
    public void RecordInfo(string message) => Append("INFO", message);

    /// <summary>通用 WARN 记录(如重连/异常)。</summary>
    public void RecordWarn(string message) => Append("WARN", message);

    private void Append(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {level} {message}";
        lock (_gate)
        {
            // 新日志插到头部(最新在上);超出上限从尾部丢弃
            _log.Insert(0, line);
            while (_log.Count > MaxLog) _log.RemoveAt(_log.Count - 1);
        }
    }
}
```

### ④ Bootstrapper —— 组合根(R7 版)

> 📂 `src/DaqMonitor.Core/AppServices/Bootstrapper.cs`
> 💡 参考工程同名文件还注册认证/配方/运控/MQTT(R9+ 内容)并种子账号配方——**R7 版先删掉这些,R9+ 做到那篇时按参考工程加回**;注释里的"换一行接真设备"示例全保留,这是可插拔的证据

```csharp
using DaqMonitor.Core.Acquisition;
using DaqMonitor.Core.Alarms;
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Diagnostics;
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DaqMonitor.Core.AppServices;

/// <summary>
/// 组合根(Composition Root):用 Microsoft.Extensions.DependencyInjection 把 Core 服务串起来。
/// 放在 Core 而不是 UI,是因为它是"整个应用的装配说明书"——UI、测试、未来服务都能复用同一套装配。
///
/// 用法:
///   using var provider = Bootstrapper.Build();
///   var pipeline = provider.GetRequiredService<AcquisitionPipeline>();
///   var device   = provider.GetRequiredService<IDevice>();   // 当前是 SimulatedDevice
///   pipeline.Register(device); device.Connect();
/// </summary>
public static class Bootstrapper
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        // 持久化:SQLite + EF Core。工厂模式便于查询 / 写入各自取短生命周期 DbContext。
        // 数据库文件放 LocalApplicationData(用户可写、随用户隔离、不会被卸载清理)。
        var dbPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DaqMonitor", "daq.db");
        var dbDir = System.IO.Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dbDir)) System.IO.Directory.CreateDirectory(dbDir);

        services.AddDbContextFactory<AppDb>(opt =>
            opt.UseSqlite($"Data Source={dbPath}"));

        // 单例:全局共享一份存储与报警引擎
        services.AddSingleton<PointStore>();
        services.AddSingleton<AlarmEngine>();
        // 诊断/调试服务:采集统计 + 结构化日志,UI 的诊断面板直接绑它
        services.AddSingleton<DiagnosticsService>();

        // 管道:定时 200ms 批量出队(统一采集架构)
        services.AddSingleton<AcquisitionPipeline>(_ => new AcquisitionPipeline(TimeSpan.FromMilliseconds(200)));

        // 设备:当前用模拟设备,没有真实硬件也能跑通整条链路。
        // 接真实硬件时只需在这里换实现,UI 与采集层一行不改(面向接口编程的胜利):
        //   services.AddSingleton<IDevice>(_ => new SerialDevice(1, "SER", new RealSerialChannel("COM3", 9600)));
        //   services.AddSingleton<IDevice>(_ => new CanDevice(2, "CAN-01", new SimulatedCanChannel()));
        //   services.AddSingleton<IDevice>(_ => new ModbusDevice(1, "MB-01", slave: 1,
        //       new[] { new ModbusDevice.RegisterMap(1, 0, "float",
        //                    ModbusFrameParser.ByteOrder.CDAB) }, simulate: false, portName: "COM3"));
        //   services.AddSingleton<IDevice>(_ => new PlcDevice(2, "PLC-01",
        //       new[] { new PlcDevice.PlcMap(3, "DB1.DBW0") }, simulate: true));
        services.AddSingleton<IDevice>(_ => new SimulatedDevice(1, "Sim-01", 1, 2, 3));

        var provider = services.BuildServiceProvider();

        // 启动期一次性建库建表(用 EnsureCreated:简单,不依赖迁移;首次运行后生成 daq.db)。
        using (var scope = provider.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDb>>();
            using var db = factory.CreateDbContext();
            db.Database.EnsureCreated();
        }

        // 预置两条报警规则:点位 1 超 100 判 Critical、点位 2 超 100 判 Warning(带回滞 2,防抖动)
        var alarms = provider.GetRequiredService<AlarmEngine>();
        alarms.Add(new AlarmRule { PointId = 1, Threshold = 100, IsHigh = true, Level = AlarmLevel.Critical, Hysteresis = 2 });
        alarms.Add(new AlarmRule { PointId = 2, Threshold = 100, IsHigh = true, Level = AlarmLevel.Warning, Hysteresis = 2 });

        return provider;
    }
}
```

### ⑤ 四个测试文件(6 个测试)

> 📂 `src/DaqMonitor.Tests/RetryTests.cs`

```csharp
using DaqMonitor.Core.Resilience;
using Xunit;

namespace DaqMonitor.Tests;

public class RetryTests
{
    [Fact]
    public async Task ExecuteAsync_RetriesThenSucceeds()
    {
        int calls = 0;
        await Retry.ExecuteAsync(async () =>
        {
            calls++;
            if (calls < 3) throw new InvalidOperationException("transient");
            await Task.Yield();
        }, maxRetries: 5);

        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsAfterExhaustingRetries()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Retry.ExecuteAsync(async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException();
            }, maxRetries: 2));
    }
}
```

> 📂 `src/DaqMonitor.Tests/DeviceHealthMonitorTests.cs`(R4 挪来的)

```csharp
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Health;
using DaqMonitor.Core.Models;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 验证 DeviceHealthMonitor:用替身 IDevice + 可控心跳,
/// 证明"连续超时判掉线 → 链路恢复自动重连回 Online"这套组合拳成立,且全单测、零硬件。
/// </summary>
public class DeviceHealthMonitorTests
{
    /// <summary>最小可控设备替身:Connect→Online,Disconnect→Offline,状态真实切换。</summary>
    private sealed class FakeDevice : IDevice
    {
        private DeviceState _state = DeviceState.Online;
        public int Id => 1;
        public string Name => "fake";
        public DeviceState State => _state;
#pragma warning disable CS0067
        public event EventHandler<DataEventArgs>? DataReceived;
#pragma warning restore CS0067
        public void Connect() => _state = DeviceState.Online;
        public void Disconnect() => _state = DeviceState.Offline;
        public double Read(int addr) => 0;
        public void Write(int addr, double v) { }
    }

    [Fact]
    public async Task Drops_Offline_After_MissThreshold_Then_Reconnects_When_Recovered()
    {
        var reachable = false;
        Func<Task> heartbeat = () =>
        {
            if (!reachable) throw new InvalidOperationException("link down");
            return Task.CompletedTask;
        };

        var dev = new FakeDevice();
        var states = new List<DeviceState>();
        var monitor = new DeviceHealthMonitor(dev, heartbeat, heartbeatIntervalMs: 5000, missThreshold: 2);
        monitor.StateChanged += s => states.Add(s);

        // 1) 连续 2 次探活失败(阈值=2)→ 判掉线
        await monitor.TickOnceAsync();
        await monitor.TickOnceAsync();
        Assert.Contains(DeviceState.Offline, states);
        Assert.Equal(DeviceState.Offline, dev.State);

        // 2) 链路恢复 → 自动重连回 Online
        reachable = true;
        await monitor.TickOnceAsync();
        Assert.Contains(DeviceState.Online, states);
        Assert.Equal(DeviceState.Online, dev.State);
    }

    [Fact]
    public async Task No_Drop_Before_Threshold()
    {
        var reachable = false;
        Func<Task> heartbeat = () =>
        {
            if (!reachable) throw new InvalidOperationException();
            return Task.CompletedTask;
        };

        var dev = new FakeDevice();
        var states = new List<DeviceState>();
        var monitor = new DeviceHealthMonitor(dev, heartbeat, missThreshold: 3);
        monitor.StateChanged += s => states.Add(s);

        await monitor.TickOnceAsync();   // 仅 1 次未达阈值,不应掉线
        Assert.Empty(states);
        Assert.Equal(DeviceState.Online, dev.State);
    }
}
```

> 📂 `src/DaqMonitor.Tests/DeviceMoqTests.cs`(R2 挪来的——那时还没有管道)

```csharp
using DaqMonitor.Core.Acquisition;
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Models;
using Moq;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 用 Moq 造"虚拟设备",验证统一采集管道能从任意 IDevice 收到数据。
/// 单元测试铁律:不碰真实串口/PLC/MQTT 等外部依赖,全部用 Mock 替身。
/// </summary>
public class DeviceMoqTests
{
    [Fact]
    public async Task Pipeline_ReceivesData_FromMockedDevice()
    {
        var device = new Mock<IDevice>();
        device.Setup(d => d.Id).Returns(1);
        device.Setup(d => d.Name).Returns("mock");

        using var pipeline = new AcquisitionPipeline(TimeSpan.FromMilliseconds(50));
        var done = new TaskCompletionSource();
        var received = new List<SensorPoint>();
        pipeline.BatchReady += (s, batch) =>
        {
            received.AddRange(batch);
            if (received.Count >= 1) done.TrySetResult();
        };

        pipeline.Register(device.Object);
        // 用 Moq 的 Raise 模拟"设备收到一帧数据并触发事件"
        device.Raise(d => d.DataReceived += null,
            new DataEventArgs { PointId = 7, Value = 42, Timestamp = DateTime.Now });

        await Task.WhenAny(done.Task, Task.Delay(2000));
        Assert.Single(received);
        Assert.Equal(7, received[0].Id);
        Assert.Equal(42, received[0].Value);
    }
}
```

> 📂 `src/DaqMonitor.Tests/CompositionSmokeTests.cs`

```csharp
using DaqMonitor.Core.Acquisition;
using DaqMonitor.Core.AppServices;
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 无界面集成冒烟测试:证明"企业级项目能跑起来"不靠肉眼看窗口。
/// 直接用组合根 Bootstrapper 装配整套服务,跑真实采集链路,断言有数据产出。
/// </summary>
public class CompositionSmokeTests
{
    [Fact]
    public async Task Bootstrapper_Wires_Device_Pipeline_Store_And_Produces_Points()
    {
        using var provider = Bootstrapper.Build();
        var device = provider.GetRequiredService<IDevice>();
        var pipeline = provider.GetRequiredService<AcquisitionPipeline>();
        var store = provider.GetRequiredService<PointStore>();

        pipeline.Register(device);
        device.Connect();

        // 测试在这里扮演"消费者"角色(真实项目里是 ViewModel/历史库订阅 BatchReady)
        var gotBatch = new TaskCompletionSource<bool>();
        var received = new List<SensorPoint>();
        var gate = new object();
        pipeline.BatchReady += (_, batch) =>
        {
            lock (gate)
            {
                foreach (var p in batch) { store.AddOrUpdate(p); received.Add(p); }
                if (batch.Count > 0) gotBatch.TrySetResult(true);
            }
        };

        // 启动模拟设备(真实设备同理,只是数据来源不同)
        ((SimulatedDevice)device).Start(TimeSpan.FromMilliseconds(20));

        // 等最多 3 秒,必须收到至少一个批次
        var completed = await Task.WhenAny(gotBatch.Task, Task.Delay(3000));
        ((SimulatedDevice)device).Stop();

        Assert.True(completed == gotBatch.Task, "管道在 3 秒内未产出任何批次——采集链路未跑通");
        Assert.NotEmpty(received);
        Assert.NotEmpty(store.GetAll());
        Assert.All(received, p => Assert.True(p.Timestamp > DateTime.MinValue));
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
已通过! - 失败: 0,通过: 56 ... DaqMonitor.Tests.dll
```
(56 = 之前 50 + 本篇 6)

## ✅ 验收清单

- [ ] build 0 错 0 警,test 56/56 绿
- [ ] 能回答:组合根模式比"哪里需要哪里 new"好在哪?(依赖集中可查、生命周期统一管理、换实现一处改)
- [ ] 能回答:指数退避的 delay 公式 `base * 2^(n-1) + rand(0,base)` 两段各起什么作用?(指数拉开间隔 / 抖动打散并发)
- [ ] 能回答:DeviceHealthMonitor 判掉线为什么用 `Disconnect()` 而不是直接改状态?(状态是 protected,走接口语义;顺带触发设备自己的清理逻辑)
- [ ] 打开 `%LocalAppData%\DaqMonitor\daq.db` 所在目录能看到冒烟测试生成的数据库文件
- [ ] git commit -m "R7: DI组合根+诊断+重试+健康监测+冒烟测试"

## 🎤 面试怎么讲这一篇

> "组装用 Microsoft.Extensions.DependencyInjection,组合根 Bootstrapper 放在 Core 层——它是装配说明书,UI、测试、以后做成 Windows 服务都复用同一套注册,换设备只改组合根一行。容错两个件:Retry 是手写的指数退避加随机抖动,不引 Polly,因为逻辑就二十行;DeviceHealthMonitor 用委托注入心跳动作,连续 N 次探活失败判掉线,恢复后自动指数退避重连,单测用 TickOnceAsync 单步驱动,不用真等定时器。诊断服务做线程安全计数和两百条环形日志。最后一条冒烟测试从容器装配出设备、管道、存储跑通全链路——回归时一条测试就知道装配有没有被我改坏。"

**✅ 打卡[ ]**
