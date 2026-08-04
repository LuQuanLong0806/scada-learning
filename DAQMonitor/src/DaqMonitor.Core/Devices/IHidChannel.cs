namespace DaqMonitor.Core.Devices;

/// <summary>
/// USB-HID 传输通道抽象：HID 是“固定长度报告(Report)”模型，不像串口能发任意长度字节流。
/// 真实仪器用 HidLibrary 实现本接口；没硬件时用 <see cref="SimulatedHidChannel"/> 内存模拟。
/// 关键点：HID 用 **VID/PID** 找到设备（操作系统原生免驱），不像串口靠 COM 号——换 USB 口也不变。
/// </summary>
public interface IHidChannel
{
    event Action<byte[]>? ReportReceived;
    bool IsOpen { get; }
    /// <summary>HID 报告固定长度（如 64 字节/包），收发都按这个长度。</summary>
    int ReportLength { get; }
    void Open();
    /// <summary>给设备发一包控制指令（如“开始采样”）。</summary>
    void Write(byte[] report);
    void Close();
}
