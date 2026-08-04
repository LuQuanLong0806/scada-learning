# WPF / XAML 入门速查（前端类比版）🖥️

> **优先级定位**：🔴 必学 · 地基里的 WPF 部分（M0 Day 8 立工程，这里集中讲「怎么写 XAML」）
> **给谁看**：你是有扎实 JS/TS 直觉的前端工程师。WPF 对前端来说**一点都不陌生**——XAML ≈ JSX，Binding ≈ 数据驱动视图。这份速查把所有 WPF 概念和你的前端知识一一对上，**先讲概念 + 怎么用，再给工程对照**。
> **技术来源**：🟩 WPF 随 .NET（`<UseWPF>true</UseWPF>`，不装包）；🟧 `CommunityToolkit.Mvvm`（MVVM 源生成器）。

## 一句话讲清楚
WPF 用 **XAML（声明式 UI）+ 数据绑定** 驱动界面，思想和 React「声明 UI + state 变 → 视图自动重渲染」完全同源。**你不是在「代码里改控件属性」，而是在「绑一个数据，数据变了界面自己变」**——这就是 WPF 和老 WinForm「直接 `label.Text = x`」的本质区别。

---

## 🟢 核心映射表（前端 → WPF，先建立肌肉记忆）

| 前端概念 | WPF 概念 | 说明 |
|---|---|---|
| `JSX` / `html` | **XAML** | 声明式描述 UI 结构 |
| `id` / `ref` | `x:Name` | 后台代码里拿控件实例 |
| `const [x,setX]=useState()` | **`{Binding X}` + 属性变化通知** | 数据变 → 界面自动变 |
| 组件 `state` / `props` | **DataContext / ViewModel** | 视图绑定的数据来源 |
| `useEffect` 监听 → 改 DOM | **INotifyPropertyChanged** | 属性变了通知 WPF 重绘 |
| CSS 类 / `:hover` | **Style / Trigger** | 状态样式 |
| 重写组件 `render` | **ControlTemplate / DataTemplate** | 改外观 / 按数据类型给不同视图 |
| `array.map(x => <li>)` | **ItemsControl + ItemTemplate** | 列表渲染 |
| `flex` / `grid` 布局 | **StackPanel / Grid / DockPanel** | 布局容器 |
| `onClick={fn}` | **`Command` / `Click` 事件** | 交互 |
| `useCallback` | **`ICommand` / RelayCommand** | 可绑定的命令（带能否执行） |
| React 自动批处理 | **`Dispatcher.Invoke`** | 后台线程改 UI 必须「回主线程」 |
| 全局 CSS 文件 | **ResourceDictionary / Themes/Generic.xaml** | 全局样式表 |
| `reactive` 状态 | **DependencyProperty** | 可被绑定/动画/样式监听的"超级属性" |

---

## 🟡 逐点精讲：怎么用（先懂用法，再落工程）

### ① 最小窗口：XAML ≈ JSX
```xml
<!-- MainWindow.xaml -->
<Window x:Class="DaqMonitor.UI.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        Title="DAQ Monitor" Height="450" Width="800">
    <Grid>
        <Button x:Name="BtnStart" Content="启动采集" Click="BtnStart_Click"/>
        <TextBlock x:Name="TxtStatus" Text="未启动" HorizontalAlignment="Center"/>
    </Grid>
</Window>
```
**前端类比**：`<Window>` 像根组件，`<Grid>` 像布局容器，`<Button Click=...>` ≈ `onClick`。`x:Name` ≈ `ref`，后台 `.cs` 里直接 `TxtStatus.Text = "..."`。这是**WinForm 式写法**（直接操作控件），能用但不地道——下面讲地道的数据驱动写法。

### ② 绑定基础（地道写法）：Text = "{Binding Temp}" ≈ v-model
**关键**：界面不写死值，而是「绑一个属性」。属性变了，TextBlock 自动更新。
```xml
<TextBlock Text="{Binding Temp, StringFormat={}{0:F1} ℃}"/>
```
后台 ViewModel（用 CommunityToolkit.Mvvm 源生成器，免写样板）：
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private double _temp;   // 自动生成 public double Temp + 变化通知
    // 后台只改 _temp 或 Temp，界面自动刷新——不用手动 .Text =
}
// MainWindow.xaml.cs 里把 VM 设为数据上下文：
public MainWindow() { InitializeComponent(); DataContext = new MainViewModel(); }
```
> 🔥 **坑**：普通 `public double Temp { get; set; }` 不会触发 UI 更新！必须实现 `INotifyPropertyChanged`（或用 `[ObservableProperty]` 源生成）。这是前端转过来**第一坑**：你改了属性界面没动，多半是忘了通知。

### ③ 列表绑定：ItemsControl ≈ array.map()
```xml
<DataGrid ItemsSource="{Binding Points}" AutoGenerateColumns="True"/>
<!-- Points 是 ObservableCollection<SensorPoint>：增删项界面自动跟着变 -->
```
**前端类比**：`ObservableCollection` ≈ 一个「带响应式能力的数组」，`.Add()` 就像 React state 数组 push 后重渲染。`SensorPoint` 里的属性要能被绑定显示（所以 M0 强调"WPF 绑定只认属性"——领域模型全用属性暴露）。

### ④ 命令绑定：Command ≈ useCallback
```xml
<Button Command="{Binding StartCmd}" Content="启动"/>
```
```csharp
[ICommand] private void StartCmd() => _pipeline.Start();  // CommunityToolkit 源生成 ICommand
```
**前端类比**：`Command` 是把"点击该干嘛"也变成可绑定的数据，比 `Click` 事件更适合 MVVM（界面和逻辑彻底解耦）。`[ICommand]` 自动生成 `StartCmd` 属性。

### ⑤ 模板：ControlTemplate ≈ 重写 render
```xml
<Button Content="报警">
  <Button.Template>
    <ControlTemplate TargetType="Button">
      <Border Background="{TemplateBinding Background}" CornerRadius="6">
        <ContentPresenter/>   <!-- 把"内容"放进来，类似 {children} -->
      </Border>
    </ControlTemplate>
  </Button.Template>
</Button>
```
**前端类比**：`ControlTemplate` = 你决定这个按钮"长什么样"，等价于 React 里重写组件的返回 JSX。`DataTemplate` 更常用：给不同类型数据自动选不同视图（≈ 根据 `type` 字段 `if/else` 选组件）。

### ⑥ 依赖属性 + 自定义控件（呼应 M14 的 GaugeControl）
```csharp
public class GaugeControl : Control
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(GaugeControl),
            new PropertyMetadata(0d, OnValueChanged));  // 值变 → 回调（可触发重绘/动画）
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    private static void OnValueChanged(DependencyProperty d, DependencyPropertyChangedEventArgs e) { /* 重绘 */ }
}
```
**前端类比**：`DependencyProperty` ≈ 一个"超级 state"——它不只是存值，还能被 **Style/动画/绑定/继承** 监听（React 里得自己用 context + effect 拼）。WPF 把这个能力内建了。

### ⑦ 跨线程改 UI：Dispatcher ≈ 回主线程
```csharp
// 后台线程拿到数据后，必须回到 UI 线程才能改界面（直接改会抛异常）
Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "收到数据");
```
**前端类比**：React 自动把 state 更新批处理回主线程；WPF **不会自动**，后台线程碰 UI 必崩，必须 `Dispatcher.Invoke`（或绑定——绑定天然在 UI 线程生效，所以**优先用绑定而不是手动 Dispatcher**）。

---

## ⭐ 重点 / 🔥 坑表

| 项 | 说明 |
|---|---|
| ⭐ **数据驱动 > 控件驱动** | 写 WPF 第一原则：界面绑数据，别在 `.cs` 里狂写 `label.Text =`。MVVM 就是把这个原则制度化 |
| 🔥 **属性要能通知** | 绑定不刷新？99% 是忘了 `INotifyPropertyChanged` / `[ObservableProperty]` |
| 🔥 **后台线程碰 UI 必崩** | 用 `Dispatcher.Invoke` 或（更好）走绑定让数据自己驱动 |
| 🔥 **`{Binding}` 找不到值** | 八成是 `DataContext` 没设对（VM 没挂上去）。调试第一看 DataContext |
| ⭐ **ItemsControl 配 ObservableCollection** | 列表要"增删自动刷新"，数据源必须是 `ObservableCollection` 不是 `List` |

---

## 🧪 三档练习
- 🟢 **基础题**：写一个 `TextBlock` 绑定 `Count` 属性，`Button` 点击让 `Count++`，要求**不写一行 `.Text =`**（纯绑定 + `[ObservableProperty]`）。
- 🟡 **进阶题**：用 `ItemsControl` + `ObservableCollection<string>` 绑定一个列表，点击按钮 `Add` 一项，验证界面自动出现新行。
- 🔴 **挑战题**：把 M0 Day 8 的「模拟采集滚动数据」从"手动 Dispatcher 改 DataGrid"改造成"ViewModel + ObservableCollection 绑定驱动"，体会数据驱动 vs 控件驱动的区别。

### 💡 工控导师说
> 新人最常犯的错：把 WPF 当 WinForm 写——在事件里 `txtTemp.Text = value`，几百个点一刷新 UI 直接卡死。记住一句：**WPF 里"数据是唯一真相"，界面只是数据的投影**。你只管改 ViewModel 的属性，刷新是 WPF 的事。

### 🎓 职业建议
面试被问"WPF 和 WinForm 区别"，答："**WinForm 控件驱动（直接改属性），WPF 数据驱动（XAML 绑定 + MVVM）**；WPF 适合复杂工业界面 + 自绘控件，老项目维护 WinForm"。能顺带说"我用 `CommunityToolkit.Mvvm` 源生成器少写样板"会很加分。

### 📅 明日预告
回到主线：M5 实时可视化——用 LiveCharts 在 WPF 里画实时曲线，数据正是来自这里讲的 `ObservableCollection` 绑定；M8 用 MVVM 把整个 DAQ Monitor 界面重构为"数据驱动"。

---

## 📌 温故知新（跨模块联动）
- **M0 Day 7/8**：并发 + 立 WPF 工程骨架——本速查是那两步里"XAML 怎么写"的集中补讲。
- **M5 可视化**：`ChartVm` + `ObservableCollection` 绑定曲线，正是这里 ②③ 的应用。
- **M8 MVVM**：`CommunityToolkit.Mvvm` 的 `[ObservableProperty]`/`[ICommand]` 源生成器，本速查 ②④ 已用。
- **M14 自绘控件**：`GaugeControl`/`StatusDot`（工程里真实存在）用的就是这里 ⑥ 的依赖属性。

## 📚 延伸阅读
- WPF 数据绑定总览：https://learn.microsoft.com/dotnet/desktop/wpf/data/
- XAML 概览：https://learn.microsoft.com/dotnet/desktop/wpf/xaml/
- CommunityToolkit.Mvvm（源生成器）：https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/
- 自定义控件：https://learn.microsoft.com/dotnet/desktop/wpf/controls/control-authoring-overview
