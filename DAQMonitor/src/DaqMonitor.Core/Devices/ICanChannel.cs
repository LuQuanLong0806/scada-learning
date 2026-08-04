namespace DaqMonitor.Core.Devices;

/// <summary>
/// CAN 传输通道抽象：把“用什么物理链路收发 CAN 帧”和“怎么解析协议”解耦。
/// 真实硬件用厂商 DLL（PCAN / Vector / 周立功）实现本接口；没硬件时用 <see cref="SimulatedCanChannel"/> 内存模拟。
/// 设计完全对齐 M1 的 <see cref="ISerialChannel"/>——换链路不换协议解析与 UI（面向接口的胜利）。
/// </summary>
public interface ICanChannel
{
    /// <summary>从总线上收到一帧（ID + 数据）时触发。</summary>
    event Action<ulong, byte[]>? FrameReceived;

    bool IsOpen { get; }

    void Open();

    /// <summary>向总线广播一帧：ID 标识信号含义，data 为 0~8 字节负载。</summary>
    void Send(ulong id, byte[] data);

    void Close();
}
