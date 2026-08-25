# 🎛️ 项目逐行导读 · MotionControl 从零到吃透

> **这份文档是什么**:把 `MotionControl` 工程主线代码**从第一行讲到最后一行**——每行干什么、哪些是背了就能用的固定写法、哪些是必须理解的核心逻辑、为什么这么设计。工业术语 + 大白话双讲解,读完能白板画出整个项目。
> **不讲什么**:csproj / sln 等工程配置文件(照抄即可)。
> **配套**:对着真实代码读(`MotionControl/src/`),本文所有行号与真实文件一一对应(2026-08-25 版本,可用 `grep -n` 抽查)。
> **姐妹篇**:[DaqMonitor 逐行导读](项目逐行导读_DaqMonitor_从零到吃透.md)(采集监控线,57 万分音符的大乐队)——本文是**运控线**(指挥电机干活的指挥棒),两篇共享同一套架构思想:接口抽象 / 模拟设备 / 事件 / 跨线程 UI。

---

## 〇、先读我:怎么用这份文档

### 三个标记(全文通用,先认脸)

| 标记 | 意思 | 你该怎么对待它 |
|---|---|---|
| 🔧 **固定写法** | 框架要求的"仪式代码",全世界的 WinForms/.NET 项目都长这样 | **不用理解为什么,抄熟即可**。就像前端 `new Vue({})` 不需要背源码 |
| 🧠 **核心逻辑** | 这个项目自己的思考,换你写要自己想出来的部分 | **必须吃透**,面试就问这里 |
| 💼 **面试点** | 面试官真的会问的点 | 背下来,能用大白话讲 1 分钟 |
| 📚 **对应讲义** | 每站开头一框,标明本站知识点出自哪份讲义/项目实践篇 | **卡壳就点过去重学那一节**,读完回来继续 |

### 从哪里开始看(按你的状态选)

```
完全菜鸟(第一次接触这个项目) → 先读下方「先懂项目再读码」(需求逼出架构,10分钟) → 再从第一站顺读
有前端背景(学过 Vue/React)   → 同上先读「先懂项目再读码」,然后第一站快读,重点读第四/六站的「前端类比」框
只剩 1 小时(面试明天)        → 只读「先懂项目再读码」③ 推导链 + 各站「🎯 一句话」+ 附录A 的 30 秒讲法
程序报错/结果不对            → 直接翻附录 D「易错点急救手册」,按报错原文搜
想动手内化                   → 每站末尾「✂️ 自己改一处」;想深度内化 → 第③⑤站末尾挂的「⚙️ 亲手造发动机工作纸」
```

### 整个项目只有一句话

> **MotionControl = 把"动起来"这件事变成一条指令链:界面按钮 → IMotionCard 合同 → 模拟卡状态机一格一格推进位置 → 定时器采样 → 轨迹图画点连线。**

工业术语叫「运动控制上位机」(Motion Control)。大白话:**一个遥控两台电机的遥控器 + 一支记录笔**——你按按钮(点动/定位/回零),它替你转电机(模拟卡仿真),还把走过的路画下来(TrajectoryPanel)。DaqMonitor 是"看"的(读数),MotionControl 是"做"的(发指令)——工业软件的两大门类各占一门。

### 运控思维 vs 采集思维(转档必读,有 DaqMonitor 底子的同学)

做过采集项目再来读运控,最容易带过来的三个惯性思维,先纠偏:

| 惯性思维(采集线) | 运控线的正确姿势 | 为什么 |
|---|---|---|
| "数据来了刷上去就行,错了顶多显示难看" | **指令错了会撞机**——所以每条运动指令前有五道检查链,插补前逐轴全检 | 采集是只读,运控是写物理世界,不可逆 |
| "丢一两帧数据无所谓,下一帧就来了" | **急停必须下一拍生效**——令牌取消后 Task.Delay 立刻抛异常,不允许走完当前批次 | 安全动作的延迟上限=一个节拍(100ms) |
| "状态没必要精确,趋势对就行" | **位置必须精确贴目标**——走完全程再官方盖章一次(357-361 行),重复定位精度是运控的立身之本 | 机械加工/点胶的精度要求以 μm 计 |

一句话:**采集项目容错靠"下一帧",运控项目容错靠"事前拦截"**——这个思维差异本身就是一道面试题("采集和运控上位机的最大区别?")。



### 一条控制指令的旅程(全文的金线,每站都会回到它)

```
【按下】用户按住"轴1 正转"按钮(MouseDown,MainForm.cs:72)
   ↓ StartJog(0, 50, forward: true) —— 界面把"人话"翻译成"卡话"
【合同】IMotionCard.JogAxis(axis, speed, forward)(IMotionCard.cs:112)
   ↓ 界面只认合同不认卡——今天厨房端上来 MockMotionCard,明天换固高真卡,这行不改
【仿真】MockMotionCard.JogAxis(MockMotionCard.cs:129):过五道检查链 →
   StartMotionLocked 启动后台任务:每 100ms 把位置向软限位推进一步
   ↓ Task.Delay(tickMs, cts.Token) …… 位置 = 起点 + 步长 × 第几步
【汇报】每推一步,PositionChanged 事件在后台线程举起手
   ↓ MainForm.OnPositionChanged 发现自己在后台线程 → BeginInvoke 切回 UI 线程
【上板】位置框刷成 "12.300"(MainForm.cs:203)
【采样】同时,100ms 定时器每帧把两轴位置一起记一个 (X,Y) 点(MainForm.cs:240)
   ↓ trajPanel.Sample(x, y) → Invalidate() 标脏
【画线】TrajectoryPanel.OnPaint:网格 → 软限位边框 → 坐标轴 → 轨迹折线 → 当前点红点
【急停】任何时刻按红色 STOP → StopAll → 取消所有令牌 → 运动循环下个节拍就地冻结
```

记住这张图,后面每一站只是把其中一格放大讲。

### 项目的文件夹地图(先认路)

```
MotionControl/src/
├─ MotionControl/                ← 主程序(WinForms,net8.0-windows)
│  ├─ Program.cs                      ← 第①站:程序入口(14 行,全项目最短)
│  ├─ Device/IMotionCard.cs           ← 第②站:卡的"合同"(145 行,宪法)
│  ├─ Device/MockMotionCard.cs        ← 第③站:模拟卡(384 行)★心脏
│  ├─ Common/LogHelper.cs             ← 第④站顺手讲:线程安全日志(34 行)
│  ├─ UI/MainForm.cs                  ← 第④站:主窗体逻辑(330 行)
│  ├─ UI/MainForm.Designer.cs         ← 第④站:主窗体长相(735 行,只讲结构)
│  └─ UI/TrajectoryPanel.cs           ← 第⑥站:轨迹自绘控件(117 行)
└─ MotionControl.Tests/
   └─ MockMotionCardTests.cs          ← 全程当"验收标准"引用,第③⑤站反复点名(14 个测试)
```

**依赖方向只有一条,绝不允许倒流:UI → Device/Common**。Device 不知道 WinForms 的存在(它的事件在后台线程触发,谁订阅谁负责切线程)——和 DaqMonitor 的 Core/UI 纪律一模一样。

---

## 开篇 · 先懂项目再读码:一个需求逼出整个架构

> **读这一节的目的**:让你带着"它解决了哪条需求"去看后面每一站。完整建设过程见 MC 系列六篇(MC1→MC6)。

### ① 客户的故事(一切从一个需求开始)

> 一家做自动化设备的小厂:新机器上有一块两轴运动控制卡 + X/Y 两个伺服轴(类似 3D 打印机的横竖两个方向)。老板提了 7 条要求:
> 1. "我要能手动微调平台位置,按住走、松手停" → **点动(JOG)**
> 2. "输入坐标让它自己走到位" → **绝对定位**
> 3. "每天开机先回到机械原点,坐标才有基准" → **回零(回原点)**
> 4. "出事故时一个按钮全停下来,电机不能滑行" → **急停 + 位置冻结**
> 5. "别让它撞导轨两头" → **软限位 + 报警**
> 6. "X 和 Y 配合走斜线,不能走楼梯形" → **两轴直线插补**
> 7. "我看不到电机,你把走过的路画给我看" → **X-Y 轨迹图**

这 7 条就是全部——**界面上每个按钮、卡里每段逻辑,都对应其中一条**。

### ② 需求 → 界面控件 → 代码站 对照表(先对上号)

| 老板的要求 | 界面上在哪 | 代码在哪一站 |
|---|---|---|
| 点动 | 两轴各一对 ▲▼ 按钮 | 第②站合同 JogAxis / 第③站仿真 / 第④站 MouseDown+MouseUp |
| 绝对定位 | 目标位置框 + 「绝对定位」按钮 | 第②站 MoveAbsolute / 第③站 StartMotionLocked |
| 回零 | 「回零 ⌂」按钮 | 第③站 HomeAxis(走回绝对 0) |
| 急停 | 顶栏唯一红色大按钮 | 第③站 StopAll + 共享令牌 / 第④站 EmergencyStop |
| 软限位+报警 | 轴框底部灰字提示 + 淡黄报警框 | 第③站 CheckMotionLocked + 点动限位保护 |
| 两轴插补 | 「⇗ 两轴插补演示」按钮 | 第⑤站 MoveLinear |
| 轨迹图 | 中间栏白底方格图 | 第⑥站 TrajectoryPanel |

### ③ 推导链:每个零件都是被"没有它会怎样"逼出来的

**这是本导读最重要的一张表。** 每一步先问"遇到什么问题",再看"所以造了什么":

| 步 | 遇到的问题(没有它会怎样) | 所以造了 | 哪一站 |
|---|---|---|---|
| 1 | 真卡(固高/雷赛)几千块一张,而且卡在客户产线上——开发时手上没有 | **IMotionCard 合同 + MockMotionCard 模拟卡**:上层只认合同,模拟卡先行,真卡到了换实现 | 第②③站 |
| 2 | 位置一按按钮"瞬移"到目标,看不出运动过程,也测不出"运动中急停" | **tickMs 节拍仿真**:每 100ms 推一步,位置随时间连续变化 | 第③站 |
| 3 | 两轴同时点动,轴 2 一动就把轴 1 停了(v1 真坑:全局一个标志管两轴) | **每轴一个 CancellationTokenSource**,各动各的 | 第③站 |
| 4 | 急停只停轴 1、轴 2 还在跑——机器照样撞 | **StopAll 取消所有轴令牌**;插补时所有轴**共享同一个令牌**=一停俱停 | 第③⑤站 |
| 5 | 卡的事件在后台线程触发,直接改控件 → `InvalidOperationException`,界面崩 | **InvokeRequired + BeginInvoke** 统一切回 UI 线程 | 第④站 |
| 6 | "运动什么时候完成"没人告诉你(模拟卡/真卡都只有状态位) | **100ms 定时器轮询 + 边沿检测**(上一帧在动、这帧停了=刚完成) | 第④站 |
| 7 | X 走到中点时 Y 还没收到事件,轨迹画出锯齿 | **定时器同一时刻取两轴快照**(而不是订阅各自的 PositionChanged) | 第④⑥站 |
| 8 | 两轴各走各的,想走斜线走出"先横后竖"的楼梯 | **MoveLinear 插补:共用总步数 + 每轴等比分步**,任意时刻 X:Y 比例恒定 | 第⑤站 |
| 9 | 画轨迹每帧擦了重画,屏幕闪成迪厅 | **双缓冲**(先画内存位图再整帧贴屏)+ 数据绘制分离(Invalidate→OnPaint) | 第⑥站 |

> **读法建议**:合上这张表,自己复述一遍——"因为没有 ___ 会 ___,所以有 ___"。复述得出来,项目在你脑子里就是一台整机。

### ④ 建造顺序 MC1→MC6(先地基后装修,为什么是这个顺序)

```
MC1 骨架+模拟卡     ← 先有"卡"才谈别的;接口先行,上层不返工
MC2 卡的行为测试    ← 用 12 个测试把卡的行为钉死(两轴并发/急停/软限位/回零),
                       之后界面出 bug 就知道"不是卡的锅"
MC3 WinForms 主界面 ← 卡测好了才配界面——界面只是"消费"卡的状态
MC4 UI 冒烟验收    ← 全流程在 STA 线程真跑一遍:跨线程、事件、定时器一次验完
MC5 两轴直线插补    ← 单轴玩明白了才玩多轴联动(进阶)
MC6 轨迹可视化      ← 最后装修:画布是纯粹的"消费者",有数据才有得画
```

> 💡 **面试金句**:"我的开发顺序是先设备抽象和模拟卡、用测试钉死行为,再做界面、最后做插补和可视化——**设备先行、测试护航、界面殿后**,界面永远只是消费者。"和 DaqMonitor 的"数据链路先行"是同一个工程节奏。

### ⑤ 带着问题去读后面的站

- 第②站:"换真卡要改多少行代码?"
- 第③站:"为什么每轴一个令牌?急停怎么做到就地冻结?"
- 第④站:"为什么点动用 MouseDown 不用 Click?运动完成是怎么知道的?"
- 第⑤站:"为什么插补要共用一个令牌?步数按什么算?"
- 第⑥站:"为什么采样在定时器里而不订阅事件?Y 轴为什么要取反?"

---

## 第一站 · 程序的第一口气:Program.cs(14 行,全项目最短)

> 文件:`src/MotionControl/Program.cs`

> 📚 **对应讲义**:[MC3 · WinForms 主界面](项目实践_MotionControl_MC3_WinForms主界面.md)(入口与窗体装配)· [M14 · WinForm 与自定义控件](M14_WinForm与自定义控件_深度版.md)(WinForms vs WPF 全景)

### 🎯 一句话

**双击 exe 后,CLR 找到的第一个 C# 文件就是它:初始化 WinForms 运行时 → 创建主窗体 → 进入消息循环,直到关窗。** 三行干完,没有一行多余。

### 逐行讲解

```csharp
01  // 📂 文件:src/MotionControl/Program.cs
02  namespace MotionControlProject;
03
04  /// <summary>程序入口。net8 WinForms 模板写法:ApplicationConfiguration.Initialize() 负责高 DPI / 字体 / 默认样式。</summary>
05  internal static class Program
06  {
07      [STAThread]   // WinForms 硬要求:UI 线程必须是 STA(剪贴板、文件对话框等 COM 组件依赖)
08      static void Main()
09      {
10          ApplicationConfiguration.Initialize();
11          // 想接真卡时,只改这一行:new UI.MainForm(new Device.XxxRealCard("192.168.0.10"))
12          Application.Run(new UI.MainForm());
13      }
14  }
```

| 行 | 讲解 |
|---|---|
| 05 | 🔧 `internal static class`:入口类不需要被外界 new,静态即可。`internal` = 只本项目可见(比 public 收紧,默认能紧不松) |
| 07 | 🔧 `[STAThread]`:告诉 COM 世界"我的 UI 线程是单线程公寓(Single-Threaded Apartment)"。剪贴板、OpenFileDialog、拖放全依赖它。**忘写这行,某些机器上剪贴板/对话框直接异常**——WinForms 固定仪式 |
| 10 | 🔧 `ApplicationConfiguration.Initialize()`:net6+ WinForms 模板方法,一行顶旧模板十几行(Application.EnableVisualStyles / SetCompatibleTextRenderingDefault / 高 DPI 配置)。**类比前端**:相当于框架的 `createApp().mount()` 之前的那次全局初始化 |
| 12 | 🧠 全项目**最值钱的一行注释**在 11 行:接真卡时只改 12 行的 `new UI.MainForm()` 为 `new UI.MainForm(new Device.XxxRealCard("192.168.0.10"))`——因为 MainForm 的构造函数收 `IMotionCard`(第④站 40 行),**换卡 = 换一个构造参数,零处业务代码改动**。DaqMonitor 用 DI 容器做同一件事,本项目小到一行手动注入就够,不必上容器 |

### 🔬 掰开揉碎:Application.Run 到底启动了什么?(消息循环)

`Application.Run(form)` 做三件事:显示窗体 → **进入消息循环(GetMessage/TranslateMessage/DispatchMessage 的 while 死循环)** → 窗体关闭时退出循环、Main 返回、进程结束。

消息循环是整个 WinForms 的心跳:**你按一下按钮,OS 把 WM_LBUTTONDOWN 消息投递到本线程消息队列,循环取出来派发给对应控件,控件的 MouseDown 事件处理链被调用**。这个项目里所有"看似自动"的行为,底层都是消息:

- 点动按钮按下 → MouseDown 消息 → 我们在构造函数订阅的 lambda(72 行)被调用 → StartJog;
- 后台线程 `BeginInvoke` → 投递一条消息到 UI 线程队列 → 循环派发 → 委托在 UI 线程执行(所以第④站能"切线程",本质是**借消息循环的车道**);
- timer1 到点 → OS 投 WM_TIMER → 循环派发 → Timer1_Tick。

**UI 线程"卡死" = 消息循环被某段长活占住了**,队列里的消息(重绘/点击)没人处理——这也是为什么运动仿真绝不能放在 UI 线程 sleep 里做(推导链第 2 步),而要 Task.Run 到后台。前端完全同构:浏览器主线程跑长任务,页面冻结、点击无响应;解法同款——Web Worker / Task.Run。

### 🔬 WinForms vs WPF(和 DaqMonitor 对照,面试爱问)

| | WinForms(本项目) | WPF(DaqMonitor) |
|---|---|---|
| UI 描述 | C# 代码摆控件(Designer.cs) | XAML 声明式(类比 HTML) |
| 数据绑定 | 弱,基本手动刷(本项目 RefreshUiState) | 强,INPC + Binding(类比 Vue 响应式) |
| 跨线程切回 | `InvokeRequired` + `BeginInvoke` | `Dispatcher.BeginInvoke` |
| 绘图 | GDI+(OnPaint + Graphics 对象) | DirectX 渲染层, retained |
| 定位 | 上位机行业**存量最大**(大量老项目) | 新项目主流 |

💼 **前端类比一句话**:WinForms 像jQuery 时代手动操作 DOM——每个按钮手动 new、手动订阅、手动刷新;WPF 像 Vue——数据驱动、声明式。**先学手动时代,才懂框架在帮你什么。**

### 🎤 面试一句话(第一站)

> "入口是 [STAThread] Main:ApplicationConfiguration.Initialize 做 WinForms 运行时初始化,Application.Run 创建 MainForm 并进入消息循环;MainForm 构造函数收 IMotionCard,默认塞 MockMotionCard,接真卡只改这一处构造参数。"

### ✂️ 自己改一处(5 分钟)

把 07 行 `[STAThread]` 删掉再跑——多数机器上照样能启动(Win10/11 容错),但把一个 `OpenFileDialog` 拖进窗体调用试试,体会"仪式代码平时看不见、出事才显形"。

---

## 第二站 · 卡的规矩:IMotionCard.cs(145 行,全项目的"宪法")

> 文件:`src/MotionControl/Device/IMotionCard.cs`

> 📚 **对应讲义**:[MC1 · 工程骨架与模拟卡](项目实践_MotionControl_MC1_工程骨架与模拟卡.md)(接口设计动机与 v1 十坑)· [M9 · 工程素养](M9_工程素养_测试DI容错_深度版.md)(面向接口/可测试性)· 对照 [DaqMonitor 导读第 3 站 IDevice](项目逐行导读_DaqMonitor_从零到吃透.md)

### 🎯 一句话

**这是一份"运动控制卡合同":任何卡(模拟的/固高的/雷赛的)想来这个项目干活,必须会这些动作 + 发这三种广播。** 上层代码只认合同不认卡——换卡换实现,上层一行不改。

### 前端类比秒懂

### 工业术语扫盲(运控五词,后面天天见)

| 术语 | 大白话 | 前端类比 |
|---|---|---|
| **轴(Axis)** | 一个能动的自由度=一台电机+一根导轨 | 一维 transform 的 x/y |
| **使能(Enable)** | 伺服上电锁轴;未使能一切运动指令被拒 | 表单 disabled——没解锁按钮全灰 |
| **回零(Home)** | 回机械零点建立坐标基准,开机第一件事 | 页面 reload 回初始 state |
| **插补(Interpolation)** | 多轴绑腿跑出直线/圆弧 | CSS 里同时动 left+top 的 transition |
| **脉冲当量** | 真卡一个脉冲走多少 mm(模拟卡用 tick 代替) | 动画的帧间隔 |

| C# 概念 | 前端类比 | 说明 |
|---|---|---|
| `interface IMotionCard` | TypeScript 的 `interface MotionCard` | 纯形状声明,不含实现 |
| `MotionResult` 返回码 | 后端 API 的 `code: 0/-1/-2` | 真卡 SDK 全是 int 错误码,枚举是"给魔数起名" |
| `event PositionChanged` | `emitter.on('position')` | 卡主动广播,谁关心谁订阅 |
| Mock 实现 | Mock Service Worker / json-server | 没有真后端时先跑通前端 |

### 文件四段式:返回码 → 事件参数 → 接口 → 模拟卡专用

**第一段:返回码枚举(9-23 行)——真卡 SDK 的"方言词典"**

```csharp
09  public enum MotionResult
10  {
12      Ok = 0,
14      NotConnected = -1,
16      AxisIndexError = -2,
18      ParamError = -3,
20      AxisDisabled = -4,
22      AlarmActive = -5,
23  }
```

| 行 | 讲解 |
|---|---|
| 12 | 🔧 0 = 成功,**负数 = 失败**——这是真卡 SDK(固高 GTN/雷赛 LTDMC/正运动)的行业习惯。用枚举把 -1 这种魔数变成 `NotConnected`,但**保留负数值**,练习者将来看真卡文档返回 -1 立刻能对上号。💼 "为什么不用异常?"——工业指令失败是**常态**(轴没使能就点了运动,操作员天天干),用返回码走正常流程,异常留给"不该发生的事" |
| 14-22 | 🧠 五种失败覆盖了运动指令的全部拒绝理由:**没连接 / 轴号写错 / 参数非法 / 没使能 / 有报警**。注意它就是第③站 `CheckMotionLocked` 检查链的顺序(302-310 行)——**合同和检查链一一对应**,这就是"合同驱动实现" |

**第二段:两个事件参数类(26-52 行)——广播的内容单**

| 行 | 讲解 |
|---|---|
| 26-36 | `PositionChangedEventArgs`:哪根轴(Axis)、现在走到哪(Position, mm)。事件参数类只读属性 + 构造函数赋值,🔧 固定套路(等同前端 event payload 对象) |
| 39-52 | `AlarmChangedEventArgs`:多一个 `IsActive`——true=报警发生,false=报警清除。🧠 **一个事件走两种方向**,比"报警发生/报警清除"两个事件更省订阅;界面端 `if (e.IsActive)` 分流(第④站 210 行) |

**第三段:接口本体(60-144 行)——合同正文**

按功能分六组,逐组看:

| 行 | 成员 | 讲解 |
|---|---|---|
| 65-68 | `IsConnected` / `AxisCount` | 状态查询属性。轴数做成属性而不是写死 2——将来 4 轴卡不改合同 |
| 73-79 | 三个事件 | 🧠 卡主动向上层"汇报"的三种广播:位置变了(PositionChanged,仿真每个节拍一次)、报警变了(AlarmChanged)、急停生效(EmergencyStopped 一次)。**这是"推模式"**;同时合同也有 IsMoving/GetAxisPosition 供"拉模式"轮询——推拉并存,界面端混合使用(第④站) |
| 84-87 | `Connect(ip)` / `Disconnect()` | 连接管理。注意 83 行注释:空 IP 返回 ParamError——**脏输入在门口挡掉**,这是 v1"IP 带前导空格永远连不上"坑的合同级修复 |
| 92-104 | 使能/运动状态/位置/报警查询 | 🧠 100 行注释是金句:**"读位置永远允许,连没使能都能读"**——现实里编码器位置任何时候都读得到(轴没上电,标尺还在那)。把现实语义写进合同,模拟卡才像真卡 |
| 112-130 | 运动指令:JogAxis / StopJog / MoveAbsolute / HomeAxis / StopAll / ClearAlarm | 六个动作对应用户故事①~⑤。119 行注释写明打断语义:**"后到的指令赢"**——运动中再发新目标,旧运动被取消(真卡常规行为)。127 行 StopAll = 急停,位置就地冻结 |
| 138 | `MoveLinear(axes, targets, speed)` | 进阶组:多轴直线插补。数组入参 = 一次驱动 N 根轴(示例注释:X 0→50、Y 0→30,任意时刻 X:Y 恒 5:3) |
| 143 | `SimulateAlarm(axis, message)` | 🧠 **模拟卡专用,真卡没有**——人为注入报警,用来在没真故障时测试报警链路。放进接口是权衡:上层(测试/演示)用起来方便;代价是真卡实现这方法时只能空实现或抛 NotSupported。💼 面试可主动讲这个权衡 |

### 🔬 掰开揉碎:换一张真卡到底改几行?(架构题,15K 分水岭)

答案:**改 1 行 + 新增 1 个类**。

```
1. 新增 Device/GoogolCard.cs : class GoogolCard : IMotionCard(内部调固高 SDK 的 GTN_Open/GTN_AxisOn/Jog…)
2. Program.cs 12 行:new UI.MainForm() → new UI.MainForm(new GoogolCard("192.168.0.10"))
```

MainForm 一行不改、14 个测试不重跑(它们测的是 MockMotionCard 本身)、业务逻辑零回归。**开闭原则(OCP)的活体标本**:加新卡写新类,不改老代码。和 DaqMonitor 的 IDevice 换 7 种设备、组合根一行注册,是同一个模式在不同领域的落地——面试可以两project串讲:**"我两个项目都用了设备抽象,采集线抽象 IDevice,运控线抽象 IMotionCard,模式相同。"**

### 🎤 面试一句话(第二站)

> "IMotionCard 是运动卡的抽象合同:两组属性、三个事件、连接/轴状态/运动指令/插补四组方法,返回码对齐真卡 SDK 的 0 成功负数失败习惯;上层 MainForm 只依赖接口,默认构造塞 MockMotionCard,换固高/雷赛真卡只改构造处一行,满足开闭原则。"

### ✂️ 自己改一处

给接口加一个方法 `MotionResult MoveRelative(int axis, double distance, double speed);`(相对定位:走 distance 毫米而不是走到某坐标)。先只加接口不实现——`dotnet build` 立刻报错 MockMotionCard 缺实现。体会:**接口是合同,合同一改,所有签字方(实现类)都必须跟着改**——这也是"接口不要随意膨胀"的切身教训。

---

## 第三站 · 模拟卡心脏:MockMotionCard.cs(384 行,★全项目最重要)

> 文件:`src/MotionControl/Device/MockMotionCard.cs`

> 📚 **对应讲义**:[MC1 · 工程骨架与模拟卡](项目实践_MotionControl_MC1_工程骨架与模拟卡.md)(本站蓝本)· [MC2 · 卡的行为测试](项目实践_MotionControl_MC2_卡的行为测试.md)(14 个测试逐条钉死行为)· [M9 · 工程素养](M9_工程素养_测试DI容错_深度版.md)(CancellationToken/Task.Run)· [C# 陷阱](C#_陷阱_前端转上位机必看_深度版.md)(闭包/线程)

### 🎯 一句话

**没有真卡,就把真卡"演"出来:每个运动指令启动一个后台任务,每 tickMs 毫秒把位置向目标推进一步——位置随时间连续变化、可随时取消、会撞限位、会报警,行为和真电机一致。** 14 个测试测的全是它。

### 🔬 掰开揉碎:为什么"模拟卡"能测真卡逻辑?(先懂思想再读码)

模拟卡模拟的是**行为**不是硬件:指令被拒绝的条件(没使能/有报警/超限位)、位置随时间连续变化、急停就地冻结、插补等比推进——这些**逻辑**在真卡上一样存在(只是真卡把推进工作交给了硬件脉冲发生器)。所以:
- 上位机的业务逻辑(界面状态机、事件处理、轨迹采样)对着模拟卡开发 = 对着真卡开发;
- 模拟卡行为用 14 个测试钉死后,**真卡接入后界面出 bug,排查范围瞬间缩小到"真卡实现那一层"**。

这就是为什么 MC1 建卡、MC2 马上补测试——模拟卡自己必须先可信。

### 文件结构总览(384 行分六段)

```
01-16   类头注释(v1→v2 三处结构性修复,必读)
17-64   字段与构造:每轴一组状态数组 + tickMs + softLimit + _gate 锁
66-125  属性/事件/连接管理/状态查询
127-200 运动指令:Jog/StopJog/MoveAbsolute/Home/StopAll/ClearAlarm
202-277 MoveLinear 两轴插补(第⑤站细讲)
279-382 SimulateAlarm + 私有工具:CheckIndex / CheckMotionLocked / StartMotionLocked ★心脏
```

### 逐段讲解

**第〇段:类头注释(1-16 行)——先读它再读代码**

类头注释把 v1→v2 的三处结构性修复写成了清单,这是**读任何"第 2 版"代码的正确入口**(先知道它修什么,再看它怎么修):

| 修复 | v1 的病 | v2 的药 | 正文位置 |
|---|---|---|---|
| 1 | 全局一个 `_isJogging/_jogCts` 管两根轴,轴 2 一动就把轴 1 停了 | 每轴一个 CancellationTokenSource,各动各的 | 44 行 `_cts` 数组 |
| 2 | 点动/定位共用状态互相打架,行为未定义 | 统一走"取消旧的、启动新的"(后到指令赢) | 319 行 |
| 3 | 没有软限位/急停/回零 | 内建 ±softLimit、StopAll、HomeAxis | 345/182/169 行 |

> 💡 **读码习惯**:注释里写"v1 坑"的地方,就是这个文件**最值得学的地方**——正常的 CRUD 逻辑谁都会写,"踩过坑的修复"才是一个工程师的经验所在。面试讲项目时,这些"坑与药"的故事远比功能列表值钱。

**第一段:字段——"每轴一组"是 v2 的灵魂(17-64 行)**

```csharp
20      private readonly int _tickMs;
23      private readonly double _softLimit;
26      private readonly object _gate = new();
31      private readonly double[] _positions;
34      private readonly bool[] _enabled;
37      private readonly string?[] _alarms;
44      private readonly CancellationTokenSource?[] _cts;
47      private readonly bool[] _moving;
```

| 行 | 讲解 |
|---|---|
| 20 | 🧠 `tickMs` 仿真节拍:默认 100ms(界面用);**测试传 10ms,运动快进 10 倍,几秒跑完全部场景**——"快进思想":时间精度是构造参数,不是写死的常量。💼 面试:"你的异步逻辑怎么测?"——"把节拍做成可注入参数,仿真时间快进" |
| 23 | 软限位 ±1000mm:超过就拒绝指令;点动撞上就夹住 + 报警。机械的最后一道**软件**防线(真机上前面还有硬件限位开关) |
| 26 | 🔧 专用锁对象 `_gate`:UI 线程发指令、后台任务改状态,都从这把锁过,防止读到半截状态。**锁要锁私有对象,不锁 this**(锁 this 等于把 internals 暴露给外部死锁风险) |
| 31-47 | 🧠 **七个字段,五个是数组**——下标 0=轴 1,下标 1=轴 2。类头注释(11-15 行)写明 v1 的头号坑:全局一个 `_isJogging` 管两根轴,轴 2 一动就把轴 1 停了。v2 的根治:**状态按轴拆分数组,尤其是 44 行 `_cts` 每轴一个取消令牌源**。💼 前端类比:v1 像把两台电梯共用一个 `running` 布尔;v2 像每台电梯自己的 state |
| 54-64 | 🔧 构造函数:`axisCount<=0` 兜底成 2、`Math.Max(1, tickMs)` 防零节拍——防御式默认值 |

**第二段:连接与查询(66-125 行)——查询要"像真卡一样宽容"**

| 行 | 讲解 |
|---|---|
| 78-89 | `Connect`:82 行 `IsNullOrWhiteSpace` 挡空/全空格 IP(v1 前导空格坑的根治);86 行注释点破:**真卡这里会做 TCP 连接/握手,模拟卡直接置 true**——模拟的就是"连得上"这个行为 |
| 91-99 | `Disconnect`:95 行 `CancelAllLocked()` 先停所有运动再断开——**先停业务再改状态**的固定顺序,后台任务干净退出不留僵尸 |
| 115-125 | 🧠 对比三条查询的宽容度:`IsAxisEnabled` 无条件查;`GetAxisPosition` 只查索引不查连接(120-122 行注释:编码器任何时候都读得到);而所有**运动指令**都过全检查链。**查询宽容、指令严格**——这就是真卡的脾气。120 行还看到 `lock (_gate) return …` 单行锁体写法 |

**第三段:运动指令(127-200 行)——四个指令一个模式**

四个运动指令(Jog/MoveAbsolute/Home/StopAll)长得几乎一样:lock → 检查链 → 启动/取消。逐个看差异:

| 行 | 指令 | 独有逻辑 |
|---|---|---|
| 129-142 | `JogAxis` | 🧠 138 行最妙:**点动 = 把目标设在软限位上**。走不走得到无所谓——松手(StopJog)就取消,只有一直按住才会撞上限位触发保护。**用"定位到极限"实现"一直走"**,点动和定位共用同一套仿真,一个 StartMotionLocked 走天下 |
| 144-152 | `StopJog` | 149 行 `_cts[axis]?.Cancel()`:取消令牌 → 仿真循环在下个节拍抛 OperationCanceledException → **位置就地冻结**(不会滑行)。`?.` = 有令牌才取消,没在动也安全 |
| 154-166 | `MoveAbsolute` | 🧠 两道防御:160 行目标超软限位直接拒绝(ParamError);**161 行已在目标位 → 立即 Ok 且不启动任务**——注释点名这是"v1 除零坑的根治":距离为 0 时步数算出 0,除 0 就崩。**边界输入在入口处短路**,是防呆的黄金位置 |
| 169-179 | `HomeAxis` | 回零 = 以固定 `HomeSpeed=100`(50 行常量)定位到绝对 0。真卡回零走原点开关+反向找 Z 相,复杂得多,但**目的相同:建立坐标基准**。模拟卡把复杂工艺简化成"低速走回 0",行为等价 |
| 182-187 | `StopAll` | 急停。🧠 两处细节:①184 行锁内只做 CancelAllLocked,**185 行事件 Invoke 放锁外**——持锁回调别人的代码 = 死锁温床(别人的事件处理器里再调卡的方法就要锁)。②急停**不检查任何前置条件**(没连接也能按),急停就该无条件生效 |
| 189-200 | `ClearAlarm` | 只清报警不动使能(196 行注释:清完报警使能还在不在,要看驱动——真卡如此)。198 行锁外发"报警清除"事件 |

> 💼 **面试点:"后到的指令赢"** 在哪体现?——`StartMotionLocked` 第一行(319 行)`_cts[axis]?.Cancel()`:运动中再按定位,先取消旧运动再启动新的。v1 里"运动中再按按钮"是未定义行为,v2 有明确语义。

**第四段:私有工具(293-382 行)★心脏,逐行精讲**

```csharp
296  private bool CheckIndex(int axis) => axis >= 0 && axis < AxisCount;
```

| 行 | 讲解 |
|---|---|
| 296 | 轴号合法性。**只查索引不查连接/使能**——供查询类方法用;运动类走下面更严的链 |

```csharp
302  private MotionResult CheckMotionLocked(int axis, double speed)
303  {
304      if (!CheckIndex(axis)) return MotionResult.AxisIndexError;
305      if (!_connected) return MotionResult.NotConnected;
306      if (speed <= 0) return MotionResult.ParamError;
307      if (!_enabled[axis]) return MotionResult.AxisDisabled;
308      if (_alarms[axis] is not null) return MotionResult.AlarmActive;
309      return MotionResult.Ok;
310  }
```

| 行 | 讲解 |
|---|---|
| 302-310 | 🧠 **运动前检查链,按"越靠前的越廉价"排序**:查索引(纯内存比较)→ 查连接(bool)→ 查速度(比较)→ 查使能(数组下标)→ 查报警(判空)。便宜的先查,贵的、可能变化的放后面——微服务鉴权链、前端表单校验链同款思想。注释点明:v1 每个指令各查各的、漏了就出怪 bug;v2 **点动/定位/回零共用这一条链**,要漏一起漏、要改一起改 |

```csharp
316  private void StartMotionLocked(int axis, double target, double speed, bool jog)
```

**这是单轴运动的总入口**——点动、定位、回零最终都落到这。分段看:

| 行 | 讲解 |
|---|---|
| 319-322 | 打断旧的 → new 新令牌 → 存槽 → 置 `_moving=true`。**"取消旧的、启动新的"** 六个字是 v2 对 v1 的第二处结构性修复(见类头 13 行注释) |
| 324-325 | 起点快照 `from = _positions[axis]`、总位移 `dist = target - from`——**启动瞬间锁定**,之后位置变化不影响本次运动的几何 |
| 327-331 | 🧠 **步数公式,全文件最值得背的三行**:`steps = Max(1, Ceiling(|dist| / speed × 1000 / _tickMs))`。走 100mm、50mm/s、100ms 节拍 → 100/50×1000/100 = **20 步,恰 2 秒**。`Math.Max(1, …)` 兜底:v1 在短距离时算出 0 步,再除以 0 就"瞬移"或除零——329 行注释原话"v1 定位一按就卡成瞬移的直接死因"。331 行 `step = dist / steps` 每步位移 |
| 333-376 | 🧠 运动循环本体,逐行看 👇 |

```csharp
337                  for (var i = 1; i <= steps; i++)
338                  {
339                      await Task.Delay(_tickMs, cts.Token);
340                      var p = from + step * i;
341                      lock (_gate) _positions[axis] = p;
342                      PositionChanged?.Invoke(this, new PositionChangedEventArgs(axis, p));
```

| 行 | 讲解 |
|---|---|
| 339 | 🧠 `Task.Delay(ms, token)`:带令牌的睡眠——**令牌一取消,睡梦中立刻被叫醒并抛 OperationCanceledException**,这是"急停下个节拍内生效"的机关。对比 `Thread.Sleep`:占着线程傻等且不可中断 |
| 340-342 | 第 i 步位置 = 起点 + 步长×i(几何级数,不用累积加,天然无浮点累积误差)。**341 行写位置必须持锁**(后台任务写、UI 线程读);**342 行事件 Invoke 在锁外**(和 185 行同一纪律:别在持锁时回调别人)。⚠️ 注意:事件在**后台线程**触发——MainForm 订阅处必须自己 Invoke 切线程(第④站) |

```csharp
345                      if (jog && Math.Abs(p) >= _softLimit - 1e-9)
346                      {
347                          var clamped = Math.Clamp(p, -_softLimit, _softLimit);
348                          lock (_gate) { _positions[axis] = clamped; _alarms[axis] = $"触发{(p > 0 ? "正" : "负")}软限位 {_softLimit:F0}mm,已自动停止"; }
349                          PositionChanged?.Invoke(this, new PositionChangedEventArgs(axis, clamped));
350                          var msg = _alarms[axis]!;
351                          AlarmChanged?.Invoke(this, new AlarmChangedEventArgs(axis, msg, isActive: true));
352                          break;
353                      }
```

| 行 | 讲解 |
|---|---|
| 345-353 | 🧠 **点动专属保护**:为什么只查 jog?——定位/回零的目标在入口就验证过不超限位(160 行),**只有点动是"朝极限走"的**(138 行),所以循环里每步都要盯。撞上限位三连:**夹位置**(Clamp,一步不多走)→ **置报警** → **break 停止**。`1e-9` 是浮点比较安全余量(别用 `==` 比浮点)。测试 `点动撞正软限位_应自动停止并报警`(Tests 181-192 行)钉死这个行为:位置精确停在 1000.00 |

```csharp
357                  if (!cts.Token.IsCancellationRequested && !jog)
358                  {
359                      lock (_gate) _positions[axis] = target;
360                      PositionChanged?.Invoke(this, new PositionChangedEventArgs(axis, target));
361                  }
```

| 行 | 讲解 |
|---|---|
| 357-361 | 🧠 走完全程后**把位置精确贴到目标**。虽然 340 行的公式终点理论上=target,但浮点运算可能有尾差;工业上定位讲**重复定位精度(repeatability)**,最后一步"官方盖章"保证 100% 精确落位。测试 108 行 `Assert.Equal(3.0, pos, precision: 3)` 就是验收它 |

```csharp
363              catch (OperationCanceledException) { /* 急停/打断:就地冻结 */ }
367              finally
368              {
369                  lock (_gate)
370                  {
371                      _moving[axis] = false;
373                      if (ReferenceEquals(_cts[axis], cts)) _cts[axis] = null;
374                  }
375              }
```

| 行 | 讲解 |
|---|---|
| 363-365 | 🧠 **取消 = 就地冻结**:catch 里什么都不做,位置停在当前值。测试 131-147 行(急停_运动中途位置就地冻结)专门等 200ms 再验一次位置没变——确认没有"惯性滑动"。这是软件急停与"断电自由滑行"的区别,面试讲得出这句就懂行 |
| 371 | finally 里复位 `_moving`——**无论正常完成、取消、异常都执行**,finally 是状态复位的唯一可靠位置 |
| 373 | 🧠 **全文件最精妙的一行**:`ReferenceEquals(_cts[axis], cts)`——只有槽里还是"自己这个令牌"时才清空。竞态场景:我启动后被打断,打断者**已经**在槽里放了他的新令牌;我的 finally 若无脑 `_cts[axis]=null`,就把别人的令牌误删了(别人的 StopJog 会失效)。**先验身份再动手**,并发代码的必修课 |

```csharp
379  private void CancelAllLocked()
380  {
381      for (var i = 0; i < AxisCount; i++) _cts[i]?.Cancel();
382  }
```

| 行 | 讲解 |
|---|---|
| 379-382 | 急停/断开的落点:逐轴取消令牌。三行代码,撑起"急停不能只停一轴"的整个需求(推导链第 4 步) |

### 🔬 验收:14 个测试怎么把卡的行为钉死?(Tests/MockMotionCardTests.cs,MC2 蓝本)

导读约定不逐行讲测试工程,但这个文件值得完整看一遍结构——**它是模拟卡的"行为合同书"**,第③⑤站讲的每个行为都能在这里找到钉子:

| 测试(行号) | 钉死的行为 | 对应正文 |
|---|---|---|
| Connect_空IP_应返回参数错误(42-50) | 空串/全空格 → ParamError,且 IsConnected 仍 false | 82 行 |
| 未连接就发运动指令(52-64) | 三种运动指令全返回 NotConnected;重复 Connect 幂等 | 305 行 |
| 连接但未使能就运动(66-75) | AxisDisabled;**读位置不受使能限制** | 307 行 + 120 行注释 |
| 两轴同时点动_互不干扰(79-95) | **v1 头号 bug 的回归测试**:轴 2 动不许停轴 1 | 每轴令牌 44 行 |
| 绝对定位_短距离(99-109) | 3mm@5mm/s 精确到位——v1 除零坑死的就是这种短距离 | 330 行 Max(1,…) |
| 绝对定位_零距离(111-117) | 目标=当前位置:立即 Ok 且不算运动 | 161 行 |
| 绝对定位_目标超软限位(119-126) | 正超/负超/速度非法三种全拒 | 160 行 |
| 急停_运动中途位置就地冻结(130-147) | 急停触发事件 + 等 200ms 复核无"惯性滑动" | 363-365 行 |
| 回零_从任意位置精确回零位(151-162) | 先走到 120,回零后精确 0.000 | 169-179 行 |
| 报警阻断运动_清报警后恢复(166-179) | SimulateAlarm 后运动被拒,ClearAlarm 后恢复 | 308 行 |
| 点动撞正软限位(181-192) | 位置被夹在 1000.00 一步不多走 + 报警文本含"软限位" | 345-353 行 |
| 直线插补_两轴等比推进且同时到位(196-212) | **中段抓比例 5:3±0.1** + 双轴精确到位 | 第⑤站 |
| 断开连接_所有运动被取消(216-232) | 两轴在动时 Disconnect → 全停 + 指令被拒 | 95 行 |
| UI冒烟_窗体全流程不崩溃(236-289) | STA 线程真跑全流程,任何跨线程碰控件当场炸 | 第④站 |

两个测试基建也值得学(29-38 行):`NewCard()` 统一 tickMs:10 **快进 10 倍**;`WaitUntil(condition, 3000)` 每 10ms 轮询等待异步结果、超时抛异常——**测试异步运动的标准写法**,比死等 Task.Delay(固定值) 又快又稳。

> 💼 **面试金句**:"我的模拟卡是测试先行的:MC1 建卡,MC2 立刻用 14 个测试把行为契约钉死——包括 v1 两轴互踩 bug 的回归测试。之后界面出问题,我第一步就是跑这 14 个测试:全绿说明卡没病,病在界面层。"**缩小排查范围**是测试最大的日常价值,不只是防回归。

### 🧠 为什么这么设计(把这一站想成一句面试答案)

> **"模拟卡的核心是一个节拍推进的状态机"**:运动=后台任务每 tick 推进一步;停止=取消令牌;两轴并发=每轴独立令牌;急停=全轴取消+位置冻结;保护=检查链前置+点动限位循环内夹逼。所有"真卡行为"都被 MC2 的 14 个测试逐条钉死。

### 🎤 面试一句话(第三站)

> "MockMotionCard 用 Task.Run + CancellationToken 实现运动仿真:每 tickMs 毫秒把位置向目标推进一步,步数=距离/速度/节拍;每轴一个令牌源保证两轴并发互不干扰,急停/打断取消令牌让循环在下个节拍就地冻结;运动前有统一检查链,点动撞软限位会夹住位置并报警;finally 里用 ReferenceEquals 防止误删打断者的新令牌。"

### ⚙️ 亲手造发动机工作纸

想把这颗心脏真正变成自己的?→ [亲手造发动机 · 注释工作纸(MotionControl 卷)工作纸 1](亲手造发动机_注释工作纸_MotionControl.md):照中文注释徒手重写 StartMotionLocked 核心段,写完替换原文件,`dotnet test` 仍应 14/14 全绿。

### ✂️ 自己改一处(5 分钟)

把 20 行注释说的事做实:构造函数第二个参数就是 tickMs。在 Program 入口临时 `new MockMotionCard(tickMs: 20)` 跑起来——所有运动快 5 倍,界面位置框明显"跳格子"变大。体会:**tick 越小越平滑,但事件越密(界面压力越大)**——这就是采样频率的经典权衡。

---

## 第四站 · 界面总指挥:MainForm 三件套(330+735+34 行)

> 文件:`src/MotionControl/UI/MainForm.cs`(逻辑)· `MainForm.Designer.cs`(长相)· `Common/LogHelper.cs`(日志)

> 📚 **对应讲义**:[MC3 · WinForms 主界面](项目实践_MotionControl_MC3_WinForms主界面.md)(本站蓝本)· [MC4 · UI 冒烟验收](项目实践_MotionControl_MC4_UI冒烟验收.md)(STA 线程全流程冒烟)· [M14 · WinForm 与自定义控件](M14_WinForm与自定义控件_深度版.md)(InvokeRequired/控件数组)· [C# 陷阱](C#_陷阱_前端转上位机必看_深度版.md)(闭包专坑)

### 🎯 一句话

**主窗体只做三件事:收集用户输入 → 调 IMotionCard → 把卡的事件刷回界面——本文件不含任何运动算法,换真卡一行不改。**

### 4.0 先逛一遍界面(Designer 布局导读,对照着想象)

```
┌────────────────────────────────────────────────────────────────────┐
│ 顶栏 panelTop: [连接控制: IP框 连接 断开 指示灯]      [ 急停 STOP(红) ]│
├──────────────┬──────────────┬───────────────┬─────────────────────┤
│ 轴1(X) 26%   │ 轴2(Y) 26%   │ 轨迹图 24%     │ 报警(52%)           │
│ 使能/失能     │ (布局与轴1    │ TrajectoryPanel│ 淡黄报警框           │
│ ▲正转 ▼反转  │  完全一致)    │ (白底方格)     │ 清除全部报警         │
│ 当前位置(只读)│              │               ├─────────────────────┤
│ 速度 / 目标   │              │ [清空轨迹]     │ 日志(48%)           │
│ 绝对定位/回零 │              │               │ 黑底绿字终端风        │
│ ⇗两轴插补演示│              │               │ (同时落盘 logs\)     │
│ 灰字:软限位   │              │               │                     │
└──────────────┴──────────────┴───────────────┴─────────────────────┘
```

两轴 GroupBox 内部 14 个控件**逐像素同布局**(Designer 377 行注释:"对照着抄第二遍即可")——**对称即正确**:操作员学会轴 1 就会轴 2,维护者改轴 1 时照抄改轴 2。这也是为什么逻辑文件敢用控件数组:两栏长得一样,才能 `new[]{btn1, btn2}`。

### 🔬 前端人一秒版:WinForms ↔ Vue 对照表(💼 面试可主动甩出)

| 本项目做法 | 前端类比 | 说明 |
|---|---|---|
| Designer.cs 摆控件 | template/HTML | 声明"长什么样" |
| 构造函数里 `+=` 订阅事件 | `@click="fn"` | 事件绑定(手动版) |
| `RefreshUiState()` 统一算按钮灰亮 | computed + `:disabled` | **单一真源**:状态集中计算,不散落各处 |
| `InvokeRequired + BeginInvoke` | worker `postMessage` 回主线程 | 后台线程不能摸 UI |
| `timer1` 100ms 轮询 | `setInterval` + 脏检查 | 完成检测靠边沿判断 |
| `AppendLog` 同屏+落盘 | console + 上报埋点 | 双写日志 |

### 4.1 MainForm.cs 构造函数(37-101 行)——装配流水线

| 行 | 讲解 |
|---|---|
| 37 | 🧠 默认构造 `: this(new MockMotionCard())`:构造链转发——Designer 需要无参构造才能在 VS 里打开设计器,生产默认模拟卡,两全其美 |
| 40-43 | 依赖注入入口:收 `IMotionCard`。**MainForm 只认合同**,这是第②站"换卡改一行"的承接点 |
| 46-54 | 🧠 **控件数组**:`new[] { btnEnable1, btnEnable2 }` 把 Designer 生成的两轴控件按轴序收进数组——之后所有"每轴逻辑"写成 for 循环(63-79 行),v1 每个按钮复制粘贴一个 handler、改一处漏一处的根治 |
| 63-66 | 🧠 **闭包坑现场,全文最值得划线的一段**:循环里 `var axis = i;` 先复制一份再被 lambda 捕获。注释原话:"for 的 i 是所有循环共享的变量,不复制一份,两个按钮的 lambda 里拿到的都会是循环结束后的 2"。**前端完全同款坑**:JS 的 `var` 在 for 里建回调,所有回调拿到同一个 i(Vue v-for 里给每个按钮传 index 反而没这问题,因为函数参数每次求值)。C# 5 起foreach 已修、for 未修——所以必须手动复制 |
| 72-75 | 🧠 点动用 **MouseDown/MouseUp 而不是 Click**:点动的语义是"按住走、松手停",Click 只有"按+松完整完成后"才触发——用 Click 的话电机永远在你松手后才动一下,完全反了。工业 UI 里**按下/抬起是两个独立指令**,触摸屏 HMI 同理 |
| 88-90 | 订阅卡的三个事件。⚠️ 88 行上方注释是铁律:这些事件**在后台仿真线程触发**,处理函数必须自己切回 UI 线程(见 OnPositionChanged) |
| 96-97 | 🔧 定时器 100ms 启动。Interval 在这里设、Tick 绑定在 Designer 651 行——"长相归 Designer、行为归逻辑文件"的分工 |
| 99-100 | 构造收尾:刷一次状态 + 开机日志。**构造完的界面必须是自洽的**(按钮灰亮和卡状态一致),不能等第一次 Timer 才正确 |

### 4.2 指令区(105-195 行)——全是同一个模式

Connect / DisconnectCard / EmergencyStop / ClearAlarms / SetAxisEnabled / StartJog / StopJog / MoveAbs / Home / MoveLinearDemo,十个方法一个模板:

```
解析输入 → _card.某指令() → 返回码 != Ok ? Fail(r, "xx") : AppendLog("成功") → RefreshUiState()
```

只挑有戏的讲:

| 行 | 讲解 |
|---|---|
| 108 | `txtIp.Text.Trim()`——v1 前导空格坑在界面侧也挡一道(模拟卡 82 行也挡):**防御做两层,门口一层、房间一层** |
| 124-129 | 🧠 急停:注释原话"一按就停,不做任何确认弹窗"。工业安全设计:**安全动作的路径上不允许任何额外确认**——弹窗的那两秒,机器还在撞。红色专属、Anchor 右侧贴边、全窗体唯一彩色按钮(Designer 166-179 行) |
| 131-138 | 清报警:两轴各清一遍,注释点出"无报警清了也无副作用"——**幂等**操作才敢无脑循环 |
| 166-170 | 🧠 目标位置解析失败要**就地报出来**:AppendLog 警告 + return。v1 是"按了没反应"式沉默失败——上位机头号可用性杀手 |
| 189-195 | 插补演示按钮:X→200、Y→120。**速度取轴 1 的速度框,语义=走得最远的轴(X)的速度**——这个"速度语义"是第⑤站的伏笔,这里先按下不表 |

### 4.3 卡事件 → 界面(197-221 行)——跨线程铁律现场

```csharp
199  private void OnPositionChanged(object? sender, PositionChangedEventArgs e)
200  {
201      // 事件来自后台仿真线程。WinForms 铁律:非 UI 线程碰控件就抛 InvalidOperationException
202      if (InvokeRequired) { BeginInvoke(() => OnPositionChanged(sender, e)); return; }
203      _txtPos[e.Axis].Text = e.Position.ToString("F3");
204  }
```

| 行 | 讲解 |
|---|---|
| 202 | 🧠 **全文最值得背的一行**。`InvokeRequired`:"我是在 UI 线程吗?"不是 → `BeginInvoke` 把"重新调用我自己"投递到 UI 线程的消息队列,然后 return。递归式自转发:**第一次进来在后台线程(转发),第二次进来在 UI 线程(干活)**。前端类比:worker 线程不能摸 DOM,必须 postMessage 回主线程——一模一样的保护机制。BeginInvoke(异步,发完就走)vs Invoke(同步等结果):高频位置事件用异步,不然 UI 线程被后台事件拖着走 |
| 203 | 控件数组兑现红利:`_txtPos[e.Axis]` 一行管两轴。`"F3"` 固定 3 位小数,数字宽度稳定不抖版式 |

`OnAlarmChanged`(206-215)同款三行开头,差异是业务:`if (e.IsActive)` 分流报警发生/清除两种文案,再 `RefreshUiState()`(报警会改变按钮可用性)。`OnEmergencyStopped`(217-221)同款,打一条 ERROR。

### 4.4 定时器(225-243 行)——轮询 + 边沿检测 + 同拍采样

| 行 | 讲解 |
|---|---|
| 227-233 | 🧠 **运动完成的边沿检测**:`_wasMoving[i]`(上一帧状态)与当前 `moving` 对比,**上一帧在动、这一帧停了 = 刚完成**,打一条"运动完成,停在 xxx mm"。为什么不用事件?93-95 行注释给了答案:**模拟卡没有"运动完成"事件,真卡 SDK 也常常只有状态位**——"定时查状态+边沿检测"是上位机最常用、最稳的完成检测手段(和 DaqMonitor 的管道心跳同理)。前端类比:MutationObserver 没有时用 setInterval 轮询比对 |
| 240 | 🧠 **轨迹采样放在定时器里,而不是订阅 PositionChanged**——236-239 行注释是第⑥站的引子:事件是"每根轴各自来报",X 到达时 Y 可能还停在上个节拍,轨迹会画锯齿;**定时器同一时刻取两轴,坐标才是一致快照**。"按周期采样对付事件乱序",采集项目同一思路 |
| 242 | 每 Tick 全量 `RefreshUiState()`——100ms 全量重算 12 个按钮的 Enabled,对 CPU 是毛毛雨,换来"状态永远正确" |

### 4.5 RefreshUiState(253-282 行)——单一真源

| 行 | 讲解 |
|---|---|
| 255 | `if (IsDisposed) return;`——窗体销毁后后台事件还可能进来,先验活 |
| 258-262 | 连接区:连接/断开互斥、IP 框连接后锁死(防误改)、指示灯绿/灰 |
| 264-275 | 🧠 **每轴三条件**:`operable = 已连接 && 已使能 && 无报警`。使能/失能按钮只要"卡在线";**运动类**(点动/定位/回零)要全三条件。规则一眼可读——v1 的教训写在 252 行注释:按钮状态散落各 handler 各改各的,btnMoveAbs1 被禁两次、btnMoveAbs2 一次没禁。**集中算 = 不可能漏**。前端类比:这就是把散落的 `:disabled` 收进一个 computed |
| 278-281 | 插补按钮要求**两轴同时可用**——"绑腿跑"语义的界面预演(卡里第⑤站还会再查一遍:**前端防呆、后端校验,两层都要**) |

### 4.6 小工具(284-329 行)

| 行 | 讲解 |
|---|---|
| 290-295 | 🧠 `SpeedOf`:解析速度,非法(空/非数字/越界)就**回退默认 50 并把文本框纠正过来**——"防呆+自愈":永远给调用方一个可用值,而不是抛异常崩给用户看。手工输入的黄金法则 |
| 298-310 | `Fail`:返回码 → 人话的翻译表(switch 表达式)。**真卡 SDK 给你的只有 int,把它翻译成操作员能看懂的句子是上位机的本职** |
| 316-322 | `AppendLog`:界面日志 + LogHelper 落盘双写;自己带 InvokeRequired 防护(后台事件线程也调它) |
| 325-329 | `OnFormClosing`:关窗先 `_card.Disconnect()`——取消所有后台任务,进程干净退出不留僵尸线程 |

### 4.7 Designer.cs(735 行)只讲结构,不逐行

它是 VS 设计器生成的"长相说明书",**读它的正确方式是看注释块**(97/181/201/377/543/573 行等):顶栏=连接+急停;主体四栏 TableLayoutPanel(轴1 26% | 轴2 26% | 轨迹 24% | 报警+日志 24%);两轴 GroupBox 内部布局完全一致(对照着抄第二遍);报警框淡黄底深红字、日志框黑底浅绿等宽字——**颜色只表达状态与危险等级,不做装饰**,工业界面铁律。649-651 行:timer1 的 Tick 绑定在这、Interval 在逻辑文件设——两文件分工。💼 被问"WinForms 界面怎么做的"→"Designer 管 partial 类的长相部分,事件订阅和状态逻辑全在 MainForm.cs,两文件一个 partial 类"。

### 4.8 LogHelper.cs(34 行)——最短的"黑匣子"

| 行 | 讲解 |
|---|---|
| 14 | 静态锁 `Gate`:v1 坑(8-9 行注释)——直接 AppendAllText 没加锁,多线程同时写,两行日志穿插成乱码。**所有写入包 lock 串行化,一行永远是一行** |
| 17 | 日志目录=exe 旁 `logs\`,**按天分文件**(motion_20260825.txt)——断网车间里出了问题,现场人员能给你的往往只有这个文件 |
| 25 | `$"[{时间:毫秒}] [{level,-5}] {消息}"`——`-5` 左对齐占 5 格,INFO/WARN/ERROR 列才整齐 |

### 4.9 UI 冒烟测试(Tests 236-289 行)——把第④站整体钉死的那个测试

MC4 的交付物就是这一个测试,值得单独看懂:

| 行 | 讲解 |
|---|---|
| 244-272 | 🧠 **在手工创建的 STA 线程上把全流程真跑一遍**:new 卡 → new MainForm(card) → Show → 连接 → 使能 → 双轴同时点动(两轴并发,事件线程全在跑)→ 定位打断点动 → 注入报警 → 清报警 → 急停 → 断开 → Close。任何一步跨线程碰控件都会当场抛异常,被 catch 收进 error |
| 273 | 🔧 `SetApartmentState(ApartmentState.STA)`:WinForms 控件必须活在 STA 线程——测试线程默认 MTA,不设这行,窗体构造就可能炸 |
| 275 | `thread.Join(60000)`:等它跑完,上限 60 秒防测试挂死(**测试必须有超时,挂死比失败更糟**) |
| 281-288 | 🧠 **手动消息泵 Pump**:Application.Run 会阻塞测试线程,这里用 `Application.DoEvents()` 循环代替——每轮处理完队列里所有消息,包括 BeginInvoke 投递回来的跨线程调用和 Timer 的 WM_TIMER。**没有泵,BeginInvoke 的委托永远没人执行,界面代码等于没测**。前端类比:不跑事件循环,setTimeout 回调永远不触发 |
| 239-242 注释 | "短暂启动冒烟测不出交互期 bug"——DaqMonitor 项目吃过的亏:只 Show 一下就 Close 全绿,真实交互期(后台事件+DoEvents+定时器并发)的 bug 全漏。**冒烟要冒"全流程",不是冒"能启动"** |

### 🎤 面试一句话(第四站)

> "MainForm 只依赖 IMotionCard:构造函数收卡实例、控件收进数组用循环订阅,for 循环里先复制循环变量再进 lambda 防闭包坑;卡的三个事件在后台线程触发,统一 InvokeRequired+BeginInvoke 递归切回 UI 线程;100ms 定时器做三件事——边沿检测运动完成、同拍采样两轴坐标给轨迹图、全量刷新按钮状态 RefreshUiState,所有按钮可用性集中一处计算,规则是连接+使能+无报警。"

### ✂️ 自己改一处(5 分钟)

把 66 行 `var axis = i;` 删掉、68 行的 `axis` 全改成 `i`,编译照样过、跑起来按"轴1 使能"——日志里打出的是"轴 3 已使能"(i 循环结束=2)。**亲手踩一次闭包坑,比读十遍注释记得牢。** 改回去后 `dotnet test`,14 个测试仍全绿(UI 冒烟测试不一定抓得到这种逻辑错——体会测试覆盖的边界)。

---

## 第五站 · 两轴画直线:MoveLinear 插补(204-277 行)

> 文件:`src/MotionControl/Device/MockMotionCard.cs` 的 MoveLinear 段 · 配套测试 Tests 196-212 行

> 📚 **对应讲义**:[MC5 · 两轴直线插补](项目实践_MotionControl_MC5_两轴直线插补.md)(本站蓝本,含演示剧本)· [M9 · 工程素养](M9_工程素养_测试DI容错_深度版.md)(Task/令牌联动)

### 🎯 一句话

**插补 = 绑腿跑:两根轴同时启动、每一步按相同比例推进、同时到位——任意时刻 X:Y 恒等于总位移之比,轨迹就是一条直线。**

### 🔬 掰开揉碎:为什么两轴各走各的画不出直线?(先懂病,再懂药)

想象 X 要走 200mm、Y 要走 120mm,两轴各自独立定位:X 速度 50 → 4 秒走完;Y 若也 50 → 2.4 秒走完。结果:**先斜着走一段(X 还在动时 Y 已停),再纯横走**——轨迹是条折线,像下楼梯。工业上激光切割/点胶/画 PCB 边框,**走的路径必须是精确直线**。药方:让两轴**共用同一条时间轴**——同一个总步数、每步按各自总位移等比分步,谁也不许先到。

### 逐行讲解(MockMotionCard.cs 204-277 行)

```csharp
204  public MotionResult MoveLinear(int[] axes, double[] targets, double speed)
207      if (axes is null || targets is null || axes.Length == 0 || axes.Length != targets.Length)
208          return MotionResult.ParamError;
```

| 行 | 讲解 |
|---|---|
| 207-208 | 🧠 入参**形状**先验:两个数组必须一一对应且非空。数组参数是"裸奔数据",入口必须验形状(长度不一致=调用方 bug,立刻拒绝) |

```csharp
217              foreach (var (axis, target) in axes.Zip(targets))
218              {
219                  if (!CheckIndex(axis)) return MotionResult.AxisIndexError;
220                  if (!_enabled[axis]) return MotionResult.AxisDisabled;
221                  if (_alarms[axis] is not null) return MotionResult.AlarmActive;
222                  if (Math.Abs(target) > _softLimit) return MotionResult.ParamError;
223              }
```

| 行 | 讲解 |
|---|---|
| 217-223 | 🧠 **逐轴全检,任何一轴不满足,整条指令拒绝**——215 行注释:"插补是绑腿跑,一个不能跑整队都不动"。为什么这么狠?插补中途发现一轴有报警再停,**轨迹已经画歪了一段**;不如出发前全队体检。💼 这是"事前校验优于事中回滚"的工业版 |

```csharp
225              foreach (var axis in axes) _cts[axis]?.Cancel();   // 插补优先:打断各轴在途运动
227              var cts = new CancellationTokenSource();
229              foreach (var axis in axes) { _cts[axis] = cts; _moving[axis] = true; }
```

| 行 | 讲解 |
|---|---|
| 225 | 先打断各轴在途的单轴运动(后到指令赢的插补版) |
| 227-229 | 🧠 **本站的命门:一个 cts 实例,塞进所有参与轴的槽里**。回想第③站:单轴时每轴一个令牌是防互相干扰;插补时**故意共享同一个令牌**——急停/新指令一取消,所有轴**同一瞬间**停。为什么插补反而要"一停俱停"?两轴绑腿跑到一半一个停一个不停,轨迹立刻拐弯——**对插补运动,"半途而废的直线"比"停下"危险**。`_cts[axis] = cts` 存的是同一个引用,Cancel 一次全员生效 |

```csharp
231              var froms = axes.Select(a => _positions[a]).ToArray();
233              var maxDist = 0.0;
234              for (var k = 0; k < axes.Length; k++)
235                  maxDist = Math.Max(maxDist, Math.Abs(targets[k] - froms[k]));
```

| 行 | 讲解 |
|---|---|
| 231-235 | 🧠 **总步数按"走得最远的轴"算**(232 行注释:速度语义 = 最长轴的速度)。为什么?若按短轴算步数,长轴每步要跨更大距离 → 长轴实际速度超过设定值;按最远轴算,最远轴恰好以 speed 前进,近轴自然更慢——**"给定速度"是对合成路径中最长腿的承诺**,界面端 191 行注释呼应了这个语义 |

```csharp
241                      var steps = Math.Max(1, (int)Math.Ceiling(maxDist / speed * 1000.0 / _tickMs));
242                      for (var i = 1; i <= steps; i++)
243                      {
244                          await Task.Delay(_tickMs, cts.Token);
245                          for (var k = 0; k < axes.Length; k++)
246                          {
249                              var p = froms[k] + (targets[k] - froms[k]) * i / steps;
250                              lock (_gate) _positions[axes[k]] = p;
251                              PositionChanged?.Invoke(this, new PositionChangedEventArgs(axes[k], p));
252                          }
253                      }
```

| 行 | 讲解 |
|---|---|
| 241 | 步数公式和单轴版(330 行)同款,距离换成 maxDist |
| 242-253 | 🧠 **双循环:外层时间,内层轴**。每个节拍:睡 tickMs(带共享令牌)→ **依次推每根轴**。249 行是插补的全部数学:`位置 = 起点 + 全程位移 × i/steps`——i/steps 是**公共进度百分比**,每根轴按自己的全程位移乘这个百分比。i=1 时 X 走了 1/steps×200、Y 走了 1/steps×120——**比例恒定 = 直线**;i=steps 时全部同时到位。对比"每轴各自等比分步"(步数不同):那种做法 X 先到、Y 后到,又回到楼梯问题。**共用步数是灵魂** |

```csharp
254                      if (!cts.Token.IsCancellationRequested)
255                      {
257                          for (var k = 0; k < axes.Length; k++)
258                          {
259                              lock (_gate) _positions[axes[k]] = targets[k];
261                              PositionChanged?.Invoke(this, new PositionChangedEventArgs(axes[k], targets[k]));
262                          }
263                      }
264                  }
265                  catch (OperationCanceledException) { /* 急停/打断:各轴就地冻结 */ }
266                  finally
267                  {
268                      lock (_gate)
269                          for (var k = 0; k < axes.Length; k++)
270                          {
271                              _moving[axes[k]] = false;
272                              if (ReferenceEquals(_cts[axes[k]], cts)) _cts[axes[k]] = null;
273                          }
274                  }
```

| 行 | 讲解 |
|---|---|
| 254-263 | 精确落点:全队一起贴到各自目标(消除浮点尾差)。`IsCancellationRequested` 先验:被取消就不"补刀"——急停冻结的位置才是真相 |
| 265 | 急停/打断:各轴就地冻结(和单轴版同款语义) |
| 266-274 | finally:批量复位 `_moving`、批量清槽——**注意 272 行仍是 ReferenceEquals 逐轴验身份**,因为某轴槽里可能已被新的单轴指令占位 |

### 🧠 验收:测试怎么证明"走的是直线"?(Tests 196-212 行)

`直线插补_两轴等比推进且同时到位`:X 0→50、Y 0→30,速度 25 → 全程 2 秒。**走到中段(300ms 时)抓一次两轴位置,断言 `midX/midY ∈ [5/3±0.1]`**——比例恒定 = 直线(207 行);走完断言两轴各自精确到位。这条测试的设计思想:**中间时刻的比例才是插补的灵魂,只验终点验不出"楼梯"**。

### 🎤 面试一句话(第五站)

> "MoveLinear 把插补做成了绑腿跑:出发前逐轴全检、一轴不满足整队拒绝;所有参与轴共享同一个 CancellationTokenSource,急停一停俱停;总步数按走得最远的轴计算,速度语义是最长轴的速度;每个节拍用公共进度 i/steps 乘各自全程位移等比推进,任意时刻各轴位移比例恒定,轨迹是精确直线且同时到位,测试在中段抓两轴比例来验收。"

### ⚙️ 亲手造发动机工作纸

→ [亲手造发动机 · 注释工作纸(MotionControl 卷)工作纸 2](亲手造发动机_注释工作纸_MotionControl.md):照注释徒手重写插补核心段,验收同样是 14/14 全绿。

### ✂️ 自己改一处(5 分钟)

把 249 行的 `i / steps` 改成 `(int)(i / steps)` 再跑插补测试——比例立刻被截断成 0,中途抓比例断言失败。体会:**插补全靠这个浮点进度比,精度就是轨迹**。改回去,`dotnet test` 恢复全绿。

---

## 第六站 · 轨迹自绘:TrajectoryPanel.cs(117 行,前端人的主场)

> 文件:`src/MotionControl/UI/TrajectoryPanel.cs`

> 📚 **对应讲义**:[MC6 · 轨迹可视化](项目实践_MotionControl_MC6_轨迹可视化.md)(本站蓝本)· [M14 · WinForm 与自定义控件](M14_WinForm与自定义控件_深度版.md)(OnPaint/双缓冲/自定义控件三铁律)· [M5 · 实时可视化](M5_实时可视化_深度版.md)(坐标映射同款思想)

### 🎯 一句话

**一个继承 Panel 的自定义控件:Sample() 只存点,OnPaint 只画图——把两根轴的毫米坐标画成屏幕上的运动轨迹,像高尔夫球的挥杆轨迹回放。**

### 🔬 自定义绘制控件三条铁律(9-13 行类头注释,面试可整段背)

1. **数据与绘制分离**:`Sample()` 只存点 + `Invalidate()` 标脏,所有画图只发生在 `OnPaint`——**相当于前端"改 state → 触发 re-render",绝不在数据更新时直接拿 Graphics 画**;
2. **DoubleBuffered = true**:先画进内存位图再整帧贴屏,否则每帧重画都闪——**离屏 canvas 同理**;
3. **mm→像素换算不存字段,OnPaint 每帧现算**:窗口会被拉伸,存下来的比例必然过期(过期状态 = bug 之源)。

### 逐行讲解

**构造与数据面(15-51 行)**

| 行 | 讲解 |
|---|---|
| 18 | `_trail` 点序列(毫米坐标,**机坐标系**,不是屏幕坐标——坐标转换全推迟到画的时候)。`[^1]` = 最后一个元素(C# 索引语法,等同 JS 的 `arr.at(-1)`) |
| 21 | `SoftLimit` 属性默认 1000:画边界框 + 定显示范围,**轨迹图的可视范围跟着卡的行程走** |
| 24 | 🧠 `MaxPoints = 4000`:到顶丢最老的——**滚动日志的思路**,无限长的点动也不会把内存吃穿(环形缓冲的简化版) |
| 26-31 | 🔧 构造三件套:DoubleBuffered(铁律2)/ ResizeRedraw(拉伸时整块重画不留残影)/ 白底 |
| 37-44 | 🧠 `Sample(x, y)`:40 行**位置没变就不记**——静止时定时器照常调,但轨迹不灌重复点(不然 4000 个名额被静止点刷光)。43 行 `Invalidate()` **只标记"画面过期",真正的画发生在下一次 OnPaint**——这就是"标记 + 系统调度重绘",和 Vue 的"改数据 → nextTick 批量更新"一个思想:数据更新廉价,重绘昂贵,合并处理 |

**绘制面:OnPaint(55-116 行)——渲染函数,五层从底到顶**

先看完整骨架(真实行号):

```csharp
55      protected override void OnPaint(PaintEventArgs e)
56      {
57          base.OnPaint(e);
58          var g = e.Graphics;
59          g.SmoothingMode = SmoothingMode.AntiAlias;
61          // —— 坐标映射:mm → 像素 ——
64          var scale = (Math.Min(Width, Height) - 16f) / (float)(SoftLimit * 2);
65          float Px(double mm) => Width / 2f + (float)(mm * scale);
66          float Py(double mm) => Height / 2f - (float)(mm * scale);
68          // 1. 网格(69-77)
79          // 2. 工作区边框 = 软限位(80-83)
85          // 3. 坐标轴十字 + 标签(86-97)
99          // 4. 轨迹折线(100-107)
109          // 5. 当前位置红点(110-115)
116      }
```

| 行 | 讲解 |
|---|---|
| 55 | 🔧 `override OnPaint`:自绘控件的标准入口,系统在"需要重画"时回调( Invalidate 之后的消息循环、窗口遮挡后露出、拉伸尺寸)。**相当于组件的 render 函数,你只管声明画什么,何时画系统定** |
| 57 | 🔧 先调 base 让父类画背景,惯例 |
| 59 | `SmoothingMode.AntiAlias`:抗锯齿,斜线不再毛刺——canvas 的 `ctx.imageSmoothingEnabled` 同族开关 |
| 65-66 | 🧠 **局部函数** `Px/Py`:mm→像素的一对翻译器,后面所有绘制只说"机坐标",翻译细节收敛在这两行——改映射规则只动一处。前端类比:组件内的工具函数,不暴露出去 |

坐标映射三行是本站的数学核心:

```csharp
64          var scale = (Math.Min(Width, Height) - 16f) / (float)(SoftLimit * 2);
65          float Px(double mm) => Width / 2f + (float)(mm * scale);
66          float Py(double mm) => Height / 2f - (float)(mm * scale);
```

| 行 | 讲解 |
|---|---|
| 64 | 🧠 **等比映射的命门**:比例取**短边**算(留 8px 边距)——画出来是面板中央一个正方形工作区,**1mm 在 X/Y 方向等长**。63 行注释点名经典坑:比例若按长宽各算各的,圆变椭圆、直线变斜线(ECharts 的 xAxis/yAxis 不设 equal 就出这种图) |
| 65 | X 映射:屏幕中心 + mm×比例——机械 X 正向=屏幕右,方向一致**不翻** |
| 66 | 🧠 Y 映射:**中心 − mm×比例,取反**。66 行注释:屏幕坐标系 Y 轴**向下**,机械坐标系 Y 向上——不取反,电机往上走轨迹往下画,操作员当场懵。这是所有绘图库(canvas/SVG/GDI+)共同的入门坑,本项目用一行减号解决 |

| 行 | 层 | 讲解 |
|---|---|---|
| 69-77 | 1 网格 | 每 SoftLimit/4(250mm)一条浅灰线,方便对着轨迹读大概位置 |
| 80-83 | 2 边框 | 工作区边框=软限位:**轴永远出不了这个方框,行程边界一眼可见**——把卡的保护参数可视化到界面上 |
| 86-97 | 3 坐标轴 | 过原点的 X/Y 十字线 + "X+/Y+/(0,0)" 标签——读轨迹的参照系。轴 1=X、轴 2=Y 的分配在 Designer 553 行标题里写明 |
| 100-107 | 4 轨迹折线 | 🧠 先把全部毫米点**批量**转屏幕点(103-104 行),再 `DrawLines` **一次**画整条折线——不是每个点画一小段。批量转换+批量绘制,几千个点也一帧画完。代码长这样(真实行号): |

```csharp
100          if (_trail.Count > 1)
101          {
102              var pts = new PointF[_trail.Count];
103              for (var i = 0; i < _trail.Count; i++)
104                  pts[i] = new PointF(Px(_trail[i].X), Py(_trail[i].Y));
105              using var trailPen = new Pen(Color.MediumSeaGreen, 2f);
106              g.DrawLines(trailPen, pts);
107          }
```

| 行 | 讲解 |
|---|---|
| 105 | `using var` 声明即释放:Pen/Brush/Font 是非托管资源(GDI 句柄),用完必须还——`using` 块保证异常路径也释放。本项目 OnPaint 里每个画笔都包 using(69/80/86/91/105/112 行),**漏了不报错,但长跑几千小时句柄泄漏,窗口画不出东西**——工业软件长跑稳定性的经典暗雷 |
| 106 | 一条 DrawLines 画整条折线;`>1` 的守卫:一个点画不了线(第一次 Sample 后只有当前点红点,没有线) |
| 110-115 | 5 当前点 | 轨迹末端红点(与急停按钮同款红 `214,64,64`——**红色统一表示"危险/注意"**),没动过时它坐在原点上 |

### 🔬 数据面补讲:Sample 的三个细节(37-44 行)

```csharp
37      public void Sample(double x, double y)
38      {
39          var p = new PointF((float)x, (float)y);
40          if (_trail.Count > 0 && _trail[^1] == p) return;
41          _trail.Add(p);
42          if (_trail.Count > MaxPoints) _trail.RemoveAt(0);
43          Invalidate();
44      }
```

| 行 | 讲解 |
|---|---|
| 39 | mm 坐标直接装箱成 `PointF`(**仍然存机坐标**,不是屏幕坐标)——延迟转换是关键设计:窗口尺寸是易变状态,数据层不该依赖它(铁律 3 的数据面体现) |
| 40 | **去重**:位置没变不记点。静止时定时器每 100ms 照常调 Sample,没有这行,静止一小时 = 36000 个重复点,把 4000 名额刷光、轨迹"变长"但没新信息。`[^1]` = 最后一个点;`PointF` 是值类型,`==` 比较的是坐标值 |
| 42 | **封顶丢弃**:`RemoveAt(0)` 丢最老的——List 头部删除是 O(n),4000 规模无所谓;真要较真长跑性能可换环形缓冲(Queue 或 head/tail 双指针),面试可主动提这个优化方向 |
| 43 | Invalidate 只标脏不画——系统把多个标脏合并成**一次** OnPaint(Windows 的 WM_PAINT 本来就是低优先级合并消息),100ms 内采样再多次,一帧画完。**批量重绘是框架送你的,前提是你别绕过它直接调 CreateGraphics 画**(新手最常犯) |

### 🔬 采样防锯齿:第④站 240 行的伏笔在这里回收

轨迹采样为什么在主窗体定时器里(`trajPanel.Sample(pos0, pos1)`)而不是控件自己订阅 PositionChanged?MainForm 236-239 行注释:事件是每根轴**各自**来报,X 到达时 Y 可能还停在上个节拍 → 轨迹画出锯齿;**定时器同一时刻取两轴,坐标才是一致的快照**。"按周期采样对付事件乱序"——和 DaqMonitor 的采集管道同一思想。💼 面试:"XY 轨迹图怎么保证画的是真实轨迹?"——答案从"采样时刻一致性"讲起。

### 🎤 面试一句话(第六站)

> "TrajectoryPanel 继承 Panel 自绘:Sample 只存毫米点+Invalidate 标脏,OnPaint 统一渲染——数据绘制分离;双缓冲防闪;mm→像素按短边等比映射防变形,屏幕 Y 向下机械 Y 向上所以取反;点数上限 4000 滚动丢弃防内存膨胀;两轴坐标由主窗体定时器同拍采样,避免事件乱序导致的锯齿轨迹。"

### ✂️ 自己改一处(5 分钟)

把 66 行的减号改成加号再跑,点两轴插补——"往上走"的轨迹在屏幕上往下画。工业界面最不能容忍的 bug 之一:**方向反了**。改回去恢复。

---

## 附录 A · 面试串联(把六站拧成一股绳)

### 30 秒版(简历上那句话的口头版)

> "我做了一个两轴运动控制上位机:IMotionCard 统一抽象运动卡、MockMotionCard 用节拍仿真+每轴 CancellationToken 模拟真卡行为,WinForms 界面控件数组+循环订阅、InvokeRequired 跨线程刷新、定时器边沿检测运动完成,两轴直线插补共用步数等比推进、共享令牌一停俱停,自定义 TrajectoryPanel 双缓冲绘制 XY 轨迹,14 个 xUnit 测试全绿,换真卡只需改一处构造。"

### 2 分钟版(面试官说"详细讲讲你的项目")

按**一条控制指令的旅程**讲(顺序即①~⑥站):

1. **合同**:IMotionCard 定义两属性三事件四组方法,返回码对齐真卡 SDK 负数习惯,上层零耦合设备(第②站)
2. **模拟卡**:运动=后台任务每 tick 推进一步,步数=距离/速度/节拍;每轴独立令牌保证两轴并发;急停全轴取消、位置就地冻结;统一检查链+点动限位夹逼报警(第③站)
3. **界面**:控件数组循环订阅防闭包坑;卡事件后台线程触发,InvokeRequired+BeginInvoke 切回 UI 线程;100ms 定时器做边沿检测+同拍采样+RefreshUiState 单一真源(第④站)
4. **插补**:逐轴全检、共享令牌一停俱停、总步数按最远轴、公共进度等比推进(第⑤站)
5. **可视化**:数据绘制分离、双缓冲、等比映射+Y 翻转(第⑥站)
6. **质量**:14 个测试钉死卡的行为契约,含 STA 线程 UI 全流程冒烟(Tests 237-289 行)

### 面试官连环追问 8 个(每题 15 秒速答)

| 追问 | 答案钩子(都在正文) |
|---|---|
| 为什么模拟卡能测真卡逻辑? | 模拟的是**行为**不是硬件:拒绝条件/连续运动/急停冻结/插补比例,逻辑与真卡同构;测试钉死后真卡接入只查实现层(第③站"掰开揉碎") |
| 急停为什么要共享令牌(插补)/ 每轴一个令牌(单轴)不矛盾吗? | 不矛盾:单轴独立防误伤,插补同 步防拐弯——绑腿跑必须同起同停(第⑤站 227 行) |
| InvokeRequired+BeginInvoke 和 WPF 的 Dispatcher 什么区别? | 机制同构(都投递回 UI 线程消息队列);WinForms 用 InvokeRequired 自查+Control.BeginInvoke,WPF 控件天然有 Dispatcher;BeginInvoke 都是异步不阻塞调用方(第④站 202 行) |
| 插补为什么等比分步、步数按谁算? | 公共进度 i/steps 保证任意时刻比例恒定=直线;步数按最远轴,速度语义=最长腿速度,不然长轴超速(第⑤站 241-249 行) |
| 双缓冲为什么能防闪烁? | 先画内存位图再整帧贴屏,屏幕永远看不到"画了一半"的中间态;等价离屏 canvas(第⑥站铁律 2) |
| for 循环里订阅事件有什么坑? | 闭包捕获共享的循环变量,所有 handler 拿到循环终值;先 `var axis = i;` 复制副本再捕获(第④站 63-66 行) |
| 运动完成你怎么知道的? | 卡只有状态位没有完成事件:100ms 轮询 IsMoving+边沿检测(_wasMoving 真→假=刚完成,只报一次)(第④站 227-233 行) |
| 换固高/雷赛真卡要改多少? | 新增一个实现类+改 Program 构造处一行;MainForm/测试/业务零改动,开闭原则(第②站"掰开揉碎") |

### 加分题:两个项目串着讲(有 DaqMonitor 底子的同学)

面试官听了运控项目常追问:"这和你那个采集项目什么关系?"——**一句话答案**:"一个是'看'的,一个是'做'的,但架构同构。"展开三处:

1. **设备抽象同款**:IDevice(采集)↔ IMotionCard(运控),都是"合同+模拟实现+真实现"三件套,都满足换真设备改一行的开闭原则;
2. **后台线程与取消同款**:SimulatedDevice 的产数循环和 MockMotionCard 的运动循环,都是 Task.Run + CancellationToken + catch OperationCanceledException 的组合拳;
3. **界面跨线程同款思想不同 API**:WPF 用 Dispatcher,WinForms 用 InvokeRequired+BeginInvoke——我能说清两者机制同构(投递回 UI 线程消息队列),说明不是背 API,是懂原理。

> 💼 这段话的价值:证明你的架构能力可迁移,不是"做过两个 CRUD"。

---

## 附录 B · 运控术语 ↔ 大白话总对照表

| 术语 | 大白话 | 本文首次出现 |
|---|---|---|
| 轴(Axis) | 一个能动的自由度,一台电机+导轨 | 开篇 |
| 点位/定位(Positioning) | 让轴走到指定坐标 | 第②站 |
| 点动(JOG) | 按住走松手停,手动微调 | 第②站 |
| 绝对坐标 vs 相对坐标 | "走到 100mm 处" vs "再前进 30mm" | 第②站 MoveAbsolute |
| 回零/回原点(Home) | 回机械零点,建立坐标基准(每天开机第一件事) | 第②站 |
| 使能(Enable/伺服上电) | 电机通电锁轴;未使能一切运动指令被拒 | 第②站 |
| 软限位(Soft Limit) | 软件里的行程边界,撞上就停+报警 | 第②站 |
| 硬限位 | 导轨两头的行程开关,最后物理防线 | 第③站(对照) |
| 急停(E-Stop) | 无条件全轴立即停,位置就地冻结 | 第②站 |
| 插补(Interpolation) | 多轴绑腿跑出直线/圆弧 | 第②站 MoveLinear |
| 直线插补 | 任意时刻各轴位移比例恒定 → 空间直线 | 第⑤站 |
| 脉冲当量 | 一个脉冲走多少 mm(真卡的速度/位置本质) | 附录(对照模拟卡 tick) |
| 重复定位精度(Repeatability) | 每次停在同一位置的误差 | 第③站 357 行 |
| 报警(Alarm) | 卡的异常状态,必须清除才能再动 | 第②站 |
| 上限位/限位报警 | 撞到行程边界触发的报警 | 第③站 345 行 |
| 仿真节拍(tick) | 模拟卡推进一步的时间片,类比脉冲周期 | 第③站 |
| 取消令牌(CancellationToken) | 控制后台任务生死的"红色按钮" | 第③站 |
| 边沿检测 | 状态翻转瞬间(动→停)才触发一次 | 第④站 |
| 双缓冲(Double Buffer) | 先画内存再贴屏,防闪 | 第⑥站 |
| 坐标翻转 | 屏幕 Y 向下机械 Y 向上,画图必须取反 | 第⑥站 |

---

## 附录 C · 自测 20 问(合上文档,白纸作答)

**第①站**:① [STAThread] 不写会怎样? ② 换真卡改哪一行?
**第②站**:③ MotionResult 为什么用负数枚举而不是抛异常? ④ SimulateAlarm 为什么放进接口,有什么代价?
**第③站**:⑤ 步数公式是什么,Math.Max(1,…) 防的是哪个 v1 坑? ⑥ 急停"就地冻结"是哪三行实现的? ⑦ finally 里 ReferenceEquals 防什么竞态? ⑧ 点动为什么把目标设在软限位上?
**第④站**:⑨ for 里订阅事件为什么要 var axis=i? ⑩ 点动为什么用 MouseDown 不用 Click? ⑪ 运动完成怎么检测,为什么不用事件? ⑫ RefreshUiState 解决 v1 什么问题?
**第⑤站**:⑬ 插补为什么共享一个令牌? ⑭ 总步数按什么算,速度语义是什么? ⑮ 测试在中段抓什么来证明走直线?
**第⑥站**:⑯ mm→像素为什么按短边算比例? ⑰ Y 为什么要取反? ⑱ 采样为什么在定时器而不订阅事件?
**全景**:⑲ 白纸画出"一条控制指令的旅程"(按钮→合同→仿真→事件→界面→轨迹),标出经过的每个类和方法名。 ⑳ 14 个测试都测了什么行为,能列出 8 条吗?

**及格线**:⑲ 必须对 + 前 18 问 ≥ 14 对 = 项目吃透,可以去面试讲了。

---

## 附录 D · 易错点急救手册(面试 + 排错双用,四段式)

> **怎么用**:每个点四段式——💥现场(真实症状,可直接当搜索词)→ 🔍根因 → ❌✅写法对照 → 🎤面试官怎么问。

### D1 · 跨线程访问 UI —— 翻车率第一名

💥 **现场**:
```
System.InvalidOperationException: 跨线程操作无效:从不是创建控件"txtPos1"的线程访问它。
```
触发场景:直接在 PositionChanged/AlarmChanged 事件回调里改控件(这些事件在后台仿真线程触发)。

🔍 **根因**:WinForms 规定界面上每个控件只能被创建它的 UI 线程访问。前端类比:浏览器不允许 worker 线程直接操作 DOM,必须 postMessage 回主线程——一模一样。

❌:
```csharp
private void OnPositionChanged(object? s, PositionChangedEventArgs e)
    => _txtPos[e.Axis].Text = e.Position.ToString("F3");   // 后台线程直接摸控件 → 崩
```
✅(MainForm.cs 202 行):
```csharp
if (InvokeRequired) { BeginInvoke(() => OnPositionChanged(sender, e)); return; }
_txtPos[e.Axis].Text = e.Position.ToString("F3");
```

🎤 "InvokeRequired 和 WPF Dispatcher 区别?"——机制同构(投递回 UI 线程),WinForms 靠控件自查 InvokeRequired,WPF 每个对象自带 Dispatcher;高频事件用 BeginInvoke(异步)别用 Invoke(同步,会拖死后台线程)。

### D2 · for 循环订阅事件的闭包坑

💥 **现场**:按"轴 1 使能"按钮,日志打出"轴 3 已使能";所有单轴按钮行为全部指向最后一根轴。

🔍 **根因**:for 的循环变量是**所有迭代共享**的同一个变量,lambda 捕获的是它的引用——循环结束后 i=2,所有 handler 都读 2。JS 的 `var` 同款,Vue v-for 传 index 反而无此问题(函数参数每次求值)。

❌:
```csharp
for (var i = 0; i < 2; i++)
    _btnEnable[i].Click += (s, e) => SetAxisEnabled(i, true);   // 全部拿到 2
```
✅(MainForm.cs 65-68 行):
```csharp
for (var i = 0; i < 2; i++)
{
    var axis = i;   // 每次迭代复制一份新变量
    _btnEnable[i].Click += (s, e) => SetAxisEnabled(axis, true);
```

🎤 "为什么复制一份就有用?"——`var axis = i` 在**每次迭代**里声明一个新变量,lambda 捕获的是各自那一份副本。

### D3 · 急停只停一轴 / 打断后令牌被误删(令牌联动)

💥 **现场**:①插补运动中急停,X 停了 Y 还在走,轨迹拐了个弯;②运动被打断后,再按"停止"没反应。

🔍 **根因**:①每轴独立令牌时急停没有"全停"语义,或插补用了独立令牌——绑腿跑必须共享一个令牌;②运动任务的 finally 里无脑 `_cts[axis]=null`,把打断者刚放进槽里的**新令牌**误删了,后续 Cancel 找不到对象。

❌:
```csharp
foreach (var axis in axes) { _cts[axis] = new CancellationTokenSource(); ... }  // 插补各发各的令牌
// finally:
_cts[axis] = null;                        // 无脑清槽 → 误删新令牌
```
✅(MockMotionCard.cs 227-229 / 272 行):
```csharp
var cts = new CancellationTokenSource();
foreach (var axis in axes) { _cts[axis] = cts; ... }      // 全员共享同一个
// finally:
if (ReferenceEquals(_cts[axes[k]], cts)) _cts[axes[k]] = null;  // 先验身份再清
```

🎤 "插补为什么必须一停俱停?"——两轴比例恒定才叫直线,中途一轴独走,轨迹立刻拐弯;对插补运动,"半途而废的直线"比"停下"危险。

### D4 · 坐标映射:Y 不翻转 / 比例不等比

💥 **现场**:①电机明明向上走,轨迹图上往下画;②窗口一拉伸,圆轨迹变椭圆、45° 直线变 37°。

🔍 **根因**:①屏幕坐标系 Y 轴向下,机械坐标系 Y 向上,两个坐标系的"上"定义相反;②mm→像素比例按面板长、宽各算各的,两个方向 1mm 像素数不同,几何形状失真。

❌:
```csharp
float Py(double mm) => Height / 2f + (float)(mm * scaleY);   // Y 没取反
var scaleX = Width / range; var scaleY = Height / range;      // 长宽各算各的
```
✅(TrajectoryPanel.cs 64-66 行):
```csharp
var scale = (Math.Min(Width, Height) - 16f) / (float)(SoftLimit * 2);   // 短边定比例
float Px(double mm) => Width / 2f + (float)(mm * scale);                 // X 不翻
float Py(double mm) => Height / 2f - (float)(mm * scale);                // Y 取反
```

🎤 "轨迹图怎么保证不变形?"——比例用短边统一计算,画出来是中央正方形工作区,X/Y 方向 1mm 等长;Y 轴机械正向向上、屏幕向下,所以取反。

---

## 结语:读码的顺序就是指令的顺序

采集线(DaqMonitor)顺着**数据**读:设备→管道→库→界面;运控线(MotionControl)顺着**指令**读:按钮→合同→仿真→事件→界面→轨迹。两条线在"接口抽象、模拟设备、CancellationToken、跨线程 UI、定时器轮询"五处交汇——**交汇点就是你可迁移的功底**,换什么行业都带走。

读完六站 → 附录 C 自测 ≥14 对 → 工作纸两张亲手造一遍 → git commit 你的版本。到那一步,面试官问"讲个你自己的项目",你有 14 个全绿测试和一个能白板画出的架构。
