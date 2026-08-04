using System.Windows;
using DaqMonitor.Core.Acquisition;
using DaqMonitor.Core.AppServices;
using DaqMonitor.Core.Devices;
using DaqMonitor.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DaqMonitor.UI;

public partial class App : Application
{
    /// <summary>全局 DI 容器，供各处取服务。真实工程常用 ServiceProvider 做组合根。</summary>
    public static ServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1) 组合根：一次性把 Core 全部服务装配好（含 SimulatedDevice + 报警规则）
        Services = Bootstrapper.Build();

        // 2) 把设备挂到统一采集管道，并连接（Start 由 UI 按钮触发）
        var device = Services.GetRequiredService<IDevice>();
        var pipeline = Services.GetRequiredService<AcquisitionPipeline>();
        pipeline.Register(device);
        device.Connect();

        // 3) 用 DI 解析出的服务构造 ViewModel，再交给 MainWindow 作为 DataContext
        var vm = new MainViewModel(Services);
        var window = new MainWindow { DataContext = vm };
        window.Closed += (_, _) => Services.Dispose(); // 退出时释放容器
        window.Show();
    }
}
