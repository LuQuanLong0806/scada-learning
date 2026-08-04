using DaqMonitor.Core.Models;
using DaqMonitor.Core.Protocol;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// 串口设备（M1 落地）：继承 <see cref="DeviceBase"/>，把“串口字节流”转成系统统一的
/// <see cref="DataEventArgs"/> 事件，从而无缝接入 <see cref="Acquisition.AcquisitionPipeline"/> ——
/// <b>UI 与采集层一行都不用改</b>，只是组合根里把 SimulatedDevice 换成了它。
///
/// 职责划分（这就是“现成 vs 自研”的边界）：
///   - 🟩 现成：字节收发靠 <see cref="ISerialChannel"/>（真实串口用 SerialPort，回环用内存）；
///   - 🛠️ 自研：协议解析靠 <see cref="FrameParser"/>（AA55|Len|Payload|CRC）+ CRC16 校验 + 载荷解码。
///
/// 接入方式（见 <c>Bootstrapper</c> 注释）：换设备只改组合根一行。
/// </summary>
public sealed class SerialDevice : DeviceBase
{
    private readonly ISerialChannel _channel;
    private readonly FrameParser _parser = new(verifyCrc: true);
    private readonly Dictionary<int, double> _last = new();

    /// <summary>M15 联调“调试开关”：非 null 时，收发字节会回调出去（接 Serilog 即可落日志）。联调定位必备。</summary>
    public Action<string>? RawLog { get; set; }

    public SerialDevice(int id, string name, ISerialChannel channel) : base(id, name)
        => _channel = channel;

    public override void Connect()
    {
        _channel.BytesReceived += OnBytes;
        _channel.Open();
        State = DeviceState.Online;
    }

    public override void Disconnect()
    {
        _channel.BytesReceived -= OnBytes;
        _channel.Close();
        State = DeviceState.Offline;
    }

    private void OnBytes(byte[] bytes)
    {
        RawLog?.Invoke($"RX {Convert.ToHexString(bytes)}");   // 联调：看清收到了啥
        // 把收到的字节流喂给解析器；它自动处理“半包/粘包”，逐帧回调载荷
        foreach (var payload in _parser.Feed(bytes))
        {
            if (payload.Length < 9) continue;                 // 载荷格式：pointId(1) + double(8)
            int pointId = payload[0];
            double value = BitConverter.ToDouble(payload, 1);  // 🛠️ 自研解码：字节 → 工程量点位
            _last[pointId] = value;
            RaiseData(pointId, value);                        // 推给采集管道 → 最终到 UI
        }
    }

    public override double Read(int addr)
        => _last.TryGetValue(addr, out var v) ? v : double.NaN;

    public override void Write(int addr, double value)
    {
        // 下发查询/置数命令帧（写方向也走同一套自研协议）
        var frame = FrameParser.Build(addr, value);
        RawLog?.Invoke($"TX {Convert.ToHexString(frame)}");    // 联调：看清发出了啥
        _channel.Write(frame);
    }
}
