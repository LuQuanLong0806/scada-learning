using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Protocol;
using Xunit;

namespace DaqMonitor.Tests;

public class TcpFrameParserTests
{
    [Fact]
    public void BuildFrame_RoundTrips_ThroughTryParse()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03 };
        var frame = TcpFrameParser.BuildFrame(payload);

        Assert.True(TcpFrameParser.TryParse(frame, out var got, out int len, out _));
        Assert.Equal(payload, got);
        Assert.Equal(frame.Length, len);
    }

    [Fact]
    public void TryParse_HalfPacket_ReturnsFalse_NoResync()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var frame = TcpFrameParser.BuildFrame(payload);
        // 只给前 5 字节（半包）
        Assert.False(TcpFrameParser.TryParse(frame.AsSpan(0, 5).ToArray(), out _, out _, out bool resync));
        Assert.False(resync);
    }

    [Fact]
    public void TryParse_BadHeader_SignalsResync()
    {
        var junk = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        Assert.False(TcpFrameParser.TryParse(junk, out _, out _, out bool resync));
        Assert.True(resync);
    }

    [Fact]
    public void ValidateFrame_RejectsCorruptCRC()
    {
        var payload = new byte[] { 0xAA, 0x55, 0x01, 0x02 };
        var frame = TcpFrameParser.BuildFrame(payload);
        frame[^1] ^= 0xFF;   // 破坏最后一个 CRC 字节
        Assert.False(TcpFrameParser.ValidateFrame(frame));
    }

    [Fact]
    public void TcpDevice_Simulate_ProducesValues()
    {
        var maps = new[] { new TcpDevice.TcpMap(1, 1001), new TcpDevice.TcpMap(2, 1002) };
        using var dev = new TcpDevice(1, "TCP-Sim", "127.0.0.1", 9999, maps, simulate: true);

        int events = 0;
        dev.DataReceived += (_, _) => Interlocked.Increment(ref events);

        dev.Connect();
        Thread.Sleep(700);   // 等一轮 500ms tick
        dev.Disconnect();

        Assert.True(events >= 2, $"expected ≥2 events, got {events}");
        Assert.False(double.IsNaN(dev.Read(1)));
    }
}
