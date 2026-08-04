using System.Windows;
using DaqMonitor.Core.Acquisition;
using DaqMonitor.Core.AppServices;
using DaqMonitor.Core.Auth;
using DaqMonitor.Core.Devices;
using DaqMonitor.UI.ViewModels;
using DaqMonitor.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DaqMonitor.UI;

public partial class App : Application
{
    /// <summary>全局 DI 容器,供各处取服务。真实工程常用 ServiceProvider 做组合根。</summary>
    public static ServiceProvider Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1) 组合根:一次性把 Core 全部服务装配好(含 SimulatedDevice + 报警规则 + 默认账号)
        Services = Bootstrapper.Build();

        // 2) 先弹登录窗 — 不登录不让用(M17 工业安全:身份先行)
        var auth = Services.GetRequiredService<AuthService>();
        var loginVm = new LoginViewModel(auth);
        var loginWin = new LoginWindow(loginVm);
        var ok = loginWin.ShowDialog();
        if (ok != true)
        {
            // 用户关闭登录窗(取消登录)→ 退出应用
            Shutdown();
            return;
        }

        // 3) 登录成功:启动采集设备(Start 由 UI 按钮触发,这里只 Connect)
        var device = Services.GetRequiredService<IDevice>();
        var pipeline = Services.GetRequiredService<AcquisitionPipeline>();
        pipeline.Register(device);
        device.Connect();

        // 4) 记录审计:系统启动(由谁登录的)
        var audit = Services.GetRequiredService<AuditService>();
        var current = Services.GetRequiredService<ICurrentUserService>();
        await audit.LogSystemAsync("app.startup", detail: $"by {current.Username}");

        // 5) 用 DI 解析出的服务构造 ViewModel,再交给 MainWindow 作为 DataContext
        var vm = new MainViewModel(Services);
        var window = new MainWindow { DataContext = vm };
        window.Closed += async (_, _) =>
        {
            await audit.LogSystemAsync("app.shutdown", detail: $"by {current.Username}");
            Services.Dispose();
        };
        window.Show();
    }
}
