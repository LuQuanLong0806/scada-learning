using System.Collections.Generic;

namespace DaqMonitor.Core.Protocol;

/// <summary>
/// 自定义二进制帧解析（M1 Day2）：帧格式 AA 55 | Len | Payload... | CRC_L CRC_H。
/// 解决串口“半包 / 粘包”：字节流持续喂入，循环拆出完整帧。缓冲永远拆不出完整帧时要清空，防内存泄漏。
///
/// - <see cref="FrameParser(bool)"/> 的 <paramref name="verifyCrc"/> 为 true 时，拆出的帧会先用
///   <see cref="Crc16.Check"/> 校验，坏帧直接丢弃（防止坏数据进入业务）。
/// - <see cref="Build"/> 是写方向：把 (pointId, value) 编码成带 CRC 的完整帧，发送端用。
/// </summary>
public class FrameParser
{
    private readonly List<byte> _buffer = new();
    private readonly bool _verifyCrc;

    public FrameParser(bool verifyCrc = false) => _verifyCrc = verifyCrc;

    /// <summary>喂入一段字节，返回本次拆出的所有完整帧（仅载荷部分，不含头/长/CRC）。</summary>
    public IReadOnlyList<byte[]> Feed(ReadOnlySpan<byte> chunk)
    {
        _buffer.AddRange(chunk.ToArray());
        var frames = new List<byte[]>();
        while (TryTakeFrame(out var frame))
            frames.Add(frame);
        return frames;
    }

    private bool TryTakeFrame(out byte[] frame)
    {
        frame = [];
        int idx = _buffer.IndexOf((byte)0xAA);
        if (idx < 0) { _buffer.Clear(); return false; }              // 找不到帧头：清空防无限增长
        if (_buffer.Count < idx + 3) return false;                   // 头后不足 3 字节
        if (_buffer[idx + 1] != (byte)0x55) { _buffer.RemoveAt(idx); return false; }
        int len = _buffer[idx + 2];
        int total = 3 + len + 2;                                     // 头 + 长 + 载荷 + CRC
        if (_buffer.Count < idx + total) return false;               // 半包：等更多数据

        if (_verifyCrc)
        {
            var full = _buffer.GetRange(idx, total).ToArray();
            if (!Crc16.Check(full))                                 // CRC 不过：丢弃该帧
            {
                _buffer.RemoveRange(0, idx + total);
                return false;
            }
        }

        frame = _buffer.GetRange(idx + 3, len).ToArray();
        _buffer.RemoveRange(0, idx + total);
        return true;
    }

    public void Reset() => _buffer.Clear();

    /// <summary>
    /// 构造一帧（写方向）：AA 55 | Len | Payload | CRC_L CRC_H。
    /// Payload = pointId(1 字节) + value(8 字节 double，<see cref="BitConverter"/>)。
    /// CRC 用 <see cref="Crc16.Modbus"/> 对 Payload 计算，低字节在前，与 <see cref="Crc16.Check"/> 对应。
    /// </summary>
    public static byte[] Build(int pointId, double value)
    {
        var payload = new List<byte> { (byte)pointId };
        payload.AddRange(BitConverter.GetBytes(value));

        // CRC 必须对“头 + 长 + 载荷”整体计算，才能和 Crc16.Check(整帧) 的校验范围一致
        var headAndPayload = new List<byte> { 0xAA, 0x55, (byte)payload.Count };
        headAndPayload.AddRange(payload);

        ushort crc = Crc16.Modbus(headAndPayload.ToArray());
        headAndPayload.Add((byte)(crc & 0xFF));
        headAndPayload.Add((byte)(crc >> 8));
        return headAndPayload.ToArray();
    }
}
