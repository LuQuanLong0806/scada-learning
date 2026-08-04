using DaqMonitor.Core.Protocol;
using Xunit;

namespace DaqMonitor.Tests;

public class FrameParserTests
{
    [Fact]
    public void Crc16_Modbus_KnownVector()
    {
        // 经典测试向量：CRC16/MODBUS of {0x01,0x03,0x00,0x00,0x00,0x01}
        // 算法寄存器结果 = 0x0A84；按 Modbus 约定「低字节在前」发送，故线上字节为 84 0A（常被记作 0x840A 的大端读法）。
        var data = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x01 };
        Assert.Equal((ushort)0x0A84, Crc16.Modbus(data));
    }

    [Fact]
    public void Feed_Splits粘包_AndHandles半包()
    {
        var p = new FrameParser();
        var payload = new byte[] { 0xAA, 0x55, 0x02, 0x11, 0x22 };
        ushort crc = Crc16.Modbus(payload);
        var frame = payload.Concat(new[] { (byte)(crc & 0xFF), (byte)(crc >> 8) }).ToArray();

        // 半包：先喂前 3 字节
        Assert.Empty(p.Feed(frame.AsSpan(0, 3).ToArray()));
        // 再喂剩下的：应拆出 1 帧，载荷为 {0x11,0x22}
        var second = p.Feed(frame.AsSpan(3).ToArray());
        Assert.Single(second);
        Assert.Equal(new byte[] { 0x11, 0x22 }, second[0]);
    }

    [Fact]
    public void Feed_ignoresBadHeader()
    {
        var p = new FrameParser();
        var bad = new byte[] { 0x00, 0x55, 0x02, 0x11, 0x22, 0x00, 0x00 };
        Assert.Empty(p.Feed(bad));
    }

    [Fact]
    public void Crc16_Check_ValidatesFrame()
    {
        var payload = new byte[] { 0xAA, 0x55, 0x02, 0x11, 0x22 };
        ushort crc = Crc16.Modbus(payload);
        var frame = payload.Concat(new[] { (byte)(crc & 0xFF), (byte)(crc >> 8) }).ToArray();
        Assert.True(Crc16.Check(frame));
    }
}
