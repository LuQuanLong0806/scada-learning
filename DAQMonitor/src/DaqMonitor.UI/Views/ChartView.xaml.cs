using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using DaqMonitor.Core.Models;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace DaqMonitor.UI.Views;

/// <summary>
/// M5 实时曲线：LiveCharts2 + ObservableCollection(double) 滚动缓冲。
/// 每 100ms tick 一次（10Hz），最多保留 600 个点（60 秒）。
///
/// 用法：MainWindow 把它放进 TabItem，并绑定/传入两条点位序列；
/// 实战里改成订阅 AcquisitionPipeline.BatchReady，把点位按 id 分流进两条线。
/// </summary>
public partial class ChartView : IDisposable, INotifyPropertyChanged
{
    private const int MaxPoints = 600;
    private const int TickMs = 100;

    private readonly ObservableCollection<double> _temp = new();
    private readonly ObservableCollection<double> _press = new();
    private readonly DispatcherTimer _timer;
    private readonly Random _demo = new();

    /// <summary>PointId → 序列映射：1=温度，2=压力。可外部配置。</summary>
    public int TemperaturePointId { get; set; } = 1;
    public int PressurePointId { get; set; } = 2;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([System.Runtime.CompilerServices.CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public ChartView()
    {
        InitializeComponent();

        Chart.Series = new ISeries[]
        {
            new LineSeries<double>
            {
                Values = _temp,
                Name = "温度 (℃)",
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0.3
            },
            new LineSeries<double>
            {
                Values = _press,
                Name = "压力 (kPa)",
                GeometrySize = 0,
                LineSmoothness = 0.3
            }
        };

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(TickMs) };
        _timer.Tick += OnTick;
    }

    /// <summary>外部喂入真实点位（典型由 MainViewModel 在 BatchReady 中调用）。</summary>
    public void Push(SensorPoint p)
    {
        if (p.Id == TemperaturePointId) PushOne(_temp, p.Value);
        else if (p.Id == PressurePointId) PushOne(_press, p.Value);
    }

    private static void PushOne(ObservableCollection<double> col, double v)
    {
        col.Add(v);
        while (col.Count > MaxPoints) col.RemoveAt(0);
    }

    /// <summary>演示模式：无外部数据源时启用，自动生成温度/压力曲线。</summary>
    public void StartDemo()
    {
        if (!_timer.IsEnabled) _timer.Start();
    }

    public void StopDemo()
    {
        _timer.Stop();
    }

    private void OnTick(object? s, EventArgs e)
    {
        // 演示数据：温度 25±5，压力 80±10
        double t = 25 + _demo.NextDouble() * 10 - 5;
        double p = 80 + _demo.NextDouble() * 20 - 10;
        PushOne(_temp, Math.Round(t, 2));
        PushOne(_press, Math.Round(p, 2));
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
