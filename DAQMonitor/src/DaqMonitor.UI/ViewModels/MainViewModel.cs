using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using DaqMonitor.Core.Acquisition;
using DaqMonitor.Core.Alarms;
using DaqMonitor.Core.AppServices;
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Diagnostics;
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Reporting;
using DaqMonitor.Core.Store;
using DaqMonitor.UI.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace DaqMonitor.UI.ViewModels;

/// <summary>点位在界面上的展示模型（值类型 SensorPoint 不适合直接绑 UI，转成带通知的属性）。</summary>
public class PointView : INotifyPropertyChanged
{
    private int _id;
    private double _value;
    private DateTime _timestamp;
    private DeviceState _state;
    private AlarmLevel _level = AlarmLevel.Normal;

    public int Id { get => _id; set { _id = value; OnChanged(); } }
    public double Value { get => _value; set { _value = value; OnChanged(); } }
    public DateTime Timestamp { get => _timestamp; set { _timestamp = value; OnChanged(); } }
    public DeviceState State { get => _state; set { _state = value; OnChanged(); } }
    /// <summary>当前报警级别，驱动 GaugeControl 表盘变色（M6 报警 → M14 控件的跨模块复用演示）。</summary>
    public AlarmLevel Level { get => _level; set { _level = value; OnChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// 主窗口 ViewModel：从 DI 容器取出真实服务（管道 / 存储 / 报警引擎 / 设备 / 诊断服务），
/// 把后台采集事件接入 UI。这是“企业级项目能跑起来”的演示核心，也是“边做边用调试能力”的落点。
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly PointStore _store;
    private readonly AcquisitionPipeline _pipeline;
    private readonly AlarmEngine _alarms;
    private readonly IDevice _device;
    private readonly DiagnosticsService _diag;
    private readonly Dictionary<int, AlarmLevel> _levels = new();
    private bool _running;
    private DateTime _from = DateTime.Today;
    private DateTime _to = DateTime.Now;

    public ObservableCollection<PointView> Points { get; } = new();
    public ObservableCollection<string> AlarmLog { get; } = new();

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ExportReportCommand { get; }

    /// <summary>诊断面板绑定的“一行式”统计摘要（每次批量后刷新）。</summary>
    public string DiagnosticsSummary
        => $"采样 {_diag.TotalSamples} 点 | 报警 {_diag.AlarmCount} 次 | 批次 {_diag.BatchCount} | 末批 {_diag.LastBatchMs}ms | 运行 {_diag.Uptime:hh\\:mm\\:ss}";

    /// <summary>诊断面板绑定的日志视图（环形缓冲，最多 200 条）。</summary>
    public ReadOnlyObservableCollection<string> DiagnosticsLog => _diag.Log;

    public bool IsRunning
    {
        get => _running;
        private set { _running = value; OnChanged(); }
    }

    public DateTime From { get => _from; set { _from = value; OnChanged(); } }
    public DateTime To { get => _to; set { _to = value; OnChanged(); } }

    public MainViewModel(ServiceProvider services)
    {
        _store = services.GetRequiredService<PointStore>();
        _pipeline = services.GetRequiredService<AcquisitionPipeline>();
        _alarms = services.GetRequiredService<AlarmEngine>();
        _device = services.GetRequiredService<IDevice>();
        _diag = services.GetRequiredService<DiagnosticsService>();

        StartCommand = new RelayCommand(_ => Start());
        StopCommand = new RelayCommand(_ => Stop());
        ExportReportCommand = new RelayCommand(_ => ExportReport());

        _pipeline.BatchReady += OnBatchReady;
        _alarms.AlarmTriggered += OnAlarmTriggered;
        _alarms.AlarmCleared += OnAlarmCleared;

        _diag.RecordInfo("应用启动，DI 容器已装配（设备/管道/存储/报警/诊断）。");
    }

    private void OnBatchReady(object? _, IReadOnlyList<SensorPoint> batch)
    {
        // 用 Stopwatch 给“批量处理耗时”计时 —— 工业排查“卡顿/丢点”的第一指标
        var sw = Stopwatch.StartNew();
        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var p in batch)
            {
                _store.AddOrUpdate(p);
                _alarms.Evaluate(p);   // 跑报警规则（命中只在上上升沿通知）

                PointView? row = Points.FirstOrDefault(x => x.Id == p.Id);
                if (row is null)
                {
                    row = new PointView { Id = p.Id, Value = p.Value, Timestamp = p.Timestamp, State = p.State };
                    Points.Add(row);
                }
                else
                {
                    row.Value = p.Value;
                    row.Timestamp = p.Timestamp;
                    row.State = p.State;
                }
                // 把“当前报警级别”同步给控件（没有报警就保持 Normal → 蓝环）
                if (_levels.TryGetValue(p.Id, out var lv)) row.Level = lv;
            }
            OnChanged(nameof(DiagnosticsSummary));
        });
        sw.Stop();
        _diag.RecordBatch(batch.Count, sw.ElapsedMilliseconds);
    }

    private void OnAlarmTriggered(object? _, AlarmEvent e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            AlarmLog.Insert(0, $"[{DateTime.Now:HH:mm:ss}] 点位 {e.PointId} → {e.Level} 报警，值 = {e.Value}");
            _levels[e.PointId] = e.Level;                 // 记住级别，下次批量刷新时同步给控件
            var row = Points.FirstOrDefault(x => x.Id == e.PointId);
            if (row is not null) row.Level = e.Level;     // 表盘立即变橙/红（GaugeControl.Level 驱动）
        });
        _diag.RecordAlarm(e.PointId, e.Level.ToString(), e.Value);
    }

    private void OnAlarmCleared(object? _, AlarmEvent e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _levels[e.PointId] = AlarmLevel.Normal;       // 复位
            var row = Points.FirstOrDefault(x => x.Id == e.PointId);
            if (row is not null) row.Level = AlarmLevel.Normal;   // 表盘恢复蓝环
        });
        _diag.RecordInfo($"点位 {e.PointId} 报警恢复（值回到正常区间）。");
    }

    private void Start()
    {
        if (IsRunning) return;
        if (_device is SimulatedDevice sd) sd.Start(TimeSpan.FromMilliseconds(100));
        _diag.RecordInfo($"启动采集：{_device.Name}（模拟设备）。");
        IsRunning = true;
    }

    private void Stop()
    {
        if (!IsRunning) return;
        if (_device is SimulatedDevice sd) sd.Stop();
        _diag.RecordInfo("停止采集。");
        IsRunning = false;
    }

    /// <summary>
    /// 导出报表（M10）：按时间窗聚合存储里的点位 → 写成 Excel。
    /// 企业每天要的“班报/日报”就是这一步，串联了 M4(存储)→M10(聚合+导出)。
    /// </summary>
    private void ExportReport()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Excel 文件|*.xlsx",
            FileName = $"DAQ报表_{DateTime.Now:yyyyMMddHHmm}"
        };
        if (dlg.ShowDialog() != true) return;

        var stats = new ReportService().Aggregate(_store.GetAll(), From, To);
        ReportExporter.ExportToExcel(stats, dlg.FileName);
        MessageBox.Show($"已导出 {stats.Count} 个点位的报表 → {dlg.FileName}", "导出成功");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
