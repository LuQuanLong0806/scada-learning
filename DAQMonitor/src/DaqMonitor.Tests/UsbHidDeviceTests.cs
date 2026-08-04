using DaqMonitor.Core.Devices;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 验证 M16 的 USB-HID 设备：用 SimulatedHidChannel（内存模拟，零硬件）喂报告，
/// 断言温度报告 0x01→25.0℃、压力报告 0x02→30.0kPa 都能正确解码。
/// </summary>
public class UsbHidDeviceTests
{
    [Fact]
    public void Decodes_TempReport_To_25C()
    {
        var ch = new SimulatedHidChannel();
        var dev = new UsbHidDevice(1, "HID", ch);
        var got = new List<(int, double)>();
        dev.DataReceived += (_, e) => got.Add((e.PointId, e.Value));

        dev.Connect();
        Thread.Sleep(150);
        dev.Disconnect();

        Assert.Contains(got, x => x.Item1 == 1 && Math.Abs(x.Item2 - 25.0) < 1e-6);
    }

    [Fact]
    public void Decodes_PressureReport_To_30kPa()
    {
        var ch = new SimulatedHidChannel();
        var dev = new UsbHidDevice(1, "HID", ch);
        var got = new List<(int, double)>();
        dev.DataReceived += (_, e) => got.Add((e.PointId, e.Value));

        dev.Connect();
        dev.Write(2, 5);            // 触发模拟设备回压力包
        Thread.Sleep(150);
        dev.Disconnect();

        Assert.Contains(got, x => x.Item1 == 2 && Math.Abs(x.Item2 - 30.0) < 1e-6);
    }
}
