using DaqMonitor.Core.Engineering;
using Xunit;

namespace DaqMonitor.Tests;

public class EngineeringConverterTests
{
    [Fact]
    public void Linear_Maps4to20mA_To0to100()
    {
        // 4mA → 0℃，20mA → 100℃，12mA → 50℃
        Assert.Equal(0, EngineeringConverter.Linear(4, 4, 20, 0, 100));
        Assert.Equal(100, EngineeringConverter.Linear(20, 4, 20, 0, 100));
        Assert.Equal(50, EngineeringConverter.Linear(12, 4, 20, 0, 100));
    }

    [Fact]
    public void Linear_OutsideRange_ClampsToExtrapolation()
    {
        // 不做硬限幅（工业现场常允许外推少量超量程），按线性公式延伸
        // 0mA ∈ [4,20] 外推：(0-4)/16 * 100 = -25.0
        Assert.Equal(-25.0, EngineeringConverter.Linear(0, 4, 20, 0, 100), 2);
    }

    [Fact]
    public void Linear_DivZero_ReturnsEngMin()
    {
        // rawMax == rawMin 不能除零，按现场惯例返回 engMin
        Assert.Equal(42.0, EngineeringConverter.Linear(123, 100, 100, 42.0, 99.0));
    }

    [Fact]
    public void Lookup_InterpolatesBetweenTableEntries()
    {
        // PT100 简化分度表：0Ω→0℃，100Ω→100℃，138.5Ω→100℃+ 线性区外
        var table = new SortedList<double, double> { [0] = 0, [100] = 100, [200] = 200 };
        Assert.Equal(50, EngineeringConverter.Lookup(50, table));
        // 边界 clamp
        Assert.Equal(0, EngineeringConverter.Lookup(-10, table));
        Assert.Equal(200, EngineeringConverter.Lookup(999, table));
    }

    [Fact]
    public void Lookup_ThrowsOnEmptyTable()
    {
        Assert.Throws<ArgumentException>(() => EngineeringConverter.Lookup(1, new SortedList<double, double>()));
    }

    [Theory]
    [InlineData(EngineeringConverter.ByteOrder.ABCD, new byte[] { 0x42, 0x48, 0x00, 0x00 })]   // 50.0f 大端
    [InlineData(EngineeringConverter.ByteOrder.CDAB, new byte[] { 0x00, 0x00, 0x42, 0x48 })]   // 字交换后还原
    [InlineData(EngineeringConverter.ByteOrder.BADC, new byte[] { 0x48, 0x42, 0x00, 0x00 })]   // 字节交换后还原
    [InlineData(EngineeringConverter.ByteOrder.DCBA, new byte[] { 0x00, 0x00, 0x48, 0x42 })]   // 全反序后还原
    public void Swap_And_ToFloat_AllOrdersDecodeToSameValue(EngineeringConverter.ByteOrder order, byte[] bytes)
    {
        // 无论字节序如何，所有 4 种排列如果原始语义是 50.0f 大端 ABCD，解码后都应是 50.0f
        float v = EngineeringConverter.ToFloat(bytes, order);
        Assert.Equal(50.0f, v, 1);
    }

    [Fact]
    public void Swap_ThrowsOnWrongLength()
    {
        Assert.Throws<ArgumentException>(() => EngineeringConverter.Swap(new byte[] { 1, 2, 3 }, EngineeringConverter.ByteOrder.ABCD));
    }

    [Fact]
    public void Swap_IdempotentForABCD()
    {
        var src = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        var dst = EngineeringConverter.Swap(src, EngineeringConverter.ByteOrder.ABCD);
        Assert.Equal(src, dst);
    }

    [Fact]
    public void Swap_WordSwap_RearrangesCorrectly()
    {
        var src = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        var dst = EngineeringConverter.Swap(src, EngineeringConverter.ByteOrder.CDAB);
        Assert.Equal(new byte[] { 0x33, 0x44, 0x11, 0x22 }, dst);
    }
}
