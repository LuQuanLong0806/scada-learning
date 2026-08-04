using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Health;
using DaqMonitor.Core.Models;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 验证 M9 容错 + M15 联调落地的 DeviceHealthMonitor：用替身 IDevice + 可控心跳，
/// 证明“连续超时判掉线 → 链路恢复自动重连回 Online”这套组合拳成立，且全单测、零硬件。
/// </summary>
public class DeviceHealthMonitorTests
{
    /// <summary>最小可控设备替身：Connect→Online，Disconnect→Offline，状态真实切换。</summary>
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

        // 1) 连续 2 次探活失败（阈值=2）→ 判掉线
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

        await monitor.TickOnceAsync();   // 仅 1 次未达阈值，不应掉线
        Assert.Empty(states);
        Assert.Equal(DeviceState.Online, dev.State);
    }
}
