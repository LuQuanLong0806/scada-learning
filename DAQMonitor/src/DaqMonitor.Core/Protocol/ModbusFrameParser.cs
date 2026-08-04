using System;
using System.Collections.Generic;

namespace DaqMonitor.Core.Protocol;

/// <summary>
/// Modbus 帧解析（M2 Day 3 知识点落地，纯协议层、不依赖串口，可单测）。
/// 对应讲义：响应帧逐字节拆解 / 线圈位打包 / 32 位浮点 4 种字节序 / 异常码。
/// CRC 校验复用 <see cref="Crc16"/>（Modbus 多项式 0xA001）。
/// </summary>
public static class ModbusFrameParser
{
    /// <summary>32 位浮点跨 2 个寄存器时的字节序排列（现场 90% 问题是 CDAB 字交换）。</summary>
    public enum ByteOrder { ABCD, CDAB, BADC, DCBA }

    /// <summary>Modbus 异常码（异常响应里功能码 | 0x80 后的下一字节）。</summary>
    public static IReadOnlyDictionary<byte, string> ExceptionMessages { get; } = new Dictionary<byte, string>
    {
        [0x01] = "非法功能（设备不支持该功能码）",
        [0x02] = "非法地址（地址超出设备范围，常是 ±1 偏移）",
        [0x03] = "非法数据值（写的值/数量不合法）",
        [0x04] = "从站设备故障（设备内部出错）",
        [0x06] = "从站忙（稍后重试）",
        [0x0B] = "网关路径失效（经转发器时目标不可达）",
    };

    /// <summary>判断是否为异常响应；是则返回异常码（功能码最高位被置 1，如 0x03→0x83）。</summary>
    public static bool IsExceptionResponse(ReadOnlySpan<byte> resp, out byte exceptionCode)
    {
        exceptionCode = 0;
        if (resp.Length < 3) return false;
        if ((resp[1] & 0x80) == 0) return false;
        exceptionCode = resp[2];
        return true;
    }

    /// <summary>
    /// 解析「读保持/输入寄存器」响应帧（功能码 0x03 / 0x04）：
    /// [从站][0x03][字节数 N*2][数据 N*2 字节，每寄存器 2 字节大端][CRC]。
    /// 返回每个寄存器的值（大端拼回 ushort）。不在此做 CRC 校验（调用方用 Crc16.Check 自查）。
    /// </summary>
    /// <exception cref="InvalidOperationException">功能码不是 0x03/0x04，或字节数不匹配。</exception>
    public static ushort[] ParseReadRegisters(ReadOnlySpan<byte> resp)
    {
        if (resp.Length < 5) throw new InvalidOperationException("响应帧太短");
        if (resp[1] != 0x03 && resp[1] != 0x04)
            throw new InvalidOperationException($"不是读寄存器响应，功能码=0x{resp[1]:X2}");
        int byteCount = resp[2];
        if (resp.Length < 3 + byteCount + 2)
            throw new InvalidOperationException("响应帧长度与声明的字节数不符（可能半包）");
        int regCount = byteCount / 2;
        var regs = new ushort[regCount];
        for (int i = 0; i < regCount; i++)
        {
            byte hi = resp[3 + i * 2];      // 高字节在前（大端）
            byte lo = resp[4 + i * 2];
            regs[i] = (ushort)(hi << 8 | lo);
        }
        return regs;
    }

    /// <summary>
    /// 解析「读线圈/离散输入」响应（功能码 0x01 / 0x02）：数据区**每字节装 8 个线圈**，按位排。
    /// bit0 = 最先返回的线圈（与寄存器「高字节在前」是两套完全不同的规则）。
    /// </summary>
    public static bool[] ParseCoils(ReadOnlySpan<byte> data, int coilCount)
    {
        var bits = new bool[coilCount];
        for (int i = 0; i < coilCount; i++)
            bits[i] = (data[i / 8] & (1 << (i % 8))) != 0;
        return bits;
    }

    /// <summary>
    /// 把两个 16 位寄存器拼成 32 位 IEEE754 浮点。
    /// 关键区分：<b>字节交换</b>(BADC) 是寄存器内 2 字节颠倒；<b>字交换</b>(CDAB) 是两寄存器顺序颠倒。
    /// 现场默认常是 CDAB——抓帧确认，别猜。
    ///
    /// ⚠️ 实现要点：BitConverter 在本机(x86)是<b>小端</b>。设备回的是<b>大端字节序</b>的 4 字节，
    /// 所以必须先把 4 个字节按"大端顺序"排好（b0 是最高位字节）再交给 ToSingle，否则会被当成小端解读成极小值。
    /// 这正是"大小端"最隐蔽的坑——下面 ABCD 分支先把 r0 高字节放最前，正是为了还原大端。
    /// </summary>
    public static float ToFloatModbus(ushort r0, ushort r1, ByteOrder order) => order switch
    {
        ByteOrder.ABCD => ToSingleBig((byte)(r0 >> 8), (byte)r0, (byte)(r1 >> 8), (byte)r1),
        ByteOrder.CDAB => ToSingleBig((byte)(r1 >> 8), (byte)r1, (byte)(r0 >> 8), (byte)r0), // 字交换
        ByteOrder.BADC => ToSingleBig((byte)r0, (byte)(r0 >> 8), (byte)r1, (byte)(r1 >> 8)), // 字节交换
        ByteOrder.DCBA => ToSingleBig((byte)r1, (byte)(r1 >> 8), (byte)r0, (byte)(r0 >> 8)), // 全小端
        _ => throw new ArgumentOutOfRangeException(nameof(order))
    };

    /// <summary>按大端解读 4 字节为 float（b0 = 最高位字节）。</summary>
    private static float ToSingleBig(byte b0, byte b1, byte b2, byte b3)
    {
        // 本机小端：把大端字节数组 Reverse 成小端顺序再 ToSingle，等价于"按大端读这 4 字节"
        var le = new[] { b3, b2, b1, b0 };
        return BitConverter.ToSingle(le, 0);
    }

    /// <summary>
    /// 组「读保持寄存器」RTU 请求帧：[从站][0x03][地址2B大端][数量2B大端][CRC低前]。
    /// 用于 ModbusDevice 真实 RTU 路径下发读取。
    /// </summary>
    public static byte[] BuildReadHoldingRequest(byte slave, ushort addr, ushort count)
    {
        var payload = new List<byte> { slave, 0x03 };
        payload.Add((byte)(addr >> 8)); payload.Add((byte)addr);
        payload.Add((byte)(count >> 8)); payload.Add((byte)count);
        ushort crc = Crc16.Modbus(payload.ToArray());
        payload.Add((byte)(crc & 0xFF));   // CRC 低字节在前
        payload.Add((byte)(crc >> 8));
        return payload.ToArray();
    }
}
