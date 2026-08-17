# R8 · WPF 主界面(给系统装上"脸")

> **定位**:R1-R7 的所有能力都活在控制台和测试里。这一篇把整条链路接到 WPF 主界面:点位表(仪表盘+状态灯)、报警日志、实时曲线、诊断面板——一个能写进简历、面试能现场演示的完整上位机。
> **前置**:R7 全绿。**预计敲码**:150 分钟(本篇代码量最大,含 3 个 XAML + 2 个自定义控件模板)。
> **产出**:DaqMonitor.UI 主屏可运行。**主屏先行策略**:参考工程主界面还含登录窗/配方管理/运动控制/报表导出,依赖 R9+ 的服务,本篇先删掉这些、留好"加回"的锚点,R9+ 各篇逐个装回。
> **测试**:本篇不加新测试(UI 交互测试成本高,主线用 56 个既有测试守住 Core 层),验收靠 build + 手动清单。

---

## 🎯 本篇交付物

```
src/DaqMonitor.UI/
├─ App.xaml / App.xaml.cs            # 启动接线:Build→Register→Connect→MainWindow(R8 无登录)
├─ ViewModels/
│  ├─ RelayCommand.cs                # 可绑定命令(原文)
│  └─ MainViewModel.cs               # 主屏 VM(R8 删权限/配方/运控/导出)
├─ Controls/
│  ├─ GaugeControl.cs                # 自定义控件①:量程指针表(原文)
│  ├─ StatusDot.cs                   # 自定义控件②:设备状态灯(原文)
│  └─ ThemeInfo.cs                   # 让 Generic.xaml 生效的程序集特性(原文)
├─ Themes/Generic.xaml               # 两控件的默认样式/模板(原文)
├─ Views/ChartView.xaml / .xaml.cs   # LiveCharts2 实时曲线(原文)
├─ Diagnostics/DiagnosticsPanel.xaml / .xaml.cs  # 诊断面板 UserControl(原文)
└─ MainWindow.xaml / .xaml.cs        # 主窗口组装(R8 删 2 个 Tab + 导出条)
(删除:AssemblyInfo.cs —— 见 ⓪ 的坑)
```

## 📋 需求单(先自己设计,再对照)

| # | 需求 | 验收 |
|---|---|---|
| FR8-1 | 启动接线:App.OnStartup → `Bootstrapper.Build()` → `pipeline.Register(device)` + `device.Connect()` → `new MainWindow{DataContext=new MainViewModel(Services)}`;窗口关闭 `Services.Dispose()` | 启动直接进主窗,无登录 |
| FR8-2 | [RelayCommand](kp:relaycommand):ICommand 极简实现,CanExecuteChanged 挂 `CommandManager.RequerySuggested` | 按钮绑命令;IsRunning 翻转后启停按钮自动灰亮互换 |
| FR8-3 | MainViewModel:PointView 展示模型([INotifyPropertyChanged](kp:binding));`BatchReady` 里用 [Dispatcher](kp:dispatcher).Invoke 更新存储+报警+点位表;报警事件插入日志并同步 Level 给表盘 | 启动采集后点位表出现 3 行,数值/时间持续跳动 |
| FR8-4 | 自定义控件 [GaugeControl/StatusDot](kp:mvvm):继承 Control + DependencyProperty + Generic.xaml 默认模板;ThemeInfo 特性指到本程序集 | 表盘指针随值转;报警时环变橙/红;状态灯绿点 |
| FR8-5 | [ChartView](kp:livecharts) 实时曲线:LiveCharts2 两条 LineSeries,ObservableCollection 滚动缓冲 600 点(60 秒) | 曲线页两条线滚动推进 |
| FR8-6 | DiagnosticsPanel(UserControl):绑 `DiagnosticsSummary` 一行式统计 + `DiagnosticsLog` 环形日志 | 诊断页统计数字实时增长 |
| FR8-7 | 主窗口组装:顶部标题+启停+状态文字,左点位表(DataGrid:点位/仪表/状态/时间),右 TabControl(报警日志/实时曲线/诊断),底部架构说明 | 全布局可用,启动/停止切换状态文字 |

**自己先想 10 分钟**:
1. `SensorPoint` 是 struct,为什么不直接把 `List<SensorPoint>` 绑给 DataGrid,而要转一层 PointView?([struct-vs-class](kp:struct-vs-class):struct 赋值即拷贝、没有属性通知,UI 要的是"活的、会自己报信的"行对象)
2. `BatchReady` 在后台线程触发,直接在里面改 `ObservableCollection` 会怎样?怎么破?([Dispatcher](kp:dispatcher):WPF UI 元素只许 UI 线程碰,后台先 `Dispatcher.Invoke` 切线程)
3. RelayCommand 的 `CanExecuteChanged` 为什么直接挂 `CommandManager.RequerySuggested`,而不是自己搞个事件?(借 WPF 全局重查机制:任何输入/焦点变化时所有命令自动重问 CanExecute,不用手动 Notify)
4. GaugeControl 为什么继承 `Control` 而不是 `UserControl`?(外观全部交给 Generic.xaml 模板,可换肤、可当基础件跨项目复用——正是 JD 里的"熟练自绘控件")
5. 为什么 ChartView 的接线放在 `MainWindow` 的 `DataContextChanged` 里,而不是构造函数?(`new MainWindow{DataContext=vm}` 先构造窗口、后赋 DataContext,构造时 VM 还不存在)

## 📚 本篇知识点

- [MVVM 模式](kp:mvvm) · [数据绑定/INotifyPropertyChanged](kp:binding) · [RelayCommand](kp:relaycommand) · [Dispatcher 跨线程](kp:dispatcher) · [LiveCharts2 实时曲线](kp:livecharts) · [struct vs class](kp:struct-vs-class) · [DI 组合根](kp:di)

## 🛠️ 参考实现

### ⓪ 装包 + 清理模板(有坑)

```bash
dotnet add src/DaqMonitor.UI package LiveChartsCore.SkiaSharpView.WPF --version 2.0.0-rc4.5
```

**坑 ① —— AssemblyInfo.cs 必须删**:R1 的 `dotnet new wpf` 模板生成了 `AssemblyInfo.cs`,里面自带一条 `[assembly: ThemeInfo(...)]`;本篇又要加 `Controls/ThemeInfo.cs`(参考工程如此),两条程序集特性重复 → 编译错 **CS0579 Duplicate 'ThemeInfo' attribute**。参考工程的做法是删掉 AssemblyInfo.cs(它只有这一段内容):

```bash
rm src/DaqMonitor.UI/AssemblyInfo.cs
```

**坑 ② —— App.xaml 删掉 StartupUri**:模板的 App.xaml 带着 `StartupUri="MainWindow.xaml"`,不删的话 WPF 会自动再开一个**没有 DataContext 的空 MainWindow**(加上 OnStartup 里手工开的那个 = 两个窗口)。改成下面这样(对照参考工程,xmlns:local 留着无害):

> 📂 `src/DaqMonitor.UI/App.xaml`

```xml
<Application x:Class="DaqMonitor.UI.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:DaqMonitor.UI">
    <Application.Resources>
    </Application.Resources>
</Application>
```

csproj 最终状态(模板属性不变,只多 LiveCharts 一个包;参考工程另有 ClosedXML,是 R9+ 报表篇的):

> 📂 `src/DaqMonitor.UI/DaqMonitor.UI.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\DaqMonitor.Core\DaqMonitor.Core.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <RollForward>Major</RollForward>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
  </PropertyGroup>

  <ItemGroup>
    <!-- LiveCharts2 实时曲线(M5 落地):Core 2.x 跨平台,WPF 用 SkiaSharpView -->
    <PackageReference Include="LiveChartsCore.SkiaSharpView.WPF" Version="2.0.0-rc4.5" />
  </ItemGroup>

</Project>
```

### ① App.xaml.cs —— 启动接线(R8 版:无登录)

> 📂 `src/DaqMonitor.UI/App.xaml.cs`
> 💡 参考工程这里先弹 `LoginWindow`、登录成功才进主窗,并写审计日志——依赖 R9+ 的 AuthService/AuditService。R8 先直进主窗,R9+ 认证篇把 2)~4) 步装回来。

```csharp
using System.Windows;
using DaqMonitor.Core.Acquisition;
using DaqMonitor.Core.AppServices;
using DaqMonitor.Core.Devices;
using DaqMonitor.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DaqMonitor.UI;

public partial class App : Application
{
    /// <summary>全局 DI 容器,供各处取服务。真实工程常用 ServiceProvider 做组合根。</summary>
    public static ServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1) 组合根:一次性把 Core 全部服务装配好(含 SimulatedDevice + 报警规则)
        Services = Bootstrapper.Build();

        // 2) 启动采集链路(Start 由 UI 按钮触发,这里只 Connect)
        var device = Services.GetRequiredService<IDevice>();
        var pipeline = Services.GetRequiredService<AcquisitionPipeline>();
        pipeline.Register(device);
        device.Connect();

        // 3) 用 DI 解析出的服务构造 ViewModel,再交给 MainWindow 作为 DataContext
        //    (R9+ 认证篇:这里先弹 LoginWindow,登录成功才进主窗)
        var vm = new MainViewModel(Services);
        var window = new MainWindow { DataContext = vm };
        window.Closed += (_, _) => Services.Dispose();
        window.Show();
    }
}
```

### ② RelayCommand —— 可绑定命令(原文)

> 📂 `src/DaqMonitor.UI/ViewModels/RelayCommand.cs`
> 🔧 无 NuGet · 💡 把"点击该干嘛"变成 VM 上的属性,XAML `Command="{Binding StartCommand}"` 直接绑

```csharp
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
```

### ③ MainViewModel —— 主屏 VM(R8 删减版)

> 📂 `src/DaqMonitor.UI/ViewModels/MainViewModel.cs`
> 💡 删了什么:当前用户/角色区、权限判断的 Can* 表达式、配方/运控两个子 VM、报表导出、登出——全部依赖 R9+ 服务。保留主干:BatchReady → 表格/存储/报警,报警事件 → 日志/表盘变色,Start/Stop
> 💡 看三处精髓:**PointView 为什么是 class**(struct 拷贝+无通知,绑不了 UI)、**Dispatcher.Invoke 包住整个批量更新**(一次跨线程,整批处理)、**_levels 字典把报警级别带回下次批量刷新**(报警恢复后表盘能变回蓝)
> 🗺️ **新手读码地图**(顺着"一批数据的旅程"看,VM 只是接线员):1. 构造函数干的全是**接线**:从 DI 容器领服务 → 造两个 ICommand → 订阅 3 个事件(管道 BatchReady、报警触发/恢复)。VM 自己不采集、不存库、不判报警,全是 R5-R7 的活 2. `OnBatchReady` 是主数据流:一批点进来 → **一次** `Dispatcher.Invoke` 切到 UI 线程(后台事件线程不能直接改 ObservableCollection)→ 循环里每条点走三步:写库 `_store.AddOrUpdate`、喂报警 `_alarms.Evaluate`、刷表格(找不到行就 Add 新 PointView,找到就改属性——属性 setter 触发 PropertyChanged,DataGrid 自动重画) 3. `_levels` 字典解决一个时序问题:报警事件可能在两批数据之间到,而表盘颜色跟着批量刷——所以报警先记进 `_levels`,每次批量刷新时 `TryGetValue` 同步给行(331 行),颜色就不丢 4. `OnAlarmTriggered/Cleared` 是旁路:插一条日志到 AlarmLog 头部(最新的在最上面)+ 立刻改该行 Level 让表盘变红 5. `Start/Stop` 只是拨开关:`SimulatedDevice.Start(100ms)`,IsRunning 一翻,四个绑定属性(按钮可用性)跟着变。**前端类比**:VM ≈ React 容器组件——`Dispatcher.Invoke` ≈ setState 必须在 React 上下文里;`OnChanged(nameof(X))` ≈ 手动触发一次针对性 re-render;`_levels` ≈ 用 ref 存一份"跨 render 也要活着"的中间状态。

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using DaqMonitor.Core.Acquisition;
using DaqMonitor.Core.Alarms;
using DaqMonitor.Core.AppServices;
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Diagnostics;
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Store;
using DaqMonitor.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DaqMonitor.UI.ViewModels;

/// <summary>点位在界面上的展示模型（值类型 SensorPoint 不适合直接绑 UI，转成带通知的属性）。</summary>
public class PointView : INotifyPropertyChanged
{
    private int _id;
    private double _value;
    private DateTime _timestamp;
    private DeviceState _state;
    private AlarmLevel _level = AlarmLevel.Normal;

    public int Id { get => _id; set { _id = value; OnChanged(); } }
    public double Value { get => _value; set { _value = value; OnChanged(); } }
    public DateTime Timestamp { get => _timestamp; set { _timestamp = value; OnChanged(); } }
    public DeviceState State { get => _state; set { _state = value; OnChanged(); } }
    /// <summary>当前报警级别，驱动 GaugeControl 表盘变色（M6 报警 → M14 控件的跨模块复用演示）。</summary>
    public AlarmLevel Level { get => _level; set { _level = value; OnChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// 主窗口 ViewModel：从 DI 容器取出真实服务（管道 / 存储 / 报警引擎 / 设备 / 诊断服务），
/// 把后台采集事件接入 UI。
/// R8 版:不含登录/权限/配方/运控/报表(R9+ 各篇加回),专注“采集数据 → 界面”主线。
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly PointStore _store;
    private readonly AcquisitionPipeline _pipeline;
    private readonly AlarmEngine _alarms;
    private readonly IDevice _device;
    private readonly DiagnosticsService _diag;
    private readonly Dictionary<int, AlarmLevel> _levels = new();
    private bool _running;
    private ChartView? _chart;

    public ObservableCollection<PointView> Points { get; } = new();
    public ObservableCollection<string> AlarmLog { get; } = new();

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }

    /// <summary>诊断面板绑定的“一行式”统计摘要（每次批量后刷新）。</summary>
    public string DiagnosticsSummary
        => $"采样 {_diag.TotalSamples} 点 | 报警 {_diag.AlarmCount} 次 | 批次 {_diag.BatchCount} | 末批 {_diag.LastBatchMs}ms | 运行 {_diag.Uptime:hh\\:mm\\:ss}";

    /// <summary>诊断面板绑定的日志视图（环形缓冲，最多 200 条）。</summary>
    public ReadOnlyObservableCollection<string> DiagnosticsLog => _diag.Log;

    public bool IsRunning
    {
        get => _running;
        private set
        {
            _running = value;
            OnChanged();
            // 运行状态翻转 → 启停按钮的可用性也跟着变
            OnChanged(nameof(CanStartAcquisition));
            OnChanged(nameof(CanStopAcquisition));
        }
    }

    /// <summary>启动采集按钮 IsEnabled:当前未在跑。(R9+ 认证篇:前面再 && 上权限判断)</summary>
    public bool CanStartAcquisition => !IsRunning;

    /// <summary>停止采集按钮 IsEnabled:当前正在跑。</summary>
    public bool CanStopAcquisition => IsRunning;

    public MainViewModel(ServiceProvider services)
    {
        _store = services.GetRequiredService<PointStore>();
        _pipeline = services.GetRequiredService<AcquisitionPipeline>();
        _alarms = services.GetRequiredService<AlarmEngine>();
        _device = services.GetRequiredService<IDevice>();
        _diag = services.GetRequiredService<DiagnosticsService>();

        StartCommand = new RelayCommand(_ => Start());
        StopCommand = new RelayCommand(_ => Stop());

        _pipeline.BatchReady += OnBatchReady;
        _alarms.AlarmTriggered += OnAlarmTriggered;
        _alarms.AlarmCleared += OnAlarmCleared;

        _diag.RecordInfo("应用启动，DI 容器已装配（设备/管道/存储/报警/诊断）。");
    }

    /// <summary>由 MainWindow 注入：把实时曲线页接到 BatchReady。</summary>
    public void AttachChart(ChartView chart)
    {
        _chart = chart;
        chart.StartDemo();   // 演示模式：无外部数据源时自动生成曲线
    }

    private void OnBatchReady(object? _, IReadOnlyList<SensorPoint> batch)
    {
        // 用 Stopwatch 给“批量处理耗时”计时 —— 工业排查“卡顿/丢点”的第一指标
        var sw = Stopwatch.StartNew();
        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var p in batch)
            {
                _store.AddOrUpdate(p);
                _alarms.Evaluate(p);   // 跑报警规则（命中只在上上升沿通知）

                PointView? row = Points.FirstOrDefault(x => x.Id == p.Id);
                if (row is null)
                {
                    row = new PointView { Id = p.Id, Value = p.Value, Timestamp = p.Timestamp, State = p.State };
                    Points.Add(row);
                }
                else
                {
                    row.Value = p.Value;
                    row.Timestamp = p.Timestamp;
                    row.State = p.State;
                }
                // 把“当前报警级别”同步给控件（没有报警就保持 Normal → 蓝环）
                if (_levels.TryGetValue(p.Id, out var lv)) row.Level = lv;
            }
            OnChanged(nameof(DiagnosticsSummary));
        });
        sw.Stop();
        _diag.RecordBatch(batch.Count, sw.ElapsedMilliseconds);
    }

    private void OnAlarmTriggered(object? _, AlarmEvent e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            AlarmLog.Insert(0, $"[{DateTime.Now:HH:mm:ss}] 点位 {e.PointId} → {e.Level} 报警，值 = {e.Value}");
            _levels[e.PointId] = e.Level;                 // 记住级别，下次批量刷新时同步给控件
            var row = Points.FirstOrDefault(x => x.Id == e.PointId);
            if (row is not null) row.Level = e.Level;     // 表盘立即变橙/红（GaugeControl.Level 驱动）
        });
        _diag.RecordAlarm(e.PointId, e.Level.ToString(), e.Value);
    }

    private void OnAlarmCleared(object? _, AlarmEvent e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _levels[e.PointId] = AlarmLevel.Normal;       // 复位
            var row = Points.FirstOrDefault(x => x.Id == e.PointId);
            if (row is not null) row.Level = AlarmLevel.Normal;   // 表盘恢复蓝环
        });
        _diag.RecordInfo($"点位 {e.PointId} 报警恢复（值回到正常区间）。");
    }

    private void Start()
    {
        if (IsRunning) return;
        if (_device is SimulatedDevice sd) sd.Start(TimeSpan.FromMilliseconds(100));
        _diag.RecordInfo($"启动采集：{_device.Name}（模拟设备）。");
        IsRunning = true;
    }

    private void Stop()
    {
        if (!IsRunning) return;
        if (_device is SimulatedDevice sd) sd.Stop();
        _diag.RecordInfo("停止采集。");
        IsRunning = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
```

### ④ 自定义控件三件套 + 默认模板(原文 ×4)

> 📂 `src/DaqMonitor.UI/Controls/GaugeControl.cs`
> 💡 "自绘控件"最正宗写法:继承 Control、无 xaml.cs、外观全在 Generic.xaml、对外只有 DependencyProperty。指针角度 = -135° + 量程比例 × 270°

```csharp
using System.Windows;
using System.Windows.Controls;
using DaqMonitor.Core.Models;

namespace DaqMonitor.UI.Controls;

/// <summary>
/// 自定义控件①：量程指针表（Gauge）。
/// 这是 WPF 里最"正宗"的自定义控件写法 —— 继承自 <see cref="Control"/>，
/// 外观全部交给 <c>Themes/Generic.xaml</c> 里的默认 <see cref="Style"/>（没有 xaml.cs 后台代码），
/// 对外只暴露一组 DependencyProperty 供 XAML 绁定。
///
/// 为什么不用 UserControl？UserControl 是把现有控件"拼"起来（适合页面级复用）；
/// 而"自定义控件"强调一套可换肤、可继承、可在不同项目里当基础件用的控件 —— 正是 JD 点名的"熟练自绘控件"。
///
/// 在 DAQMonitor 里它直接绑 <c>PointView.Value</c>，让每个点位一眼看出当前读数；
/// M12 工程量转换后，绑定的 Value 会变成"工程量"（如 ℃、MPa），控件零改动。
/// </summary>
public class GaugeControl : Control
{
    /// <summary>告诉 WPF：本控件的默认样式去 Generic.xaml 里找 TargetType=GaugeControl 的那条。</summary>
    static GaugeControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(GaugeControl),
            new FrameworkPropertyMetadata(typeof(GaugeControl)));
    }

    // ---- 依赖属性：控件对外暴露的全部"可绑定点" ----
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(GaugeControl),
            new PropertyMetadata(0d, (d, _) => RecalcAngle(d)));

    public static readonly DependencyProperty MinProperty =
        DependencyProperty.Register(nameof(Min), typeof(double), typeof(GaugeControl),
            new PropertyMetadata(0d, (d, _) => RecalcAngle(d)));

    public static readonly DependencyProperty MaxProperty =
        DependencyProperty.Register(nameof(Max), typeof(double), typeof(GaugeControl),
            new PropertyMetadata(150d, (d, _) => RecalcAngle(d)));

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(GaugeControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(GaugeControl),
            new PropertyMetadata(string.Empty));

    /// <summary>报警级别：Normal 蓝环 / Warning 橙环 / Critical 红环（M6 报警引擎会驱动它）。</summary>
    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(nameof(Level), typeof(AlarmLevel), typeof(GaugeControl),
            new PropertyMetadata(AlarmLevel.Normal));

    /// <summary>指针角度（度）。由 Value/Min/Max 算出，XAML 模板里绑给指针的 RotateTransform。</summary>
    public static readonly DependencyProperty NeedleAngleProperty =
        DependencyProperty.Register(nameof(NeedleAngle), typeof(double), typeof(GaugeControl),
            new PropertyMetadata(-135d));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Min { get => (double)GetValue(MinProperty); set => SetValue(MinProperty, value); }
    public double Max { get => (double)GetValue(MaxProperty); set => SetValue(MaxProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public AlarmLevel Level { get => (AlarmLevel)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public double NeedleAngle { get => (double)GetValue(NeedleAngleProperty); set => SetValue(NeedleAngleProperty, value); }

    /// <summary>把当前 Value 映射到 -135°~+135°（270° 量程，缺口在底部），即表针角度。</summary>
    private static void RecalcAngle(DependencyObject d)
    {
        var g = (GaugeControl)d;
        var max = g.Max;
        if (max <= g.Min) max = g.Min + 1;
        var ratio = (g.Value - g.Min) / (max - g.Min);
        ratio = Math.Max(0, Math.Min(1, ratio));
        g.NeedleAngle = -135 + ratio * 270;
    }
}
```

> 📂 `src/DaqMonitor.UI/Controls/StatusDot.cs`
> 💡 同套路再来一个;Connecting 状态的脉冲动画写在模板 Storyboard 里,代码零动画

```csharp
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
```

> 📂 `src/DaqMonitor.UI/Controls/ThemeInfo.cs`
> 💡 没有这一行(或 AssemblyInfo.cs 里那条),Generic.xaml 找不到 → 控件**不报错但画不出来**(空白)。这就是 ⓪ 坑①要删 AssemblyInfo.cs 的原因:两条 ThemeInfo 重复编译错 CS0579,留哪条?留这条(参考工程的选择,两个参数都是 SourceAssembly)

```csharp
using System.Windows;

// 关键：告诉 WPF "本程序集里自定义控件的默认主题字典在程序集内部（Themes/Generic.xaml）"。
// 没有这一行，GaugeControl / StatusDot 会因为找不到默认 Style 而"画不出来"（不报错但空白）。
// 这正是自定义控件 vs UserControl 最容易踩的坑，M14 会专门讲。
[assembly: ThemeInfo(
    ResourceDictionaryLocation.SourceAssembly,
    ResourceDictionaryLocation.SourceAssembly)]
```

> 📂 `src/DaqMonitor.UI/Themes/Generic.xaml`
> 💡 两个控件的默认 ControlTemplate:表盘 = 双椭圆环 + 旋转矩形指针 + DataTrigger 按 Level 换色;状态灯 = 圆点 + Connecting 脉冲 Storyboard。**文件必须在 `Themes/` 目录、名字必须叫 `Generic.xaml`**(WPF 约定)

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:local="clr-namespace:DaqMonitor.UI.Controls">

  <!-- ============ GaugeControl 默认样式 ============ -->
  <Style TargetType="{x:Type local:GaugeControl}">
    <Setter Property="Width" Value="120"/>
    <Setter Property="Height" Value="120"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type local:GaugeControl}">
          <Grid>
            <!-- 表盘底环（轨道） -->
            <Ellipse Width="108" Height="108" Stroke="#dde3ee" StrokeThickness="10" Fill="#fbfcfe"/>
            <!-- 报警级别环：默认蓝；Warning 橙虚线；Critical 红加粗（见下方 Trigger） -->
            <Ellipse x:Name="ring" Width="108" Height="108" Stroke="#2f6fed" StrokeThickness="10"/>
            <!-- 指针：一个从表盘中心向上的细矩形，绕中心(0.5,1)旋转 -->
            <Border Width="0" Height="0" HorizontalAlignment="Center" VerticalAlignment="Center">
              <Rectangle Width="4" Height="44" Fill="#2f6fed" RadiusX="2" RadiusY="2"
                         VerticalAlignment="Bottom" HorizontalAlignment="Center"
                         RenderTransformOrigin="0.5,1">
                <Rectangle.RenderTransform>
                  <RotateTransform Angle="{Binding NeedleAngle, RelativeSource={RelativeSource TemplatedParent}}"/>
                </Rectangle.RenderTransform>
              </Rectangle>
            </Border>
            <!-- 中心读数 -->
            <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Margin="0,16,0,0">
              <TextBlock Text="{Binding Value, RelativeSource={RelativeSource TemplatedParent}, StringFormat=F1}"
                         FontSize="20" FontWeight="Bold" HorizontalAlignment="Center"/>
              <TextBlock Text="{Binding Unit, RelativeSource={RelativeSource TemplatedParent}}"
                         FontSize="11" Foreground="Gray" HorizontalAlignment="Center"/>
            </StackPanel>
            <!-- 标签（如 点位编号） -->
            <TextBlock Text="{Binding Label, RelativeSource={RelativeSource TemplatedParent}}"
                       HorizontalAlignment="Center" VerticalAlignment="Bottom" Margin="0,0,0,8"
                       FontSize="11" Foreground="#67708a"/>
          </Grid>
          <ControlTemplate.Triggers>
            <DataTrigger Binding="{Binding Level, RelativeSource={RelativeSource TemplatedParent}}" Value="Warning">
              <Setter TargetName="ring" Property="Stroke" Value="#e0a800"/>
              <Setter TargetName="ring" Property="StrokeDashArray" Value="2 2"/>
              <Setter TargetName="ring" Property="StrokeThickness" Value="12"/>
            </DataTrigger>
            <DataTrigger Binding="{Binding Level, RelativeSource={RelativeSource TemplatedParent}}" Value="Critical">
              <Setter TargetName="ring" Property="Stroke" Value="#e24b4b"/>
              <Setter TargetName="ring" Property="StrokeThickness" Value="12"/>
            </DataTrigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============ StatusDot 默认样式 ============ -->
  <Style TargetType="{x:Type local:StatusDot}">
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type local:StatusDot}">
          <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
            <Ellipse x:Name="dot" Width="12" Height="12" Fill="#28a745"/>
            <TextBlock x:Name="txt" Text="{TemplateBinding Text}" Margin="6,0,0,0"
                       VerticalAlignment="Center" FontSize="12"/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <!-- 正在连接：黄点 + 透明度脉冲动画，模拟"通讯中" -->
            <DataTrigger Binding="{Binding State, RelativeSource={RelativeSource TemplatedParent}}" Value="Connecting">
              <Setter TargetName="dot" Property="Fill" Value="#e0a800"/>
              <DataTrigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard RepeatBehavior="Forever" AutoReverse="True">
                    <DoubleAnimation Storyboard.TargetName="dot"
                                     Storyboard.TargetProperty="Opacity"
                                     From="1" To="0.3" Duration="0 0:0:0.6"/>
                  </Storyboard>
                </BeginStoryboard>
              </DataTrigger.EnterActions>
            </DataTrigger>
            <!-- 离线：红点 -->
            <DataTrigger Binding="{Binding State, RelativeSource={RelativeSource TemplatedParent}}" Value="Offline">
              <Setter TargetName="dot" Property="Fill" Value="#e24b4b"/>
            </DataTrigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

</ResourceDictionary>
```

### ⑤ ChartView —— LiveCharts2 实时曲线(原文 ×2)

> 📂 `src/DaqMonitor.UI/Views/ChartView.xaml`
> 💡 XAML 只放最简壳,Series 由后台代码注入——避免和 ViewModel 的可观察集合强耦合

```xml
<UserControl x:Class="DaqMonitor.UI.Views.ChartView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:lvc="clr-namespace:LiveChartsCore.SkiaSharpView.WPF;assembly=LiveChartsCore.SkiaSharpView.WPF">
    <Grid Margin="6">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Margin="2,0,0,4" FontSize="13" FontWeight="Bold"
                   Text="实时曲线 · 最近 60 秒（温度 / 压力，10Hz）" />

        <!-- CartesianChart 由 ChartView.xaml.cs 在 DataContext 设置后用代码注入 Series；
             XAML 里只用最简控件，避免和 ViewModel 上下文里的可观察集合强耦合 -->
        <lvc:CartesianChart x:Name="Chart" Grid.Row="1" />
    </Grid>
</UserControl>
```

> 📂 `src/DaqMonitor.UI/Views/ChartView.xaml.cs`
> 💡 两条 `ObservableCollection<double>` 当滚动缓冲(超 600 个就 RemoveAt(0));外部可 `Push(SensorPoint)` 喂真实点位,`StartDemo()` 是无数据源的演示模式

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using DaqMonitor.Core.Models;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace DaqMonitor.UI.Views;

/// <summary>
/// M5 实时曲线：LiveCharts2 + ObservableCollection(double) 滚动缓冲。
/// 每 100ms tick 一次（10Hz），最多保留 600 个点（60 秒）。
///
/// 用法：MainWindow 把它放进 TabItem，并绑定/传入两条点位序列；
/// 实战里改成订阅 AcquisitionPipeline.BatchReady，把点位按 id 分流进两条线。
/// </summary>
public partial class ChartView : IDisposable, INotifyPropertyChanged
{
    private const int MaxPoints = 600;
    private const int TickMs = 100;

    private readonly ObservableCollection<double> _temp = new();
    private readonly ObservableCollection<double> _press = new();
    private readonly DispatcherTimer _timer;
    private readonly Random _demo = new();

    /// <summary>PointId → 序列映射：1=温度，2=压力。可外部配置。</summary>
    public int TemperaturePointId { get; set; } = 1;
    public int PressurePointId { get; set; } = 2;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([System.Runtime.CompilerServices.CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public ChartView()
    {
        InitializeComponent();

        Chart.Series = new ISeries[]
        {
            new LineSeries<double>
            {
                Values = _temp,
                Name = "温度 (℃)",
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0.3
            },
            new LineSeries<double>
            {
                Values = _press,
                Name = "压力 (kPa)",
                GeometrySize = 0,
                LineSmoothness = 0.3
            }
        };

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(TickMs) };
        _timer.Tick += OnTick;
    }

    /// <summary>外部喂入真实点位（典型由 MainViewModel 在 BatchReady 中调用）。</summary>
    public void Push(SensorPoint p)
    {
        if (p.Id == TemperaturePointId) PushOne(_temp, p.Value);
        else if (p.Id == PressurePointId) PushOne(_press, p.Value);
    }

    private static void PushOne(ObservableCollection<double> col, double v)
    {
        col.Add(v);
        while (col.Count > MaxPoints) col.RemoveAt(0);
    }

    /// <summary>演示模式：无外部数据源时启用，自动生成温度/压力曲线。</summary>
    public void StartDemo()
    {
        if (!_timer.IsEnabled) _timer.Start();
    }

    public void StopDemo()
    {
        _timer.Stop();
    }

    private void OnTick(object? s, EventArgs e)
    {
        // 演示数据：温度 25±5，压力 80±10
        double t = 25 + _demo.NextDouble() * 10 - 5;
        double p = 80 + _demo.NextDouble() * 20 - 10;
        PushOne(_temp, Math.Round(t, 2));
        PushOne(_press, Math.Round(p, 2));
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
```

### ⑥ DiagnosticsPanel —— 诊断面板 UserControl(原文 ×2)

> 📂 `src/DaqMonitor.UI/Diagnostics/DiagnosticsPanel.xaml`
> 💡 UserControl vs 自定义控件的取舍现场:页面级组合复用用 UserControl(两个绑定就完事),基础件/换肤用 Control

```xml
<UserControl x:Class="DaqMonitor.UI.Diagnostics.DiagnosticsPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d" d:DesignHeight="320" d:DesignWidth="420">
  <!--
    诊断面板：这是「边做边用自定义控件」的第二个例子 —— 用 UserControl 把“统计 + 日志”组合成一个可复用面板。
    和 GaugeControl(继承 Control + 控件模板) 不同：UserControl 适合“页面级组合复用”，Control 适合“基础件/换肤复用”。
    M14 会讲两者取舍。DataContext 继承自父窗口(MainViewModel)，所以直接绑它的属性即可。
  -->
  <StackPanel Margin="12" Orientation="Vertical">
    <TextBlock Text="🔧 诊断 / 调试面板" FontSize="14" FontWeight="Bold" Margin="0,0,0,8"/>
    <Border Background="#f4f8ff" BorderBrush="#d5e2ff" BorderThickness="1" CornerRadius="6" Padding="10" Margin="0,0,0,10">
      <TextBlock Text="{Binding DiagnosticsSummary}" FontFamily="Consolas" FontSize="12" TextWrapping="Wrap"/>
    </Border>
    <TextBlock Text="最近日志（环形缓冲，最多 200 条）" FontSize="12" Foreground="#67708a" Margin="0,0,0,4"/>
    <ListBox ItemsSource="{Binding DiagnosticsLog}" Height="200"
             FontFamily="Consolas" FontSize="11" BorderBrush="#d5e2ff">
      <ListBox.ItemTemplate>
        <DataTemplate>
          <TextBlock Text="{Binding}" TextWrapping="Wrap"/>
        </DataTemplate>
      </ListBox.ItemTemplate>
    </ListBox>
  </StackPanel>
</UserControl>
```

> 📂 `src/DaqMonitor.UI/Diagnostics/DiagnosticsPanel.xaml.cs`

```csharp
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
```

### ⑦ MainWindow —— 主窗口组装(R8 删减版 ×2)

> 📂 `src/DaqMonitor.UI/MainWindow.xaml`
> 💡 删了什么:右侧「配方管理」「运动控制」两个 Tab(R9+ 各篇加回)、顶部导出报表的日期区间+按钮(报表篇)、右上角当前用户区+登出(认证篇)。布局骨架三行:顶部操作条 / 中部左表右 Tab / 底部架构说明
> ⚠️ **坑 ③ —— 状态文字的绑定必须写 `Mode=OneWay`(对参考工程的一处纠错)**:`Run.Text` 和 `TextBox.Text` 一样**默认 TwoWay**,而 `IsRunning` 是 `private set` 只读属性——不带 OneWay 的话窗口一 Show 就抛 `InvalidOperationException: 无法对只读属性 IsRunning 执行 TwoWay 绑定`(我在沙盒实跑抓到的,参考工程原文漏了这半句,启动即崩)。这是 WPF 经典冷知识:默认 TwoWay 的常用属性只有 `TextBox.Text`、`Run.Text`、`Slider.Value`、`CheckBox.IsChecked` 等少数几个,其余默认 OneWay。

```xml
<Window x:Class="DaqMonitor.UI.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DaqMonitor.UI.ViewModels"
        xmlns:ctrl="clr-namespace:DaqMonitor.UI.Controls"
        xmlns:diag="clr-namespace:DaqMonitor.UI.Diagnostics"
        xmlns:views="clr-namespace:DaqMonitor.UI.Views"
        Title="DAQ Monitor · 工业数据采集监控" Height="560" Width="880">
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <!-- 顶部：标题 + 启动/停止 + 状态(R9+ 报表篇加回时间窗+导出按钮;认证篇加回当前用户区) -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="📡 DAQ Monitor" FontSize="18" FontWeight="Bold" VerticalAlignment="Center" />
            <Button Content="启动采集" Command="{Binding StartCommand}" Width="90" Height="30" Margin="16,0,0,0"
                    IsEnabled="{Binding CanStartAcquisition}" />
            <Button Content="停止采集" Command="{Binding StopCommand}" Width="90" Height="30" Margin="8,0,0,0"
                    IsEnabled="{Binding CanStopAcquisition}" />
            <TextBlock VerticalAlignment="Center" Margin="16,0,0,0">
                <Run Text="状态：" />
                <Run Text="{Binding IsRunning, Converter={StaticResource RunningText}, Mode=OneWay}" FontWeight="Bold" />
            </TextBlock>
        </StackPanel>

        <!-- 左：实时点位表（数值列用 GaugeControl 显示读数，并绑 Level 让报警驱动变色） -->
        <GroupBox Grid.Row="1" Header="实时点位" Margin="0,0,8,0">
            <DataGrid ItemsSource="{Binding Points}" AutoGenerateColumns="False" IsReadOnly="True"
                      CanUserAddRows="False" FontSize="13">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="点位" Binding="{Binding Id}" Width="55" />
                    <!-- 数值列：自定义控件 GaugeControl，Level 绑定当前报警级别（M6→M14 跨模块复用） -->
                    <DataGridTemplateColumn Header="数值(仪表)" Width="130">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <ctrl:GaugeControl Value="{Binding Value}" Min="0" Max="150" Level="{Binding Level}"
                                                   Label="{Binding Id, StringFormat=P{0}}"
                                                   Height="84" Margin="2" />
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>
                    <!-- 状态列：自定义控件 StatusDot，M1/M3 真设备会出现 Connecting 脉冲 -->
                    <DataGridTemplateColumn Header="状态" Width="110">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <ctrl:StatusDot State="{Binding State}" Text="{Binding State}" />
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>
                    <DataGridTextColumn Header="采样时间" Binding="{Binding Timestamp, StringFormat=HH:mm:ss.fff}" Width="*" />
                </DataGrid.Columns>
            </DataGrid>
        </GroupBox>

        <!-- 右：Tab 页（报警日志 + 实时曲线 + 诊断/调试面板;R9+ 配方篇/运控篇各加回一个 Tab） -->
        <TabControl Grid.Row="1" Margin="8,0,0,0" HorizontalAlignment="Stretch">
            <TabItem Header="报警日志">
                <ListBox ItemsSource="{Binding AlarmLog}" FontSize="12" />
            </TabItem>
            <TabItem Header="实时曲线">
                <views:ChartView x:Name="ChartTab" />
            </TabItem>
            <TabItem Header="诊断 / 调试">
                <diag:DiagnosticsPanel />
            </TabItem>
        </TabControl>

        <!-- 底：架构说明 -->
        <TextBlock Grid.Row="2" Margin="0,8,0,0" FontSize="11" Foreground="Gray" TextWrapping="Wrap">
            架构：SimulatedDevice → DataReceived → AcquisitionPipeline(Channel 缓冲 + 200ms 定时批量) → PointStore + AlarmEngine → UI。
            当前用模拟设备演示；M1/M3 把 SimulatedDevice 换成真实串口/PLC 设备即可，UI 与采集层零改动。右侧「诊断/调试」页可实时看采集统计与日志，是现场排查的第一抓手。
        </TextBlock>
    </Grid>
</Window>
```

> 📂 `src/DaqMonitor.UI/MainWindow.xaml.cs`
> 💡 两个 Converter 放进窗口资源(XAML 里 StaticResource 才找得到);`DataContextChanged` 时机接 ChartView——因为 `new MainWindow{DataContext=vm}` 构造时 VM 还没来

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using DaqMonitor.UI.ViewModels;
using DaqMonitor.UI.Views;

namespace DaqMonitor.UI;

/// <summary>把 bool 取反，给按钮的 IsEnabled 用。</summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, System.Type _, object __, CultureInfo ___)
        => value is bool b ? !b : true;
    public object ConvertBack(object _, System.Type __, object ___, CultureInfo ____) => Binding.DoNothing;
}

/// <summary>把采集状态显示成文字。</summary>
public class RunningTextConverter : IValueConverter
{
    public object Convert(object value, System.Type _, object __, CultureInfo ___)
        => value is bool b && b ? "采集中" : "已停止";
    public object ConvertBack(object _, System.Type __, object ___, CultureInfo ____) => Binding.DoNothing;
}

public partial class MainWindow : Window
{
    public MainWindow()
    {
        // 在 XAML 里用到的两个 Converter 需要事先放进资源
        Resources.Add("InverseBool", new InverseBoolConverter());
        Resources.Add("RunningText", new RunningTextConverter());
        InitializeComponent();
        // 订阅 DataContext 变化:当 VM 注入后,把曲线页接到 VM
        // (R9+ 配方篇/运控篇:在这里把对应 Tab 的 Content 换成各自 View)
        DataContextChanged += MainWindow_DataContextChanged;
    }

    private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            if (ChartTab is not null) vm.AttachChart(ChartTab);
        }
    }
}
```

## ✅ 验证(必做)

```bash
dotnet build
dotnet test
dotnet run --project src/DaqMonitor.UI
```

**期望输出(关键行)**:
```
0 个错误
6 个警告   ← 全部 NU1701,不是代码问题(见下)
已通过! - 失败: 0,通过: 56 ... DaqMonitor.Tests.dll
```
> ⚠️ **NU1701 警告是预期的**:LiveCharts2(2.0.0-rc4.5)的传递依赖 `SkiaSharp.Views.WPF` / `OpenTK` 只发了 .NET Framework 版包,NuGet 用兼容模式还原时提示"可能不完全兼容"——参考工程同样如此,不影响编译与运行,**代码本身 0 警告**(R1-R7 的"0 错 0 警"标准从本篇起放宽为"0 错、0 代码警告")。
(build 含 DaqMonitor.UI;test 仍是 56——本篇不动 Core,UI 靠下面的手动清单)
`dotnet run` 弹出主窗口 **「DAQ Monitor · 工业数据采集监控」**。

**手动验收清单(窗口交互,自己点一遍)**:

- [ ] 弹出主窗口,无第二个空窗口(App.xaml 的 StartupUri 已删)
- [ ] 点「启动采集」→ 状态变 **采集中**;「启动采集」按钮变灰、「停止采集」亮起
- [ ] 左侧「实时点位」出现 **P1/P2/P3 三行**:每个一个仪表盘,读数/指针/采样时间持续跳动(100ms 一发)
- [ ] 状态列三个绿点 Online(模拟设备 Connect 后即 Online)
- [ ] 数值偶尔冲上 95~120(模拟设备 10% 概率越限):某行表盘**环变红/橙**,右侧「报警日志」插入一条 `[时间] 点位 1 → Critical 报警,值 = 1xx`;值回落后表盘恢复蓝环、日志加一条"报警恢复"——这就是 R5 的**回滞+边沿**在界面上的样子
- [ ] 「实时曲线」页两条线(温度/压力)滚动推进
- [ ] 「诊断 / 调试」页:统计行"采样 N 点|报警 N 次|批次 N|末批 Nms"持续增长,日志列表有启动/报警记录
- [ ] 点「停止采集」→ 状态变 **已停止**,仪表读数停住,曲线仍在滚(演示模式自走,正常)
- [ ] 关窗退出,`%LocalAppData%\DaqMonitor\daq.db` 存在(R6 的库在真实路径上)

> 🧪 R8 的 UI 我在交付前用 `dotnet build` + 短暂启动做了冒烟(窗口能起来、无启动崩溃;冒烟还真抓到并修掉一个启动崩溃,见 ⑦ 坑③);**交互效果(指针/报警变色/曲线滚动)按上面清单逐条自验**——每一行都能追溯到 R2~R7 某个已测过的模块。

## ✅ 验收清单

- [ ] build 0 错、代码 0 警告(仅 LiveCharts2 传递依赖的 NU1701),test 56/56 绿
- [ ] 手动验收清单 9 条全过
- [ ] 能回答:为什么 PointView 是 class 而 SensorPoint 是 struct?([struct-vs-class](kp:struct-vs-class):高频小值传拷贝用 struct;UI 绑定要引用+通知用 class)
- [ ] 能回答:BatchReady 里不包 `Dispatcher.Invoke` 会怎样?(后台线程改 ObservableCollection → InvalidOperationException,这就是 [Dispatcher](kp:dispatcher) 存在的意义)
- [ ] 能回答:自定义控件和 UserControl 什么时候选哪个?(基础件/换肤/跨项目复用 → Control+Generic.xaml;页面级组合 → UserControl)
- [ ] 能回答:Generic.xaml 为什么必须在 Themes/ 目录、ThemeInfo 特性干什么?(WPF 按约定找主题字典;ThemeInfo 告诉它"字典在本程序集内")
- [ ] 能回答:状态文字 `<Run Text="{Binding IsRunning,...}"/>` 为什么必须写 `Mode=OneWay`?(Run.Text 默认 TwoWay,IsRunning 只读 → 不写启动即崩)
- [ ] git commit -m "R8: WPF主界面(点位表+仪表+状态灯+报警日志+曲线+诊断,主屏先行)"

## 🎤 面试怎么讲这一篇

> "界面用 MVVM:MainWindow 的 XAML 只做布局,数据全部绑 MainViewModel——点位表绑 ObservableCollection,启停按钮绑 RelayCommand,IsRunning 翻转时通知 CanStart/CanStop 按钮自动灰亮。采集回调在后台线程,更新 UI 前统一 Dispatcher.Invoke 切回 UI 线程。展示模型上我把领域层的 SensorPoint(struct)转成 PointView(class + INotifyPropertyChanged),因为 struct 是值拷贝、没有属性通知,绑不上 UI——领域模型和视图模型各管各的。控件层我写了两个自定义控件:量程仪表盘和设备状态灯,继承 Control、依赖属性对外、外观全部在 Generic.xaml 的 ControlTemplate 里,报警级别用 DataTrigger 驱动换色——换肤、复用都不用动 C# 代码。曲线用 LiveCharts2 双线滚动缓冲六百点。整窗从 DI 组合根装配,换真设备只改 Bootstrapper 一行注册,UI 和采集层零耦合。"

**✅ 打卡[ ]**
