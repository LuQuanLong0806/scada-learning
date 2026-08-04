using System.Windows;
using DaqMonitor.UI.ViewModels;

namespace DaqMonitor.UI.Views;

/// <summary>
/// 登录窗 code-behind。
///
/// 注意:PasswordBox 不支持直接双向绑定(安全考虑 — 密码不能进视觉树/log),
/// 所以这里用 PasswordChanged 事件手动同步到 ViewModel。
/// 这跟前端 &lt;input type="password"&gt; 的设计哲学不一样,WPF 是故意的(防内存泄漏密码)。
/// </summary>
public partial class LoginWindow : Window
{
    public LoginWindow(LoginViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        // 让密码框初始 focus,Enter 键提交
        Loaded += (_, _) => PasswordBox.Focus();
    }

    /// <summary>密码改变时,同步到 ViewModel.Password。</summary>
    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm)
        {
            vm.Password = PasswordBox.Password;
        }
    }
}
