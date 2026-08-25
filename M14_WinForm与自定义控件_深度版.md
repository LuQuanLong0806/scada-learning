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

> 🔧 **创建命令**(在解决方案根目录执行,注意模板名是 `winform` 不是 `winforms`):
> ```bash
> # 注意模板名:dotnet new winform(单数),不是 winforms(复数)
> dotnet new winform -o src/DaqMonitor.WinFormsDemo
> dotnet sln add src/DaqMonitor.WinFormsDemo/DaqMonitor.WinFormsDemo.csproj
> ```
> 💡 这条命令会自动生成两个文件,见下「Form1 双文件结构」。
> 💡 `dotnet new winform` 自动在 `.csproj` 里写好 `<UseWindowsForms>true</UseWindowsForms>` + `<OutputType>WinExe</OutputType>`,不用手动改。

**📂 Form1 双文件结构(WinForm 老项目标准,小白必懂)**
| 文件 | 内容 | 你做什么 |
|---|---|---|
| `Form1.cs` | 事件处理代码(`button1_Click` 等) | 写业务逻辑 |
| `Form1.Designer.cs` | 控件声明 + 布局(`this.button1 = new Button();`) | **别手改**,VS 拖拽自动生成 |

> 💡 WinForm 把"界面布局"和"事件代码"分两个文件,Designer 文件由 VS 自动维护;**手动改 Designer 会被下次拖拽覆盖**,小白踩过这个坑就懂了。

**② 控件 + 事件**（🟩）
```csharp
// 写在 Form1.cs 里(不是 Designer.cs)
button1.Click += (s, e) => label1.Text = "clicked";
```
**③ 后台线程刷新 UI —— 必须 Invoke**（🟦，WinForm 专属坑）
```csharp
// 写在 Form1.cs 的某个方法里
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

> 📂 `DaqMonitor.UI/Controls/GaugeControl.cs` · namespace `DaqMonitor.UI.Controls`
> 🔧 无 NuGet(`System.Windows.Controls` 是 WPF BCL)
> 💡 WPF 自定义控件标准套路:`DependencyProperty` + `DefaultStyleKey` + `Themes/Generic.xaml` + `Properties/AssemblyInfo.cs` 的 `[assembly: ThemeInfo]`

```csharp
// DaqMonitor.UI/Controls/GaugeControl.cs
using System.Windows;
using System.Windows.Controls;

namespace DaqMonitor.UI.Controls;

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

---

## 🔧 工程对齐补丁:WinForms 双件套(2026-08-25 审计后补)

> 本讲义 Day 1 是 WinForms 速成、Day 2 走的是 **WPF** 控件路线(DefaultStyleKey/XAML 模板);而姊妹工程 **MotionControl(WinForms)** 需要的是另两条 WinForms 路线:**①界面怎么组织(WinForms 版"MVVM")②自定义控件怎么做(GDI+ 自绘)**。补在这里。深挖去 [项目实践 MC3](项目实践_MotionControl_MC3_WinForms主界面.md) / [MC6](项目实践_MotionControl_MC6_轨迹可视化.md) 与运控逐行导读。

### 补丁① WinForms 的"MVVM":集中刷新单一真源(WinForms 界面组织正解)

**先说清一件事**:WinForms **没有** WPF 那套 DataContext/Binding 生态(有 BindingSource/INPC 绑定但弱、老项目少用)。**业界主流做法 = code-behind + 集中刷新**:

```csharp
// MotionControl MainForm 的模式(工程真实代码,可直接抄):
private void RefreshUiState()          // ← 单一真源:界面长什么样只由这一个方法说了算
{
    foreach (var (i, grp) in _axisGroups.Select((g, i) => (i, g)))
    {
        var st = _card.GetAxisState(i);          // 读卡的真实状态
        grp.BtnEnable.Enabled = !st.IsEnabled;   // 按钮灰亮 = 状态推导,不是散落各处手改
        grp.BtnStop.Enabled = st.IsMoving;
        grp.LblState.Text = st.State.ToString();
    }
    btnConnect.Enabled = !_card.IsConnected;
    btnEstop.Enabled = _card.IsConnected;
}
// 任何事件回调/状态变化 → 改完数据 → 一句 RefreshUiState() 全量刷
```

| | WPF(你项目 DaqMonitor) | WinForms(你项目 MotionControl) |
|---|---|---|
| 心智 | 响应式:改数据,界面自己刷新(Vue) | 命令式:改完数据,**喊一嗓子全量重画**(老 jQuery 时代的 render()) |
| 刷新 | 属性 setter → INPC → 精准刷那一格 | `RefreshUiState()` 集中重算所有按钮/文本 |
| 优 | 精细、绑定声明式 | **土但可控**——界面状态只看一个方法就知道全貌 |
| 劣 | 忘 OnChanged 不刷新(坑多) | 忘调 RefreshUiState 不刷新(坑集中一处) |

**配套三件**(MC3 都有实战):
1. **控件数组 + 循环订阅**——两轴界面长得一样,把每轴的按钮/文本框打包成组,循环订阅:**闭包坑**`foreach (var i in ...) { btn.Click += (s,e) => Do(i); }` 老版本 C# 的 i 是共享变量,循环完全是最后一个值——现代 C# foreach 已修复,for 循环仍要 `int local = i;` 拦一道;
2. **InvokeRequired + BeginInvoke**(WinForms 版 Dispatcher):后台事件改 UI 前先问"我在 UI 线程吗"(`if (InvokeRequired) BeginInvoke(...)`),不是就把自己快递回 UI 线程——**和 WPF 的 Dispatcher.Invoke 同一思想,两套 API**;
3. **定时器边沿检测**——Timer 每 100ms 查轴状态,要自己记"上一拍是运动中"才能发现"刚刚停了"(上升沿/下降沿),WinForms 不会替你记。

### 补丁② WinForms 自定义控件 = GDI+ 自绘(TrajectoryPanel 路线)

Day 2 的 GaugeControl 是 **WPF 路线**;WinForms 的自定义控件走 **OnPaint 自绘**(工程 `UI/TrajectoryPanel.cs`,画 X-Y 运动轨迹):

```csharp
public class TrajectoryPanel : Control
{
    private readonly List<(float x, float y)> _pts = new();   // ① 数据与绘制分离:只存点
    public void Sample(float x, float y) { _pts.Add((x, y)); Invalidate(); }  // ② 采样→标记重绘

    protected override void OnPaint(PaintEventArgs e)          // ③ 系统要画时回调你
    {
        // ④ 双缓冲防闪烁(this.DoubleBuffered = true 或构造里开 OptimizedDoubleBuffer)
        // ⑤ mm→像素等比映射:x/y 用同一个缩放系数(取 min!),否则画的线变形
        // ⑥ Y 轴翻转:屏幕 Y 向下,数学 Y 向上 → py = Height - y*scale
        // ⑦ e.Graphics.DrawLine/DrawEllipse 把点连成轨迹
    }
}
```

**两路线对照表**(面试被问"自定义控件怎么做"先问清哪家):

| | WPF 控件(Day2 GaugeControl) | WinForms 自绘(TrajectoryPanel) |
|---|---|---|
| 核心机制 | `Control` + XAML 模板(Generic.xaml) | `Control` + `override OnPaint` + GDI+ |
| 值怎么进 | DependencyProperty(参与绑定系统) | 普通属性 + 手动 `Invalidate()` |
| 刷新 | 绑定/属性系统自动 | **自己调 Invalidate 触发重绘** |
| 外观 | XAML 声明,样式可换皮肤 | 全靠 Graphics 手画(线条/填充/抗锯齿) |
| 防闪烁 | WPF 合成系统天然双缓冲 | **要自己开双缓冲**(经典追问点) |

💼 **连环追问预埋**:"轨迹图闪烁怎么办?"→双缓冲(先画到内存位图再一次上屏);"画出来的直线是斜的/变形?"→等比映射必须 x/y 共用一个缩放系数;"为什么 WinForms 要手动 Invalidate?"→它没有属性→UI 的自动通知管道,一切手搓——这正是 WPF 诞生要解决的痛。
