using System.Windows.Controls;
using System.Windows.Input;
using DaqMonitor.UI.ViewModels;

namespace DaqMonitor.UI.Views;

/// <summary>
/// 运动控制 view code-behind。
/// Jog 按钮的特殊处理:PreviewMouseDown 启动 Jog,PreviewMouseUp 停止(模拟按住按钮就动)。
/// 这是 WPF 处理"按住交互"的常见模式 —— ICommand 的 Execute 不够用,因为它只在点击完成时触发一次。
/// </summary>
public partial class MotionControlView : UserControl
{
    public MotionControlView(MotionControlViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    /// <summary>Jog 按钮按下:启动点动(方向由 Tag 决定)。</summary>
    private void Jog_Down(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is AxisRow row)
        {
            if (btn.Tag as string == "pos") row.JogPositive();
            else row.JogNegative();
        }
    }

    /// <summary>Jog 按钮松开:停止点动。</summary>
    private void Jog_Up(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is AxisRow row)
        {
            row.JogStop();
        }
    }
}
