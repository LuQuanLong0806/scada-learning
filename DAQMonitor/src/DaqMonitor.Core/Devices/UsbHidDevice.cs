using DaqMonitor.Core.Models;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// USB-HID 仪器设备（M16 落地）：继承 <see cref="DeviceBase"/>，把 HID “报告(Report)”解码成统一 <see cref="DataEventArgs"/>。
///
/// 约定（和仪器厂家的协议文档对齐）：
///   - report[0] = 报告类型：0x01 = 温度，0x02 = 压力；
///   - report[1..2] = 2 字节大端原始值，÷10 得工程量（M12 标定）。
///
/// 真实仪器用 HidLibrary 实现 <see cref="IHidChannel"/>（Enumerate(Vid,Pid) 找设备、ReadReport 异步等包、Write 发控制命令）。
/// 本类只认接口，换仪器/换厂商库不改动业务——M9“面向接口”的胜利。
/// </summary>
public sealed class UsbHidDevice : DeviceBase
{
    private readonly IHidChannel _ch;
    private readonly Dictionary<int, double> _last = new();

    public UsbHidDevice(int id, string name, IHidChannel channel) : base(id, name)
        => _ch = channel;

    public override void Connect()
    {
        _ch.ReportReceived += OnReport;
        _ch.Open();
        State = DeviceState.Online;
    }

    public override void Disconnect()
    {
        _ch.ReportReceived -= OnReport;
        _ch.Close();
        State = DeviceState.Offline;
    }

    private void OnReport(byte[] report)
    {
        if (report.Length < 3) return;
        if (report[0] == 0x01)                              // 温度
        {
            double v = ((report[1] << 8) | report[2]) / 10.0;
            _last[1] = v; RaiseData(1, v);
        }
        else if (report[0] == 0x02)                         // 压力
        {
            double v = ((report[1] << 8) | report[2]) / 10.0;
            _last[2] = v; RaiseData(2, v);
        }
    }

    public override double Read(int addr)
        => _last.TryGetValue(addr, out var v) ? v : double.NaN;

    public override void Write(int addr, double value)
    {
        var outBuf = new byte[Math.Max(3, _ch.ReportLength)];
        outBuf[0] = 0x02;
        outBuf[1] = (byte)value;
        _ch.Write(outBuf);                                  // 发控制命令（如置数/启动）
    }
}
