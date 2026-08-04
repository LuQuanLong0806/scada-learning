using System.Windows;
using System.Windows.Controls;
using DaqMonitor.Core.Models;

namespace DaqMonitor.UI.Controls;

/// <summary>
/// 自定义控件②：设备状态灯（StatusDot）。
/// 同样的"自定义控件"套路：继承 <see cref="Control"/> + Generic.xaml 默认样式 + DependencyProperty。
///
/// 这里特意演示了"用动画表达状态"：Connecting 时小圆点做透明度脉冲（Storyboard 写在 Generic.xaml 里），
/// 比单纯改颜色更接近工业 HMI 的"正在连接/通讯中"观感。
///
/// 在 DAQMonitor 里它直接绑 <c>PointView.State</c>（Offline/Connecting/Online）；
/// M1 接入真实串口、M3 接入真实 PLC 后，连接握手过程就会出现 Connecting 脉冲，控件原样复用。
/// </summary>
public class StatusDot : Control
{
    static StatusDot()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(StatusDot),
            new FrameworkPropertyMetadata(typeof(StatusDot)));
    }

    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(nameof(State), typeof(DeviceState), typeof(StatusDot),
            new PropertyMetadata(DeviceState.Offline));

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusDot),
            new PropertyMetadata(string.Empty));

    public DeviceState State { get => (DeviceState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
}
