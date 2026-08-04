namespace DaqMonitor.Core.Engineering;

/// <summary>
/// 工程量转换（M12 落地）：把 AD 原始码 / 模数比例还原成现场熟悉的物理量，
/// 同时处理 32 位浮点的 4 种字节序排列（Modbus / OPC UA 现场常见坑）。
/// 全部静态 + 扩展方法，零依赖，便于单测。
/// </summary>
public static class EngineeringConverter
{
    /// <summary>
    /// 线性标定：把 raw ∈ [rawMin, rawMax] 线性映射到 [engMin, engMax]。
    /// 例：4-20mA → 0-100℃，AD 0-65535 → -50~150℃。
    /// </summary>
    /// <remarks>
    /// 注意 rawMax == rawMin 时不能除零，按现场惯例返回 engMin（也避免抛异常打断采集）。
    /// </remarks>
    public static double Linear(double raw, double rawMin, double rawMax, double engMin, double engMax)
    {
        double span = rawMax - rawMin;
        if (Math.Abs(span) < double.Epsilon) return engMin;
        double ratio = (raw - rawMin) / span;
        return engMin + ratio * (engMax - engMin);
    }

    /// <summary>
    /// 非线性查表（PT100 / 热电偶分度表用）：
    /// 在 <paramref name="table"/>（key=raw 升序，value=eng）中插值。
    /// raw 低于最小 key 返回表首；高于最大 key 返回表尾；中间做线性插值。
    /// </summary>
    /// <exception cref="ArgumentException">table 为 null 或空。</exception>
    public static double Lookup(double raw, SortedList<double, double> table)
    {
        if (table is null || table.Count == 0)
            throw new ArgumentException("查表失败：分度表为空", nameof(table));
        if (raw <= table.Keys[0]) return table.Values[0];
        if (raw >= table.Keys[^1]) return table.Values[^1];

        // 二分找到第一个 key > raw 的位置，与前一档做线性插值
        int idx = Bisect(table.Keys, raw);
        double x0 = table.Keys[idx - 1];
        double x1 = table.Keys[idx];
        double y0 = table.Values[idx - 1];
        double y1 = table.Values[idx];
        double span = x1 - x0;
        if (Math.Abs(span) < double.Epsilon) return y0;
        return y0 + (raw - x0) / span * (y1 - y0);
    }

    /// <summary>在升序只读 keys 中找到第一个 > target 的索引（标准 bisect_right 语义）。</summary>
    private static int Bisect(IList<double> keys, double target)
    {
        int lo = 0, hi = keys.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (keys[mid] <= target) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    /// <summary>32 位浮点跨寄存器时的字节序排列（与 ModbusFrameParser.ByteOrder 同义，这里独立定义避免协议层耦合）。</summary>
    public enum ByteOrder { ABCD, CDAB, BADC, DCBA }

    /// <summary>
    /// 字节序交换：把 4 字节 [a,b,c,d] 按指定顺序重排。常用于 Modbus 浮点 / OPC UA / CAN 信号解析。
    /// ABCD=不动；CDAB=字交换（现场最常见）；BADC=字节交换；DCBA=全反序。
    /// </summary>
    /// <exception cref="ArgumentException">输入长度不是 4。</exception>
    public static byte[] Swap(byte[] abcd, ByteOrder order)
    {
        if (abcd is null || abcd.Length != 4)
            throw new ArgumentException("字节序交换需要恰好 4 字节", nameof(abcd));
        return order switch
        {
            ByteOrder.ABCD => new[] { abcd[0], abcd[1], abcd[2], abcd[3] },
            ByteOrder.CDAB => new[] { abcd[2], abcd[3], abcd[0], abcd[1] },
            ByteOrder.BADC => new[] { abcd[1], abcd[0], abcd[3], abcd[2] },
            ByteOrder.DCBA => new[] { abcd[3], abcd[2], abcd[1], abcd[0] },
            _ => throw new ArgumentOutOfRangeException(nameof(order))
        };
    }

    /// <summary>把按指定字节序排列的 4 字节解为 float（默认按大端 ABCD 还原）。</summary>
    public static float ToFloat(byte[] bytes, ByteOrder order = ByteOrder.ABCD)
    {
        var ordered = Swap(bytes, order);
        // 本机小端，把 ABCD（大端）逆转一次再 ToSingle
        if (BitConverter.IsLittleEndian) Array.Reverse(ordered);
        return BitConverter.ToSingle(ordered, 0);
    }
}
