using System.Windows.Input;

namespace DaqMonitor.UI.ViewModels;

/// <summary>
/// 极简 ICommand 实现：把按钮点击映射到 ViewModel 里的一个方法。
/// （M8 会讲更完整的 MVVM；这里先用它能跑、能演示就够了。）
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);
}
