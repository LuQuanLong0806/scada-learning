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
| FR8-3 | MainViewModel:PointView 展示模型([INotifyPropertyChanged](kp:binding));`BatchReady` 里用 [Dispatcher](kp:dispatcher).Invoke 更新存储+报警+点位表+曲线(`Push`);报警事件插入日志并同步 Level 给表盘 | 启动采集后点位表出现 3 行,数值/时间持续跳动 |
| FR8-4 | 自定义控件 [GaugeControl/StatusDot](kp:mvvm):继承 Control + DependencyProperty + Generic.xaml 默认模板;ThemeInfo 特性指到本程序集 | 表盘指针随值转;报警时环变橙/红;状态灯绿点 |
| FR8-5 | [ChartView](kp:livecharts) 实时曲线:LiveCharts2 两条 LineSeries,ObservableCollection 滚动缓冲 600 点(60 秒) | **启动采集后**曲线页两条线滚动推进(真实采集值);未启动时曲线静止不动 |
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

> ⚠️ **本篇贴法与 R2-R7 不同**:文件之间互相引用(App.xaml.cs → MainWindow → MainViewModel → ChartView → 自定义控件),没法像 Core 层那样"每贴一步 build 一次"。**按 ①→⑦ 顺序把所有文件贴完再 build**——中途 build 会报"找不到类型",属预期。每个文件内部仍标明贴法:两个大文件(MainViewModel / GaugeControl)拆成小步走,小文件整段贴。

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

📚 **知识点**
- **`App.OnStartup` = WPF 的 main()**:Application 生命周期钩子,窗口显示前的一切接线都在这——**前端类比**:Next.js 的 `_app.tsx` / React 根组件的 useEffect 初始化,框架先起、你后跑。
- **`public static ServiceProvider Services` 全局容器**:WPF 没有"构造注入到窗口"的原生通道,静态属性是最朴素的解法(参考工程如此)。更讲究的做法是三行 DI 扩展,但静态属性直白、面试好讲。**前端类比**:全局单例 store,`getAppStore()` 谁都能取。
- **`window.Closed += (_,_) => Services.Dispose()`**:窗关 = 应用退,顺手把 DI 容器(连同 PointStore 写泵、管道 Timer)优雅关停——**谁开门谁锁门**,R6/R7 两个 Dispose 链在这里落地。
- **`Connect()` 在启动期、`Start()` 在按钮里**:设备连上(链路通)不等于开始采集(有数据)——Connect 是"电话接通",Start 是"开始说话",分层含义别混。

### ② RelayCommand —— 可绑定命令(原文)

> 📂 `src/DaqMonitor.UI/ViewModels/RelayCommand.cs`
> 🔧 无 NuGet · 💡 把"点击该干嘛"变成 VM 上的属性,XAML `Command="{Binding StartCommand}"` 直接绑

#### 🏗️ 为什么这样设计:命令为什么要包一层 RelayCommand,而不是按钮直接 Click 事件?

**当时面临的选择**:

| 方案 | 优点 | 代价 |
|---|---|---|
| code-behind:`Button Click="OnStart"` | 直观零封装 | 逻辑长在 .xaml.cs 上:**不可单测**(要 new 窗口)、按钮状态(能否点击)要手写 IsEnabled 同步、VM 不知道命令存在 |
| `Command="{Binding StartCommand}"`(选定) | 多写 20 行 ICommand | — |

**为什么选它**:RelayCommand 把"点击"翻译成 **VM 上的一个属性**——命令的执行体(Execute)和可执行性(CanExecute)都活在 ViewModel 里。三个直接收益:①VM 不引用任何控件,**测试直接 new VM 调命令**,不碰 UI;②`CanExecute` 返回 false 时按钮**自动灰掉**(WPF 替你做 IsEnabled 同步);③同一个命令可绑按钮/菜单/快捷键多处。本质是把"事件回调"换成"可绑定可测试的意图声明"——前端类比:从 `addEventListener('click')` 换成声明式的 `:disabled` + 纯函数 handler,handler 进了 VM 就能脱离 DOM 测试。

**不这样会怎样**:逻辑全在窗体类里,想验证"点启动后 IsRunning 变 true"必须启动整个 WPF 窗口;按钮可用性散在各处手写,状态多了必不同步。

**🎤 面试一句话**:"按钮我用 Command 绑定不用 Click 事件:RelayCommand 把'点击'变成 VM 的属性,执行体和 CanExecute 都在 ViewModel——VM 不引用控件可以纯单测,CanExecute 自动灰按钮,还能多处复用同一个命令。"

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

📚 **知识点**
- **ICommand 是 WPF 按钮的"事件协议"**:XAML `Command="{Binding StartCommand}"` 要求目标是个 ICommand——WPF 只认这个接口,不认方法名。RelayCommand 就是"把 C# 方法包成 ICommand"的适配器,20 行手写,不引 CommunityToolkit.Mvvm 也能跑。
- **事件访问器 `add/remove` 转租**:CanExecuteChanged 不自己存订阅列表,而是**把订阅转给 `CommandManager.RequerySuggested`**——WPF 的全局重查机制,任何输入/焦点变化时所有命令自动重问 CanExecute。所以 IsRunning 一翻,按钮灰亮自动跟着变,**不需要手动触发事件**。**前端类比**:像把组件的 re-render 托管给 React 的状态调度,不自己 diff。
- **`_canExecute?.Invoke(parameter) ?? true`**:没给 CanExecute 就默认可点(`?? true`)——"无约束"是合理缺省,写按钮的场景永远多于禁用按钮。

### ③ MainViewModel —— 主屏 VM(R8 删减版)

> 📂 `src/DaqMonitor.UI/ViewModels/MainViewModel.cs`
> 💡 删了什么:当前用户/角色区、权限判断的 Can* 表达式、配方/运控两个子 VM、报表导出、登出——全部依赖 R9+ 服务。保留主干:BatchReady → 表格/存储/报警,报警事件 → 日志/表盘变色,Start/Stop
> 💡 看三处精髓:**PointView 为什么是 class**(struct 拷贝+无通知,绑不了 UI)、**Dispatcher.Invoke 包住整个批量更新**(一次跨线程,整批处理)、**_levels 字典把报警级别带回下次批量刷新**(报警恢复后表盘能变回蓝)
> 🗺️ **新手读码地图**(顺着"一批数据的旅程"看,VM 只是接线员):1. 构造函数干的全是**接线**:从 DI 容器领服务 → 造两个 ICommand → 订阅 3 个事件(管道 BatchReady、报警触发/恢复)。VM 自己不采集、不存库、不判报警,全是 R5-R7 的活 2. `OnBatchReady` 是主数据流:一批点进来 → **一次** `Dispatcher.Invoke` 切到 UI 线程(后台事件线程不能直接改 ObservableCollection)→ 循环里每条点走三步:写库 `_store.AddOrUpdate`、喂报警 `_alarms.Evaluate`、刷表格(找不到行就 Add 新 PointView,找到就改属性——属性 setter 触发 PropertyChanged,DataGrid 自动重画) 3. `_levels` 字典解决一个时序问题:报警事件可能在两批数据之间到,而表盘颜色跟着批量刷——所以报警先记进 `_levels`,每次批量刷新时 `TryGetValue` 同步给行(331 行),颜色就不丢 4. `OnAlarmTriggered/Cleared` 是旁路:插一条日志到 AlarmLog 头部(最新的在最上面)+ 立刻改该行 Level 让表盘变红 5. `Start/Stop` 只是拨开关:`SimulatedDevice.Start(100ms)`,IsRunning 一翻,四个绑定属性(按钮可用性)跟着变。**前端类比**:VM ≈ React 容器组件——`Dispatcher.Invoke` ≈ setState 必须在 React 上下文里;`OnChanged(nameof(X))` ≈ 手动触发一次针对性 re-render;`_levels` ≈ 用 ref 存一份"跨 render 也要活着"的中间状态。

#### 🏗️ 为什么这样设计:为什么整个界面走 MVVM,而不是像 WinForms 那样事件写在 code-behind 里?

**当时面临的选择**:

| 方案 | 优点 | 代价 |
|---|---|---|
| code-behind 事件驱动 | 上手最快 | 逻辑长在窗体上:不可单测、界面改版逻辑跟着重写、状态散在几十个控件里 |
| MVVM:View 只绑 ViewModel(选定) | 多一层 VM + 绑定 | 学习曲线(绑定/通知/命令一整套) |

**为什么选它**:上位机界面**变化极频繁**——客户今天要加一列、明天要换布局、后天要做第二套皮肤。MVVM 把"长什么样"(XAML)和"什么逻辑"(VM)切开:逻辑在 VM 上没有控件引用,**纯 new 就能测**;界面随便改,绑定名不变逻辑不动。数据流也统一成一条:**数据 → VM 属性 → 绑定 → 控件**,调试看 VM 就知道界面该长什么样,不用人肉点一遍界面。前端类比:这不是新知识——React 的"组件=render(state)"就是这个思想的实现,VM ≈ 容器组件的状态层,XAML ≈ 模板,MVVM 是 2005 年前端圈还没发明 JSX 时就定型的"声明式 UI"。

**不这样会怎样**:三百行 MainWindow.xaml.cs 里找"为什么这个数没刷新",事件处理器互相改状态,和 jQuery 时代的面条回调一个下场。

**🎤 面试一句话**:"界面我用 MVVM:View 只做绑定,逻辑全在 ViewModel——VM 不引用控件可以脱离 UI 单测,客户改界面不动逻辑,数据流是'数据→VM→绑定→控件'一条线。本质就是声明式 UI,和 React 的 render(state) 同一个思想,WPF 在 2005 年就内建了。"

#### 🏗️ 为什么这样设计:UI 表格绑的为什么是 PointView(class),而不是直接绑 R1 的 SensorPoint(struct)?

**当时面临的选择**:

| 方案 | 优点 | 代价 |
|---|---|---|
| DataGrid 直接绑 `List<SensorPoint>` | 零转换 | struct 值拷贝:改了 `Value` 界面不知道(**没有 PropertyChanged 通知**);整行替换又丢"行"的稳定性 |
| 造 PointView : INotifyPropertyChanged(选定) | 多一个展示模型类 | 领域和展示两套类型要同步 |

**为什么选它**:WPF 绑定刷新的机制是 **PropertyChanged 事件**——控件订阅它才知道"这个格子该重画"。SensorPoint 是 struct:①值语义,赋值即拷贝,根本没有"同一个对象属性变了"的事件可发;②就算改成 struct+接口,装箱和拷贝也会让通知丢失。所以 UI 层要有**自己的展示模型**:class + 每个属性 setter 里 `OnChanged()`,改一格亮一格。这不是重复造类型,而是**两个层次各要各的形状**——Core 里 SensorPoint 为性能而生(struct 零 GC),UI 里 PointView 为通知而生(class+事件);中间一层转换是 MVVM 的标准件(前端类比:接口返回的 DTO ≠ 组件的 state model,redux 里还要 mapStateToProps 一道)。

**不这样会怎样**:绑 struct 集合,表格只在整集合替换时才刷新,高频点位变灰;为救场把 SensorPoint 改 class,Core 的 GC 压力又回来了——一处将就,两层都坏。

**🎤 面试一句话**:"Core 和 UI 各有各的数据形状:SensorPoint 是 struct,为了采集高频零 GC;表格绑的是 PointView class,每个属性带 PropertyChanged——WPF 刷新靠事件通知,struct 值拷贝发不出通知。两层之间一道转换,是 MVVM 的标准分工而不是冗余。"

**第 1 步 · PointView:展示模型**(新文件,先贴文件头 + 第一个类)

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
```

📚 **知识点**
- **为什么必须转一层 PointView**(需求单问过):`SensorPoint` 是 struct——赋值即拷贝、没有属性通知。DataGrid 绑过去后,改了集合里的值界面**不会重画**(WPF 不知道它变了)。PointView 是 class + 每个属性 setter 触发 `PropertyChanged`——"活的、会自己报信的"行对象。**这是 struct 领域模型进 UI 的标准姿势:门上转一层,门内不动**。
- **`[CallerMemberName]`**:编译器自动把"调用者名字"填进参数——`set { _value = value; OnChanged(); }` 不用手写 `OnChanged(nameof(Value))`,**改名不怕漏**。
- **`PropertyChanged?.Invoke(this, new ...(n))`**:WPF 绑定引擎订阅这个事件,收到通知就刷新对应属性——**前端类比**:手写一次"精准 setState",只有绑了 `Value` 的 TextBlock 重渲染。

**第 2 步 · MainViewModel 骨架:字段 + 事件引擎 + 绑定属性群**(同文件接着贴第二个类;事件 OnChanged 和属性一起贴,因为 IsRunning 的 setter 要用)

```csharp
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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
```

📚 **知识点**
- **VM 的字段清单就是"它认识谁"**:_store/_pipeline/_alarms/_device/_diag 五个服务 + `_levels` 报警级别缓存 + `_chart` 曲线引用。**VM 不采集、不存库、不判报警——全是 R5-R7 的活,它只是接线员**。
- **`ObservableCollection<PointView>`**:集合级的增删自动通知 UI(DataGrid 加行/删行);行内的值变化靠 PointView 的属性通知——**两层通知,各管各的**。**前端类比**:数组 key 变化触发列表 diff + 行组件自己的 state 变化触发局部渲染。
- **`IsRunning` 的 setter 连环三报**:自身 + CanStart + CanStop——一个开关翻动,两个按钮的灰亮跟着换。`CanXxx` 是只读计算属性,自己不会"变",必须由 IsRunning **代为广播**。这是手写 MVVM 最容易漏的点。
- **`DiagnosticsSummary` 是拼接字符串属性**:每次批量后手动 `OnChanged(nameof(DiagnosticsSummary))` 通知重算——WPF 不会自动知道 `_diag.TotalSamples` 变了(它不是绑定目标)。**前端类比**:没有响应式系统时,ref 依赖要手动触发依赖方更新。

**第 3 步 · OnBatchReady:主数据流**(贴进 MainViewModel 类里,最后一个 `}` 之前)

```csharp
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

                _chart?.Push(p);   // 实时曲线：点位 1/2 分流进温度/压力两条线
            }
            OnChanged(nameof(DiagnosticsSummary));
        });
        sw.Stop();
        _diag.RecordBatch(batch.Count, sw.ElapsedMilliseconds);
    }
```

📚 **知识点**
- **`Dispatcher.Invoke` 包住整个批量更新**:BatchReady 在管道的后台线程触发,而 UI 元素(含 ObservableCollection)只许 UI 线程碰——不切线程直接改,第一批数据就抛 InvalidOperationException(R8 坑⑤的姊妹坑)。**一次 Invoke 包整批**,别在循环里一条一切(每条都排队,开销翻倍)。**前端类比**:Worker 线程算完,postMessage 回主线程再 setState。
- **每条点的三步舞**:写库(`AddOrUpdate`,R6 双写)→ 喂报警(`Evaluate`,R5 边沿)→ 刷表格(首见 Add 新行,再见改属性)。**VM 是流式处理器,数据过了就过了,状态全在服务里**。
- **`_levels.TryGetValue` 解决时序错位**:报警事件可能在两批数据**之间**到,而表盘颜色跟着批量刷——报警先记进 `_levels` 字典,每次批量刷新时同步给行,颜色就不丢。**前端类比**:用 ref 存"跨 render 也要活着"的中间状态,下次 render 带出来。
- **`Stopwatch` 计时整批处理耗时**:sw 在 Invoke 外启动、Invoke 后停止——测的是"含切线程"的真实耗时,`_diag.RecordBatch` 记进诊断面板。工业排查"卡顿/丢点"第一指标。

**第 4 步 · 报警旁路 + Start/Stop + AttachChart**(继续贴进类里)

```csharp
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

    /// <summary>
    /// 由 MainWindow 注入：曲线页只吃真实采集数据（OnBatchReady 里 Push），
    /// 不启动演示模式——否则没开始采集曲线也在跳，且跳的是随机数不是真实值。
    /// </summary>
    public void AttachChart(ChartView chart)
    {
        _chart = chart;
    }
```

📚 **知识点**
- **报警是"旁路",不走批量**:AlarmTriggered/Cleared 事件一到就**立刻**处理(插日志头 + 表盘变色),不等下一批——报警等 200ms 都是迟到的紧迫感。`AlarmLog.Insert(0, ...)` 头插,最新永远在最上面。
- **触发/恢复成对出现**:Triggered 记级别进 `_levels`,Cleared 复位成 Normal——表盘变红和变回蓝是同一套机制的两次触发。R5 的"回滞 + 边沿"在界面上的样子:值冲高变红一次、回落穿过回滞带变蓝一次,不会闪烁。
- **Start/Stop 里的模式匹配 `is SimulatedDevice sd`**:接口引用向下转型 + 判空一步走——只有模拟设备有 Start/Stop(节奏器),真设备 Connect 后自己就吐数据。接真设备时这两行改成对应的起停逻辑,**其余全部不动**。
- **AttachChart 只存引用、不开演示**:坑⑥的教训现场——这里若手滑调 `chart.StartDemo()`,没点启动曲线也会跳(假数据)。**演示入口和真实数据通路是两条线,接了真的必须关假的**。

**第 5 步 · 构造函数:接线员最后上岗**(继续贴进类里——至此所有零件就位,ctor 引用的方法全部存在)

```csharp
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
```

📚 **知识点**
- **ctor 四拍:领服务 → 造命令 → 订事件 → 记一条日志**。全 VM 的"接线图"就这 13 行——五个服务(R7 组合根注册的)、两个命令(RelayCommand 包两个私有方法)、三个事件(管道批量 + 报警触发/恢复)。
- **为什么这一步最后贴**:ctor 引用了第 3/4 步的 OnBatchReady/OnAlarmTriggered/OnAlarmCleared——先有零件后接电,文件在任何中间状态都语法完整。
- **VM 拿的是 `ServiceProvider` 整个容器**而不是五个单参注入:参考工程的选择,省掉一层包装。更纯的做法是"只收它需要的接口"(构造注入五兄弟),测试时更好替身——两种都常见,面试说出取舍即可。

<details markdown="1">
<summary>📄 完整文件 MainViewModel.cs(对答案 / 整体粘贴用)</summary>

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

    /// <summary>
    /// 由 MainWindow 注入：曲线页只吃真实采集数据（OnBatchReady 里 Push），
    /// 不启动演示模式——否则没开始采集曲线也在跳，且跳的是随机数不是真实值。
    /// </summary>
    public void AttachChart(ChartView chart)
    {
        _chart = chart;
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

                _chart?.Push(p);   // 实时曲线：点位 1/2 分流进温度/压力两条线
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

</details>

### ④ 自定义控件三件套 + 默认模板(原文 ×4)

> 📂 `src/DaqMonitor.UI/Controls/GaugeControl.cs`
> 💡 "自绘控件"最正宗写法:继承 Control、无 xaml.cs、外观全在 Generic.xaml、对外只有 DependencyProperty。指针角度 = -135° + 量程比例 × 270°

#### 🏗️ 为什么这样设计:仪表盘/状态灯为什么要自己写控件,而不是找现成库拖一个?

**当时面临的选择**:

| 方案 | 优点 | 代价 |
|---|---|---|
| 现成仪表库(NILMTune/LiveCharts Gauges 等) | 拖上就能用 | 样式定制受限(现场客户常要"照我们旧软件的样子");引入整库只为一两个控件 |
| 继承 Control 自绘 + Generic.xaml 模板(选定) | 多写百来行 | 要懂模板/依赖属性一整套 |

**为什么选它**:工业客户对界面常有**像素级迁移要求**("和原来那套 WinForms 长得一样,师傅才肯用")——现成库的默认外观改到深处就是和库搏斗。自定义控件把**外观(XAML 模板)和逻辑(角度换算)分离**:同一套 GaugeControl 逻辑,换个 Generic.xaml 就是另一种风格;依赖属性(Value/Min/Max)让控件**可绑定**——VM 里数值一动指针自己转,和绑定 TextBlock 没有区别。这也是 WPF 的"正字标记":会用依赖属性+模板,才算摸到 WPF 的核心机制,而不只是"拖控件的 WinForms 搬家"。

**不这样会怎样**:引库后客户要求改指针形状/刻度密度,改不动库模板只能再造一层包装;面试问"依赖属性怎么回事"也答不上——因为从没写过。

**🎤 面试一句话**:"仪表和状态灯我自绘:继承 Control、外观全在 Generic.xaml、对外只暴露依赖属性——逻辑换算在类里,风格换模板就行,应对工业客户'照旧软件长'的要求最灵活。依赖属性让控件像 TextBlock 一样可绑定,数值动指针自己转。"

> ⚠️ **这个类用"先贴后读"模式**:7 个依赖属性里 Value/Min/Max 的变更回调指向 `RecalcAngle`,而 `RecalcAngle` 又反过来读写这些属性——成员互相成环,没法拆成可编译的中间态。**先展开文末折叠块把完整文件贴进工程,再按 3 步读懂**。

**第 1 步 · 读:类壳 + 静态构造(主题挂钩)**

```csharp
    /// <summary>告诉 WPF：本控件的默认样式去 Generic.xaml 里找 TargetType=GaugeControl 的那条。</summary>
    static GaugeControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(GaugeControl),
            new FrameworkPropertyMetadata(typeof(GaugeControl)));
    }
```

📚 **知识点**
- **`static` 构造函数**只在类型第一次被使用时跑一次,全进程仅此一回——这里干的活:告诉 WPF"我的默认样式去 Generic.xaml 里找 `TargetType=GaugeControl` 那条"。**没这一步,控件长得像普通 Border(空白),不报错**——自绘控件第一大坑是静默失败,不是崩溃。
- **为什么继承 `Control` 而不是 `UserControl`**:UserControl = 把现有控件拼起来(页面级复用);自定义控件 = 外观全在模板里,**可换肤、可继承、可跨项目当基础件**——JD 里"熟练自绘控件"指的就是这套。

**第 2 步 · 读:依赖属性三件套(注册 + 元数据 + CLR 包装)**

```csharp
    // ---- 依赖属性：控件对外暴露的全部"可绑定点" ----
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(GaugeControl),
            new PropertyMetadata(0d, (d, _) => RecalcAngle(d)));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
```

(Unit/Min/Max/Label/Level/NeedleAngle 六个属性同套路:Min/Max 也挂 RecalcAngle 回调,Unit/Label/Level/NeedleAngle 只给默认值——完整清单见折叠块)

📚 **知识点**
- **一个依赖属性 = 三件套**:①`static readonly DependencyProperty` 字段(注册进 WPF 属性系统)②CLR 包装属性(get/set 转发 GetValue/SetValue——给 C# 代码当普通属性用)③`PropertyMetadata`(默认值 + 变更回调)。**照着这个模子数一遍,7 个属性 21 件,一件不多**。
- **`PropertyMetadata(0d, (d, _) => RecalcAngle(d))` 的第二个参数**:属性变了自动调 RecalcAngle——**值一变角度立刻重算**,这是"数据驱动 UI"在控件层的最小实现。**前端类比**:自定义组件的 `useEffect(() => recalc(), [value])`,依赖变了副作用自动跑。
- **`nameof(Value)` 而不是硬编码字符串**:改名安全。WPF 靠字符串匹配找属性,手写字符串一旦拼错,绑定静默失败(又是"不报错但空白")。
- **为什么属性必须是 DependencyProperty**:普通 CLR 属性不能被 XAML `{Binding}` 绑定、不能参与样式/动画/模板系统——依赖属性是 WPF 属性系统的"户口",没户口处处受限。

**第 3 步 · 读:RecalcAngle 指针数学**

```csharp
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
```

📚 **知识点**
- **值域 → 角度域的线性映射**:`ratio = (值-最小)/(最大-最小)` 归一到 0~1,再乘 270° 加 -135° 起角——表盘从左下(-135°)扫到右下(+135°),缺口留在底部。**仪表盘控件的核心就这一行数学**。
- **`if (max <= g.Min) max = g.Min + 1` 防除零**:量程配错(Min=Max)时给 1 的假量程,而不是 NaN 指针——控件要对自己的输入做防御,这一行救过无数现场。
- **`Math.Max(0, Math.Min(1, ratio))` 双向钳位**:值超量程指针也不飞出表盘,停在两端。**前端类比**:CSS progress 的 width 用 `clamp(0%, x, 100%)`。
- **写到 NeedleAngle(也是依赖属性)而不是直接转指针**:C# 只算角度,**"转"这个动作由 XAML 模板里的 `RotateTransform Angle="{Binding NeedleAngle...}"` 完成**——逻辑和外观彻底分家,这就是第 1 步说的"可换肤"的底气。

<details markdown="1">
<summary>📄 完整文件 GaugeControl.cs(先把这个贴进工程,再回头读上面 3 步)</summary>

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

</details>

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

📚 **知识点**
- **同套路第二遍,这次看"差异点"**:GaugeControl 有 7 个属性 + 角度计算;StatusDot 只有 2 个属性、**零计算**——状态到外观的映射(绿/黄/红/脉冲)全部交给模板里的 DataTrigger + Storyboard。**C# 管数据和状态,XAML 管长相**,两类控件一个纪律。
- **动画写在 XAML 不写在 C#**:Connecting 的脉冲是 Storyboard(`EnterActions` 里 `BeginStoryboard`)——进入状态自动播、离开自动停,C# 一行动画代码都没有。**前端类比**:CSS transition/animation 由 class 切换驱动,不用 JS 逐帧画。

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

**第 1 步 · GaugeControl 默认样式**(新文件,贴字典开头 + 第一个 Style)

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
```

📚 **知识点**
- **Style = Setter 集合,Template 是最重的一个 Setter**:前两个 Setter 给默认宽高(120×120),第三个把整棵视觉树(椭圆环/指针/读数/标签)塞进 `Template`——**换肤 = 换这一个 Setter,类零改动**。
- **`x:Name="ring"` 命名模板内部元素**:Trigger 里 `TargetName="ring"` 靠它寻址——模板内部元素不暴露给外部,只能用名字在 Triggers 里改。
- **`{Binding NeedleAngle, RelativeSource={RelativeSource TemplatedParent}}`**:模板里绑"宿主控件"的属性——TemplatedParent 就是那个 GaugeControl 实例。**模板是通用的,宿主各带各的数据**,这根纽带别写错成普通 Binding(会绑到 DataContext 上,静默失败)。
- **DataTrigger 三态换色**:Level=Warning → 橙色虚线加粗;Critical → 红色加粗;默认蓝。**C# 只改 Level 这一个枚举,颜色/线型/粗细全由 Trigger 组合表达**——GaugeControl.cs 里那 7 个属性没有一个叫"颜色",外观决策全在 XAML。
- **指针的旋转支点 `RenderTransformOrigin="0.5,1"`**:矩形绕自己的"底边中点"转——针从表盘中心向外指,数学在 GaugeControl.cs 的 RecalcAngle,转动在这里。

**第 2 步 · StatusDot 默认样式 + 收尾**(紧接着贴,最后补 `</ResourceDictionary>`)

```xml
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
                    <!-- 坑④:参考工程原文写的是 Duration="0 0:0:0.6"——不是合法 TimeSpan,
                         状态灯一进 Connecting 就 XamlParseException 崩溃。正确写法 "0:0:0.6"(0.6 秒)。 -->
                    <DoubleAnimation Storyboard.TargetName="dot"
                                     Storyboard.TargetProperty="Opacity"
                                     From="1" To="0.3" Duration="0:0:0.6"/>
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

📚 **知识点**
- **`{TemplateBinding Text}` vs `{Binding Text, RelativeSource=TemplatedParent}`**:两个都是"读宿主属性",TemplateBinding 是简写(性能更好但只能单向、不能加 StringFormat);表盘里用了完整版(要 StringFormat=F1),这里文本直读用简写。**见到两种写法别懵,是同一件事的两档**。
- **`DataTrigger.EnterActions` → `BeginStoryboard`**:进 Connecting 状态的那刻启动动画;`RepeatBehavior="Forever" AutoReverse="True"` = 无限次 1↔0.3 透明度往返——"通讯中"的呼吸感。**坑④现场**:Duration 必须写 `"0:0:0.6"`,写成 `"0 0:0:0.6"`(多一个空格段)不是合法 TimeSpan,一进触发器就 XamlParseException。
- **没写 Online 的 Trigger**:默认 Fill 就是绿色 `#28a745`——**默认即常用态,Trigger 只写偏离项**,模板才不会越写越长。
- **文件位置是契约**:必须在 `Themes/Generic.xaml`(目录名/文件名都定死),配合 Controls/ThemeInfo.cs 的程序集特性,WPF 才找得到——这是"约定大于配置"的 WPF 版。

<details markdown="1">
<summary>📄 完整文件 Generic.xaml(对答案 / 整体粘贴用)</summary>

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
                    <!-- 坑④:参考工程原文写的是 Duration="0 0:0:0.6"——不是合法 TimeSpan,
                         状态灯一进 Connecting 就 XamlParseException 崩溃。正确写法 "0:0:0.6"(0.6 秒)。 -->
                    <DoubleAnimation Storyboard.TargetName="dot"
                                     Storyboard.TargetProperty="Opacity"
                                     From="1" To="0.3" Duration="0:0:0.6"/>
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

</details>

### ⑤ ChartView —— LiveCharts2 实时曲线(原文 ×2)

> 📂 `src/DaqMonitor.UI/Views/ChartView.xaml`
> 💡 XAML 只放最简壳,Series 由后台代码注入——避免和 ViewModel 的可观察集合强耦合

#### 🏗️ 为什么这样设计:曲线为什么 XAML 只放壳、Series 后台注入?演示模式和真实数据为什么必须严格分家?

**当时面临的选择(Series 归属)**:

| 方案 | 优点 | 代价 |
|---|---|---|
| Series 全在 XAML 里声明,VM 提供集合 | 声明式,看着"标准" | LiveCharts 的 Series 对象带大量自身状态,XAML 静态声明和 VM 动态数据源互相缠死 |
| XAML 只放壳,后台 `Push(SensorPoint)` 喂点(选定) | 多几行代码 | 控件知道自己怎么被喂数 |

**为什么选它**:实时曲线的关键设计是**滚动缓冲**:两条 `ObservableCollection<double>` 超 600 点就 `RemoveAt(0)`,内存有界、画面像心电图一样左移。这层缓冲逻辑放 VM 会让 VM 沾上图表库类型;放 XAML 又写不了循环——所以 ChartView 自己管缓冲,对外只开一个 `Push(SensorPoint)` 小口,VM 每批数据来了 push 一轮。**小接口 + 自包含**,控件可以整体搬走复用。

**演示模式为什么必须分家(真实修过的 bug)**:ChartView 有个 `StartDemo()`——无数据源时自动生成随机曲线,本是给"界面还没接采集"时看效果用的。早期版本在 `AttachChart` 里顺手调了它,结果**没点启动采集曲线也在跳**,用户当场发现"跳的是随机数不是真实值"。教训固化成一条纪律:**演示入口和真实数据通路是两条线,接了真实通路必须关演示入口**——任何"演示/mock 方便开关"都要有显式的单一开关,不能两套并行。前端类比:dev 环境 mock 和真接口共存时,最经典的 bug 就是"忘了关 mock,页面上线还在吐假数据"。

**🎤 面试一句话**:"实时曲线我让控件自管 600 点滚动缓冲,对外只开 Push 一个口,XAML 只放壳——内存有界、控件可整体复用。这里还修过一个真实 bug:演示模式 StartDemo 被顺手调用,没启动采集曲线也在跳假数据——从此定死规矩:演示通路和真实通路绝不并存,接了真的必须关假的。"

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

📚 **知识点**
- **XAML 只是壳,Series 后台注入**:这里不放任何 Series 配置——由 ChartView.xaml.cs 在构造时用代码塞进去。好处:曲线集合是 `ObservableCollection<double>` 字段,代码里 Push 就滚动,不和 ViewModel 的绑定链强耦合。**前端类比**:图表组件只留 `<div ref>`,实例化和数据全在 JS 里管(ECharts 的经典用法)。
- **`xmlns:lvc` 引入第三方命名空间**:LiveCharts 的 WPF 控件都在这个命名空间下,`lvc:CartesianChart` 即直角坐标系图表(还有 PieChart 等)。
- **`Grid.RowDefinitions` 两行**:Auto(标题)+ `*`(图表占满剩余)——WPF 版 flex 布局,`*` 就是 `flex: 1`。

> 📂 `src/DaqMonitor.UI/Views/ChartView.xaml.cs`
> 💡 两条 `ObservableCollection<double>` 当滚动缓冲(超 600 个就 RemoveAt(0));外部可 `Push(SensorPoint)` 喂真实点位,`StartDemo()` 是无数据源的演示模式

**第 1 步 · 骨架:常量 + 双缓冲集合 + 属性**(新文件,先贴文件头和字段区)

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using DaqMonitor.Core.Models;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace DaqMonitor.UI.Views;

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
}
```

📚 **知识点**
- **600 点 = 60 秒的数学**:采集 10Hz(100ms 一发),600 个点正好一分钟窗口。`MaxPoints`/`TickMs` 两个 const 把节奏写在最顶上——**调窗长只改这一个数字**,不用全文搜魔法数。
- **两条 `ObservableCollection<double>` 就是两条线**:LiveCharts2 直接吃 ObservableCollection,集合 Add/Remove 图表自动重画——**不需要手动调 Invalidate/Update**。**前端类比**:图表库绑响应式数组(ECharts 配合 Vue 的 reactive),数据动图自动动。
- **`DispatcherTimer` 不是 `System.Timers.Timer`**:它把 Tick 排到 **UI 线程**执行——回调里可以安全碰 UI 集合,不用 Dispatcher.Invoke。选哪个 Timer 的判据:碰 UI 用 DispatcherTimer,后台干活用 System.Threading.Timer(R5 管道那个)。

**第 2 步 · 数据通路:Push/PushOne + 演示三件套 + Dispose**(贴进类里,最后一个 `}` 之前)

```csharp
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
```

📚 **知识点**
- **`Push` 按 PointId 分流**:点位 1 进温度线、点位 2 进压力线——id 映射做成属性(`TemperaturePointId`),现场换点位号改属性就行,不用改代码。
- **`PushOne` = Add + 超限淘汰**:`while (col.Count > MaxPoints) RemoveAt(0)`——滚动窗口的经典三行。数据永远最新 600 个,内存恒定,挂机一个月也不涨。
- **`StartDemo`/`OnTick` 是演示模式**:没有数据源时自造 25±5℃ / 80±10kPa 随机数,给销售演示用。**坑⑥的案发现场**:演示模式和真实 Push 是两条进料口,接了真实数据就必须不开演示——否则你看到的永远是假数据。
- **Dispose 里 `-=` 退订再 Stop**:先退订后停,彻底没有"停了以后还有一发 Tick 进来"的竞态窗口。

**第 3 步 · 构造函数:注入 Series + 备好 Timer**(最后贴——它引用第 2 步的 OnTick)

```csharp
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
```

📚 **知识点**
- **`InitializeComponent()` 是 XAML 与 C# 的合体仪式**:partial 类的另一半(XAML 编译产物)在这里被加载,`Chart` 这个 x:Name 字段从此可用——**UserControl 构造函数的第一行永远是它**,忘了 = NullReferenceException。
- **`Chart.Series = new ISeries[] { ... }`**:两条 LineSeries,各自 `Values = _temp/_press` **引用同一个集合**——图表从此盯住这两个集合,Push 一条它画一条。`GeometrySize = 0`(不画数据点圆点)、`Fill = null`(不填充线下面积)、`LineSmoothness = 0.3`(轻微软化)——工业曲线要的是"细线快滚",不是漂亮面积图。
- **`DispatcherPriority.Background`**:Timer 回调排在渲染之后——**数据再密也不抢绘制帧**,宁可 tick 晚一点不让界面卡。优先级思维是 WPF 性能调优的第一课。

<details markdown="1">
<summary>📄 完整文件 ChartView.xaml.cs(对答案 / 整体粘贴用)</summary>

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

</details>

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

📚 **知识点**
- **UserControl 的取舍现场**:这个面板 = 一个 TextBlock + 一个 ListBox,纯"页面级组合"——UserControl 三分钟搞定;GaugeControl 要换肤/跨项目复用,才值得上 Control+模板。**先问"要不要跨项目复用",再选路线**。
- **`{Binding DiagnosticsSummary}` 直绑 VM 属性**:UserControl 的 DataContext **继承自父窗口**(MainViewModel),零接线直接绑——这就是为什么 DiagnosticsPanel.xaml.cs 里几乎没有代码。
- **`{Binding}` 空路径绑定**:ListBox 的 ItemTemplate 里 `Text="{Binding}"`——绑定"项本身"(项是 string,没有子属性)。列表项是纯字符串时的标准写法。

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

📚 **知识点**
- **"几乎没有逻辑"正是满分答案**:MVVM + UserControl 的正确姿势——UI 只负责长什么样,数据(DiagnosticsSummary/DiagnosticsLog)和行为全在 ViewModel/R7 的 DiagnosticsService 里。**后台代码文件越空,分层越干净**。**前端类比**:组件只有 JSX 和样式,状态全在 store。

### ⑦ MainWindow —— 主窗口组装(R8 删减版 ×2)

> 📂 `src/DaqMonitor.UI/MainWindow.xaml`
> 💡 删了什么:右侧「配方管理」「运动控制」两个 Tab(R9+ 各篇加回)、顶部导出报表的日期区间+按钮(报表篇)、右上角当前用户区+登出(认证篇)。布局骨架三行:顶部操作条 / 中部左表右 Tab / 底部架构说明
> ⚠️ **坑 ③ —— 状态文字的绑定必须写 `Mode=OneWay`(对参考工程的一处纠错)**:`Run.Text` 和 `TextBox.Text` 一样**默认 TwoWay**,而 `IsRunning` 是 `private set` 只读属性——不带 OneWay 的话窗口一 Show 就抛 `InvalidOperationException: 无法对只读属性 IsRunning 执行 TwoWay 绑定`(我在沙盒实跑抓到的,参考工程原文漏了这半句,启动即崩)。这是 WPF 经典冷知识:默认 TwoWay 的常用属性只有 `TextBox.Text`、`Run.Text`、`Slider.Value`、`CheckBox.IsChecked` 等少数几个,其余默认 OneWay。

**第 1 步 · 窗口壳 + 顶部操作条**(新文件,贴 Window 开头、Grid 行定义和顶部)

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
```

📚 **知识点**
- **四个 xmlns 是四条"import 语句"**:vm/ctrl/diag/views 各引入一个命名空间,后面 `<ctrl:GaugeControl>`、`<views:ChartView>` 才能用——**XAML 版 import,前缀就是别名**。
- **三行 Grid = 顶栏 / 主区 / 底栏**:`Auto`(按内容)+ `*`(占满剩余)+ `Auto`——经典页面骨架,WPF 版 `flex-direction: column` + `flex: 1`。
- **按钮双绑定:Command + IsEnabled**:`Command="{Binding StartCommand}"` 管"点了干嘛"(ICommand),`IsEnabled="{Binding CanStartAcquisition}"` 管"能不能点"。IsRunning 一翻,VM 连环通知(③ 步2 讲过),按钮灰亮自动切换。
- **坑③现场:`Run.Text` 必须 `Mode=OneWay`**:`Run.Text` 和 `TextBox.Text` 一样默认 TwoWay,而 IsRunning 是只读属性——不带 OneWay 窗口一 Show 就抛 InvalidOperationException。**默认 TwoWay 的常用属性只有 Text/Run.Text/Slider.Value/CheckBox.IsChecked 等少数几个**,这个冷知识面试能加分。

**第 2 步 · 左侧点位表:DataGrid 里嵌自定义控件**(紧接着贴)

```xml
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
```

📚 **知识点**
- **DataGrid 三种列型混用**:TextColumn(纯文本)/ TemplateColumn(塞任意控件)——仪表列和状态列都是 TemplateColumn,里面放 ④ 的两个自定义控件。**自定义控件的消费者在此兑现**:一行 XAML,GaugeControl 七个依赖属性用上四个(Value/Min/Max/Level/Label)。
- **`AutoGenerateColumns="False"`**:不让 DataGrid 自作主张按属性名生成列——工业表格要的是"列序/格式/宽度全可控"。
- **`StringFormat=P{0}` / `HH:mm:ss.fff`**:点位列显示成 P1/P2/P3,时间列显示毫秒——毫秒是工业排查的眼睛(两个批次差 200ms 一眼看出)。
- **模板里的 `{Binding Value}` 绑的是行对象**(PointView),不是窗口 VM——DataGrid 把每行 DataContext 换成该项,**前端类比**:表格 render 函数里 `record.value`,作用域自动切到行。

**第 3 步 · 右侧 Tab + 底部说明 + 收尾**(贴完补 `</Grid></Window>`)

```xml
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

📚 **知识点**
- **左表右 Tab 同在 Grid.Row=1**:两个控件同行重叠?不——GroupBox 用 `Margin="0,0,8,0"`、TabControl 用 `HorizontalAlignment="Stretch"`,实际由 Grid 默认布局各占一半(WPF Grid 同格多子元素会叠加,这里靠 GroupBox 内容宽度撑开——**这个布局是参考工程原样,真要精确分栏该用 Grid.ColumnDefinitions,面试别背错**)。
- **`x:Name="ChartTab"`**:给 ChartView 起名,MainWindow.xaml.cs 靠它把曲线页接给 VM(AttachChart)——**x:Name = 编译成字段,后台代码直接引用**。
- **报警日志一行搞定**:`ListBox ItemsSource="{Binding AlarmLog}"`——VM 头插日志、集合通知、ListBox 自动刷新,零后台代码。**MVVM 的甜点时刻:数据链路通了,界面就是声明式的**。
- **底部架构说明不是装饰**:面试演示时指着这行讲数据流——设备→管道→存储/报警→UI,一条线讲完整个项目。

<details markdown="1">
<summary>📄 完整文件 MainWindow.xaml(对答案 / 整体粘贴用)</summary>

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

</details>

> 📂 `src/DaqMonitor.UI/MainWindow.xaml.cs`
> 💡 两个 Converter 放进窗口资源(XAML 里 StaticResource 才找得到);`DataContextChanged` 时机接 ChartView——因为 `new MainWindow{DataContext=vm}` 构造时 VM 还没来

**第 1 步 · 两个值转换器**(新文件,先贴 usings + 两个小类)

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
```

📚 **知识点**
- **IValueConverter = 绑定管道里的 pipe**:`bool → "采集中"/"已停止"`,XAML 里 `Converter={StaticResource RunningText}` 调用——**数据不改,显示改**,转换器是 MVVM 里"展示逻辑"的家。**前端类比**:Vue 的 filter / Angular pipe,一模一样的角色。
- **`ConvertBack` 返回 `Binding.DoNothing`**:单向转换器不写回流——绑定引擎收到 DoNothing 就跳过写回,比抛 NotSupportedException 温和。
- **四个参数全用 `_`/`__` 弃名**:参数用不上就弃名,C# 的"我看见了但不用"——比 parameter1/parameter2 诚实。

**第 2 步 · MainWindow 类:资源注册 + 曲线接线**(同文件接着贴)

```csharp
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

📚 **知识点**
- **`Resources.Add` 必须在 `InitializeComponent()` 之前**:XAML 解析时就要找 `{StaticResource RunningText}`——资源没提前注册,窗口加载即抛"找不到资源"。**顺序就是契约**。
- **为什么接线放 `DataContextChanged` 不放构造函数**:`new MainWindow { DataContext = vm }` 先跑构造函数、**后**赋 DataContext——构造那一刻 VM 还不存在。订阅 DataContextChanged,等 VM 注入了再 AttachChart——时序问题的标准解法。**前端类比**:constructor 里 props 还没到,用 didMount/Effect 接。
- **窗口后台代码只有两件事**:注册资源 + 接线曲线。布局归 XAML、数据归 VM,Code-behind 只做"胶水"——**判断 MVVM 干不干净,看 code-behind 行数**。

<details markdown="1">
<summary>📄 完整文件 MainWindow.xaml.cs(对答案 / 整体粘贴用)</summary>

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

</details>

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
- [ ] 「实时曲线」页:启动前**静止不动**(不接演示模式,没数据就不画);启动采集后两条线(温度/压力)滚动推进,数值与左侧仪表一致(是真实采集值,不是随机数)
- [ ] 「诊断 / 调试」页:统计行"采样 N 点|报警 N 次|批次 N|末批 Nms"持续增长,日志列表有启动/报警记录
- [ ] 点「停止采集」→ 状态变 **已停止**,仪表读数停住,曲线也停住(没有批量数据进来,曲线自然冻结)
- [ ] 关窗退出,`%LocalAppData%\DaqMonitor\daq.db` 存在(R6 的库在真实路径上)

> 🧪 R8 的 UI 我在交付前做了**全流程模拟运行**(点启动 → 批量数据 → 报警触发 → 长时间挂机),抓掉参考工程的 3 个潜伏运行时 bug;上线后真机运行又抓出第 4 个(坑⑥)——这类 bug 短暂冒烟根本暴露不出来,都是真跑起来才炸:
> - **坑③(Run.Text)**:状态文字绑定只读 IsRunning,默认 TwoWay 启动即崩 → `Mode=OneWay`(见 ⑦)
> - **坑④(Duration)**:Generic.xaml 脉冲动画 `Duration="0 0:0:0.6"` 不是合法 TimeSpan,状态灯进 Connecting 即崩 → `0:0:0.6`(见 ④)
> - **坑⑤(跨线程日志)**:DiagnosticsService 后台线程直改被 UI 绑定的日志集合,点启动第一批数据即崩 → R7 已修(捕获 SynchronizationContext 投递)
> - **坑⑥(演示模式接管主屏,真机运行发现)**:ChartView 自带 `StartDemo()` 演示模式(无数据源时自造随机数),接线时顺手在 `AttachChart` 里调了它——结果**没点启动采集曲线也在跳**,而且跳的是随机数;更隐蔽的是 `OnBatchReady` 里忘了 `_chart.Push(p)`,就算启动了,曲线显示的也永远不是真实采集值。修法两行:AttachChart 只存引用不开演示,BatchReady 循环里补 `Push`。教训:**演示入口和真实数据通路是两条线,接了真实通路就必须关演示入口**,否则你看到的永远是假数据。
> **交互效果(指针/报警变色/曲线滚动)按上面清单逐条自验**——每一行都能追溯到 R2~R7 某个已测过的模块。

## ✅ 验收清单

- [ ] build 0 错、代码 0 警告(仅 LiveCharts2 传递依赖的 NU1701),test 56/56 绿
- [ ] 手动验收清单 9 条全过
- [ ] 能回答:为什么 PointView 是 class 而 SensorPoint 是 struct?([struct-vs-class](kp:struct-vs-class):高频小值传拷贝用 struct;UI 绑定要引用+通知用 class)
- [ ] 能回答:BatchReady 里不包 `Dispatcher.Invoke` 会怎样?(后台线程改 ObservableCollection → InvalidOperationException,这就是 [Dispatcher](kp:dispatcher) 存在的意义)
- [ ] 能回答:自定义控件和 UserControl 什么时候选哪个?(基础件/换肤/跨项目复用 → Control+Generic.xaml;页面级组合 → UserControl)
- [ ] 能回答:Generic.xaml 为什么必须在 Themes/ 目录、ThemeInfo 特性干什么?(WPF 按约定找主题字典;ThemeInfo 告诉它"字典在本程序集内")
- [ ] 能回答:状态文字 `<Run Text="{Binding IsRunning,...}"/>` 为什么必须写 `Mode=OneWay`?(Run.Text 默认 TwoWay,IsRunning 只读 → 不写启动即崩)
- [ ] 能回答:R7 的 DiagnosticsService 为什么要捕获 SynchronizationContext?(后台线程写被 UI 绑定的集合,lock 防互斥但不解决线程亲和;Post 回 UI 上下文才安全)
- [ ] git commit -m "R8: WPF主界面(点位表+仪表+状态灯+报警日志+曲线+诊断,主屏先行)"

## 🎤 面试怎么讲这一篇

> "界面用 MVVM:MainWindow 的 XAML 只做布局,数据全部绑 MainViewModel——点位表绑 ObservableCollection,启停按钮绑 RelayCommand,IsRunning 翻转时通知 CanStart/CanStop 按钮自动灰亮。采集回调在后台线程,更新 UI 前统一 Dispatcher.Invoke 切回 UI 线程。展示模型上我把领域层的 SensorPoint(struct)转成 PointView(class + INotifyPropertyChanged),因为 struct 是值拷贝、没有属性通知,绑不上 UI——领域模型和视图模型各管各的。控件层我写了两个自定义控件:量程仪表盘和设备状态灯,继承 Control、依赖属性对外、外观全部在 Generic.xaml 的 ControlTemplate 里,报警级别用 DataTrigger 驱动换色——换肤、复用都不用动 C# 代码。曲线用 LiveCharts2 双线滚动缓冲六百点,真实点位从 BatchReady 批量回调里 Push 进去,未启动采集时曲线静止。整窗从 DI 组合根装配,换真设备只改 Bootstrapper 一行注册,UI 和采集层零耦合。"

**✅ 打卡[ ]**
