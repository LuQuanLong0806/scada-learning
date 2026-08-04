using DaqMonitor.Core.Models;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// CAN 设备（M16 落地）：继承 <see cref="DeviceBase"/>，把 CAN 总线上“按 ID 广播的帧”解码成统一 <see cref="DataEventArgs"/>。
///
/// CAN 与 Modbus 的核心区别（面试常考）：
///   - Modbus 是“主从问答”：主站点名从站地址 + 功能码 + 寄存器；
///   - CAN 是“多主广播”：总线上谁都能发一帧，靠 **ID** 区分这是哪路信号，没有地址/功能码概念。
///     例如约定 ID=0x100 = 温度，2 字节大端 raw，÷10 得 ℃（工程量标定，见 M12）。
///
/// 真实硬件用 PCANChannel 等实现 <see cref="ICanChannel"/>，换链路不换本类——与 M1 <see cref="SerialDevice"/> 同一套路。
/// 接入方式与 SerialDevice 一致：组合根里 new 一个即可，UI/采集层零改动。
/// </summary>
public sealed class CanDevice : DeviceBase
{
    private readonly ICanChannel _ch;
    private readonly Dictionary<int, double> _last = new();

    public CanDevice(int id, string name, ICanChannel channel) : base(id, name)
        => _ch = channel;

    public override void Connect()
    {
        _ch.FrameReceived += OnFrame;
        _ch.Open();
        State = DeviceState.Online;
    }

    public override void Disconnect()
    {
        _ch.FrameReceived -= OnFrame;
        _ch.Close();
        State = DeviceState.Offline;
    }

    private void OnFrame(ulong id, byte[] data)
    {
        if (id != 0x100 || data.Length < 2) return;   // 只认“温度帧”，其它 ID 忽略
        int raw = (data[0] << 8) | data[1];           // 大端：高字节在前
        double value = raw / 10.0;                     // 工程量标定（M12）
        _last[1] = value;
        RaiseData(1, value);                           // 推给采集管道 → 最终到 UI
    }

    public override double Read(int addr)
        => _last.TryGetValue(addr, out var v) ? v : double.NaN;

    public override void Write(int addr, double value)
    {
        // CAN 是广播总线，“写单个寄存器”语义不存在；具体写入请由网关/子类覆盖。
        // 这里留空以免破坏统一接口调用方（同 M1 SerialDevice.Write 的“下发命令帧”思路可在此扩展）。
    }
}
