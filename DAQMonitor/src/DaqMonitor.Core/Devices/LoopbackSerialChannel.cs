namespace DaqMonitor.Core.Devices;

/// <summary>
/// 内存回环通道（零硬件）：Write 进来的字节，直接作为“从线上收到的数据”异步回调出去。
/// 用途：演示 / 单元测试——不需要任何真实串口，也不需要 com0com 虚拟串口，就能验证
/// <see cref="SerialDevice"/> 的协议解析与“换设备 UI 零改动”是否真的成立。
/// 生产环境别用它，它不接任何硬件。
/// </summary>
public sealed class LoopbackSerialChannel : ISerialChannel
{
    public event Action<byte[]>? BytesReceived;

    public void Open() { /* 回环通道无需打开 */ }

    public void Write(ReadOnlySpan<byte> data)
    {
        var copy = data.ToArray();
        // 用后台线程回调，模拟串口“异步到达”，更贴近真实行为
        Task.Run(() => BytesReceived?.Invoke(copy));
    }

    public void Close() { /* 无操作 */ }
    public void Dispose() { BytesReceived = null; }
}
