# M14 — WinForm 与 自定义控件

> **优先级定位**：🟡 缓学 · WinForm 双修 + 自绘控件（JD 常写 WinForm/WPF 双修）
> **技术来源**：🟩 `System.Windows.Forms`（.NET 8 需 `<UseWindowsForms>true</UseWindowsForms>`，BCL）；🟦 自绘 GDI+ / WPF。
> **给简历加的能力**：能维护老 WinForm 项目（JD 双修要求）+ 能自绘仪表/趋势控件（JD「熟练自定义控件」点名）。
> **前置**：M0（C#/事件）、M5（可视化）。
> **前端类比总纲**：WinForm 像 jQuery 时代"直接 `$(el).text()` 改 DOM"；WPF 像 React"绑数据驱动视图"；自定义控件像"封装一个可复用的 React 组件"。

---

> 📎 **本项目已提前落地**：DAQMonitor 里已经有**两个真正的 WPF 自定义控件**，而且从早期就接在 `MainWindow` 实时点位表里"边做边用"了，不是等到 M14 才学：
> - `DaqMonitor.UI/Controls/GaugeControl.cs` + `Themes/Generic.xaml` —— 量程指针表，绑 `PointView.Value`，每个点位一格仪表。
> - `DaqMonitor.UI/Controls/StatusDot.cs` + `Themes/Generic.xaml` —— 设备状态灯，绑 `PointView.State`，`Connecting` 带脉冲动画。
> 它们用的是**最正宗的自定义控件写法**（继承 `Control` + `DefaultStyleKey` + `Generic.xaml` + `ThemeInfo` 程序集特性 + `DependencyProperty`），所以本模块 Day 2 是"讲透你已经用着的控件"，再扩展出波形 `Sparkline`、LED 数码管等，而不是从零讲概念。M5 的可视化、M6 的报警会把 `GaugeControl.Level` 驱动起来；M12 工程量转换后，绑的 `Value` 直接变成工程量（℃/MPa），控件零改动。

> ⏱️ **阅读路径**(按时间预算选入口)
> - **3 分钟**:看「模块目标」— 知道 WinForm 像 jQuery 时代 DOM 操作
> - **30 分钟**:加看 Day 1 WinForm 跑一个 Hello World + 看 WPF/WinForm 区别
> - **3 小时**:全文精读 + Day 2 **自绘控件(GDI+)** 仪表盘
> - 🎯 **面试高频**:WPF 数据驱动 vs WinForm 事件驱动 / **GDI+ 自绘圆弧仪表** / 双缓冲防闪烁
> - 🔁 **配套复习**:[速记卡 Q2 WPF vs WinForms](面试高频知识点_速记卡.md) · [间隔重复表](记忆与复习机制_间隔重复版.md)

> 📚 **前置语法**(M14 用到的,陌生请查 [C# 语法速查 — 前端视角](CSharp语法速查_前端视角.md))
> - `public class GaugeControl : Control` — 继承 WPF Control
> - `public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(...)` — 依赖属性(WPF 特有)
> - `public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }` — 依赖属性完整 get/set
> - `protected override void OnRender(DrawingContext dc)` — GDI+/WPF 自绘
> - `dc.DrawArc(pen, rect, startAngle, sweepAngle)` — 画圆弧(仪表盘指针)
> - `class WinFormsForm : Form` — WinForm 窗体继承(对比 WPF Window)

## 模块目标
写出一个 WinForm 小窗体（按钮 + 文本框 + 后台线程刷新）+ 讲透自绘仪表盘控件（开发 / 复用 / 扩展 / 调试四场景）。

---

## Day 1 — WinForm 速成（与 WPF 对照）🟢

### 一句话讲清楚
WinForm = 拖控件 + 双击写事件，**直接操作控件属性**；WPF = XAML 描述 + 数据绑定，UI 与逻辑分离。老工业软件 90% 是 WinForm，必须会读会改。

### 前端类比秒懂
| WinForm | 前端类比 |
|---|---|
| 拖控件 + 事件 | jQuery `$(btn).on('click')` 直接改 DOM |
| 数据绑定弱 | 没有 React 的单向数据流 |
| 跨线程改 UI 要 Invoke | 必须在主线程更新 DOM |

### 分点精讲
**① 创建工程**（🟩）
```xml
<PropertyGroup><UseWindowsForms>true</UseWindowsForms></PropertyGroup>
```
**② 控件 + 事件**（🟩）
```csharp
button1.Click += (s, e) => label1.Text = "clicked";
```
**③ 后台线程刷新 UI —— 必须 Invoke**（🟦，WinForm 专属坑）
```csharp
Task.Run(() =>
{
    var v = ReadSensor();
    // 跨线程直接改 label1.Text 会抛"线程间操作无效"
    label1.Invoke(() => label1.Text = v.ToString());
});
```
> WPF 用 `Dispatcher` 或绑定，WinForm 用 `Control.Invoke`——本质都是"回到 UI 线程"。

### 🔬 掰开揉碎：WinForm vs WPF 心智模型
- WinForm：**控件是老大**，你代码里到处 `textBox1.Text = ...`，UI 和逻辑耦合。
- WPF：**数据是老大**，XAML 绑定 `Text="{Binding Temp}"`，后台只改 `Temp` 属性。
- 选型：新项目 WPF（我们 DAQ Monitor 用）；接手老项目 WinForm。

### ⭐ 重点 / 🔥 坑
| | 内容 |
|---|---|
| ⭐ 重点 | WinForm 跨线程改 UI 必须 Invoke，否则抛异常 |
| 🔥 坑 | 控件闪烁（双缓冲 `DoubleBuffered=true`）、高 DPI 缩放模糊 |
| 🔥 坑 | `Invoke` 嵌套/死锁：背景线程里又等 UI 线程结果会卡死 |

### 🟢 基础题
WinForm 窗体放 Button，点击后后台线程计数，用 Invoke 更新 Label。

### 🟡 进阶题
把 M0 的 `SimulatedDevice` 产生的点接到 WinForm：后台线程收 `DataReceived`，Invoke 到 Label/TextBox 显示最新值。

### 🔴 挑战题
写一个"线程安全"的 `SetText(Control c, string t)` 辅助方法（内部判断 `c.InvokeRequired`，需要时 Invoke），并在多个控件上复用——体会 WinForm 跨线程的标准写法。

**✅ 答案（基础题）**
```csharp
button1.Click += (s, e) => Task.Run(() =>
{
    int n = 0; while (n < 100) { n++; label1.Invoke(() => label1.Text = n.ToString()); Thread.Sleep(50); }
});
```

**🏗️ 项目任务**：把 DAQ Monitor 的"点位表"概念用 WinForm 复刻一个最简版（证明你能双修），后台线程驱动 UI。

**🎓 工控导师说**：去工厂维护老设备软件，十有八九是 WinForm。你不会 WinForm，连"改个显示精度"都不敢动，只能等原厂。双修不是加分，是**上岗门票**。而且 WinForm 的 `Invoke` 跨线程是天天踩的坑——不写 `InvokeRequired` 判断，偶尔能跑、偶尔崩溃，最难查。

**💼 职业建议**：JD 几乎都写"WinForm/WPF 双修"。简历老实写"WPF 主，能维护 WinForm 老项目"，比只写 WPF 更稳。面试被问"两者区别"，答"WinForm 控件驱动、WPF 数据驱动 + 自绘控件写法不同"就到位。

**✅ 打卡[ ]**

---

## Day 2 — 自定义控件（自绘仪表盘/趋势）🟡

### 一句话讲清楚
现成控件不够"工业感"——仪表盘、液位、趋势是上位机门面。自绘控件 = 把"显示逻辑"封装成可复用、可换肤、可扩展的组件，这正是 JD「熟练自定义控件」要求的。

### 分点精讲
**① WPF 自绘**：继承 `UserControl` + 依赖属性，或重写 `OnRender` 用 `DrawingContext`。
**② WinForm 自绘**：`override OnPaint`，用 `Graphics` 画弧/线（GDI+）。
**③ 为什么 JD 点名**：现成控件不够"工业感"，自绘仪表盘/液位/趋势是上位机门面。

### 🧩 多场景应用：自定义控件怎么「开发 / 复用 / 扩展」

光会写一个控件不够 —— 真实项目里你要面对的是：**同一个控件要在 10 个界面用、要换肤、要加新行为、要适配不同数据源**。下面用我们项目里的 `GaugeControl` 把"开发 → 复用 → 扩展"全跑一遍（这就是你问的"多种用到的场景"）。

### 场景 A · 开发（从 0 写一个控件）
你已经做过了：`GaugeControl` 继承 `Control`，只暴露 `DependencyProperty`（Value/Min/Max/Unit/Label/Level），外观全在 `Generic.xaml`。
> **关键点**：`static GaugeControl()` 里 `DefaultStyleKeyProperty.OverrideMetadata` 指向自己 + `ThemeInfo.cs` 的 `[assembly: ThemeInfo]` 让 WPF 找到 `Generic.xaml`。**缺了 `ThemeInfo` 控件不报错但一片空白**（最经典坑，已踩平）。

### 场景 B · 复用（一处写、处处用，零复制）
| 复用方式 | 怎么做的 | 控件改动 |
|---|---|---|
| 同界面多实例 | DataGrid 模板列每行一个 `GaugeControl`，绑各自 `Value` | 0（模板自动克隆） |
| 跨窗口/跨项目 | 把 `Controls/` 抽成 `DaqMonitor.Controls` 类库，`dotnet add reference` | 0（样式随程序集走） |
| 绑不同数据源 | 同一控件绑"温度℃/压力MPa/流量"，只改 `Unit`+量程 | 0（M12 工程量转换就这么用） |

### 场景 C · 扩展（不动原控件，加能力）—— 4 档梯度
1. **加依赖属性（最轻）**：给 `GaugeControl` 加 `ShowScale`/`DecimalDigits`，模板里 `TemplateBinding` 控制显示，源码只动 1 处。
2. **换肤（不改逻辑）**：新建 `Themes/Dark.xaml` 覆盖 `TargetType=GaugeControl` 的 Style，运行时切 `MergedDictionaries` 即换深色皮肤，逻辑零碰。
3. **派生子类（加新行为）**：`class TrendGauge : GaugeControl { public ObservableCollection<double> History; }` —— 继承全部能力，只加"迷你趋势条"，父类零改。
4. **附加行为 AttachedProperty（横切）**：`static class GaugeBehaviors { public static readonly DependencyProperty PulseOnAlarm; }` 给任意 `GaugeControl` 附加"报警脉冲"行为，不影响原类 —— WPF 最优雅的扩展方式。

> 💡 **面试加分原话**："复用靠依赖属性 + Generic.xaml，扩展靠派生/附加行为/换肤，而不是复制代码" —— 这句话直接拉开和"只会拖控件"的差距。

### 场景 D · 调试自定义控件（排查"为什么没显示/不更新"）
- **空白/没显示** → 99% 缺 `ThemeInfo` 或 `Generic.xaml` 的 `TargetType` 写错；用 **VS 实时可视化树(Live Visual Tree)** 看控件有没有被创建。
- **值不更新** → **Live Property Explorer** 看 `DataContext` 对不对、`DependencyProperty` 的变更回调有没有触发；或给 `RecalcAngle` 下断点。
- **样式不生效** → 查资源字典合并顺序；开源神器 **Snoop** 能实时改属性看效果。

### 🔗 本项目已落地的"多场景"实证
- **复用实证**：`MainWindow` 实时表每行 `GaugeControl`（场景 B-1）；`GaugeControl` 同时被 M12 工程量、M6 报警驱动（场景 B-3 跨模块复用）。
- **扩展实证**：`Level` 属性让 M6 报警引擎直接把表盘变橙/红（场景 C-1 的实战版，控件零改）；`StatusDot` 的 `Connecting` 脉冲是场景 C-4 附behavior 的简化版。

### 🟢 基础题
给 `GaugeControl` 加一个 `ShowScale` 依赖属性（bool），模板里用它控制是否显示刻度，写一个最小 XAML 测试。

### 🟡 进阶题
新建 `Themes/Dark.xaml` 覆盖 `GaugeControl` 的 Style，把指针/背景改成深色，运行时切换 `MergedDictionaries` 验证换肤。

### 🔴 挑战题
写 `TrendGauge : GaugeControl` 子类，加 `History` 依赖属性（ObservableCollection<double>），在控件底部画一条迷你趋势线——父类零改动，验证"派生扩展"套路。

**✅ 答案（进阶题要点）**
```csharp
// App.xaml.cs 或启动时
Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/Dark.xaml", UriKind.Relative) });
// Dark.xaml 里 <Style TargetType="local:GaugeControl"> ... 改 Background/Foreground ...
```

**🏗️ 项目任务**：在 DAQ Monitor 的 `GaugeControl` 上加 `ShowScale`/`DecimalDigits` 两个依赖属性，并用 `Level` 属性让 M6 报警直接驱动表盘变色（你项目里其实已经接了，这里是"讲透 + 再加一个能力"）。

**🎓 工控导师说**：自绘控件最大的误区是"为炫技而自绘"。工厂里自绘的真实动机是**现成控件没有工业样式**（报警闪红、量程指针、液位动画）。我要求学员：先确认现成控件真满足不了，再自绘；自绘一定做成可复用控件，别在窗体 `Paint` 事件里写一堆一次性绘图代码——那是维护噩梦。

**💼 职业建议**："自定义控件"是区分"拖控件程序员"和"会造轮子工程师"的分水岭。能讲清"依赖属性 + Generic.xaml + ThemeInfo + 复用/扩展四场景"，面试官会直接把你归到"资深中级"那一档。

**✅ 打卡[ ]**

---

## 📌 温故知新 / 跨模块联动
- **M5**：LiveCharts 是"现成图表"；M14 自绘是"理解底层绘制"，面试能讲清原理。
- **M0**：后台采集 → M14 自绘控件实时刷新（同 Invoke/Dispatcher 套路）。
- **M12 / M6**：`GaugeControl` 的 `Value`/`Level` 被工程量、报警驱动，控件零改 = 企业级复用实证。

## 🧩 完整代码组装（GaugeControl 关键片段，已在你工程里）
```csharp
// DaqMonitor.UI/Controls/GaugeControl.cs
public class GaugeControl : Control
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(GaugeControl),
            new PropertyMetadata(0.0, (d, e) => ((GaugeControl)d).RecalcAngle()));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    // Min/Max/Unit/Label/Level(AlarmLevel) 同理 …
    static GaugeControl() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(GaugeControl), new FrameworkPropertyMetadata(typeof(GaugeControl)));
    private void RecalcAngle() { /* 由 Value/Min/Max 算出指针角度 -135°~+135° */ }
}
// DaqMonitor.UI/Themes/Generic.xaml 提供默认 Style；ThemeInfo.cs 让 WPF 找得到它
```
> 真实工程已把 `GaugeControl`/`StatusDot` 接进 `MainWindow` 实时表，M6 报警驱动 `Level`、M12 工程量驱动 `Value`，控件零改。

## 🔗 明日预告
**M15 工程协作与联调（Git + 敏捷 + 调试工具）**：技术到位了，但企业招你是要"在团队里交付"。M15 补齐 Git/敏捷/调试工具/联调定位——这些是初级岗 100% 要求的软硬技能。

## 📚 延伸阅读
- WPF · [自定义控件](https://learn.microsoft.com/zh-cn/dotnet/desktop/wpf/controls/control-authoring-overview)
- WinForms · [GDI+ 绘图](https://learn.microsoft.com/zh-cn/dotnet/desktop/winforms/advanced/using-managed-graphics-classes)

## 📎 关联
- 可视化主章节：**M5**；UI 工程化：**M8**。
