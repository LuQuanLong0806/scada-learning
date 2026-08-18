using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DaqMonitor.Core.Acquisition;
using DaqMonitor.Core.Alarms;
using DaqMonitor.Core.AppServices;
using DaqMonitor.Core.Auth;
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Diagnostics;
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Recipes;
using DaqMonitor.Core.Reporting;
using DaqMonitor.Core.Store;
using DaqMonitor.UI.Reporting;
using DaqMonitor.UI.Views;
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
    private readonly ICurrentUserService _current;
    private readonly AuthService _auth;
    private readonly Dictionary<int, AlarmLevel> _levels = new();
    private bool _running;
    private DateTime _from = DateTime.Today;
    private DateTime _to = DateTime.Now;
    private ChartView? _chart;

    public ObservableCollection<PointView> Points { get; } = new();
    public ObservableCollection<string> AlarmLog { get; } = new();

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ExportReportCommand { get; }
    public ICommand LogoutCommand { get; }

    /// <summary>诊断面板绑定的“一行式”统计摘要（每次批量后刷新）。</summary>
    public string DiagnosticsSummary
        => $"采样 {_diag.TotalSamples} 点 | 报警 {_diag.AlarmCount} 次 | 批次 {_diag.BatchCount} | 末批 {_diag.LastBatchMs}ms | 运行 {_diag.Uptime:hh\\:mm\\:ss}";

    /// <summary>诊断面板绑定的日志视图（环形缓冲，最多 200 条）。</summary>
    public ReadOnlyObservableCollection<string> DiagnosticsLog => _diag.Log;

    public bool IsRunning
    {
        get => _running;
        private set
        {
            _running = value;
            OnChanged();
            // 运行状态翻转 → 启停按钮的可用性也跟着变（权限 && 状态）
            OnChanged(nameof(CanStartAcquisition));
            OnChanged(nameof(CanStopAcquisition));
        }
    }

    public DateTime From { get => _from; set { _from = value; OnChanged(); } }
    public DateTime To { get => _to; set { _to = value; OnChanged(); } }

    // —— M17 工业安全:当前用户信息(头部显示) + 按钮级权限 IsEnabled ——

    /// <summary>当前登录用户名(Header 右上角显示)。</summary>
    public string CurrentUsername => _current.Username;

    /// <summary>当前角色文本(Admin/Engineer/Operator)。</summary>
    public string CurrentRole => _current.Role?.ToString() ?? "—";

    /// <summary>
    /// 角色对应的颜色(IEC 60073 借鉴:红=Admin 高权限需慎用、蓝=Engineer 技术、灰=Operator 基础)。
    /// 返回 Frozen Brush,WPF 跨线程使用安全。
    /// </summary>
    public SolidColorBrush CurrentRoleColor => RoleBrush(_current.Role);

    /// <summary>启动采集按钮 IsEnabled:有权限 && 当前未在跑。</summary>
    public bool CanStartAcquisition
        => _current.HasPermission(Permissions.AcquisitionStart) && !IsRunning;

    /// <summary>停止采集按钮 IsEnabled:有权限 && 当前正在跑。</summary>
    public bool CanStopAcquisition
        => _current.HasPermission(Permissions.AcquisitionStop) && IsRunning;

    /// <summary>导出报表按钮 IsEnabled:Engineer 及以上(含敏感生产数据)。</summary>
    public bool CanExportReport => _current.HasPermission(Permissions.ReportExport);

    /// <summary>M18 配方管理 VM(MainWindow 用它创建 RecipeManagementView)。</summary>
    public RecipeManagementViewModel? Recipes { get; private set; }

    /// <summary>M19 运动控制 VM(MainWindow 用它创建 MotionControlView)。</summary>
    public MotionControlViewModel? Motion { get; private set; }

    private static SolidColorBrush RoleBrush(UserRole? role) => role switch
    {
        UserRole.Admin => Freeze(new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C))),    // 红
        UserRole.Engineer => Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0xA0, 0xFF))), // 蓝
        UserRole.Operator => Freeze(new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E))), // 灰
        _ => Freeze(new SolidColorBrush(Colors.Black)),
    };

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    public MainViewModel(ServiceProvider services)
    {
        _store = services.GetRequiredService<PointStore>();
        _pipeline = services.GetRequiredService<AcquisitionPipeline>();
        _alarms = services.GetRequiredService<AlarmEngine>();
        _device = services.GetRequiredService<IDevice>();
        _diag = services.GetRequiredService<DiagnosticsService>();
        _current = services.GetRequiredService<ICurrentUserService>();
        _auth = services.GetRequiredService<AuthService>();

        StartCommand = new RelayCommand(_ => Start());
        StopCommand = new RelayCommand(_ => Stop());
        ExportReportCommand = new RelayCommand(_ => ExportReport(), _ => CanExportReport);
        LogoutCommand = new RelayCommand(_ => Logout());

        // M18 配方管理 VM:由 MainViewModel 持有,MainWindow 构造 RecipeManagementView 时传入
        Recipes = new RecipeManagementViewModel(
            services.GetRequiredService<RecipeService>(),
            _current);

        // M19 运动控制 VM:从 DI 拿所有 IAxisController(IEnumerable<IAxisController> 自动解析多实例)
        Motion = new MotionControlViewModel(
            services.GetServices<Core.Motion.IAxisController>());

        _pipeline.BatchReady += OnBatchReady;
        _alarms.AlarmTriggered += OnAlarmTriggered;
        _alarms.AlarmCleared += OnAlarmCleared;

        _diag.RecordInfo("应用启动，DI 容器已装配（设备/管道/存储/报警/诊断/认证/配方）。");
    }

    /// <summary>
    /// 由 MainWindow 注入：曲线页只吃真实采集数据（OnBatchReady 里 Push），
    /// 不启动演示模式——否则没开始采集曲线也在跳，且跳的是随机数不是真实值。
    /// </summary>
    public void AttachChart(ChartView chart)
    {
        _chart = chart;
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

                _chart?.Push(p);   // 实时曲线：点位 1/2 分流进温度/压力两条线
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
    /// 登出:写审计 → 关主窗 → 弹登录窗重新进。
    /// 工业现场"换班"标准动作:不退出进程,只换当前用户(UI 状态/采集不受影响)。
    /// </summary>
    private async void Logout()
    {
        if (_device is SimulatedDevice sd && IsRunning) sd.Stop();
        await _auth.LogoutAsync();
        // 关掉主窗,App.xaml.cs 的 window.Closed 会触发 audit + dispose
        // 这里直接 Shutdown,让用户重新双击启动重新登录(最简、最安全)
        Application.Current.Shutdown();
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
