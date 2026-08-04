using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DaqMonitor.Core.Motion;

namespace DaqMonitor.UI.ViewModels;

/// <summary>
/// 运动控制 ViewModel:管理多个轴,提供 Jog/定位/回零/急停 命令。
///
/// 设计要点(对标真实 HMI):
///   ① 每个轴一个 AxisRow VM(UI DataGrid 一行一个轴)
///   ② 急停是全局的(一条命令停所有轴,IEC 60204-1)
///   ③ Jog 用 Click 按下/松开两阶段(PreviewMouseDown/Up 在 View 处理,VM 提供 Start/Stop)
/// </summary>
public class MotionControlViewModel : INotifyPropertyChanged
{
    private readonly IReadOnlyList<IAxisController> _axes;
    private string _statusText = "";

    public ObservableCollection<AxisRow> Axes { get; } = new();

    /// <summary>急停(全局,所有轴同时停)。</summary>
    public ICommand EmergencyStopAllCommand { get; }

    public string StatusText { get => _statusText; private set { _statusText = value; OnChanged(); } }

    public MotionControlViewModel(IEnumerable<IAxisController> axes)
    {
        _axes = axes.ToList();
        EmergencyStopAllCommand = new MotionRelayCommand(_ => EmergencyStopAll());

        foreach (var axis in _axes)
        {
            var row = new AxisRow(axis);
            axis.StateChanged += (_, s) =>
            {
                StatusText = $"[{DateTime.Now:HH:mm:ss}] {axis.Name} → {s}";
                // 急停或运动完成时,刷新命令可用性
                row.RaiseCommandsChanged();
            };
            axis.AlarmRaised += (_, msg) =>
            {
                StatusText = $"⚠ 报警:{msg}";
            };
            Axes.Add(row);
        }
    }

    private void EmergencyStopAll()
    {
        foreach (var a in _axes) a.EmergencyStopAsync().GetAwaiter().GetResult();
        StatusText = $"⚠ 已急停全部 {_axes.Count} 个轴";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// 单个轴的 ViewModel(一行):绑 UI 表格,包装轴的状态 + 命令。
/// 类比前端:每个 AxisRow 就是一个 axis card 的 React/Vue 子组件。
/// </summary>
public class AxisRow : INotifyPropertyChanged
{
    private readonly IAxisController _axis;
    private double _jogTarget = 50;       // Jog 输入的速度
    private double _moveTarget = 0;       // 绝对定位输入的目标位置
    private double _moveVelocity = 100;   // 定位速度

    public AxisRow(IAxisController axis) { _axis = axis; }

    public string Name => _axis.Name;
    public int AxisId => _axis.AxisId;
    public double MinPos => _axis.Configuration.MinPosition;
    public double MaxPos => _axis.Configuration.MaxPosition;

    /// <summary>当前位置(绑 UI 表盘,实时刷新)。</summary>
    public double CurrentPosition
    {
        get => _axis.CurrentPosition;
        private set { }   // 写空,因为外部更新不了 — 实际值从轴直接读
    }

    /// <summary>当前状态(Idle/Moving/...)。</summary>
    public string StateText
    {
        get
        {
            var s = _axis.State;
            return s switch
            {
                AxisState.Idle => _axis.IsHomed ? "空闲(已回零)" : "空闲(未回零)",
                AxisState.Moving => "运动中",
                AxisState.Homing => "回零中",
                AxisState.Alarm => "⚠ 报警",
                _ => s.ToString()
            };
        }
    }

    public double MoveTarget { get => _moveTarget; set { _moveTarget = value; OnChanged(); } }
    public double MoveVelocity { get => _moveVelocity; set { _moveVelocity = value; OnChanged(); } }
    public double JogSpeed { get => _jogTarget; set { _jogTarget = value; OnChanged(); } }

    // —— 命令(每个按钮一条) ——
    public ICommand HomeCommand => new MotionRelayCommand(_ => Run(() => _axis.HomeAsync()), _ => _axis.State == AxisState.Idle);
    public ICommand MoveAbsCommand => new MotionRelayCommand(
        _ => Run(() => _axis.MoveAbsoluteAsync(_moveTarget, _moveVelocity)),
        _ => _axis.State == AxisState.Idle && _axis.IsHomed);
    public ICommand MoveRelCommand => new MotionRelayCommand(
        _ => Run(() => _axis.MoveRelativeAsync(_moveTarget, _moveVelocity)),
        _ => _axis.State == AxisState.Idle && _axis.IsHomed);
    public ICommand StopCommand => new MotionRelayCommand(_ => Run(() => _axis.StopAsync()), _ => _axis.State != AxisState.Idle);
    public ICommand ResetAlarmCommand => new MotionRelayCommand(_ => Run(() => _axis.ResetAlarmAsync()), _ => _axis.State == AxisState.Alarm);

    // —— Jog 是按住按钮的特殊场景:外部 PreviewMouseDown → 调 Jog+,松开 → 调 Stop ——
    public void JogPositive() => Run(() => _axis.JogAsync(Math.Abs(_jogTarget)));
    public void JogNegative() => Run(() => _axis.JogAsync(-Math.Abs(_jogTarget)));
    public void JogStop() => Run(() => _axis.StopAsync());

    /// <summary>UI 状态刷新辅助(状态变化时调,触发 PropertyChanged 让绑定更新)。</summary>
    public void RaiseCommandsChanged()
    {
        OnChanged(nameof(StateText));
        OnChanged(nameof(CurrentPosition));
    }

    private void Run(Func<Task> f)
    {
        try { f().GetAwaiter().GetResult(); }
        catch (Exception ex) { System.Windows.MessageBox.Show($"{_axis.Name} 命令失败:{ex.Message}", "运动控制错误"); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class MotionRelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;
    public MotionRelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged;
}
