using DaqMonitor.Core.Protocol;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 验证 M2 Day 3 的报文解析知识点真落地：响应帧拆解 / 线圈位解包 / 浮点 4 种字节序 / 异常码 / 组帧。
/// 纯协议层，不碰串口，CI 可跑绿。
/// </summary>
public class ModbusFrameParserTests
{
    [Fact]
    public void ParseReadRegisters_DecodesTwoRegisters()
    {
        // 响应：01 03 04 | 00 0A 00 14 | C4 0B
        var resp = new byte[] { 0x01, 0x03, 0x04, 0x00, 0x0A, 0x00, 0x14, 0xC4, 0x0B };
        var regs = ModbusFrameParser.ParseReadRegisters(resp);
        Assert.Equal(new ushort[] { 10, 20 }, regs);
    }

    [Fact]
    public void ParseCoils_UnpacksBits()
    {
        // 字节 FF 03 → 线圈 0–7 全在线；8、9 在线；10–15 离线
        var bits = ModbusFrameParser.ParseCoils(new byte[] { 0xFF, 0x03 }, 12);
        Assert.True(bits[0] && bits[7] && bits[8] && bits[9]);
        Assert.False(bits[10] && bits[11]);
    }

    [Fact]
    public void ToFloatModbus_ABCD_IsCorrect_But_CDAB_IsNot()
    {
        // 干净样例：0x42C80000 = 100.0f（r0=0x42C8, r1=0x0000）。
        // 注意：本机 x86 是小端，解析器内部已按"大端字节序"正确还原，ABCD 才得 100.0。
        float abcd = ModbusFrameParser.ToFloatModbus(0x42C8, 0x0000, ModbusFrameParser.ByteOrder.ABCD);
        float cdab = ModbusFrameParser.ToFloatModbus(0x42C8, 0x0000, ModbusFrameParser.ByteOrder.CDAB);
        Assert.Equal(100.0f, abcd);                      // ABCD 正确还原：100.0
        Assert.True(System.Math.Abs(cdab - 100.0f) > 1); // 字交换得到错值，现场翻车根因
    }

    [Fact]
    public void IsExceptionResponse_DetectsIllegalAddress()
    {
        // 设备拒了：功能码 0x83，异常码 0x02（非法地址）
        var resp = new byte[] { 0x01, 0x83, 0x02 };
        Assert.True(ModbusFrameParser.IsExceptionResponse(resp, out var code));
        Assert.Equal((byte)0x02, code);
        Assert.Equal("非法地址（地址超出设备范围，常是 ±1 偏移）", ModbusFrameParser.ExceptionMessages[code]);
    }

    [Fact]
    public void BuildReadHoldingRequest_ProducesFrameWithValidCrc()
    {
        var frame = ModbusFrameParser.BuildReadHoldingRequest(0x01, 0, 2);
        // 期望：01 03 00 00 00 02 [CRC低][CRC高]
        Assert.Equal(new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x02 }, frame[..6]);
        Assert.True(Crc16.Check(frame));             // CRC 校验通过
    }
}
