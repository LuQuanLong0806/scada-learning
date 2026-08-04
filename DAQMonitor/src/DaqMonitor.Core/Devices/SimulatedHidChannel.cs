namespace DaqMonitor.Core.Devices;

/// <summary>
/// USB-HID 内存模拟通道（零硬件）：Open 时回调一包温度报告 [0x01,0,0xFA]，Write 时回一包压力报告 [0x02,1,0x2C]。
/// 用于单元测试 <see cref="UsbHidDevice"/>，无需真实 HID 仪器。生产别用它。
/// </summary>
public sealed class SimulatedHidChannel : IHidChannel
{
    public event Action<byte[]>? ReportReceived;
    public bool IsOpen { get; private set; }
    public int ReportLength => 64;

    public void Open()
    {
        IsOpen = true;
        ReportReceived?.Invoke(new byte[] { 0x01, 0x00, 0xFA }); // 温度 25.0℃
    }

    public void Write(byte[] report)
        => ReportReceived?.Invoke(new byte[] { 0x02, 0x01, 0x2C }); // 压力 30.0kPa

    public void Close() => IsOpen = false;
}
