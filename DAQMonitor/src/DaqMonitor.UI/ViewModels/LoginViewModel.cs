using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using DaqMonitor.Core.Auth;

namespace DaqMonitor.UI.ViewModels;

/// <summary>
/// 登录窗 ViewModel。
///
/// 类比前端:跟 Vue/React 的 login 组件 state 一模一样 — username/password 双向绑定 + loading + 提交。
/// 区别:不通过 axios 走网络,直接调本地 AuthService(模式 B 单机认证)。
/// </summary>
public class LoginViewModel : INotifyPropertyChanged
{
    private readonly AuthService _auth;
    private string _username = "admin";
    private string _password = "";
    private string _error = "";
    private bool _isBusy;

    // XAML 设计时用(Blend / VS 设计器需要无参构造)。运行时用 DI 注入。
    public LoginViewModel() : this(null!) { }

    public LoginViewModel(AuthService auth)
    {
        _auth = auth;
        LoginCommand = new DelegateCommand(async _ => await LoginAsync(), _ => !IsBusy);
    }

    public string Username
    {
        get => _username;
        set { _username = value; OnChanged(); }
    }

    public string Password
    {
        get => _password;
        set { _password = value; OnChanged(); }
    }

    public string Error
    {
        get => _error;
        set { _error = value; OnChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            OnChanged();
            LoginCommand?.RaiseCanExecuteChanged();
        }
    }

    public DelegateCommand? LoginCommand { get; }

    private async Task LoginAsync()
    {
        if (_auth is null) { Error = "认证服务未初始化"; return; }
        if (IsBusy) return;

        Error = "";
        IsBusy = true;
        try
        {
            var (ok, err) = await _auth.LoginAsync(Username, Password);
            if (ok)
            {
                // 登录成功:把登录窗 DialogResult=true,App.xaml.cs 关 login 开 main
                foreach (Window w in Application.Current.Windows)
                    if (w is Views.LoginWindow lw) { lw.DialogResult = true; break; }
            }
            else
            {
                Error = err;
            }
        }
        catch (Exception ex)
        {
            Error = $"登录异常: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>极简 ICommand 实现(登录场景不需要 RelayCommand 全套,够用就行)。</summary>
public class DelegateCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    public DelegateCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public async void Execute(object? parameter) => await _execute(parameter);
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
