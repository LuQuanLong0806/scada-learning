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
> 🗺️ **新手读码地图**(10 行代码两个亮点):1. `catch ... when (attempt < maxRetries ...)`:异常过滤器——次数没用完才吞掉异常进重试,用完了让异常正常抛出去给调用方。这比 try-catch 里 if-else 干净得多 2. 延迟公式 `base * 2^(n-1) + 随机抖动`:第 1/2/3 次重试分别约 200/400/800ms——翻倍是**指数退避**(别猛敲刚出故障的设备),再加 0~200ms 随机数是**抖动**(100 个客户端同时失败,不会变成 200ms 后又同时涌回来)。**前端类比**:请求失败自动重连的 axios-retry / react-query retry,默认策略一模一样(指数+抖动),这是业界通用套路不是本项目发明。

**第 1 步 · 无返回值版 ExecuteAsync**(新文件,整段贴)

```csharp
namespace DaqMonitor.Core.Resilience;

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
}
```

📚 **知识点**
- **`catch ... when (条件)` 异常过滤器**:`when` 里条件不成立时,这个 catch **根本不接**,异常继续往上抛——"重试次数没用完才吞,用完了原样抛给调用方"。对比 try-catch 里写 if-else 再手动 `throw;`,过滤器不破坏堆栈、语义直读。
- **延迟公式 `base * 2^(n-1) + rand(0, base)` 两段分工**:前段指数拉开间隔(200/400/800ms——刚出故障的设备需要喘息);后段随机抖动(100 个客户端同时失败,不会在 200ms 后又齐刷刷涌回来打死服务端)。**前端类比**:react-query / axios-retry 的默认重试策略一模一样,业界通用套路。
- **`Random.Shared`**:.NET 6+ 的线程安全随机源,不用自己 new Random(老写法并行时种子相同,随机变假随机)。
- **`Task.Delay(delay, ct)` 带 CancellationToken**:外部要求取消时,等待立刻被打断抛 OperationCanceledException——重试链路必须可取消,否则关软件都要等它睡完。

**第 2 步 · 带返回值版 ExecuteAsync\<T\>**(贴进类里,最后一个 `}` 之前)

```csharp
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
```

📚 **知识点**
- **重载而非泛型默认值**:`ExecuteAsync`(做事)和 `ExecuteAsync<T>`(做事拿结果)两个签名,C# 不允许用默认参数合并。用法:`await Retry.ExecuteAsync(() => port.Write(...))` vs `var v = await Retry.ExecuteAsync(() => port.ReadDouble(...))`。
- **两个方法体几乎逐行相同是"有意的重复"**:抽一个共享核心要引入 `Func<Task<T>>` + object 装箱或泛型基类,20 行的工具库为消重复上设计,得不偿失——**DRY 是原则不是教条**。

<details markdown="1">
<summary>📄 完整文件 Retry.cs(对答案 / 整体粘贴用)</summary>

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

</details>

### ② DeviceHealthMonitor —— 心跳探活 + 自动重连

> 📂 `src/DaqMonitor.Core/Health/DeviceHealthMonitor.cs` · namespace `DaqMonitor.Core.Health`
> 💡 解决"IsConnected 不可信、设备悄悄掉线、数据突然不动"——工业现场真实痛点;测试用 `TickOnceAsync` 单步驱动,不真等定时器

**第 1 步 · 骨架:字段 + 事件 + 构造 + Dispose**(新文件,整段贴)

```csharp
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Resilience;

namespace DaqMonitor.Core.Health;

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

    public void Dispose() => _cts.Cancel();
}
```

📚 **知识点**
- **心跳是注入的 `Func<Task>`,不是写死的"读寄存器"**:串口设备探活 = 发一帧等回包,PLC = 读 DB 块,Ping = ICMP——探活方式千人千面,这个类只管"**什么时候探、探失败了怎么办**",动作本身交给调用方。**前端类比**:组件不写死 fetch 地址,只暴露一个 prop 回调。
- **`event Action<DeviceState>?`**:状态机每次跳变(Online↔Offline)对外广播,UI 红绿灯、日志、短信网关都能订阅——和 R2 `DataReceived` 同一套事件机制,只是载荷从数据变成了状态。
- **`CancellationTokenSource _cts = new()` + `Dispose() => _cts.Cancel()`**:最简停机档——Dispose 时取消令牌,后台循环(第 4 步)在下一个 `Task.Delay` 处感知退出。一行退场,先立好。

**第 2 步 · 两个私家工人:ProbeAsync + ReconnectAsync**(贴进类里,最后一个 `}` 之前)

```csharp
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
```

📚 **知识点**
- **ProbeAsync 把异常翻译成布尔**:`Retry.ExecuteAsync` 耗尽重试会抛,这里 catch 住返回 false——上层(TickOnceAsync)只关心"活/没活",不关心异常细节。**异常在边界处转成语义值**,内部逻辑就不用层层 try。
- **`maxRetries: 1, baseDelayMs: 0`**:探活只要"一次往返",失败再补一发就定性;delay 0 表示补发不等待——探活要快,重连才要慢。**同一个工具类,两种参数两种性格**。
- **ReconnectAsync 的参数正相反**:`maxRetries: 5, baseDelayMs: 500` → 500/1000/2000/4000/8000ms 退避——链路刚恢复通常不稳,猛连会再次压垮。失败后不打崩:catch 里记一句"等待下次心跳",下个 Tick 重来。
- **两个方法都先于 TickOnceAsync 存在**:C# 类内方法互相调用不看定义顺序(和 C++ 不同),但"工人先招好、调度后上岗"的贴法保证你**每一步贴完都能编译**。

**第 3 步 · 大脑:TickOnceAsync 单步调度**(继续贴进类里)

```csharp
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
```

📚 **知识点**
- **为什么公开 TickOnceAsync**:把"一次判定"从定时器里剥出来,测试就能**单步驱动**——`await monitor.TickOnceAsync()` 两次就模拟两次心跳失败,不用真等 10 秒定时器。**可测性设计**:时钟是依赖,能注入/绕开的依赖才测得动。**前端类比**:把 `setInterval` 回调抽成独立函数,jit 测试直接调函数不等 interval。
- **`_consecutiveMisses` 连击计数**:成功一次就清零(`= 0`),失败累加,`>= threshold` 才动手——单次抖动(电网打嗝、GC 停顿)不误杀,连续失联才判死。工业现场误报掉线比漏报更烦人。
- **判掉线走 `_device.Disconnect()` 不直接改状态**:`State` 在 DeviceBase 里是 protected,外部改不了也不该改——走接口方法顺带触发设备自己的清理逻辑(关串口、停定时器)。**别绕过对象自己的生命周期**。
- **`StateChanged?.Invoke(...)`**:订阅者可能为空,`?.` 空传播调用是事件广播的标准写法,R2 讲过,这里再加深一遍肌肉。

**第 4 步 · 点火:Start 后台循环**(继续贴进类里)

```csharp
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
```

📚 **知识点**
- **`if (_running) return` 幂等启动**:Start 被手抖调两次,不会起两条循环——后台服务的标配护栏。
- **`_ = Task.Run(...)` 弃元**:丢弃返回的 Task = "fire-and-forget,我不等它"。循环内部自己兜异常,不会出现未观察的异常炸进程。和 R5 管道 `_ = ConsumeAsync()`、R6 写泵同一个姿势——**后台常驻任务的三大件都这么起**。
- **循环体三段式:睡 → 醒 → 干**:先 `Task.Delay` 等一个心跳周期(取消则 break),再执行一次 Tick(取消 break,**其他异常吞掉继续**)——监测器自己不能因为一次意外先死掉,它死了谁盯着设备?

<details markdown="1">
<summary>📄 完整文件 DeviceHealthMonitor.cs(对答案 / 整体粘贴用)</summary>

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

</details>

### ③ DiagnosticsService —— 指标 + 环形日志

> 📂 `src/DaqMonitor.Core/Diagnostics/DiagnosticsService.cs` · namespace `DaqMonitor.Core.Diagnostics`
> 💡 工业现场 80% 的时间在排查"为什么没数据"——指标和日志是第一手证据;放 Core 与 UI 无关,R8 的诊断面板直接绑它

**第 1 步 · 骨架:字段 + 七个只读属性 + 构造**(新文件,整段贴)

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace DaqMonitor.Core.Diagnostics;

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
    private readonly SynchronizationContext? _uiCtx;

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

    public DiagnosticsService()
    {
        // 坑⑤修复:捕获构造时的同步上下文。WPF 里在 UI 线程构造(Bootstrapper 在 OnStartup 里跑)
        // => 之后任何后台线程 Append 都自动投递回 UI 线程,不再抛跨线程集合异常。
        // 参考工程原文没有这层(它从没真正跑过界面),照抄会在 R8 点「启动采集」的第一批数据就崩。
        _uiCtx = SynchronizationContext.Current;
        Log = new ReadOnlyObservableCollection<string>(_log);
    }
}
```

📚 **知识点**
- **七个属性全是只读视图**:`=> _totalSamples` 表达式体,外界只能看不能改——写入只走 Record 方法(下一步)。计数器像 React 的 state:改它的唯一入口是专门的动作函数。
- **`ReadOnlyObservableCollection` 包一层再暴露**:`_log` 内部可变(服务自己写),`Log` 对外只读但**保留变更通知**——UI 绑 `Log` 后,内部 Insert/Remove 会自动触发界面刷新。这是 WPF 的"受控暴露"标准姿势。
- **`SynchronizationContext.Current` 在构造时抓快照**:坑⑤的核心。WPF 里谁在 UI 线程构造,就把 UI 线程的"邮局地址"存下来;之后任何后台线程写日志,都能按这个地址把操作**投递回 UI 线程**执行。**前端类比**:提前拿到 `postMessage` 的 target,Worker 里算完数据寄回主线程。
- **`MaxLog = 200` 环形上限**:日志无限增长 = 内存慢性泄漏——挂机一个月的采集站,一个 List 能吃几个 GB。常量放字段旁,一眼看到"这个服务有边界"。

**第 2 步 · Append:日志发动机(含跨线程投递)**(贴进类里,最后一个 `}` 之前)

```csharp
    private void Append(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {level} {message}";
        // 坑⑤:_log 被 UI 的诊断面板绑定,非 UI 线程直接改会抛
        // NotSupportedException("CollectionView 不支持从不同线程更改 SourceCollection")。
        // lock 只保证线程互斥,不解决"改绑定了 UI 的集合必须在其线程上"——这是两码事。
        if (_uiCtx is not null && SynchronizationContext.Current != _uiCtx)
        {
            _uiCtx.Post(_ => InsertLine(), null);
            return;
        }
        InsertLine();

        void InsertLine()
        {
            lock (_gate)
            {
                // 新日志插到头部(最新在上);超出上限从尾部丢弃
                _log.Insert(0, line);
                while (_log.Count > MaxLog) _log.RemoveAt(_log.Count - 1);
            }
        }
    }
```

📚 **知识点**
- **`lock` 和"UI 线程"是两码事**:lock 保证同一时刻只有一个线程进代码块(互斥);但 WPF 的 ObservableCollection 被 UI 绑定后,**必须由 UI 线程本人来改**——你锁得再严,工作线程改它照样抛异常。这一坑前端没有对应物,是 WPF 三大跨线程坑里最阴的一个(另两个在 R8)。
- **`_uiCtx.Post(...)` = 邮局寄信**:`Post` 把一个委托排进 UI 线程的消息队列(异步,不等执行完);对比 `Send`(同步堵到执行完,死锁风险)。**前端类比**:`Post` ≈ `queueMicrotask`/`setTimeout(...,0)`,`Send` ≈ 同步阻塞调用。
- **`Insert(0, line)` 头插 + 尾部淘汰**:最新日志永远在第 0 行,UI 不用滚动就能看到;超过 200 条从尾部 RemoveAt——环形缓冲的列表实现。**前端类比**:聊天窗永远 push 到底部 + 虚拟列表只留可视区。
- **局部函数 `void InsertLine()`**:定义在方法体内,两个分支(Post 回调/直呼)复用同一段插入逻辑,且能闭包捕获 `line`。C# 局部函数比 lambda 干净(不产生委托分配),方法私有的小工具首选。

**第 3 步 · 四个 Record 方法:对外计数入口**(继续贴进类里)

```csharp
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
```

📚 **知识点**
- **计数在锁内、日志在锁外**:锁只包住三个整数的自增(纳秒级),`Append`(可能跨线程 Post)放锁外——**锁内干得越少,竞争越少**,和 R5"锁内换包、锁外触发"同一条纪律。
- **`RecordBatch` 是管道的搭档**:R8 组装时 `pipeline.BatchReady += (_, b) => diag.RecordBatch(b.Count, elapsed)`——每批到达自动记账,`LastBatchMs` 突然变大就是"处理卡了"的第一现场。
- **两对方法看日志级别**:INFO(正常流水)/ WARN(报警、重连)——级别在行首 `[HH:mm:ss.fff] WARN`,肉眼扫日志时先找 WARN。

<details markdown="1">
<summary>📄 完整文件 DiagnosticsService.cs(对答案 / 整体粘贴用)</summary>

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

    public DiagnosticsService()
    {
        // 坑⑤修复:捕获构造时的同步上下文。WPF 里在 UI 线程构造(Bootstrapper 在 OnStartup 里跑)
        // => 之后任何后台线程 Append 都自动投递回 UI 线程,不再抛跨线程集合异常。
        // 参考工程原文没有这层(它从没真正跑过界面),照抄会在 R8 点「启动采集」的第一批数据就崩。
        _uiCtx = SynchronizationContext.Current;
        Log = new ReadOnlyObservableCollection<string>(_log);
    }

    private readonly SynchronizationContext? _uiCtx;

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
        // 坑⑤:_log 被 UI 的诊断面板绑定,非 UI 线程直接改会抛
        // NotSupportedException("CollectionView 不支持从不同线程更改 SourceCollection")。
        // lock 只保证线程互斥,不解决"改绑定了 UI 的集合必须在其线程上"——这是两码事。
        if (_uiCtx is not null && SynchronizationContext.Current != _uiCtx)
        {
            _uiCtx.Post(_ => InsertLine(), null);
            return;
        }
        InsertLine();

        void InsertLine()
        {
            lock (_gate)
            {
                // 新日志插到头部(最新在上);超出上限从尾部丢弃
                _log.Insert(0, line);
                while (_log.Count > MaxLog) _log.RemoveAt(_log.Count - 1);
            }
        }
    }
}
```

</details>

### ④ Bootstrapper —— 组合根(R7 版)

> 📂 `src/DaqMonitor.Core/AppServices/Bootstrapper.cs`
> 💡 参考工程同名文件还注册认证/配方/运控/MQTT(R9+ 内容)并种子账号配方——**R7 版先删掉这些,R9+ 做到那篇时按参考工程加回**;注释里的"换一行接真设备"示例全保留,这是可插拔的证据
> 🗺️ **新手读码地图**(这就是全项目的"总装配车间"):1. 前面 R2-R6 造的都是零件(设备/管道/存储/报警/诊断),这个类只干一件事——**把零件按依赖关系拧在一起**:建容器 → 注册每个服务 → Build 出 ServiceProvider,谁要什么自己 `GetRequiredService` 领 2. 三种注册姿势看仔细:`AddDbContextFactory`(工厂,EF 短生命周期专用)、`AddSingleton`(全局一份:存储/报警/诊断/管道——管道注册时顺手 `new AcquisitionPipeline(200ms)`,这就是 R5"构造即启动"的落点)、`AddSingleton<IDevice>(_ => ...)`(**注册的是接口,给的是实现**——最底下那行注释就是"换真设备只改这一行"的实物证据) 3. `EnsureCreated` 启动时建库建表(首次运行生成 daq.db);随后预置两条报警规则——配置也集中在组合根,不散落在代码里 4. 为什么放 Core 不放 UI:测试、未来的无界面服务,都能 `Bootstrapper.Build()` 复用同一套装配。**前端类比**:组合根 ≈ 应用入口的 Provider 装配(store/router/i18n 一次配好)+ NestJS 的 AppModule——依赖注入框架哪个语言都长这样,会一个就都通了。

> ⚠️ **这个文件是"一个静态类 + 一个 Build() 方法"**——方法是原子的,没法"贴一半编译一半"。**先展开文末折叠块把完整文件贴进工程,再按下面 3 步逐段读懂**。贴完这一步,R1-R6 的所有零件就装箱完毕。

**第 1 步 · 读:建容器 + 数据库选址**(对应文件开头到 AddDbContextFactory)

```csharp
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
```

📚 **知识点**
- **`ServiceCollection` = 一个字典**:接口/类型 → "怎么造出实例"的配方。注册阶段只记配方不造东西,**真正 new 发生在第一次被索取时**(懒构造)。
- **数据库放 `LocalApplicationData`**(%LocalAppData%\DaqMonitor\daq.db):Program Files 普通用户没有写权限,安装目录放库文件 = 第一天就崩;用户目录随账户隔离,多账号共用一台机互不干扰。**车间软件的常规选址**,面试官爱问。
- **`AddDbContextFactory<AppDb>`**:`AddSingleton` 的 EF 专用变体——注册的是**工厂**不是 DbContext 本身。DbContext 该短命(一次查询一个),工厂 singleton 常驻;谁要谁 `CreateDbContext()`,用完即弃。R6 的 PointStore 收的就是这个工厂。
- **`Directory.CreateDirectory(dbDir)` 幂等**:目录已存在不报错——首次运行建目录,之后每次直接过。

**第 2 步 · 读:单例注册群 + "换真设备只改一行"**(对应四个 AddSingleton 区块)

```csharp
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
```

📚 **知识点**
- **两种注册写法**:`AddSingleton<PointStore>()`——容器自己 new(构造函数收什么它给什么,R6 的 DI 构造正等着工厂);`AddSingleton<T>(_ => new T(...))`——lambda 手工造,适合要传定制参数的(`AcquisitionPipeline(200ms)`、带地址的设备)。
- **注册接口、给实现**:`AddSingleton<IDevice>(...)` 后,所有索要 `IDevice` 的地方拿到的都是这一份——**UI/管道/测试只知道 IDevice,不知道背后是模拟还是真串口**。注释里那四行就是"换真设备只改一行"的实物证据,面试讲可插拔架构时直接背这段。
- **为什么全是单例**:PointStore 一份(内存索引只有一份才有意义)、AlarmEngine 一份(报警状态 `_active` 不能各持一词)、管道一份(所有设备往同一个 Channel 喂)。**单例不是偷懒,是这些对象的语义本来就是全局**。
- **`SimulatedDevice(1, "Sim-01", 1, 2, 3)`**:设备 1,三个点位(1/2/3)。没有硬件也全链路可跑——培训、演示、自动化测试三用。

**第 3 步 · 读:Build 之后的启动期动作**(对应文件结尾)

```csharp
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
```

📚 **知识点**
- **`BuildServiceProvider()` 才"封箱"**:之后容器不可再注册——配方阶段结束,出品阶段开始。返回的 `ServiceProvider` 由调用方 `using` 包住,释放时所有 singleton 依次 Dispose——PointStore 的泵、管道的 timer 全部优雅退场。
- **建库放在 Build 之后、return 之前**:装配完立刻自检——库建不出来(目录没权限、盘满了)就在启动当场爆,总好过跑半小时采集才发现一条都没存。**fail fast 原则**。
- **`CreateScope()` 包一段式作用域**:EnsureCreated 只需要一个临时 DbContext,用 scope 圈住它的生命周期,出了 using 连同工厂解析出的东西一起收走。
- **预置报警规则 = "配置也进组合根"**:点位 1 超 100 判 Critical、点位 2 超 100 判 Warning,都带回滞 2(R5 的防抖动)——报警阈值这种"现场会调"的参数集中在一处,后续升级成从配置文件/数据库读,也只改这一个文件。**前端类比**:App 根组件统一注入 theme/i18n 配置,不在各组件里散写。

<details markdown="1">
<summary>📄 完整文件 Bootstrapper.cs(先把这个贴进工程,再回头读上面 3 步)</summary>

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

</details>

### ⑤ 四个测试文件(6 个测试)

> 📂 `src/DaqMonitor.Tests/RetryTests.cs`

**第 1 步 · 空测试类 + "失败两次第三次成功"**(新文件,整段贴)

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
}
```

📚 **知识点**
- **闭包计数器 `calls` 是测试的灵魂**:lambda 每被调用一次就 +1,前两次抛异常、第三次放行——精确演出"偶发故障自愈"。断言 `calls == 3` 钉死"首试失败 + 两次重试失败 + 第三次成功"的调用次数。
- **`maxRetries: 5` 故意给富余**:上限 5 但第 3 次就成功,证明**成功即停**、不会傻乎乎把 5 次用完。测试在验证行为边界,不是走形式。
- **`await Task.Yield()`**:让 lambda 真正成为异步状态机(没有 await 的 async lambda 会告警 CS1998)。测试代码也要 0 警告。

**第 2 步 · "重试耗尽抛原异常"**(贴进类里,最后一个 `}` 之前)

```csharp
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
```

📚 **知识点**
- **`Assert.ThrowsAsync<T>` 不只是"期望抛"**:它返回捕获的异常对象,类型不匹配照样红。这里验证 FR7-1 的另一半——**耗尽后抛的是原异常类型**,不是被包装过的 AggregateException(调用方 catch 才能对症)。
- **两个测试合成一对正反面**:正面"会重试到成功",反面"不会无限惯着"。工具类测试的最小完备集。

<details markdown="1">
<summary>📄 完整文件 RetryTests.cs(对答案 / 整体粘贴用)</summary>

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

</details>

> 📂 `src/DaqMonitor.Tests/DeviceHealthMonitorTests.cs`(R4 挪来的)

**第 1 步 · 空测试类 + FakeDevice 替身**(新文件,整段贴)

```csharp
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Health;
using DaqMonitor.Core.Models;
using Xunit;

namespace DaqMonitor.Tests;

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
}
```

📚 **知识点**
- **手写替身 FakeDevice**:R2 用 Moq,这里手写——因为健康监测的关键断言是 `dev.State` **真实切换**(Mock 默认不会改属性,Setup 起来反而啰嗦)。**替身选型**:只要"被调用"用 Moq 快;要"有状态地演"用 Fake 类稳。**前端类比**:msw 拦截请求 vs 手写一个有状态的假 store。
- **`#pragma warning disable CS0067`**:替身永远不触发 DataReceived,编译器警告"事件从未使用"——压掉这一条,其他警告照常。测试代码也要 0 警告,但不用为了安静把整个警告等级关掉。

**第 2 步 · 主测试:掉线 → 恢复 → 自动重连**(贴进类里,最后一个 `}` 之前)

```csharp
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
```

📚 **知识点**
- **`reachable` 布尔是"链路导演"**:外部一个变量控制心跳成败——第 1 段 false(链路断),翻成 true(链路恢复)。**测试想怎么演,替身就怎么配合**,这就是心跳做成委托注入的回报(第②步讲过的设计决策,在这里兑现)。
- **`states` 列表当"事件行车记录仪"**:订阅 StateChanged 把每次跳变按序存下,断言 Contains(Offline) 再 Contains(Online)——既验证事件发过,又隐含验证顺序。**前端类比**:监听 store 变化 push 进数组,断言数组内容。
- **`TickOnceAsync` 手动走表**:两行 await = 两次心跳周期,不用等真 5 秒定时器——第②步"可测性设计"的直接受益。整个测试毫秒级跑完。

**第 3 步 · 反面测试:没到阈值不许掉线**(继续贴进类里)

```csharp
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
```

📚 **知识点**
- **防"太敏感"的反面用例**:阈值 3,只失败 1 次就必须还是 Online、`states` 必须为空——单次网络抖动误杀设备,产线上就是"明明没断报断了"的狼来了。和第 2 步合起来:阈值下不动、阈值上必动,边界两头钉死。

<details markdown="1">
<summary>📄 完整文件 DeviceHealthMonitorTests.cs(对答案 / 整体粘贴用)</summary>

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

</details>

> 📂 `src/DaqMonitor.Tests/DeviceMoqTests.cs`(R2 挪来的——那时还没有管道;单测试小文件,整段贴)

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

📚 **知识点**
- **`Mock<IDevice>` 只 Setup 用到的两个属性**:`Id`/`Name` 之外的成员 Moq 自动给默认值(方法返回 default)。管道 Register 只读这两个,别的不用管——**替身只演剧本需要的戏份**。
- **`device.Raise(d => d.DataReceived += null, args)`**:让 Mock **主动触发事件**,扮演"设备来数据了"。这是 Moq 替身和 FakeDevice 的分工差异:Fake 演状态,Raise 演事件。**前端类比**:Testing Library 的 `fireEvent.click()`——替别人按按钮。
- **整条链路零硬件**:Mock 设备 → 真管道 → BatchReady 收到点数 7/值 42。证明管道**只认 IDevice 接口**,不关心背后是不是真设备——R2 埋的问题"UI 换设备零改动"在管道这层先兑现了一次。

> 📂 `src/DaqMonitor.Tests/CompositionSmokeTests.cs`

**第 1 步 · 空测试类**(新文件,整段贴)

```csharp
using DaqMonitor.Core.Acquisition;
using DaqMonitor.Core.AppServices;
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DaqMonitor.Tests;

public class CompositionSmokeTests
{
}
```

**第 2 步 · 冒烟主测试:装配 → 采集 → 入库一镜到底**(贴进类里,最后一个 `}` 之前)

```csharp
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
```

📚 **知识点**
- **冒烟测试 vs 单元测试**:单元测试验证"每个零件合格"(前 55 个),这一条验证"**装配线能出车**"——直接调 `Bootstrapper.Build()` 真装配真跑。以后任何一次改代码跑全量测试,这条红了就是"装配被我改坏了",一眼定位方向。**前端类比**:单测过了但 build 挂了 / 页面白屏——缺的就是这条 e2e 冒烟。
- **`GetRequiredService` 三个 = 免费的装配自检**:设备/管道/存储都能从容器解析出来,DI 注册这条线就没断——测试还没断言数据,先验了组装。Resolve 失败会直接抛,用例自动红。
- **`((SimulatedDevice)device).Start(...)`:唯一一处"知道自己是谁"**:容器给的是 IDevice,Start/Stop 是 SimulatedDevice 特有的——测试里显式向下转型可以接受(测试本来就知道自己在测模拟设备);**生产代码里这种转型是坏味道**(换真设备就崩),R8 的 ViewModel 会用另一个办法处理。
- **`Assert.All(received, p => p.Timestamp > DateTime.MinValue)`**:时间戳不是默认值——R1 铁律("抄 Timestamp")的最终验收:数据从设备 → 管道 → 存储走完全程,时间戳一路存活。

<details markdown="1">
<summary>📄 完整文件 CompositionSmokeTests.cs(对答案 / 整体粘贴用)</summary>

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

</details>

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
