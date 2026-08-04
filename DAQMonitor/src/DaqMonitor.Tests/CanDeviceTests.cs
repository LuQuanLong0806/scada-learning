using DaqMonitor.Core.Devices;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 验证 M16 的 CAN 设备：用 SimulatedCanChannel（内存模拟，零硬件）喂温度帧，
/// 断言 CanDevice 把 ID=0x100 的帧解码成 point1 = 25.0℃，且不认其它 ID 的帧。
/// </summary>
public class CanDeviceTests
{
    [Fact]
    public void Decodes_TempFrame_To_25C()
    {
        var ch = new SimulatedCanChannel();
        var dev = new CanDevice(1, "CAN", ch);
        var got = new List<(int, double)>();
        dev.DataReceived += (_, e) => got.Add((e.PointId, e.Value));

        dev.Connect();
        Thread.Sleep(150);
        dev.Disconnect();

        Assert.Contains(got, x => x.Item1 == 1 && Math.Abs(x.Item2 - 25.0) < 1e-6);
    }

    /// <summary>非 0x100 的帧（如 0x999）应被忽略，不污染业务。</summary>
    private sealed class OtherIdChannel : ICanChannel
    {
        public event Action<ulong, byte[]>? FrameReceived;
        public bool IsOpen { get; private set; }
        public void Open() { IsOpen = true; FrameReceived?.Invoke(0x999, new byte[] { 0x00, 0xFA }); }
        public void Send(ulong id, byte[] d) { }
        public void Close() { IsOpen = false; }
    }

    [Fact]
    public void Ignores_NonTempId_Frames()
    {
        var dev = new CanDevice(1, "CAN", new OtherIdChannel());
        int count = 0;
        dev.DataReceived += (_, e) => count++;
        dev.Connect();
        Thread.Sleep(100);
        dev.Disconnect();
        Assert.Equal(0, count);
    }
}
