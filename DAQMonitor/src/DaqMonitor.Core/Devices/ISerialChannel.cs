namespace DaqMonitor.Core.Devices;

/// <summary>
/// 串口传输通道抽象：把“用什么物理链路收发字节”和“怎么解析协议”解耦。
/// 这是面向接口编程的又一处落地——<see cref="SerialDevice"/> 只认这个接口，
/// 不关心底层是真实串口还是内存回环，因此：
///   - 生产环境用 <see cref="RealSerialChannel"/>（包 SerialPort，接真实硬件）；
///   - 没硬件时用 <see cref="LoopbackSerialChannel"/>（内存回环）也能跑通整条链路。
/// 切换链路 = 换一个实现，协议解析与 UI 一行都不用改。
/// </summary>
public interface ISerialChannel : IDisposable
{
    /// <summary>从“线上”收到一段字节时触发（异步到达，模拟串口 DataReceived）。</summary>
    event Action<byte[]>? BytesReceived;

    /// <summary>打开链路（真实串口即 Open，回环通道为空操作）。</summary>
    void Open();

    /// <summary>向“线下”写出一段字节（命令/置数帧）。</summary>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>关闭链路。</summary>
    void Close();
}
