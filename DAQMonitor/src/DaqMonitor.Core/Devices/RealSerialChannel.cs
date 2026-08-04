using System.IO.Ports;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// 真实串口通道：用 <see cref="SerialPort"/>（🟩 .NET 官方串口类，需装 <c>System.IO.Ports</c> 包）收发光字。
/// 这是“直接用现成库”的部分——你不用自己写串口驱动。
/// 自己要写的，是把收到的字节流按协议解析（见 <see cref="SerialDevice"/> / <see cref="Protocol.FrameParser"/>）。
/// </summary>
public sealed class RealSerialChannel : ISerialChannel
{
    private readonly SerialPort _sp;
    public event Action<byte[]>? BytesReceived;

    public RealSerialChannel(string portName, int baud = 9600)
    {
        _sp = new SerialPort(portName, baud) { ReadTimeout = 500, WriteTimeout = 500 };
        _sp.DataReceived += (_, _1) =>
        {
            int n = _sp.BytesToRead;
            if (n <= 0) return;
            var buf = new byte[n];
            _sp.Read(buf, 0, n);
            BytesReceived?.Invoke(buf);
        };
    }

    public void Open() { if (!_sp.IsOpen) _sp.Open(); }
    public void Write(ReadOnlySpan<byte> data) => _sp.Write(data.ToArray(), 0, data.Length);
    public void Close() { if (_sp.IsOpen) _sp.Close(); }
    public void Dispose() { Close(); _sp.Dispose(); }
}
