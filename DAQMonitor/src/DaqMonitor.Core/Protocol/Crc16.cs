namespace DaqMonitor.Core.Protocol;

/// <summary>CRC16 工具（Modbus 多项式 0xA001）。M1 帧解析 + M2 Modbus 校验共用。校验与回滞无关，纯算法。</summary>
public static class Crc16
{
    public static ushort Modbus(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    public static ushort Modbus(byte[] data) => Modbus(data.AsSpan());

    /// <summary>校验整帧（载荷 + 2 字节 CRC，低字节在前）。</summary>
    public static bool Check(ReadOnlySpan<byte> frameWithCrc)
    {
        if (frameWithCrc.Length < 2) return false;
        int payloadLen = frameWithCrc.Length - 2;
        ushort calc = Modbus(frameWithCrc[..payloadLen]);
        ushort got = (ushort)(frameWithCrc[^2] | (frameWithCrc[^1] << 8));
        return calc == got;
    }
}
