using DaqMonitor.Core.Models;
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Resilience;

namespace DaqMonitor.Core.Health;

/// <summary>
/// 设备健康监测（M9 容错 + M15 联调的“边学边做”落地）：
/// 周期性发<b>心跳探活</b>，连续多次超时判<b>掉线</b> → <b>指数退避重连</b>；设备恢复正常后自动回 Online。
///
/// 解决工业现场真实痛点：“IsConnected 不可信、设备悄悄掉线、数据突然不动了”。
/// 设计要点：
///   - 探活动作 <paramref name="heartbeat"/> 由外部注入（读一个寄存器 / 发心跳包 / Ping），不直接耦合具体协议；
///   - 重连复用已有的 <see cref="Retry"/>（指数退避 + 随机抖动），不重复造轮子；
///   - 状态变化通过 <see cref="StateChanged"/> 广播，UI / 日志（M6 Serilog）可订阅；
///   - 全程可单测：heartbeat 用 delegate 控制、IDevice 用替身，无需真实硬件。
///
/// 用法（通常在组合根里包住设备）：
///   var dev = new CanDevice(2, "CAN", new SimulatedCanChannel());
///   var health = new DeviceHealthMonitor(dev, heartbeat: () =&gt; Task.Run(() =&gt; dev.Read(1)),
///                                        heartbeatIntervalMs: 5000, missThreshold: 2,
///                                        log: m =&gt; Console.WriteLine("[health] " + m));
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

    /// <summary>状态变化通知：掉线时发 Offline，重连成功发 Online。</summary>
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

    /// <summary>启动后台心跳循环（真实运行调用）。测试请用 <see cref="TickOnceAsync"/> 单步验证。</summary>
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

    /// <summary>执行一次探活 + 掉线判定 + 重连（可单测入口）。</summary>
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
            // 判掉线：用 Disconnect 把状态切到 Offline（DeviceBase 内部置位）
            _device.Disconnect();
            _log?.Invoke("连续心跳超时，判定掉线");
            StateChanged?.Invoke(DeviceState.Offline);
        }
    }

    private async Task<bool> ProbeAsync(CancellationToken ct)
    {
        try
        {
            // maxRetries:1 即“探一次，失败再试一次就放弃”，模拟一次心跳往返
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
            _log?.Invoke("重连失败，等待下次心跳");
        }
    }

    public void Dispose() => _cts.Cancel();
}
