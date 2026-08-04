using DaqMonitor.Core.Acquisition;
using DaqMonitor.Core.Alarms;
using DaqMonitor.Core.Auth;
using DaqMonitor.Core.Cloud;
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Diagnostics;
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DaqMonitor.Core.AppServices;

/// <summary>
/// 组合根（Composition Root）：用 Microsoft.Extensions.DependencyInjection 把 Core 服务串起来。
/// 放在 Core 而不是 UI，是因为它是“整个应用的装配说明书”——UI、测试、未来服务都能复用同一套装配。
/// 真实工程里设备/引擎/存储都从容器取，便于替换与单元测试（A 类必补项：DI 容器）。
///
/// 用法：
///   using var provider = Bootstrapper.Build();
///   var pipeline = provider.GetRequiredService&lt;AcquisitionPipeline&gt;();
///   var device   = provider.GetRequiredService&lt;IDevice&gt;();   // 当前是 SimulatedDevice
///   pipeline.Register(device); device.Connect();             // 把设备挂到统一采集管道
/// </summary>
public static class Bootstrapper
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        // 持久化：SQLite + EF Core。工厂模式便于 BackgroundService / 查询 / 写入各自取短生命周期 DbContext。
        // 数据库文件放 LocalApplicationData（用户可写、随用户隔离、不会被卸载清理）。
        var dbPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DaqMonitor", "daq.db");
        var dbDir = System.IO.Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dbDir)) System.IO.Directory.CreateDirectory(dbDir);

        services.AddDbContextFactory<AppDb>(opt =>
            opt.UseSqlite($"Data Source={dbPath}"));

        // 单例：全局共享一份存储与报警引擎
        services.AddSingleton<PointStore>();
        services.AddSingleton<AlarmEngine>();
        // 诊断/调试服务：采集统计 + 结构化日志，UI 的诊断面板直接绑它
        services.AddSingleton<DiagnosticsService>();

        // —— M17 工业安全:用户认证 + 审计日志(单例,整个应用生命周期共享) ——
        services.AddSingleton<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<AuditService>();
        services.AddSingleton<AuthService>();

        // 管道：定时 200ms 批量出队（见 AcquisitionPipeline，统一采集架构）
        services.AddSingleton<AcquisitionPipeline>(_ => new AcquisitionPipeline(TimeSpan.FromMilliseconds(200)));

        // 设备：当前用模拟设备，没有真实硬件也能跑通整条链路。
        // M1/M3 落地真实串口 / PLC 设备后，只需在这里把 SimulatedDevice 换成对应实现，
        // UI 与采集层代码一行都不用改（面向接口编程的胜利）。例如接真实串口：
        //   services.AddSingleton<IDevice>(_ => new SerialDevice(1, "COM3", new RealSerialChannel("COM3", 9600)));
        // 想零硬件先验证协议解析，则用 LoopbackSerialChannel 同上替换。
        //
        // —— M16 新设备（CAN / USB-HID），同一套路，换一行即可接入：——
        //   services.AddSingleton<IDevice>(_ => new CanDevice(2, "CAN-01", new SimulatedCanChannel()));
        //   services.AddSingleton<IDevice>(_ => new UsbHidDevice(3, "HID-01", new SimulatedHidChannel()));
        // 真实硬件形态：把 SimulatedXxxChannel 换成 PCANChannel / HidLibrary 实现的 ICanChannel / IHidChannel。
        //
        // —— M2 / M3 新设备（Modbus / PLC），同一套路，换一行即可接入：——
        //   零硬件先验证整条链路（simulate 模式，后台轮询产生值）：
        //   services.AddSingleton<IDevice>(_ => new ModbusDevice(1, "MB-01", slave: 1,
        //       new[] { new ModbusDevice.RegisterMap(1, 0, "float"),
        //               new ModbusDevice.RegisterMap(2, 1, "word") }, simulate: true));
        //   services.AddSingleton<IDevice>(_ => new PlcDevice(2, "PLC-01",
        //       new[] { new PlcDevice.PlcMap(3, "DB1.DBW0") }, simulate: true));
        //   真实硬件（有串口 / PLC 时，simulate 改 false，并填端口 / IP；浮点务必抓帧确认 ByteOrder）：
        //   services.AddSingleton<IDevice>(_ => new ModbusDevice(1, "MB-01", slave: 1,
        //       new[] { new ModbusDevice.RegisterMap(1, 0, "float", ModbusFrameParser.ByteOrder.CDAB) },
        //       simulate: false, portName: "COM3", baud: 9600));
        //
        // —— M9 容错 + M15 联调落地：给设备加“心跳探活 + 断线重连” ——
        //   var dev = new CanDevice(2, "CAN-01", new SimulatedCanChannel());
        //   services.AddSingleton<IDevice>(dev);
        //   services.AddSingleton(_ => new DeviceHealthMonitor(
        //       dev, heartbeat: () => Task.Run(() => dev.Read(1)),  // 探活：读一个值
        //       heartbeatIntervalMs: 5000, missThreshold: 2,
        //       log: m => Console.WriteLine("[health] " + m)));
        //   // 真实运行：provider.GetRequiredService<DeviceHealthMonitor>().Start();
        services.AddSingleton<IDevice>(_ => new SimulatedDevice(1, "Sim-01", 1, 2, 3));

        // —— M11 TcpDevice：以工厂方法注册，便于运行时按 host/port 创建多个实例 ——
        // 用法：var factory = provider.GetRequiredService<Func<string,int,IEnumerable<TcpDevice.TcpMap>,bool,TcpDevice>>();
        //   var maps = new[] { new TcpDevice.TcpMap(1, 1), new TcpDevice.TcpMap(2, 2) };
        //   var tcp = factory("192.168.1.10", 502, maps, simulate: false);
        //   pipeline.Register(tcp); tcp.Connect();
        services.AddSingleton<Func<string, int, IEnumerable<TcpDevice.TcpMap>, bool, TcpDevice>>(
            _ => (host, port, maps, simulate) => new TcpDevice(
                id: 100 + ((host, port).GetHashCode() & 0x7FFF),
                name: $"TCP-{host}:{port}",
                host: host,
                port: port,
                maps: maps,
                simulate: simulate));

        // 简化版工厂：不带点位映射，开模拟模式直接跑（最常用的“先跑通”入口）
        services.AddSingleton<Func<string, int, TcpDevice>>(sp => (host, port) =>
            sp.GetRequiredService<Func<string, int, IEnumerable<TcpDevice.TcpMap>, bool, TcpDevice>>()
              .Invoke(host, port, Enumerable.Empty<TcpDevice.TcpMap>(), true));

        // —— M7 MQTT 上云：单例，IAsyncDisposable 由容器统一释放 ——
        // 用法：var mqtt = provider.GetRequiredService<MqttPublisher>();
        //       mqtt.OnCommand = async (topic, payload) => { /* 处理下行命令 */ };
        //       await mqtt.StartAsync();
        //       pipeline.BatchReady += (_, batch) => { foreach (var p in batch) mqtt.Enqueue(p); };
        services.AddSingleton<MqttPublisher>(_ => new MqttPublisher("broker.emqx.io", 1883, deviceId: "daq-01"));

        var provider = services.BuildServiceProvider();

        // 启动期一次性建库建表（用 EnsureCreated：简单，不依赖迁移；首次运行后会生成 daq.db）。
        // 真实生产后期想加字段时，可改用 Migrate() + EF Core 迁移脚手架，不影响当前调用方。
        using (var scope = provider.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDb>>();
            using var db = factory.CreateDbContext();
            db.Database.EnsureCreated();
            SeedDefaultUsers(db);
        }

        // 预置两条报警规则：点位 1 超 100 判 Critical、点位 2 超 100 判 Warning（带回滞 2，防抖动）
        var alarms = provider.GetRequiredService<AlarmEngine>();
        alarms.Add(new AlarmRule { PointId = 1, Threshold = 100, IsHigh = true, Level = AlarmLevel.Critical, Hysteresis = 2 });
        alarms.Add(new AlarmRule { PointId = 2, Threshold = 100, IsHigh = true, Level = AlarmLevel.Warning, Hysteresis = 2 });

        return provider;
    }

    /// <summary>
    /// 种子默认账号:3 角色 demo(开发期用,生产部署应首次启动让管理员自设密码)。
    /// 默认密码:
    ///   admin / admin123      (管理员,全部权限)
    ///   engineer / engineer123 (工程师,改配方/参数 + 看审计)
    ///   operator / operator123 (操作工,只读 + 启停采集)
    /// 已有数据则跳过(EnsureCreated 后调,幂等)。
    /// </summary>
    private static void SeedDefaultUsers(AppDb db)
    {
        if (db.Users.Any()) return;  // 已有用户,不重复种子

        db.Users.AddRange(
            new User
            {
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123", workFactor: 11),
                Role = UserRole.Admin,
                DisplayName = "系统管理员",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new User
            {
                Username = "engineer",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("engineer123", workFactor: 11),
                Role = UserRole.Engineer,
                DisplayName = "工艺工程师",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new User
            {
                Username = "operator",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("operator123", workFactor: 11),
                Role = UserRole.Operator,
                DisplayName = "操作工",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        db.SaveChanges();
    }
}
