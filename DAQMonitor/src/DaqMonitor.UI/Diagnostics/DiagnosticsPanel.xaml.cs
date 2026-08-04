using System.Windows.Controls;

namespace DaqMonitor.UI.Diagnostics;

/// <summary>
/// 诊断面板 UserControl 的后台代码。
/// 注意：这里几乎没有逻辑 —— 数据全部来自 DataContext(MainViewModel) 暴露的属性。
/// 这正是 MVVM + UserControl 的正确姿势：UI 只负责“长什么样”，数据和行为都在 ViewModel。
/// </summary>
public partial class DiagnosticsPanel : UserControl
{
    public DiagnosticsPanel() => InitializeComponent();
}
