namespace DaqMonitor.Core.Devices;

/// <summary>
/// CAN 内存模拟通道（零硬件）：Open/Send 时直接回调一帧“温度 = 25.0℃”的假数据（ID=0x100，数据 [0x00,0xFA]=250）。
/// 用途：单元测试 / 没有真实 CAN 卡时验证 <see cref="CanDevice"/> 解析与整条链路，无需任何硬件或厂商 DLL。
/// 生产别用它——它不接任何真实总线。
/// </summary>
public sealed class SimulatedCanChannel : ICanChannel
{
    public event Action<ulong, byte[]>? FrameReceived;
    public bool IsOpen { get; private set; }

    public void Open()
    {
        IsOpen = true;
        // 模拟“设备上线即广播当前温度”
        FrameReceived?.Invoke(0x100, new byte[] { 0x00, 0xFA });
    }

    public void Send(ulong id, byte[] data)
        => FrameReceived?.Invoke(0x100, new byte[] { 0x00, 0xFA }); // 模拟设备回温度

    public void Close() => IsOpen = false;
}
