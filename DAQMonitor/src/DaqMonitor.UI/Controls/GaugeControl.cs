using System.Windows;
using System.Windows.Controls;
using DaqMonitor.Core.Models;

namespace DaqMonitor.UI.Controls;

/// <summary>
/// 自定义控件①：量程指针表（Gauge）。
/// 这是 WPF 里最"正宗"的自定义控件写法 —— 继承自 <see cref="Control"/>，
/// 外观全部交给 <c>Themes/Generic.xaml</c> 里的默认 <see cref="Style"/>（没有 xaml.cs 后台代码），
/// 对外只暴露一组 DependencyProperty 供 XAML 绑定。
///
/// 为什么不用 UserControl？UserControl 是把现有控件"拼"起来（适合页面级复用）；
/// 而"自定义控件"强调一套可换肤、可继承、可在不同项目里当基础件用的控件 —— 正是 JD 点名的"熟练自绘控件"。
///
/// 在 DAQMonitor 里它直接绑 <c>PointView.Value</c>，让每个点位一眼看出当前读数；
/// M12 工程量转换后，绑定的 Value 会变成"工程量"（如 ℃、MPa），控件零改动。
/// </summary>
public class GaugeControl : Control
{
    /// <summary>告诉 WPF：本控件的默认样式去 Generic.xaml 里找 TargetType=GaugeControl 的那条。</summary>
    static GaugeControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(GaugeControl),
            new FrameworkPropertyMetadata(typeof(GaugeControl)));
    }

    // ---- 依赖属性：控件对外暴露的全部"可绑定点" ----
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(GaugeControl),
            new PropertyMetadata(0d, (d, _) => RecalcAngle(d)));

    public static readonly DependencyProperty MinProperty =
        DependencyProperty.Register(nameof(Min), typeof(double), typeof(GaugeControl),
            new PropertyMetadata(0d, (d, _) => RecalcAngle(d)));

    public static readonly DependencyProperty MaxProperty =
        DependencyProperty.Register(nameof(Max), typeof(double), typeof(GaugeControl),
            new PropertyMetadata(150d, (d, _) => RecalcAngle(d)));

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(GaugeControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(GaugeControl),
            new PropertyMetadata(string.Empty));

    /// <summary>报警级别：Normal 蓝环 / Warning 橙环 / Critical 红环（M6 报警引擎会驱动它）。</summary>
    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(nameof(Level), typeof(AlarmLevel), typeof(GaugeControl),
            new PropertyMetadata(AlarmLevel.Normal));

    /// <summary>指针角度（度）。由 Value/Min/Max 算出，XAML 模板里绑给指针的 RotateTransform。</summary>
    public static readonly DependencyProperty NeedleAngleProperty =
        DependencyProperty.Register(nameof(NeedleAngle), typeof(double), typeof(GaugeControl),
            new PropertyMetadata(-135d));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Min { get => (double)GetValue(MinProperty); set => SetValue(MinProperty, value); }
    public double Max { get => (double)GetValue(MaxProperty); set => SetValue(MaxProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public AlarmLevel Level { get => (AlarmLevel)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public double NeedleAngle { get => (double)GetValue(NeedleAngleProperty); set => SetValue(NeedleAngleProperty, value); }

    /// <summary>把当前 Value 映射到 -135°~+135°（270° 量程，缺口在底部），即表针角度。</summary>
    private static void RecalcAngle(DependencyObject d)
    {
        var g = (GaugeControl)d;
        var max = g.Max;
        if (max <= g.Min) max = g.Min + 1;
        var ratio = (g.Value - g.Min) / (max - g.Min);
        ratio = Math.Max(0, Math.Min(1, ratio));
        g.NeedleAngle = -135 + ratio * 270;
    }
}
