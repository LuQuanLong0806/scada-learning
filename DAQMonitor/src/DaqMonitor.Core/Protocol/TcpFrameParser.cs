namespace DaqMonitor.Core.Protocol;

/// <summary>
/// TCP 长度前缀帧解析（M11 讲义落地）。
///
/// 帧格式（小端长度，便于 C# 直接 BitConverter 拼装）：
///   [0xAA][0x55][LEN_LO][LEN_HI][PAYLOAD(LEN 字节)][CRC_LO][CRC_HI]
///
/// 设计要点：
///   - 协议层零 I/O 依赖：只吃字节缓冲、吐字节缓冲，便于单测（参考 ModbusFrameParser 风格）。
///   - CRC 复用 <see cref="Crc16"/>（Modbus 多项式 0xA001，工业现场通用）。
///   - 粘包/半包由调用方维护滚动缓冲区，本类提供「尝试解析一帧」语义：
///     TryFrame 返回 true=已凑齐一帧（并从缓冲区移除）；false=数据不够，继续收。
/// </summary>
public static class TcpFrameParser
{
    /// <summary>帧头固定 2 字节：0xAA 0x55（与 MostSignificant 设备通信常用对齐方式）。</summary>
    public const byte Head0 = 0xAA;
    public const byte Head1 = 0x55;

    /// <summary>帧头 + 长度域共 4 字节；帧尾 CRC 2 字节。</summary>
    public const int HeaderSize = 4;
    public const int CrcSize = 2;
    /// <summary>payload 最大 8KB，防御乱码长度域导致的巨型分配。</summary>
    public const int MaxPayload = 8 * 1024;

    /// <summary>
    /// 组装一帧：[AA 55][LEN_LO][LEN_HI][payload][CRC_LO][CRC_HI]。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">payload 超过 <see cref="MaxPayload"/>。</exception>
    public static byte[] BuildFrame(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPayload)
            throw new ArgumentOutOfRangeException(nameof(payload), $"payload 超过 {MaxPayload} 字节上限");

        var frame = new byte[HeaderSize + payload.Length + CrcSize];
        frame[0] = Head0;
        frame[1] = Head1;
        frame[2] = (byte)(payload.Length & 0xFF);          // 小端长度
        frame[3] = (byte)((payload.Length >> 8) & 0xFF);
        payload.CopyTo(frame.AsSpan(HeaderSize));
        ushort crc = Crc16.Modbus(payload);                // 仅对 payload 计算 CRC
        frame[^2] = (byte)(crc & 0xFF);
        frame[^1] = (byte)((crc >> 8) & 0xFF);
        return frame;
    }

    /// <summary>
    /// 校验完整帧（含头/长度/payload/CRC）。返回 false 表示帧损坏，调用方应整帧丢弃。
    /// </summary>
    public static bool ValidateFrame(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < HeaderSize + CrcSize) return false;
        if (frame[0] != Head0 || frame[1] != Head1) return false;
        int len = frame[2] | (frame[3] << 8);
        if (len != frame.Length - HeaderSize - CrcSize) return false;
        // CRC 区 = payload + 2B CRC，复用 Crc16.Check（低字节在前）
        return Crc16.Check(frame.Slice(HeaderSize));
    }

    /// <summary>
    /// 从缓冲区头部尝试解析一帧。
    /// 成功：写入 payload、返回该帧总长度（调用方据此 Skip 缓冲区）。
    /// 失败（数据不足 / 头不对 / 长度非法 / CRC 坏）：返回 0，调用方继续 Append。
    ///   - 头不对齐：返回 0 并设置 needResync=true，提示调用方可以丢弃 1 字节重同步；
    ///     本方法自身不丢弃，保持缓冲区语义纯粹。
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> buffer, out byte[] payload, out int frameLength, out bool needResync)
    {
        payload = Array.Empty<byte>();
        frameLength = 0;
        needResync = false;

        if (buffer.Length < HeaderSize) return false;
        if (buffer[0] != Head0 || buffer[1] != Head1) { needResync = true; return false; }

        int len = buffer[2] | (buffer[3] << 8);
        if (len > MaxPayload) { needResync = true; return false; }   // 长度域乱码：重同步
        int total = HeaderSize + len + CrcSize;
        if (buffer.Length < total) return false;                     // 半包：等更多数据

        var frame = buffer[..total];
        if (!Crc16.Check(frame.Slice(HeaderSize))) { needResync = true; return false; } // CRC 坏：重同步

        payload = frame.Slice(HeaderSize, len).ToArray();
        frameLength = total;
        return true;
    }
}
