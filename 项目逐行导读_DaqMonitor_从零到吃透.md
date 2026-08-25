# 🔬 项目逐行导读 · DaqMonitor 从零到吃透

> **这份文档是什么**:把 `DAQMonitor` 工程主线代码**从第一行讲到最后一行**——每行干什么、哪些是背了就能用的固定写法、哪些是必须理解的核心逻辑、为什么这么设计。工业术语 + 大白话双讲解,读完能白板画出整个项目。
> **不讲什么**:Tests 测试工程(略过)、周边扩展模块(运控/配方/登录等只在第十站给"一页图"指路)。
> **配套**:对着真实代码读(`DAQMonitor/src/`),本文所有行号与真实文件一一对应(2026-08-25 版本)。

---

## 〇、先读我:怎么用这份文档

### 三个标记(全文通用,先认脸)

| 标记 | 意思 | 你该怎么对待它 |
|---|---|---|
| 🔧 **固定写法** | 框架要求的"仪式代码",全世界的 WPF/EF Core 项目都长这样 | **不用理解为什么,抄熟即可**。就像前端 `new Vue({})` 不需要背源码 |
| 🧠 **核心逻辑** | 这个项目自己的思考,换你写要自己想出来的部分 | **必须吃透**,面试就问这里 |
| 💼 **面试点** | 面试官真的会问的点 | 背下来,能用大白话讲 1 分钟 |
| 📚 **对应讲义** | 每站开头一框,标明本站知识点出自哪份模块讲义 | **卡壳就点过去重学那一节**,读完回来继续 |

### 从哪里开始看(按你的状态选)

```
完全菜鸟(第一次接触这个项目) → 先读下方「先懂项目再读码」(需求逼出架构,10分钟) → 再从第一站顺读
有前端背景(学过 Vue/React)   → 同上先读「先懂项目再读码」,然后第一站快读,重点读第三/五/八站的「前端类比」框
只剩 1 小时(面试明天)        → 只读「先懂项目再读码」③ 八步推导链 + 各站「🎯 一句话」+ 附录A 的 30 秒讲法
程序报错/结果不对            → 直接翻附录 D「易错点急救手册」,按报错原文搜
面试官爱追"为什么/会出什么问题" → 附录 D 每个点都有「🎤 面试官怎么问」
面试当天出门前 10 分钟        → 只翻附录 E「项目操作速查」:启动方式/入口/账号/db 路径 10 连问秒答
读完想动手                   → 每站末尾有「✂️ 自己改一处」小实验,改完跑 dotnet test 仍应全绿
```

### 整个项目只有一句话

> **DaqMonitor = 不停地从设备读数 → 排队缓冲 → 存起来/查报警 → 刷到屏幕上。**

工业术语叫「数据采集与监控系统」(SCADA 的单机简化版)。大白话:**一个永远在填表的抄表员**——抄表(采集)、登记入库(持久化)、超标了喊一嗓子(报警)、领导随时看板(界面)。

### 先懂项目再读码:一个需求逼出整个架构(需要什么 → 怎么做 → 为什么)

> **读这一节的目的**:让你带着"它解决了哪条需求"去看后面每一站——知识点的组合不再是天书,而是一条因果链。完整需求文档见 [R0 需求总纲](项目实践_DaqMonitor_R0_需求总纲.md),界面长什么样见 [README 原型图](DAQMonitor/README.md)。

#### ① 客户的故事(一切从一个需求开始)

> 一家小工厂:车间有 6 台设备(温控仪、压力表、PLC…)。老板提了 6 条要求:
> 1. "我坐在办公室能看到每台设备的实时温度压力" → **实时监控**
> 2. "超温了必须立刻告诉我,不能等我去看才发现" → **报警**
> 3. "昨天下午那批产品温度多少?调出来看曲线" → **历史查询**
> 4. "月底给我一张班报表,Excel 发我" → **报表导出**
> 5. "停电重启,数据不能全没了" → **持久化**
> 6. "参数不是谁都能改的,小工只能看,工程师才能改" → **登录 + 权限**

这 6 条就是全部——**界面上每个按钮、代码里每个类,都对应其中一条**。

#### ② 需求 → 界面区域 → 代码模块 对照表(先对上号)

| 老板的要求 | 界面上在哪 | 代码在哪一站 |
|---|---|---|
| 实时监控 | 点位表 + 仪表盘 | 3/4/5/8 站(设备→管道→界面) |
| 报警 | 报警日志页 + 表盘变红 | 7 站(报警引擎) |
| 历史查询 | 报表日期框 + 曲线页 | 6 站(双写落库)+ 8 站(曲线) |
| 报表导出 | 「导出报表」按钮 | 8 站(ExportReport) |
| 持久化 | (看不见,但断电就靠它) | 6 站(SQLite) |
| 登录权限 | 登录窗 + 按钮灰亮 | 1 站(登录拦截)+ 8 站(权限) |

#### ③ 八步推导链:每个零件都是被"没有它会怎样"逼出来的

**这是本导读最重要的一张表。** 建造顺序 = 因果顺序,每一步先问"遇到什么问题",再看"所以造了什么":

| 步 | 遇到的问题(没有它会怎样) | 所以造了 | 哪一站 |
|---|---|---|---|
| 1 | 设备五花八门:串口的、网口的、PLC…每种连法都不同,上层代码写一份就锁死一种设备 | **统一合同 IDevice**:4 个动作 + 1 个广播,7 种设备随便换 | 第 3 站 |
| 2 | 手上没设备怎么开发?总不能买个 PLC 放桌上 | **模拟设备 SimulatedDevice**:假数据真链路 | 第 4 站 |
| 3 | 数据来了直接刷界面?100Hz×多设备,每秒几百次挤 UI 线程,**界面卡死** | **管道 Channel + 批量**:事件只排队,200ms 整批放行 | 第 5 站 |
| 4 | 数据只在内存里,断电全没;要查昨天曲线,内存翻不动 | **落库 SQLite**;但每秒几百次写盘,采集被拖死 → **双写**:内存管现在,数据库管历史 | 第 6 站 |
| 5 | 温度在 100 上下抖,报警响个不停,操作工直接静音——**真报警没人看了** | **报警引擎:回滞带 + 边沿触发**,一次真实越界只响两声(报+恢复) | 第 7 站 |
| 6 | 界面代码和业务代码搅在一起,换个界面全重写 | **MVVM**:ViewModel 管状态,View 只管长相 | 第 8 站 |
| 7 | 零件多了:谁 new 谁?设备写死在界面里换不动 | **DI 组合根 Bootstrapper**:全项目只有一个地方知道"谁配谁" | 第 9 站 |
| 8 | (二期扩建)客户又加需求:操作留痕(审计)、工艺参数要版本化管理(配方)、还想控制电机(运控) | Auth/Recipe/Motion 三个模块——**同一套架构,加新类不改老代码** | 第 10 站 |

> **读法建议**:现在合上这张表,自己复述一遍——"因为没有 ___ 会 ___,所以有 ___"。复述得出来,这个项目在你脑子里就不再是零件堆,是一台整机。

#### ④ 如果从零自己写:建造顺序(先地基,后装修)

真实开发顺序(R1→R8)和阅读顺序(数据流)不同——**写代码先打地基,读代码顺着数据走**:

```
R1 立骨架(3 个项目+领域模型)     ← 先定"数据长什么样",不然后面全返工
R2 设备抽象(IDevice+模拟设备)     ← 地基:数据源头
R3 协议解析(CRC/帧/粘包)          ← 设备说的"方言"怎么翻
R4 真实设备接入(串口/Modbus/TCP)  ← 方言翻译官上岗
R5 管道+报警(大动脉+大脑)          ← 数据流动与判断
R6 落库(双写)                     ← 数据落地
R7 组装(DI+诊断+容错)             ← 零件进车间
R8 界面(WPF 主屏)                 ← 最后才装修 —— 因为界面是"消费"数据的,数据链路没通,界面没东西可显示
```
> 💡 **面试金句**:"我的开发顺序是先领域模型再设备抽象、最后才做 UI——数据链路先行,界面只是消费者。" 这一句话比"我会 WPF"值钱,它证明你懂工程节奏。

#### ⑤ 带着问题去读后面的站

每一站开头,先自问一句再往下读(答案都在表里):

- 第 3 站:"7 种设备为什么能随便换?"
- 第 5 站:"不做管道直接刷界面会怎样?"
- 第 6 站:"为什么存两层而不是一层?"
- 第 7 站:"报警为什么不能每条数据都响?"
- 第 9 站:"为什么全项目只有一个地方 new 设备?"

### 一条数据的完整旅程(全文的金线,每站都会回到它)

```
【出生】SimulatedDevice 每 100ms 生成一个随机数,比如 点位1=105.3
   ↓ RaiseData(1, 105.3) —— 设备举起手说"我有新数据"
【排队】AcquisitionPipeline 收进 Channel 队列(不处理,只排队)
   ↓ 每 200ms 或攒满 500 条,整批倒出来
【分诊】MainViewModel.OnBatchReady 一次性处理整批:
   ├→ PointStore.AddOrUpdate  → 内存立刻更新 + SQLite 排队落库   【入库】
   ├→ AlarmEngine.Evaluate    → 105.3 > 100 阈值?报警!           【喊话】
   └→ Points 集合/曲线        → 屏幕上这一行变红、曲线跳一下       【上板】
```

记住这张图,后面每一站只是把其中一格放大讲。

### 项目的文件夹地图(先认路)

```
DAQMonitor/src/
├─ DaqMonitor.Core/           ← 大脑(不依赖任何界面,换掉 WPF 也能跑)
│  ├─ Models/SensorPoint.cs        ← 第2站:数据的形状(19行,全项目最短)
│  ├─ Devices/IDevice.cs           ← 第3站:设备的"合同"(59行)
│  ├─ Devices/SimulatedDevice.cs   ← 第4站:第一个设备(80行)
│  ├─ Acquisition/AcquisitionPipeline.cs ← 第5站:大动脉(80行)★心脏
│  ├─ Store/SensorRecord.cs        ← 第6站:数据的"入库服装"(51行)
│  ├─ Store/AppDbContext.cs        ← 第6站:数据库图纸(128行)
│  ├─ Store/PointStore.cs          ← 第6站:仓库管理员(191行)★最长
│  ├─ Alarms/AlarmRule.cs 等3个    ← 第7站:报警大脑(78行合计)
│  └─ AppServices/Bootstrapper.cs  ← 第9站:总装配车间(249行)
├─ DaqMonitor.UI/             ← 脸面
│  ├─ App.xaml.cs                  ← 第1站:程序入口(57行)
│  ├─ ViewModels/RelayCommand.cs   ← 第8站:按钮的翻译官(29行)
│  ├─ ViewModels/MainViewModel.cs  ← 第8站:界面的总指挥(284行)
│  └─ MainWindow.xaml(.cs)         ← 第8站:长什么样(108+55行)
└─ DaqMonitor.Tests/          ← (略,按约定不讲)
```

**依赖方向只有一条,绝不允许倒流:UI → Core**。Core 不知道 WPF 的存在——这是全项目最重要的纪律,面试必问(见第 1 站)。

---

## 第一站 · 程序的第一口气:App.xaml.cs(57 行)

> 文件:`src/DaqMonitor.UI/App.xaml.cs` · 讲解顺序 = 程序真实执行顺序

> 📚 **对应讲义**:[M0 · C#/.NET 热身 + 工程骨架](M0_每日讲义_深度版.md)(async void/事件/启动流程)· [M9 · 工程素养](M9_工程素养_测试DI容错_深度版.md)(DI 组合根)· [M17 · 工业安全](M17_工业安全与MES对接_深度版.md)(审计日志)· [⚠️ C# 陷阱](C#_陷阱_前端转上位机必看_深度版.md)(async void 专坑)

### 🎯 一句话

**双击 exe 后,WPF 找到的第一个 C# 文件就是它:装配全部服务 → 弹登录窗 → 启动设备 → 打开主窗口。** 它是整条生产线的"开机检查单"。

### 先看兄弟文件 App.xaml(7 行,扫一眼就走)

```xml
<Application x:Class="DaqMonitor.UI.App"          <!-- 🔧 告诉 WPF:这个 xml 对应哪个 C# 类 -->
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"  <!-- 🔧 WPF 默认命名空间 -->
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">            <!-- 🔧 x: 前缀命名空间 -->
    <Application.Resources>                       <!-- 🔧 全局资源字典(放全局样式/颜色),现在空着 -->
    </Application.Resources>
</Application>
```

🔧 全部是固定写法。`StartupUri` 属性没写——**因为入口不是直接开主窗,而是要先走登录**,所以入口逻辑写在下面的 `OnStartup` 里手动控制(WPF 规矩:没写 StartupUri,就得自己 `window.Show()`,不 Show 程序直接退出)。

### App.xaml.cs 逐行讲解

```csharp
01  using System.Windows;                        // Window/Application 这些 WPF 基础类型
02  using DaqMonitor.Core.Acquisition;           // AcquisitionPipeline(第5站)
03  using DaqMonitor.Core.AppServices;           // Bootstrapper(第9站)
04  using DaqMonitor.Core.Auth;                  // AuthService/ICurrentUserService(第十站一带而过)
05  using DaqMonitor.Core.Devices;               // IDevice(第3站)
06  using DaqMonitor.UI.ViewModels;              // LoginViewModel/MainViewModel(第8站)
07  using DaqMonitor.UI.Views;                   // LoginWindow/MainWindow
08  using Microsoft.Extensions.DependencyInjection;  // ServiceProvider(DI 容器)
09
10  namespace DaqMonitor.UI;                     // 🔧 文件夹路径 = 命名空间,全项目统一
11
12  public partial class App : Application       // 🔧 继承 WPF 的 Application 类;partial = 一半逻辑在 App.xaml 里
13  {
14      /// <summary>全局 DI 容器,供各处取服务。</summary>
15      public static ServiceProvider Services { get; private set; } = null!;
```

| 行 | 讲解 |
|---|---|
| 12 | `partial`(半成品类):C# 会把 `App.xaml` 和 `App.xaml.cs` 编译时拼成一个类。🔧 WPF 固定玩法 |
| 15 | **全局容器**:一个 static 属性,装着全项目的服务清单。谁要服务,来这拿。`null!` 的 `!` 是告诉编译器"我知道它现在是 null,启动后立刻赋值,别警告我"。💼 类似前端的"全局 store",但存的是**服务**不是数据 |

```csharp
17      protected override async void OnStartup(StartupEventArgs e)
18      {
19          base.OnStartup(e);
```

| 行 | 讲解 |
|---|---|
| 17 | 🔧 **WPF 开机钩子**:程序启动时 WPF 自动回调这个方法,相当于前端的 `main()` / `app.mount()`。三个修饰词拆开:①`protected override` = 重写父类方法(固定)②`async` = 里面可以用 await ③`void` = 事件回调签名必须是 void,不能改 |
| 19 | 🔧 先让父类做默认初始化,再干自己的事。所有 override 的第一行惯例 |

> 💼 **面试连环追问 1**:"async void 不是反模式吗?"
> 答:**事件回调是 async void 唯一的合法场景**(OnStartup/按钮点击/定时器)。因为事件签名由框架定死是 void,没得选。规则是:async void 方法里必须自己 try-catch 全包住,异常不能漏出去(漏出去进程直接崩)。普通方法永远用 async Task。

```csharp
21          // 1) 组合根:一次性把 Core 全部服务装配好
22          Services = Bootstrapper.Build();
```

| 行 | 讲解 |
|---|---|
| 22 | 🧠 **全项目第一根线**:调用第 9 站的 Bootstrapper,把所有服务(存储/报警/管道/设备/登录账号)一次性装配进容器。开机装配、全程复用、关机销毁——这行就是"组合根"三个字的落地。**看不懂没关系,读到第 9 站回来就懂** |

```csharp
24          // 2) 先弹登录窗 — 不登录不让用
25          var auth = Services.GetRequiredService<AuthService>();
26          var loginVm = new LoginViewModel(auth);
27          var loginWin = new LoginWindow(loginVm);
28          var ok = loginWin.ShowDialog();
29          if (ok != true)
30          {
31              Shutdown();
32              return;
33          }
```

| 行 | 讲解 |
|---|---|
| 25 | `GetRequiredService<AuthService>()` = 🔧 从容器"点菜"的标准句式:给我一个 AuthService,没有你就报错(区别于 `GetService` 的"没有给 null") |
| 26-27 | 🧠 手动 new ViewModel 再塞进 Window——**MVVM 标配动作**:窗口(View)不写业务,业务在 ViewModel;先造 VM,再造 View,把 VM 喂给 View |
| 28 | 🔧 `ShowDialog()` = 模态弹窗(前端 `alert` 的正经版):**代码停在这一行等用户操作**,窗口关了才继续。返回 `bool?`:true=点了确认 |
| 29-33 | 🧠 取消登录 → `Shutdown()` 关程序 → `return` 立刻退出方法(不 return 会继续往下开主窗!)。**先关后 return 是标准安全写法** |

```csharp
37          // 3) 登录成功:启动采集设备
38          var device = Services.GetRequiredService<IDevice>();
39          var pipeline = Services.GetRequiredService<AcquisitionPipeline>();
40          pipeline.Register(device);
41          device.Connect();
```

| 行 | 讲解 |
|---|---|
| 38 | 🧠 注意点的是**菜名 `IDevice`(接口)**,不是具体某台设备。今天厨房端上来的是 SimulatedDevice(模拟设备),明天想换成真 PLC,**这行代码一个字不改**——这就是第 3 站要讲的"面向接口"的回报 |
| 40 | 🧠 `Register`:把设备**挂到管道**上——从此设备每喊一声"有新数据",管道都听得见(内部自动订阅事件,第 5 站细讲) |
| 41 | `Connect()`:设备上线(模拟设备 50ms 后变 Online;真串口设备这一步是打开 COM 口)。**注意:只连接,还没开始产数**——产数要等用户点"启动采集"按钮 |

```csharp
42          // 4) 记录审计:系统启动(由谁登录的)
43          var audit = Services.GetRequiredService<AuditService>();
44          var current = Services.GetRequiredService<ICurrentUserService>();
45          await audit.LogSystemAsync("app.startup", detail: $"by {current.Username}");
```

| 行 | 讲解 |
|---|---|
| 45 | 工业术语:**审计日志(Audit Log)**——"谁在什么时候干了什么"的法律级记录,医药/食品行业是硬性合规要求。大白话:监控室的登记本。`await` 等写完再走(启动日志必须落盘才算启动完成)。`$"..."` = 字符串插值,等同 JS 的模板字符串 `` `by ${name}` `` |

```csharp
47          // 5) 用 DI 解析出的服务构造 ViewModel,再交给 MainWindow 作为 DataContext
48          var vm = new MainViewModel(Services);
49          var window = new MainWindow { DataContext = vm };
50          window.Closed += async (_, _) =>
51          {
52              await audit.LogSystemAsync("app.shutdown", detail: $"by {current.Username}");
53              Services.Dispose();
54          };
55          window.Show();
56      }
57  }
```

| 行 | 讲解 |
|---|---|
| 48 | 造主窗口的"总指挥" VM(第 8 站),把整个容器递给它,它自己去点菜 |
| 49 | 🔧 `{ DataContext = vm }` = 对象初始化器(一行干两件事:new + 赋值)。**`DataContext = vm` 是 WPF 数据绑定的总开关**:XAML 里所有 `{Binding XXX}` 都是从 vm 的属性里找 XXX。相当于 Vue 的 `:data="vm"` |
| 50-54 | 🧠 订阅**窗口关闭事件**做善后:再写一条关机审计日志 + `Services.Dispose()` 销毁容器(容器销毁时会连带释放它管理的所有单例——数据库连接、后台泵、定时器都在这一刻被收走)。`(_, _)` = 两个参数都用不上,丢弃参数的新语法。🧠 为什么用 lambda 而不写方法:逻辑只有 3 行且只此一处用 |
| 55 | 🔧 前面说过:没写 StartupUri,就必须手动 `Show()`,否则窗口不出现、程序秒退 |

### 🎤 面试一句话(第一站)

> "程序的组合根在 App.OnStartup:先 Build DI 容器,模态登录窗拦截未授权访问,然后注册设备到采集管道、Connect 上线,写启动审计,最后构造 MainViewModel 作为 MainWindow 的 DataContext 显示主界面;窗口 Closed 时写关机审计并 Dispose 容器,保证后台资源全部释放。"

### ✂️ 自己改一处(5 分钟)

把 41 行 `device.Connect();` 删掉再跑:程序照样开、照样能登录——但点位表永远是空的、设备状态是 Offline。体会:**连接 ≠ 采集中**。

---

## 第二站 · 数据的形状:SensorPoint.cs(27 行,全项目最短)

> 文件:`src/DaqMonitor.Core/Models/SensorPoint.cs`

> 📚 **对应讲义**:[M0](M0_每日讲义_深度版.md)(Day 2 领域模型——SensorPoint/DeviceState/AlarmLevel 就在这天落地)· [C# 语法速查 §struct](CSharp语法速查_前端视角.md)(值类型 vs 引用类型,JS 对照)· [📦 前置类型定义](前置类型定义_学员粘贴版.md)(本站类型的"字典")

### 🎯 一句话

**整个项目搬运的每一份数据,都是这张"快递单":哪个点位、值多少、设备状态、什么时间采的。** 先认识它,后面每一站都在传它。

### 工业术语扫盲(这 4 个词后面天天见)

| 术语 | 大白话 | 前端类比 |
|---|---|---|
| **点位(Point/Tag)** | 一个被监控的测量点,如"1 号炉温度"。每个点位有唯一编号 | 一条具名 state,如 `state.sensors.temp1` |
| **采样(Sampling)** | 隔一段时间读一次数 | `setInterval` 定时器 |
| **上位机** | 你写的这台电脑上的监控软件(老板/工程师看的那端) | 管理后台 |
| **下位机** | 车间里真正接传感器的单片机/PLC(干活的那端) | 埋点 SDK / 边缘设备 |

### 逐行讲解

```csharp
01  namespace DaqMonitor.Core.Models;      // 🔧 Models 文件夹放"数据的长相"
02
03  /// <summary>设备状态(工业设备生命周期)</summary>
04  public enum DeviceState { Offline, Connecting, Online }
```

| 行 | 讲解 |
|---|---|
| 04 | 🔧 `enum`(枚举)= 一组带名字的整数。**设备生命周期三段论**是工业通用模型:离线(没插电/断线)→ 连接中(握手/拨号中)→ 在线(能收发了)。没有"Dead"——工业设备永远可能回来,只说 Offline。💼 面试问"设备状态怎么设计",答这三态 + 每次操作前先查状态 |

```csharp
06  /// <summary>报警级别</summary>
07  public enum AlarmLevel { Normal, Warning, Critical }
```

| 行 | 讲解 |
|---|---|
| 07 | 🧠 报警三级制,颜色遵循 **IEC 60073 国际标准**(工业人机界面颜色规范):绿=正常、黄=警告(还能跑但要盯)、红=严重(要停机处理)。为什么分级?**车间的人扫一眼颜色就知道要不要跑过去**——报警系统的本质是给人的,不是给机器的 |

```csharp
09  /// <summary>一个传感器读数点位。值类型 struct,适合高频小数据。</summary>
12  public struct SensorPoint
13  {
14      public int Id;              // 点位编号(业务身份证,如 1 = 一号温度点)
15      public double Value;        // 读数(温度/压力/流量的物理量)
16      public DeviceState State;   // 采样那一刻设备的状态
17      /// <summary>采样时间戳。统一由采集源打,下游共用。</summary>
18      public DateTime Timestamp;
19  }
```

| 行 | 讲解 |
|---|---|
| 12 | 🧠 `struct` vs `class` 是 C# 的**值类型 vs 引用类型**之分:struct 赋值 = **复印一份**(两份独立),class 赋值 = **递张名片**(两个名字指向同一个东西)。等同 JS 的原始值 vs 对象 |
| 14-18 | 🧠 为什么带 `Timestamp`:工业数据的铁律是**"值 + 时间"不可分割**——"温度 105"没有时间戳毫无意义。注意注释:**时间统一由采集源打**(设备举手那一刻),不是由存储层/界面层打——不然一个数据过三道手,三个层各打一个时间,历史曲线全乱套。💼 这叫"单一事实来源"(Single Source of Truth) |
| 为什么用 struct 不用 class | 🧠 100Hz 采样 × 多设备 = 每秒几百个对象。class 每个都要堆分配+垃圾回收(GC)兜着,struct 在栈上生灭,**零 GC 压力**。这是高频采集场景的标准选择。💼 面试高频:"为什么 SensorPoint 是 struct?"——答"高频小数据,避免堆分配和 GC 压力"就及格 |

```csharp
21  /// <summary>报警记录(Day 2 练习落地)</summary>
22  public struct Alarm
23  {
24      public int PointId;       // 哪个点位报警
25      public AlarmLevel Level;  // 什么级别
26      public double Value;      // 报警瞬间的值
27  }
```

| 行 | 讲解 |
|---|---|
| 22-27 | 报警发生瞬间的快照。**为什么记录 Value**:温度报警 3 秒后回落,查日志时必须知道"当时冲到了多少"——报警日志的价值就在"案发瞬间" |

### 🎤 面试一句话(第二站)

> "核心领域模型是 SensorPoint 结构体:点位 Id + 值 + 设备状态 + 采样时间戳,时间戳统一由采集源打保证一致性;用 struct 是因为高频采样下避免 GC 压力。设备状态是 Offline/Connecting/Online 三态枚举,报警分 Warning/Critical 两级,颜色遵循 IEC 60073。"

### ✂️ 自己改一处

给 `SensorPoint` 加一个字段 `public string Unit;`(单位,如 "℃")。然后 `dotnet build` —— 会发现**编译居然全过**:struct 加字段不破坏任何调用方。体会值类型"拿走即复印"的扩展安全性。

---

## 第三站 · 设备的规矩:IDevice.cs(59 行,全项目的"宪法")

> 文件:`src/DaqMonitor.Core/Devices/IDevice.cs`

> 📚 **对应讲义**:[M0](M0_每日讲义_深度版.md)(Day 5 IDevice 项目任务 + Day 6 事件机制——本站的两个地基)· [C# 语法速查 §7](CSharp语法速查_前端视角.md)(event/delegate/接口,前端 EventEmitter 对照)

### 🎯 一句话

**这是一份"设备合同":任何设备(模拟的/串口的/PLC 的/网口的)想来这个项目干活,必须会这 6 个动作 + 1 个广播。** 上层代码只认合同不认设备——7 种设备随便换,上层一行不改。

### 前端类比秒懂

| C# 概念 | 前端类比 | 说明 |
|---|---|---|
| `interface IDevice` | TypeScript 的 `interface Device` | 纯形状声明,不含实现 |
| `abstract class DeviceBase` | 组件基类(如 React 的抽象 Component) | 公共逻辑写一次,子类继承 |
| `event DataReceived` | `EventEmitter` / `emitter.on('data')` | 设备广播,谁关心谁订阅 |
| 实现类必须 override | 子类必须实现抽象方法 | 同 |

### 文件三段式:事件参数 → 接口 → 基类

**第一段:广播的内容单(1-12 行)**

```csharp
05  /// <summary>数据到达事件参数</summary>
06  public class DataEventArgs : EventArgs
07  {
08      public int PointId { get; init; }
09      public double Value { get; init; }
11      /// <summary>采样时间戳,由采集源统一打,下游共用。</summary>
12      public DateTime Timestamp { get; init; } = DateTime.Now;
13  }
```

| 行 | 讲解 |
|---|---|
| 06 | 🔧 **事件参数类惯例**:继承 `EventArgs`(NET 的祖宗规矩,现在非必须但行业习惯),类名以 EventArgs 结尾。"参数"装的是广播内容:谁(PointId)、多少(Value)、何时(Timestamp) |
| 08 | 🔧 `{ get; init; }` = 属性:外部可读、**只能在构造时写一次**(init = initialize only),之后不可改。比 `public int PointId;` 裸字段正规:能挂断点、能数据绑定。事件参数不可变是防御性设计——广播出去后谁也别想偷改 |
| 12 | `= DateTime.Now` = **默认值**:不传时间就取当前。防御性兜底 |

**第二段:合同本体(14-31 行)**

```csharp
18  public interface IDevice
19  {
20      int Id { get; }               // 设备编号(整型身份证)
21      string Name { get; }          // 设备名(如 "Sim-01",给人看的)
22      DeviceState State { get; }    // 当前状态(三态枚举)
23
24      void Connect();               // 动作1:连接(串口设备=打开COM口,网口=TCP握手)
25      void Disconnect();            // 动作2:断开(释放资源,状态回 Offline)
26      double Read(int addr);        // 动作3:主动读一个地址的值(问一句答一句)
27      void Write(int addr, double value);  // 动作4:主动写(下发设定值给设备)
28
30      /// <summary>采集层拿到数据后单向通知订阅方(UI刷新用)</summary>
31      event EventHandler<DataEventArgs>? DataReceived;
32  }
```

| 行 | 讲解 |
|---|---|
| 18 | 🔧 `interface` 只写签名不写实现——**合同只规定会什么,不管怎么干** |
| 24-27 | 🧠 为什么是这 4 个动作?对应设备的物理生命周期:**连上它 → 断开它 → 问它 → 命令它**。任何工业设备(温控仪/PLC/变频器)都逃不出这 4 个动词。`Read(addr)` 的 addr 工业术语叫**地址/寄存器地址**——设备内部的一排格子,每格有编号,读 3 号格就是 `Read(3)` |
| 31 | 🧠 **全项目最重要的一个成员**。`event` 关键字 = 受控的广播台:外部只能 `+=`(订阅)和 `-=`(退订),**不能直接触发也不能偷看订阅名单**。`?` 表示可为 null(没人订阅时就是 null)。为什么要事件而不是让上层来轮询读?——**推模式 vs 拉模式**:设备有数据才喊一嗓子(推),比上层傻问"有了吗?有了吗?"(拉)高效且实时。💼 必考 |

> 💼 **面试连环追问**:"为什么不直接让 UI 调 `device.Read()`?"
> 答:①Read 是一问一答的同步调用,占着线程;设备主动推(DataReceived)才能支撑 100Hz 高频 ②解耦:UI 订阅事件,不认识具体设备类,换设备 UI 不改 ③多播:一个事件多个订阅方(管道、诊断、测试)同时听,互不影响。

**第三段:基类——公共逻辑只写一次(33-59 行)**

```csharp
37  public abstract class DeviceBase : IDevice
38  {
39      public int Id { get; }                        // 只读:身份证出生就定死
40      public string Name { get; }
41      public DeviceState State { get; protected set; } = DeviceState.Offline;
42
43      public event EventHandler<DataEventArgs>? DataReceived;
44
45      protected DeviceBase(int id, string name)     // 🔧 构造函数:子类 new 时必须报上名号
46      {
47          Id = id;
48          Name = name;
49      }
50
51      public abstract void Connect();               // abstract:子类必须实现(每设备连法不同)
52      public abstract void Disconnect();
53      public abstract double Read(int addr);
54      public abstract void Write(int addr, double value);
55
56      /// <summary>采集线程拿到数据后调用,单向推给订阅方</summary>
57      protected void RaiseData(int pointId, double value)
58          => DataReceived?.Invoke(this, new DataEventArgs { PointId = pointId, Value = value, Timestamp = DateTime.Now });
59  }
```

| 行 | 讲解 |
|---|---|
| 37 | 🔧 `abstract`(抽象类)= **半成品父类**:自己不能被 new(你不能 new 一个"抽象设备"),只能被继承。` : IDevice` = 签了合同,本类负责实现合同 |
| 39-40 | 🧠 `{ get; }` 只读属性 + 构造函数赋值 = **出生定死模式**:设备编号/名字创建后不可变(否则运行中改了名,日志和 UI 对不上账) |
| 41 | 🧠 **`protected set`** 是精妙处:State 外部**只读**,但**子类可写**(串口设备连上 COM 口时自己把状态改成 Online)。外部想改状态?不行——状态只能随设备真实动作变化,不能被外人伪造。💼 面试问"protected 什么时候用",这就是标准答案 |
| 43 | 事件在基类声明一次,7 个子类自动拥有(复用) |
| 45-49 | 🔧 固定的构造模板:存下 id/name。子类构造函数用 `: base(id, name)` 上交(第 4 站见) |
| 51-54 | 🔧 `abstract` 方法 = **父类立规矩不干活,子类必须干**:每台设备怎么连/怎么读完全不同,父类没法统一,只能立条款 |
| 57-58 | 🧠 **RaiseData 是全项目的"广播按钮"**,只有 protected(子类内部按):子类采到数据就按一下,外面想伪造数据广播?没门。`=>` 单表达式方法体(等同 JS 箭头函数的一行写法)。`?.Invoke` = **有人订阅才广播,没人订阅就跳过**(不然 null 引用崩溃)——这是 C# 事件的标准触发姿势,背下来 |

### 🔬 掰开揉碎:接口到底约束谁?(新手最后一片迷雾)

> 典型困惑:"接口规定好了必须是这些属性方法——但**设备不是有自己的实现方式吗?它凭什么遵循我们的接口?**"
> 一句话答案:**它不遵循。接口约束的从来不是设备(硬件),是我们自己雇的"翻译官"(适配类)。**

**先纠正脑子里的画面**:

```
❌ 错误画面:  PLC/串口设备 ——遵循——> IDevice 接口   (机器怎么会遵守 C# 合同?)

✅ 真实画面:
  上层(管道/UI/报警) ——只认识——> IDevice 这份"合同"
                                      ↑ 签合同的是"人"
                        ┌─────────────┼──────────────┐
                  SimulatedDevice  SerialDevice   PlcDevice
                   (翻译官1)       (翻译官2)      (翻译官3)
                   肚子里:          肚子里:        肚子里:
                   随机数发生器      SerialPort     S7.Net 协议
                                        ↓               ↓
                                     COM 口字节流    TCP+S7 报文
                                   (设备想说啥说啥,没人约束它)
```

**每个设备配一个翻译官类,翻译官是我们自己写的**:对外(对我们系统)说 IDevice 的语言,对内(对硬件)说设备的语言。设备"自己的实现方式"一个字没丢——全被装进各自翻译官的**肚子**里。

**三个事实,用本项目代码验证**:

**事实 1:设备的实现方式都在翻译官肚子里,谁也没"统一"谁**

```csharp
// SimulatedDevice.Connect(第4站):肚子里是"睡50ms装样子"
public override void Connect() { State = DeviceState.Connecting; Thread.Sleep(50); State = DeviceState.Online; }

// SerialDevice.Connect(M1):肚子里是"打开COM口"
public override void Connect() { _port = new SerialPort("COM3", 9600); _port.Open(); }

// PlcDevice.Connect(M3):肚子里是"S7协议连PLC"
public override void Connect() { _plc = new Plc(CpuType.S71200, "192.168.1.10"); _plc.Open(); }
```

三个 Connect 内部干的事天差地别——接口没有、也不可能规定"怎么连",只规定"你必须**有**一个 Connect"。**统一的只有签名,内部随便。**

**事实 2:"遵循"不是讲礼貌,是编译器拿枪逼着**

```csharp
public class PlcDevice : IDevice { }   // ❌ 编译失败:缺 Connect/Read/Write/DataReceived
```

写上 `: IDevice` 却少实现任何一个成员,编译直接红。所以"每个设备都遵循接口"的真相是:我们写的类**过不了编译这关**。

**事实 3:上层代码里,具体设备的名字一次都没出现**

```csharp
// App.xaml.cs 38 行:      var device = Services.GetRequiredService<IDevice>();
// MainViewModel 55 行:    private readonly IDevice _device;
// AcquisitionPipeline 34: public void Register(IDevice device)
```

全项目写死 `SimulatedDevice` 的地方**只有一处**——Bootstrapper 110 行(第 9 站,注册点=交换点)。

**那"换设备不用动代码"到底怎么回事?(诚实账)**

改 Bootstrapper 110 行那一行注册,`_device.Read(1)` 这行调用在**运行时自动执行 PlcDevice 肚子里的 S7 协议代码**——同一个调用点,跑哪段由变量里实际装的**对象**决定,这叫**多态**。前端类比:`storage.get("k")` 这一行,跑 localStorage 还是 Redis 版,看你注入的是谁。

| 不用动 ✅ | 必须做 🔧 |
|---|---|
| UI / 管道 / 报警 / 存储——全部上层代码 | **写**一个新的翻译官类(PlcDevice) |
| 85 个测试 | 改 Bootstrapper 一行注册 |

> 措辞精确性(面试这样说反而加分):**"零修改" ≠ "零工作"**。新设备要**新增**一个类,但**不改**任何老代码——这正是开闭原则的原话:*对扩展开放,对修改关闭*。上位机工程师日常一半的活,就是给各种不讲道理的设备写翻译官。

**❌✅ 三个常见误解,一次纠正**:

| ❌ 误解 | ✅ 正解 |
|---|---|
| 接口是约束**设备**的 | 接口约束的是**我们自己写的适配类**;设备该咋样还咋样 |
| 实现了接口,各设备的实现就"统一"了 | 统一的只有**签名**(有 Connect);**内部**一个 Sleep 一个开串口一个走 S7,互不干涉 |
| 换设备 = 什么都不用干 | 换设备 = 新写一个适配类 + 改一行注册;省掉的是"改上层",不是"写适配" |

💼 **连环追问预埋**:"SimulatedDevice.Start(开始产数)这种特有功能,为什么不放进接口?"——接口只收"上层用得到的共性";Start 是模拟设备特有的,放接口会逼 7 个设备全实现一个用不上的方法。上层需要时用模式匹配按需领取:`if (_device is SimulatedDevice sd) sd.Start(...)`(第 8 站 237 行)。

> 一句话收尾:**接口不是给设备戴的笼头,是给我们自己定的"插座国标"——插头(适配类)各厂各样,插座(上层代码)全国统一。**

### 🧠 为什么这么设计(架构题,面试 15K 分水岭)

**问题**:7 种设备(模拟/串口/Modbus/PLC/TCP/CAN/USB-HID)底层原理天差地别——串口读 COM 口字节流,PLC 走 S7 协议,CAN 是广播总线。如果上层(管道/UI)直接 `new SerialDevice()`,换设备 = 上层全部重写。

**方案**:上层只认 `IDevice` 合同。设备细节被锁死在实现类内部。

**收益**(面试照这个顺序说):
1. **可替换**:Bootstrapper 里改一行注册,SimulatedDevice 换成 PlcDevice,上层零改动(第 9 站眼见为实)
2. **可测试**:测试里塞一个假设备,不需要真 PLC 就能测整条链路(85 个测试全绿的根基)
3. **可扩展**:明天来个新设备(比如串口服务器),写个新类实现合同,老代码一行不动——**对扩展开放,对修改关闭**(开闭原则,OCP)

### 🎤 面试一句话(第三站)

> "所有设备实现统一的 IDevice 接口:连接/断开/读/写四个动作加一个 DataReceived 事件;DeviceBase 抽象基类固化了只读的身份属性、protected 的状态机和唯一的 RaiseData 广播入口。上层只依赖接口,设备可插拔、可 mock、可扩展,是典型的开闭原则落地。"

### ✂️ 自己改一处

把 41 行的 `protected set` 改成 `set`,`dotnet build` 后在 MainViewModel 里试着写 `_device.State = DeviceState.Online;` —— 能编译通过,但这是**架构污染**:UI 居然能伪造设备状态。改回 protected,体会这道锁的意义。

---

## 第四站 · 第一个设备:SimulatedDevice.cs(80 行)

> 文件:`src/DaqMonitor.Core/Devices/SimulatedDevice.cs`

> 📚 **对应讲义**:[M0](M0_每日讲义_深度版.md)(Day 7 并发——Task.Run/CancellationToken/后台循环全在这天)· [C# 语法速查 §8](CSharp语法速查_前端视角.md)(async/await/Task)

### 🎯 一句话

**一个"假温度计":后台线程每 100ms 随机造几个数,举手广播。** 它是全项目的数据源头——没有真实硬件时,它让整条流水线有水可流。

### 逐行讲解

```csharp
01  using DaqMonitor.Core.Models;      // 用到 SensorPoint 相关的 DeviceState
02  using System.Threading;            // 用到 CancellationTokenSource
03
04  namespace DaqMonitor.Core.Devices;
05
14  public class SimulatedDevice : DeviceBase     // 继承基类 = 自动实现 IDevice 合同
15  {
16      private readonly int[] _pointIds;         // 这个设备管哪几个点位(如 {1,2,3})
17      private readonly Random _rnd = new();     // 随机数发生器(造数据用)
18      private CancellationTokenSource? _cts;    // 取消令牌源:停采集的"红色按钮"
19      private Task? _loop;                      // 后台循环任务本体
```

| 行 | 讲解 |
|---|---|
| 14 | 🧠 只继承 DeviceBase,不用再写 `: IDevice`——基类已签合同,孙辈自动有 |
| 16 | 🔧 `private readonly` + 下划线开头命名(`_pointIds`)= C# 私有字段标准姿势:构造后不可变,防止中途被偷换 |
| 17 | `new()` = 目标类型推断(等同 `new Random()`,C# 9 起右边类型可省)。一个 Random 实例反复用,别在循环里 new(老 .NET 的 Random 短时间连 new 会出一样的种子) |
| 18-19 | 🧠 这两个字段是**后台任务的操纵杆**:`_cts` 负责"叫停"、`_loop` 是任务本体(用它 Wait 等退出)。成对出现是 C# 后台循环的标配 |

```csharp
21      public SimulatedDevice(int id, string name, params int[] pointIds)
22          : base(id, name)
23          => _pointIds = pointIds.Length > 0 ? pointIds : new[] { 1 };
```

| 行 | 讲解 |
|---|---|
| 21 | 🔧 `params` = 可变参数:调用方可以 `new SimulatedDevice(1,"Sim-01", 1, 2, 3)` 随便塞几个点位,它们自动打包成数组 |
| 22 | 🔧 `: base(id, name)` = 把身份信息上交给父类构造函数(第 3 站立的规矩在这兑现) |
| 23 | 🧠 三元表达式兜底:一个点位都不传?默认管 1 号点位。防御性默认值,防止空数组导致"设备活着但永不产数"的假在线 |

```csharp
25      public override void Connect()
26      {
27          State = DeviceState.Connecting;     // 先报"正在连接"
28          Thread.Sleep(50);                   // 模拟握手耗时(真设备=拨号/打开串口的时间)
29          State = DeviceState.Online;         // 变在线
30      }
```

| 行 | 讲解 |
|---|---|
| 25-30 | 🧠 **三态状态机的教科书演示**:Connecting →(耗时动作)→ Online。为什么要中间态?真设备连接要几百毫秒到几秒,UI 这个瞬间显示黄色转圈(第 8 站的 StatusDot 控件),操作工知道"它在连,不是死了"。`Thread.Sleep(50)` 是故意的——模拟真实世界没有瞬时的连接 |

```csharp
32      public override void Disconnect()
33      {
34          Stop();                            // 先停掉产数循环(下面讲)
35          State = DeviceState.Offline;       // 再报离线
36      }
```

| 行 | 讲解 |
|---|---|
| 32-36 | 🧠 断开的正确顺序:**先停业务再改状态**。反过来的话:状态已经 Offline 了,后台循环还在产数,UI 上出现"离线设备刷数据"的灵异事件 |

```csharp
38      public override double Read(int addr) => Math.Round(_rnd.NextDouble() * 100, 2);
40      public override void Write(int addr, double value) { /* 模拟设备只读,忽略写 */ }
```

| 行 | 讲解 |
|---|---|
| 38 | 合同要求的 Read:返回 0~100 的两位小数随机数(`Math.Round(x, 2)` 保留两位)。真设备这里是"发查询帧→等应答帧→解析出值" |
| 40 | 合同要求的 Write:模拟设备没有可写的东西,**空实现也是实现**——合同必须全兑现,哪怕身体是空的。真设备这里对应"下发设定值" |

```csharp
46      public void Start(TimeSpan interval)          // ← 注意:这不是合同里的方法,是模拟设备独有的扩展
47      {
48          if (_loop is not null) return;             // 已经在跑?直接返回(防重复启动)
49          _cts = new CancellationTokenSource();      // 发一只新的"取消令牌"
50          var token = _cts.Token;                    // 拿令牌本体
51          _loop = Task.Run(async () =>
52          {
53              try
54              {
55                  while (!token.IsCancellationRequested)     // 只要没人按红色按钮就一直转
56                  {
57                      foreach (var pid in _pointIds)         // 管的每个点位轮流产一个数
58                      {
59                          var v = _rnd.NextDouble() < 0.1
60                              ? 95 + _rnd.NextDouble() * 25   // 10% 概率:95~120(故意越界!)
61                              : 20 + _rnd.NextDouble() * 70;  // 90% 概率:20~90(正常)
62                          RaiseData(pid, Math.Round(v, 2));   // 按广播按钮 → 数据上路!
63                      }
64                      await Task.Delay(interval, token);     // 睡 interval(默认100ms),睡梦中也能被叫醒
65                  }
66              }
67              catch (OperationCanceledException) { /* 正常退出 */ }
68          }, token);
69      }
```

这是全文件的心脏,逐行掰开:

| 行 | 讲解 |
|---|---|
| 46 | 🧠 Start/Stop 不在 IDevice 合同里——**不是每台设备都需要"开始产数"**(真 PLC 的数据是它自己一直在变的,不需要你启动)。扩展方法放子类,正是接口设计的分寸:合同只放共性 |
| 48 | 🧠 幂等守卫:连点两次"启动"不会开两个循环(两个循环 = 数据翻倍 + 资源泄漏)。**写后台任务第一件事想防重复** |
| 49-50 | 🔧 **CancellationTokenSource 套路**(背下来):`CancellationTokenSource` 是发令牌的人,`Token` 是令牌本体。把令牌传给后台任务,外界喊 `_cts.Cancel()` 时,任务里的 `token.IsCancellationRequested` 变 true、正在 `Task.Delay` 的会立刻抛 `OperationCanceledException` 被叫醒。类比:给夜班保安一个对讲机(token),值不动了主管(cts)随时喊话收工 |
| 51 | 🔧 `Task.Run(...)` = 把一段活丢给线程池后台干,**这行立刻返回**,主线程(UI)绝不等待。**一切后台化的起点** |
| 55 | 🔧 `while (!token.IsCancellationRequested)` = 后台循环标准写法:循环条件第一查"要不要停" |
| 59-61 | 🧠 **10% 概率故意越界**:正常区间 20~90,报警阈值是 100(第 9 站会看到),10% 概率冲到 95~120 → 演示时每隔十几秒能看到一次报警变红。**测试数据的剧本设计**:随机但不失控,想演示什么就让它发生什么 |
| 62 | 🧠 `RaiseData` = 第 3 站基类里的广播按钮。数据从这里正式上路,接下来的一切(排队→入库→报警→上屏)都由这一按触发。**断点打在这行,能看见整个项目的血液源头** |
| 64 | 🔧 `Task.Delay(interval, token)` ≠ `Thread.Sleep`:Delay 是**异步睡**(线程还给线程池,睡完回来接着干),Sleep 是**抱着线程睡**(浪费)。带 token = 睡梦中被 Cancel 立刻醒。**UI 卡不卡,就看你有没有把 Sleep 误用在异步上下文** |
| 67 | 🔧 取消是靠抛异常实现的(不优雅但高效),所以必须有这个 catch 收尾,**吞掉它 = 优雅退场**。不 catch 会怎样?Task.Run 的异常被存进任务对象,没人观察就是隐患 |

```csharp
72      public void Stop()
73      {
74          _cts?.Cancel();                              // 按红色按钮:发取消信号
75          try { _loop?.Wait(500); } catch { /* 忽略 */ }  // 最多等它 500ms 收尾
76          _cts?.Dispose();                             // 释放令牌源
77          _cts = null;
78          _loop = null;                                // 两个字段清空 → 下次 Start 能重新启动
79      }
80  }
```

| 行 | 讲解 |
|---|---|
| 74 | `?.` = 是 null 就跳过(还没 Start 过就 Stop,不崩) |
| 75 | 🧠 `Wait(500)` = **给后台任务 500ms 体面收尾的时间**(把当轮数据处理完)。带超时是防死等——万一循环卡死,最多等半秒,绝不让"停止采集"按钮把整个 UI 拖挂 |
| 76-78 | 🧠 释放 + 清空:`Dispose` 释放资源,置 null 让 Start 的幂等守卫(48 行)重新放行。**Stop 和 Start 必须严格对称**,否则第二次启动失灵 |

### 🎤 面试一句话(第四站)

> "模拟设备用 Task.Run 起后台循环,每 100ms 给所有点位产数并 RaiseData 广播;用 CancellationTokenSource 控制启停,Stop 时 Cancel 后最多等 500ms 收尾,Start 有幂等守卫防重复启动;数据故意设计 10% 越界概率用于演示报警链路。"

### ✂️ 自己改一处

61 行 `20 + _rnd.NextDouble() * 70`(正常值上限 90)改成 `* 95` ——正常值就能摸到 95+,报警会更频繁。跑起来直观感受"阈值与数据分布的关系"。

---

## 第五站 · 大动脉:AcquisitionPipeline.cs(80 行,★全项目心脏)

> 文件:`src/DaqMonitor.Core/Acquisition/AcquisitionPipeline.cs`

> 📚 **对应讲义**:[M0](M0_每日讲义_深度版.md)(Day 7 Channel 并发闭环——本站的"官方出身")· [M5 · 实时可视化](M5_实时可视化_深度版.md)("逐事件刷 UI 的病"详版)· [M7 · OPC UA/MQTT](M7_OPCUA_MQTT_深度版.md)("逐事件上云的病"详版)· [M9](M9_工程素养_测试DI容错_深度版.md)(统一采集管道的架构视角)

### 🎯 一句话

**所有设备的数据先扔进一条传送带(Channel 队列),后台慢慢消费,攒够一批(500 条)或到点(200ms)整批放行。** 它解决一个致命问题:设备产数太快太猛,直接怼到 UI 会把界面卡死。

### 🔬 掰开揉碎:为什么必须有它(先懂病,再懂药)

**病**:6 台设备 × 100Hz = 每秒 600 次数据到达。如果每次到达都直接刷 UI:
- WPF 规定**只有 UI 线程能改界面**,所以每次都要 `Dispatcher.Invoke` 挤 UI 线程——600 次/秒的挤,UI 线程只干活不喘气,界面假死
- 即使不卡死,**人眼每秒最多感知 ~10 次变化**,600 次刷新里 590 次是白白烧 CPU

**药**:生产者(设备)与消费者(UI)之间加一个**缓冲区 + 节流阀**:
1. 数据到达 → 只做一件极轻的事:进队(TryWrite,纳秒级)
2. 后台消费者慢慢攒
3. **每 200ms 才整批放行一次** → UI 每秒只被打扰 5 次,每次一批

**前端类比**:这不就是你们熟悉的**防抖(debounce)+ 消息队列**吗?Vue 里搜索框 input 事件不直接发请求,先攒 300ms;高频滚动事件用 rAF 合并。**工业版换了个名字:Channel + 批量刷新**。💼 面试被问"Channel 像前端什么",答:一个自带线程安全的生产者-消费者队列,像 RxJS 的 Subject 缓冲 + 防抖落地版。

**工业术语**:**背压(Backpressure)**——当消费者跟不上生产者时,中间的缓冲层保护下游不被冲垮。Channel 就是背压的落地。

### 逐行讲解

```csharp
12  public sealed class AcquisitionPipeline : IDisposable
13  {
14      private readonly Channel<SensorPoint> _channel = Channel.CreateUnbounded<SensorPoint>();
15      private readonly List<IDevice> _devices = new();
16      private readonly CancellationTokenSource _cts = new();
17      private readonly Timer _flushTimer;
18      private readonly object _gate = new();
19      private List<SensorPoint> _pending = new();
20      private readonly int _maxBatch;
```

| 行 | 讲解 |
|---|---|
| 12 | 🔧 `sealed`(密封类)= 禁止继承。这个类是终态设计,没有"某某管道子类"的需求,sealed 还能让编译器优化。🔧 `IDisposable` = 签下"我会自己打扫卫生"的协议,using/容器销毁时自动调 Dispose(见 72 行) |
| 14 | 🧠 **主角登场**。`Channel<SensorPoint>` = 只能装 SensorPoint 的线程安全队列。`CreateUnbounded` = 无限容量(永不拒收)。为什么敢无限?采集数据是小结构体,内存涨速远低于消费速度,瓶颈不在队列长度;真要极端,上层设备早该限流了。💼 另一种选择 `CreateBounded` = 有界队列(满了丢旧或等待),音频流/网络流用它 |
| 15 | 登记挂上来的设备(Dispose 时好逐个退订,见 77 行) |
| 16 | 又见取消令牌(第 4 站讲过):控制后台消费循环的生死 |
| 17 | 🔧 定时器:`System.Threading.Timer`,到点自动回调 Flush。注意它回调在**线程池线程**,不是 UI 线程 |
| 18-19 | 🧠 **_gate + lock 是手工锁**:一个专用的锁对象(`_gate`)配一个共享的缓冲列表(`_pending`)。为什么需要锁往下看,这里先记:**跨线程读写同一个 List,必须锁** |
| 20 | 攒批上限(默认 500):攒到 500 条不等定时器,立刻放行 |

```csharp
22      /// <summary>批量就绪事件:在后台线程触发,UI 订阅方需自行 Dispatcher 回 UI 线程。</summary>
23      public event EventHandler<IReadOnlyList<SensorPoint>>? BatchReady;
24      public event EventHandler<Exception>? Error;
```

| 行 | 讲解 |
|---|---|
| 23 | 🧠 **对上层的出口**:一批数据凑齐,广播出去(UI 的 MainViewModel 订阅它,第 8 站接上)。注意注释的警告:**这个事件在后台线程触发**——订阅方拿到的数据不能直接摸 UI,必须自己 Dispatcher 切线程。为什么不在管道里切?**Core 层根本不知道 WPF 的存在**(依赖纪律!),切线程是 UI 层的事 |
| 24 | 后台消费循环万一崩了,通过它上报而不是无声死亡。异常也要广播——"静默失败"是工业软件大忌 |

```csharp
26      public AcquisitionPipeline(TimeSpan flushInterval, int maxBatch = 500)
27      {
28          _maxBatch = maxBatch;
29          _flushTimer = new Timer(_ => Flush(), null, flushInterval, flushInterval);
30          _ = ConsumeAsync();
31      }
```

| 行 | 讲解 |
|---|---|
| 29 | 🔧 Timer 四件套含义:`_ => Flush()` 是到点干的事(丢弃定时器参数);`null` = 立即开始计时?不——第 3 参是"多久后**第一次**触发"(null=不立即触发),第 4 参是"之后**每隔**多久重复"。所以这行 = 每 flushInterval 毫秒(生产配置 200ms)调一次 Flush。**丢弃参数 `_` 是新语法,等同 `delegate(object o){...}` 的简写** |
| 30 | 🔧 `_ = xxx` = **故意丢弃**。ConsumeAsync 返回 Task,这里明确表态"我知道它是异步的,我不等它,它的异常它自己处理(内部有 try-catch)"。下划线赋值是给编译器和读代码的人一个交代:不是忘了 await,是有意的。💼 面试聊"fire-and-forget"的规范做法 |

```csharp
33      /// <summary>注册一个设备:自动订阅它的 DataReceived,把点塞进缓冲。</summary>
34      public void Register(IDevice device)
35      {
36          device.DataReceived += OnPoint;      // 设备每广播一次,OnPoint 被调一次
37          _devices.Add(device);
38      }
```

| 行 | 讲解 |
|---|---|
| 34-38 | 🧠 **管道与设备的唯一接缝**:第 1 站 App.xaml.cs 40 行 `pipeline.Register(device)` 走到这里。`+=` 订阅后,设备与管道从此联动。登记设备本体是为了 Dispose 时能 `-=` 退订(见 77 行)——**订阅不退 = 内存泄漏 + 僵尸回调**,事件订阅必须成对,这是 C# event 的纪律 |

```csharp
40      private void OnPoint(object? sender, DataEventArgs e)
41          => _channel.Writer.TryWrite(new SensorPoint { Id = e.PointId, Value = e.Value, Timestamp = e.Timestamp });
```

| 行 | 讲解 |
|---|---|
| 40-41 | 🧠 **全项目最关键的两行**。设备广播 → 这里被调用 → 立刻把事件参数**翻译成领域模型** SensorPoint(第 2 站那张快递单)→ 塞进 Channel → **完事,立刻返回**。签名是 `object? sender` = 事件回调的标准签名(谁发的+发了什么),sender 用不上。**为什么只做一件事**:这个回调的执行频率 = 数据到达频率(每秒几百次),它每多花 1 微秒,整条链路每秒就多烧几百微秒。重活全推给下游慢慢干——这是**事件回调的铁律:回调越轻,系统越稳** |

```csharp
43      private async Task ConsumeAsync()
44      {
45          try
46          {
47              await foreach (var p in _channel.Reader.ReadAllAsync(_cts.Token))
48              {
49                  List<SensorPoint>? batch = null;
50                  lock (_gate)
51                  {
52                      _pending.Add(p);
53                      if (_pending.Count >= _maxBatch) { batch = _pending; _pending = new(); }
54                  }
55                  if (batch is not null) BatchReady?.Invoke(this, batch);
56              }
57          }
58          catch (OperationCanceledException) { /* 正常退出 */ }
59          catch (Exception ex) { Error?.Invoke(this, ex); }
60      }
```

| 行 | 讲解 |
|---|---|
| 47 | 🔧 **`await foreach` + `ReadAllAsync`** = Channel 消费端标准姿势:队列里有数据就取一条,没数据就**异步等待**(不占线程,数据来了被唤醒),循环往复。带 token = Dispose 时能叫醒它退出 |
| 50-54 | 🧠 **攒批核心**。每取到一条:锁住 → 攒进 `_pending` → 检查是否攒满 500(`_maxBatch`)→ **满则"交卷":把整张 pending 列表交出去,当场换一张新的空列表继续攒**。这个"换列表"手法(call batch = _pending; _pending = new())很精妙:锁内只做引用交接(纳秒级),**真正的批量处理(可能耗时)挪到锁外**——锁内干重活 = 所有线程排队等锁 = 系统瓶颈。💼 面试必考:"为什么 invoke 放锁外面" |
| 55 | 锁外广播批次。`is not null` = C# 7.3+ 的 null 判断新姿势(比 `!= null` 多一层模式匹配能力,这里可读性也好) |
| 58-59 | 🔧 双 catch 分层:**预期中的取消**(正常关机,吞掉)+ **意外异常**(广播上报,绝不静默)。后台循环的异常处理模板 |

> **为什么需要锁?** 两个线程同时摸 `_pending`:消费循环(53 行 Add)和定时器 Flush(64 行也要取走 `_pending`)。不锁的话:消费线程正在 Add 到一半,Flush 线程把列表端走了 → 数据丢一半或集合损坏。**凡是多个线程读写同一个对象,必须锁**——这就是 18 行 `_gate` 存在的意义。

```csharp
62      private void Flush()
63      {
64          List<SensorPoint>? batch = null;
65          lock (_gate)
66          {
67              if (_pending.Count > 0) { batch = _pending; _pending = new(); }
68          }
69          if (batch is not null) BatchReady?.Invoke(this, batch);
70      }
```

| 行 | 讲解 |
|---|---|
| 62-70 | 🧠 **节流阀本体**:每 200ms 被 Timer 调用一次。逻辑与 53 行对称:锁内把攒的列表端走换新的,锁外广播。**有了它,即使数据量小(攒不满 500),也最多 200ms 必放行一次**——两条放行路径(攒满即走/到点必走)取先到者。类比:大巴车"坐满就发车,坐不满 20 分钟也发车" |
| 67 | `Count > 0` 判断:没数据就不广播空批次(广播空批是浪费下游感情) |

```csharp
72      public void Dispose()
73      {
74          _cts.Cancel();                    // 叫醒并终止消费循环
75          _flushTimer.Dispose();            // 停定时器
76          _channel.Writer.TryComplete();    // 封住队列入口:不再收新数据
77          foreach (var d in _devices) d.DataReceived -= OnPoint;   // 逐个退订
78          _cts.Dispose();                   // 释放令牌源
79      }
```

| 行 | 讲解 |
|---|---|
| 72-79 | 🧠 **善后五连,顺序有讲究**:先停消费(74)再停定时器(75),再封入口(76)防新数据进来,然后退订设备(77,Register 的对称操作,38 行登记的清单在此兑现),最后释放令牌(78)。**每个后台资源都有始有终**——7×24 长跑程序不出内存泄漏的秘密就在这一丝不苟的 Dispose。💼 面试:"你的程序怎么保证不泄漏?"答:事件订阅成对、Timer/CTS/Ctl 全 Dispose、由 DI 容器统一调度生命周期 |

### 🧠 为什么这么设计(把这一站想成一句面试答案)

> "100Hz×多设备场景下,逐事件刷 UI 会把 UI 线程打爆(每秒几百次 Dispatcher.Invoke)。我设计了统一采集管道:设备事件回调里只做 Channel.TryWrite 一件纳秒级的事;后台消费者攒批,攒满 500 条或每 200ms 定时整批放行;BatchReady 在后台线程广播,由 UI 层自己 Dispatcher 切线程批量刷新。**事件只入队、重活批处理、锁内只换引用**——这三句就是管道的全部设计哲学。"

### ✂️ 自己改一处(教练计划 L2 的实验)

Bootstrapper 里 `new AcquisitionPipeline(TimeSpan.FromMilliseconds(200))` 改成 `FromMilliseconds(2000)` → 跑起来:点位表 2 秒才跳一次,但**一次跳一大截**——直观理解"批量=延迟换吞吐"。

---

## 第六站 · 数据落库三件套(_SENSOR-record → AppDb → PointStore)

> 文件:`Store/SensorRecord.cs`(51行)+ `Store/AppDbContext.cs`(128行)+ `Store/PointStore.cs`(191行,全项目最长)

> 📚 **对应讲义**:[M4 · 数据持久化](M4_数据持久化_深度版.md)(EF Core/DbContext/双写/SQLite——本站主讲)· [M12 · 多数据库](M12_工程量转换与多数据库_深度版.md)(想升级 SQL Server/MySQL 再看)· [M9](M9_工程素养_测试DI容错_深度版.md)(IDbContextFactory 的 DI 注册)

### 🎯 一句话

**数据要存两层:内存里一份"现在的值"(查询零延迟),SQLite 里一份"全部的历史"(断电不丢)。** 工业术语叫**双写(Dual-Write)**;大白话:柜台放一张实时价目牌,后面仓库还留着所有流水账。

### 🔬 掰开揉碎:为什么要两层?(先懂病)

- 只存内存:程序一关,数据全没;昨天的曲线查不了 → 必须有数据库
- 只存数据库:**每秒几百次写 SQLite,每次写盘几毫秒**,采集线程被 IO 拖住,实时界面卡成幻灯片;而且 SQLite 是**单写者**数据库(同一时刻只允许一个写操作),并发写直接报错
- 双写:**内存层服务"现在"(实时表、报警判断,纯内存操作,微秒级);数据库服务"过去"(历史查询、报表)**。两条路各司其职,互不拖累

### 6.1 SensorRecord.cs —— 数据的"入库服装"(51 行)

**为什么需要它**:第 2 站说过 SensorPoint 是 struct(为了采集快),但 **EF Core 不喜欢 struct 实体**(配置主键、变更跟踪都费劲)。于是:内存世界穿 struct 运动服(快),入库前换成 class 正装(规矩)。**同一个数据的两种形态,工业术语叫"领域模型 ↔ 持久化模型分离"**——大白话:平时穿工装,见客户换西装。

```csharp
17  public class SensorRecord
18  {
20      public int Id { get; set; }          // 数据库自增主键(技术身份证,和业务 PointId 无关)
23      public int PointId { get; set; }     // 业务键:哪个点位(来自 SensorPoint.Id)
26      public double Value { get; set; }    // 采样值
29      public DeviceState State { get; set; }
32      public DateTime Time { get; set; }   // 采样时间
33  }
```

| 行 | 讲解 |
|---|---|
| 20 | 🧠 **两个 Id 的区别是本文件灵魂**:Id = 数据库行的自增流水号(第 1 行、第 2 行…纯技术用途);PointId = 业务点位号(1 号温度点…)。新人最常犯的混用:拿数据库 Id 当点位号查数据。💼 面试:"为什么不用 PointId 直接当主键?"——①点位会反复写入(每次采样一行),主键必须每行唯一 ②自增主键无锁竞争,插入更快 |

```csharp
35      public static SensorRecord FromPoint(in SensorPoint p) => new()
36      {
37          PointId = p.Id,  Value = p.Value,  State = p.State,  Time = p.Timestamp
38      };
39
44      public SensorPoint ToPoint() => new()
45      {
46          Id = PointId,  Value = Value,  State = State,  Timestamp = Time
47      };
```

| 行 | 讲解 |
|---|---|
| 35-50 | 🔧 **一对转换函数**(注意字段名的镜像关系:Point.Id→Record.PointId / Point.Timestamp→Record.Time)。`in` 参数 = 只进不出的引用传递(struct 传参免拷贝的小优化)。查历史时走 `ToPoint()` 还原成领域模型——**数据库的细节被关在 Store 文件夹里,外界只见 SensorPoint**,这就是仓储模式(Repository)的味道:仓库怎么堆货是仓库的事,柜台只管出货 |

### 6.2 AppDbContext.cs —— 数据库图纸(128 行)

**它是什么**:EF Core 的"建表图纸"类。第一次运行时 EF Core 读它自动建 SQLite 表;每次读写也通过它。🔧 继承 `DbContext` 是 EF Core 铁律。

```csharp
21  public class AppDb : DbContext
22  {
23      public AppDb(DbContextOptions<AppDb> options) : base(options) { }   // 🔧 固定构造:数据库配置(用哪个库/哪个文件)从外面注入
24
26      public DbSet<SensorRecord> Records => Set<SensorRecord>();   // 一张表 = 一个 DbSet 属性
29      public DbSet<User> Users => Set<User>();                     // 用户表(第十站)
32      public DbSet<AuditLog> AuditLogs => Set<AuditLog>();         // 审计表
35      public DbSet<Recipe> Recipes => Set<Recipe>();               // 配方表
38      public DbSet<RecipeSnapshot> RecipeSnapshots => Set<RecipeSnapshot>();   // 配方历史快照表
```

| 行 | 讲解 |
|---|---|
| 23 | 🔧 Options 模式:数据库放哪(连接字符串)不写死在类里,构造时注入——测试想换内存库、生产想换 SQL Server,换个 options 就行 |
| 26-38 | 🔧 **一表一属性**:`DbSet<T>` 就是"一张可以被 LINQ 查询的表"。`=> Set<T>()` 是简写,等同 `Records { get { return Set<SensorRecord>(); } }` |

```csharp
40      protected override void OnModelCreating(ModelBuilder mb)     // 🔧 建表细节的配置钩子,EF Core 建库前自动回调
41      {
42          base.OnModelCreating(mb);
43
44          var e = mb.Entity<SensorRecord>();       // 拿到 SensorRecord 表的配置器
45          e.ToTable("sensor_record");              // 表名(蛇形命名,数据库端惯例)
46          e.HasKey(x => x.Id);                     // 主键 = Id
47          e.Property(x => x.Id).ValueGeneratedOnAdd();   // 主键自增(插入时数据库自动编号)
48
49          e.Property(x => x.PointId).HasColumnName("point_id").IsRequired();   // C# 属性 → 数据库列名映射
50          e.Property(x => x.Value).HasColumnName("value").IsRequired();
51          e.Property(x => x.State).HasColumnName("state").HasConversion<string>().IsRequired();
52          e.Property(x => x.Time).HasColumnName("time").IsRequired();
53
55          // 主查询路径:按点位 + 时间窗
56          e.HasIndex(x => new { x.PointId, x.Time }).HasDatabaseName("ix_record_point_time");
57          e.HasIndex(x => x.PointId).HasDatabaseName("ix_record_point");
58          e.HasIndex(x => x.Time).HasDatabaseName("ix_record_time");
```

| 行 | 讲解 |
|---|---|
| 40-42 | 🔧 OnModelCreating 是 EF Core 固定钩子:库模(表结构)怎么定,在这说了算 |
| 45-47 | 🔧 三件套:表名/主键/自增。套路固定,抄熟即可 |
| 49-52 | 🔧 列名映射:`HasColumnName` 把 C# 的帕斯卡命名(PointId)翻译成数据库的蛇形命名(point_id)——两边的命名惯例都保住。`HasConversion<string>()`:State 是枚举,数据库里存字符串("Online"),人查库时能看懂,比存数字 2 强。`IsRequired()` = 非空约束 |
| 56-58 | 🧠 **索引是性能题也是面试题**。类比:**书的目录**。没索引查"1 号点位昨天的数据"= 全表逐行翻(表大了就是秒级);有 (PointId, Time) 复合索引 = 翻目录直达(毫秒级)。为什么建这三个:①(PointId,Time) 覆盖最高频查询"某点位某时段曲线" ②PointId 单列覆盖"某点位统计" ③Time 单列覆盖"某时段全部点位"。**按查询路径建索引,不是越多越好**(每个索引都拖慢写入——本场景写多读少,三个刚好) |

> 59-127 行是 User/AuditLog/Recipe/RecipeSnapshot 四张表的同款配置(表名+主键+列映射+索引),**套路与上面 100% 相同**,只是字段更多。面试点到即可:"另外四张表同款 Fluent API 配置,用户名建了唯一索引防重复注册,审计表按时间和动作建索引支持翻页查询。"自己读时扫一遍即可。

### 6.3 PointStore.cs —— 仓库管理员(191 行,★最长但套路清晰)

**它管什么**:两条路——①内存索引(实时查询)②SQLite 落库(历史查询)。对上层只暴露简单方法,内部复杂度全部隐藏。

```csharp
23  public class PointStore : IDisposable
24  {
25      // ===== 内存索引 =====
26      private readonly List<SensorPoint> _points = new();              // 列表:保序(界面按插入序显示)
27      private readonly Dictionary<int, SensorPoint> _byId = new();     // 字典:按 Id 秒查(O(1))
28      private readonly object _gate = new();                           // 锁(又是它!)
29
31      // ===== SQLite 持久化 =====
31      private readonly IDbContextFactory<AppDb> _dbFactory;
32      private readonly bool _ownsFactory;
34      // 串行化所有写库操作(SQLite 单写者)
35      private readonly System.Threading.Channels.Channel<SensorRecord> _writeQueue =
36          System.Threading.Channels.Channel.CreateUnbounded<SensorRecord>(
37              new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
38      private readonly Task _writePump;
39      private volatile bool _disposed;
```

| 行 | 讲解 |
|---|---|
| 26-27 | 🧠 **同一份数据存两种结构**:List 按"第 1 个、第 2 个"顺序遍历(UI 列表),Dictionary 按 Id 直取(更新时 O(1) 定位)。空间换时间——内存便宜,查询贵。为什么不像管道那样用 Channel?**因为读写模式不同**:界面要"随时读全部",字典列表才是对的工具 |
| 31 | 🔧 `IDbContextFactory<AppDb>` = DbContext 工厂。**为什么用工厂不直接持有一个 DbContext**:①EF Core 的 DbContext 不是线程安全的,多个操作共用一个会崩 ②DbContext 设计就是短命的(用完就扔)。工厂负责"每次要就造一个新的" |
| 35-37 | 🧠 **第二个 Channel!** 又见生产者消费者:写库请求进队,专门的写泵串行处理。`SingleReader = true` = 告诉 Channel 只有一个消费者(内部可以去掉一些锁,更快),`SingleWriter = false` = 生产者可能多个(任何线程都可能调 AddOrUpdate)。**SQLite 单写者的约束,用"排队 + 单窗口处理"化解**——像银行:多个客户随便取号(SingleWriter=false),但窗口只有一个,按号顺序办(SingleReader=true) |
| 38 | 写泵任务本体(下面 127 行讲) |
| 39 | 🔧 `volatile` = 告诉编译器"这个布尔随时会被别的线程改,每次都去内存读最新值,别优化缓存到寄存器"。跨线程状态标志的标配修饰符 |

**双构造函数(46-52 行)——兼容的艺术**:

```csharp
46      public PointStore()                                // 无参构造:自己造一个临时目录的 SQLite 工厂
47          : this(CreateDefaultFactory(), ownsFactory: true) { }
51      public PointStore(IDbContextFactory<AppDb> factory) // DI 构造:外面注入工厂(生产路径)
52          : this(factory, ownsFactory: false) { }
```

| 行 | 讲解 |
|---|---|
| 46-52 | 🧠 **为什么两个构造**:生产环境用 DI 注入(数据库放正式位置 LocalApplicationData);老测试代码 `new PointStore()` 无参也要能跑。`this(...)` 链式构造 = 两个入口最终汇合到同一个真构造。`ownsFactory` 标记"这个工厂是自己造的吗"——自己造的自己 Dispose(见 181 行),别人注入的别乱碰。**改造遗留代码的温柔手法:不改任何老调用方,新能力照加** |

**核心写入方法 AddOrUpdate(65-81 行)**:

```csharp
65      public void AddOrUpdate(SensorPoint p)
66      {
67          // 1) 内存索引同步更新
68          lock (_gate)
69          {
70              _byId[p.Id] = p;                                // 字典:有则覆盖无则新增(一行顶 if-else)
71              var idx = _points.FindIndex(x => x.Id == p.Id); // 列表:找到同 Id 的下标
72              if (idx >= 0) _points[idx] = p;                 // 找到 → 原位替换(保持显示顺序)
73              else _points.Add(p);                            // 没找到 → 追加(新点位首次出现)
74          }
75
76          // 2) 异步落盘:仅追加(历史库保留全部时序样本)
78          if (!_disposed)
79          {
80              _writeQueue.Writer.TryWrite(SensorRecord.FromPoint(p));
81          }
82      }
```

| 行 | 讲解 |
|---|---|
| 70 | 🔧 字典索引器 `_byId[p.Id] = p` 的妙处:**存在就覆盖,不存在就新增**,天然就是 AddOrUpdate,不用先判断 |
| 71-73 | 🧠 为什么 List 不也这么省事:List 没有索引器按 Id 存取,只能 FindIndex 线性找(O(n))。点位数一般 < 几百,性能无虞,换来保序。**LINQ 的 FindIndex + Lambda `x => x.Id == p.Id`** = JS 的 `arr.findIndex(x => x.id === p.id)`,一模一样 |
| 76-81 | 🧠 **双写的"写"落在这**:内存改完,数据库这边**只是往队列里塞一条**(FromPoint 换正装),**不等落盘,立刻返回**。调用方(第 8 站 OnBatchReady)零感知——它以为 AddOrUpdate 已经"存好了",其实是"内存好了,数据库在路上"。`if (!_disposed)`:关门之后不再收快递 |
| 💼 | **面试连环追问:"不等待落盘,断电丢数据怎么办?"** 答:①丢的只是最后几十毫秒的样本,不是状态——点位值下一秒设备还会再报,历史曲线少一两个点无伤大雅 ②实时控制路径(报警/界面)本来就依赖内存层,不受影响 ③工业口径:**实时性 > 个别样本完整性**,要强一致就该先写日志库(WAL)再回复,那是另一档成本。这个取舍能讲清 = 存储设计及格 |

**读路径(83-97 行,旧 API)**:

```csharp
83      public SensorPoint? Get(int id)                 // 查单个:字典直取,O(1)
88      public IReadOnlyList<SensorPoint> GetAll()      // 查全部:列表拷贝快照
94      public IReadOnlyList<SensorPoint> GetAlarms(double threshold)   // 查超阈值的点(报警备用)
```

| 行 | 讲解 |
|---|---|
| 83-97 | 三个读方法,全部 `lock` 内操作 + 返回**拷贝**(`ToList()`)。🧠 为什么返回拷贝不返回原列表:原列表还会被采集线程不停改,外部拿着引用遍历时底层一动就崩(集合被修改异常)。**快照式返回 = 读到的永远是某一瞬间的完整照片**。`IReadOnlyList` 接口 = 我发誓不改它(编译器监督) |

**历史查询(102-115 行,走 SQLite 的新 API)**:

```csharp
102      public async Task<List<SensorPoint>> QueryHistoryAsync(
103          int pointId, DateTime from, DateTime to, CancellationToken ct = default)
104      {
105          await using var db = await _dbFactory.CreateDbContextAsync(ct);
106          var rows = await db.Records.AsNoTracking()
107              .Where(r => r.PointId == pointId && r.Time >= from && r.Time <= to)
108              .OrderBy(r => r.Time)
109              .ToListAsync(ct);
110          return rows.ConvertAll(r => r.ToPoint());
111      }
```

| 行 | 讲解 |
|---|---|
| 105 | 🔧 标准三连:**造 DbContext → 查 → 用完自动扔**。`await using` = 异步版的 using,离开作用域自动 Dispose(连接还给池子) |
| 106 | 🔧 `AsNoTracking()` = 只读查询声明:EF Core 默认会把查出的实体"跟踪"起来(为修改保存做准备),纯读取场景关掉跟踪能省一大块内存和 CPU。**查多改少场景的必背优化** |
| 106-109 | 🔧 **LINQ 三件套 Where + OrderBy + ToList** = SQL 的 WHERE + ORDER BY + 执行。精髓:EF Core 把这个 Lambda **翻译成 SQL 发给数据库执行**,不是把整表拉回来内存过滤(新手的 LINQ 和老手的 LINQ 差在这一条)。数据库端走 (point_id,time) 索引,百万行毫秒级 |
| 110 | 出库时 `ToPoint()` 换回运动服——Store 边界进出各换一次装,里面穿什么外面永远不知道 |

**写泵 PumpWritesAsync(127-152 行)——后台默默落库的搬运工**:

```csharp
127      private async Task PumpWritesAsync()
128      {
129          var reader = _writeQueue.Reader;
130          try
131          {
132              await foreach (var rec in reader.ReadAllAsync())     // 单消费者排队取号
133              {
134                  if (_disposed) break;
135                  try
136                  {
137                      await using var db = await _dbFactory.CreateDbContextAsync();
138                      db.Records.Add(rec);                         // INSERT 一行
139                      await db.SaveChangesAsync();                  // 真正写盘
140                  }
141                  catch
142                  {
143                      // 落盘失败不阻断采集 —— 实时路径已用内存索引服务
144                  }
145              }
146          }
147          catch { /* pump 异常不应冒泡 */ }
148      }
```

| 行 | 讲解 |
|---|---|
| 132 | 与第 5 站管道的消费循环**同款姿势**——这个项目里你将看到三次 Channel 消费(管道/写泵),这就是标准范式 |
| 137-139 | 🔧 EF Core 插入三连:造 DbContext → Add(登记"要插入")→ SaveChangesAsync(真正执行)。**Add 不落盘,SaveChanges 才落盘**——EF 把两次操作分开是有意的(攒多个一起提交) |
| 141-144 | 🧠 **吞异常的哲学**:单条落盘失败(磁盘满/库锁)不能杀死泵——采集还在继续,内存还在更新,界面还在跳;等磁盘恢复,后续数据接着写。**失败的是历史存档,不能陪葬实时业务**。(真实工程此处应注入 ILogger 记录 + 告警,注释也这么写了) |

**默认工厂 CreateDefaultFactory(155-172 行)**:给无参构造造一个"临时目录 + GUID 文件名"的 SQLite——每次 new 一个新库文件,并行测试互不串库。一行 `EnsureCreated()` = 按图纸建表(建库动作)。🔧 测试友好设计。

**Dispose(174-182 行)**:关门动作 = 封队列入口 → 等泵最多 2 秒收尾(把没写完的写完)→ 释放自造的工厂。又是"有始有终"。

### 🎤 面试一句话(第六站)

> "存储用双写:内存索引(List 保序 + Dictionary 按 Id 秒查)服务实时路径,查询永远不碰 IO;SQLite 侧用一个 SingleReader 的 Channel 把写操作串行化,既满足 SQLite 单写者约束,又让采集线程零等待。历史查询走 EF Core LINQ 翻译成 SQL,命中 (PointId,Time) 复合索引;领域层用 struct SensorPoint,持久化层用 class SensorRecord,进出边界各转换一次,是仓储模式的落地。"

### ✂️ 自己改一处

跑一次 UI 采几分钟,停掉,到 `%LocalAppData%\DaqMonitor\daq.db` 用 VSCode 的 SQLite 插件打开,`SELECT * FROM sensor_record LIMIT 10` —— 你刚在界面上看到的每一个数,都安静地躺在这里。**这一眼,比读十遍代码更能理解"落库"**。

---

## 第七站 · 报警大脑:AlarmRule + AlarmEvent + AlarmEngine(78 行合计)

> 文件:`Alarms/AlarmRule.cs`(14行)+ `Alarms/AlarmEvent.cs`(11行)+ `Alarms/AlarmEngine.cs`(53行)

> 📚 **对应讲义**:[M6 · 报警引擎 + 日志](M6_报警引擎日志_深度版.md)(阈值规则/回滞/Serilog——本站主讲的完整版)

### 🎯 一句话

**每条数据过一遍规则表:越界就报警,但只在"从好变坏"的那一刻报一次;回界就解除,同样只报一次恢复。** 没有它,车间警报器会像坏掉的闹钟一样响个不停,没人再理它——工业术语叫**报警泛滥(Alarm Flood)**,是真实事故的著名根源。

### 7.1 AlarmRule —— 规则单(14 行)

```csharp
06  public class AlarmRule
07  {
08      public int PointId { get; set; }              // 管哪个点位
09      public double Threshold { get; set; }         // 阈值(如 100)
10      public AlarmLevel Level { get; set; }         // 命中算什么级别(Warning/Critical)
11      public bool IsHigh { get; set; } = true;      // true:超过阈值报警;false:低于阈值报警
13      public double Hysteresis { get; set; }        // 回滞带宽(下面的灵魂概念)
14  }
```

| 行 | 讲解 |
|---|---|
| 06-14 | 🧠 **规则和数据分离**:规则是配置(哪些点位、什么阈值),数据是流水(每秒几百条)。改报警线不用改代码,改配置就行——车间工艺调整时,工艺员自己就能改阈值,不用等程序员发版 |

### 🔬 掰开揉碎:回滞(Hysteresis)——本站灵魂,面试必考

**病**:温度阈值 100。传感器在 99.8 / 100.2 / 99.9 / 100.1 之间抖动(真实物理世界永远在抖)。没有回滞:每抖一次越界就报一次 + 每抖回来就恢复一次 → **1 秒钟报警 50 次**,日志爆炸,警报器变成背景噪音,操作工直接静音——然后真事故来了没人看。这就是报警泛滥。

**药**:设回滞带宽 2(Threshold=100, Hysteresis=2):
- 触发线抬高:必须冲过 **102** 才算真越界(100~102 之间视为"还在抖",不理)
- 恢复线压低:必须回落到 **98 以下** 才算真恢复
- 效果:抖动区间被两个缓冲带夹住,**一次真实越界 = 一次报警 + 一次恢复,就两声**

**大白话类比**:**空调温控**。设 26℃,空调不是 26.0 就停、26.1 就开(压缩机抖到报废),而是 26 以上才制冷、24 左右才停——那 2 度的"迟滞"就是回滞。电子学里这叫**施密特触发器**(面试说出来,立刻显得懂行)。💼 必背:"回滞 = 触发和恢复用两条线,中间的带不理,防阈值附近抖动导致的报警泛滥"。

### 7.2 AlarmEvent —— 广播内容单(11 行)

```csharp
06  public class AlarmEvent : EventArgs
07  {
08      public int PointId { get; init; }      // 谁报警
09      public AlarmLevel Level { get; init; } // 什么级别
10      public double Value { get; init; }     // 案发值
11  }
```

与第 3 站 DataEventArgs 同款套路(EventArgs 子类 + init 只读),不赘述。触发(AlarmTriggered)和恢复(AlarmCleared)**共用这一个类**——它们携带的信息结构相同,只是语义相反。

### 7.3 AlarmEngine —— 引擎本体(53 行)

```csharp
13  public class AlarmEngine
14  {
15      private readonly List<AlarmRule> _rules = new();       // 规则表(可运行时增删)
16      private readonly HashSet<int> _active = new();         // 当前正处于报警状态的点位集合
17      private readonly object _gate = new();                 // 锁
18
19      public event EventHandler<AlarmEvent>? AlarmTriggered;  // 上升沿广播:变坏了
21      public event EventHandler<AlarmEvent>? AlarmCleared;   // 下降沿广播:变好了
22
23      public void Add(AlarmRule r) { lock (_gate) _rules.Add(r); }        // 运行时加规则
24      public void Clear() { lock (_gate) { _rules.Clear(); _active.Clear(); } }
```

| 行 | 讲解 |
|---|---|
| 15 | 规则表:List 而不是 Dictionary——点位数少(几条规则),遍历足够快,简单优先 |
| 16 | 🧠 **`_active` 是边沿触发的记忆**:HashSet 记着"哪些点位现在正在报警中"。它存在,引擎才分得清"第一次越界"(要广播)和"持续越界"(闭嘴,已经报过了)。**没有记忆就没有边沿**——这是所有边沿检测(硬件软件皆然)的共同原理 |
| 19-21 | 两个事件一上一下,UI 据此变红/复绿 |

```csharp
26      public void Evaluate(SensorPoint p)                     // 每条数据喂进来评一次
27      {
28          List<AlarmRule> snapshot;
29          lock (_gate) snapshot = _rules.ToList();            // 规则表拍快照
30
31          foreach (var r in snapshot)
32          {
33              if (r.PointId != p.Id) continue;                // 规则不关这个点位的事,跳过
34              bool breach = r.IsHigh ? p.Value > r.Threshold : p.Value < r.Threshold;
35              bool inBand = r.Hysteresis > 0 && Math.Abs(p.Value - r.Threshold) <= r.Hysteresis;
36
37              if (breach && !inBand)
38              {
39                  bool wasActive;
40                  lock (_gate) wasActive = !_active.Add(p.Id);
41                  if (!wasActive)
42                      AlarmTriggered?.Invoke(this, new AlarmEvent { PointId = p.Id, Level = r.Level, Value = p.Value });
43              }
44              else if (!breach && r.Hysteresis > 0)
45              {
46                  bool wasActive;
47                  lock (_gate) wasActive = _active.Remove(p.Id);
48                  if (wasActive)
49                      AlarmCleared?.Invoke(this, new AlarmEvent { PointId = p.Id, Level = r.Level, Value = p.Value });
50              }
51          }
52      }
```

这是判断逻辑的心脏,逐行掰:

| 行 | 讲解 |
|---|---|
| 28-29 | 🧠 **快照遍历**:先把规则表复制一份再遍历。为什么:遍历期间别的线程可能 Add 新规则(23 行有 lock,但 foreach 本身不认锁)——边遍历边改集合 = 抛异常。**拍快照 = 遍历的是照片,真身随便动**。💼 面试问"规则为什么 ToList",标准答案在这 |
| 33 | `continue` = 这条规则管别的点位,与本数据无关,跳过(M7 写法的紧凑版) |
| 34 | 🧠 判断方向:IsHigh 规则看"是否高于阈值",IsLow 规则看"是否低于"(比如压力低于下限也要报警——管道快抽空了)。三元表达式一行兼容两个方向 |
| 35 | 🧠 **回滞带判断**:值距离阈值 ≤ Hysteresis 就是"在带内"(抖动区,不理它)。`Math.Abs` = 绝对值,等同 JS 的 Math.abs |
| 37 | 🧠 **真越界 = 越界 && 不在回滞带内**。两个条件缺一不可:值 101 虽然越过了 100,但还在 100~102 的抖动带里,装没看见 |
| 40 | 🔧 精巧的原子操作:`_active.Add(p.Id)` 往集合加这个点位,**返回值是"之前在不在"**(false=刚加上=第一次,true=早就在=持续越界)。外面取反 → `wasActive` 语义直白。**一行干了"查 + 加"两件事且锁内原子**,比"先 Contains 再 Add"少一半开销还不会闪进竞态缝 |
| 41-42 | 🧠 **边沿触发**:只有"第一次变坏"才广播。持续越界的第 2、3、4…条数据全部静默——**这就是防报警泛滥的另一半**(回滞防抖动,边沿防刷屏) |
| 44-47 | 恢复方向:值回落后(且不在带内)把点位从 `_active` 移除。`Remove` 同样返回"之前在不在":true=刚才还在报警=这次是真恢复 → 广播;false=本来就没报,瞎恢复什么 |
| 48-49 | 下降沿广播:UI 听到它把红色表盘变回蓝色 |

### 🧠 为什么这么设计

1. **规则/数据分离** → 改阈值不改代码,工艺员自助
2. **无状态数据流 + 有状态引擎**:数据是无记忆的流水,引擎用 `_active` 记住"谁还在报警中"——状态集中一处,好推理好测试
3. **两个事件而非一个**:触发和恢复是两种不同的 UI 动作(变红/复绿),分开订阅比订阅一个再判断类型干净
4. **线程安全全套**:规则增删、_active 读写全在锁内——报警判断跑在 UI 批量刷新线程,规则配置可能来自别的线程

### 🎤 面试一句话(第七站)

> "报警引擎三条设计:①规则与数据分离,阈值是配置不是代码;②边沿触发——用 HashSet 记录活跃报警,只在状态翻转的瞬间广播,持续越界不刷屏;③回滞带——触发线和恢复线之间留缓冲,阈值附近抖动不反复报警。三条合起来防的就是工业现场最忌讳的报警泛滥。"

### ✂️ 自己改一处(教练计划 L3 的实验)

写个 10 行控制台:阈值 80、回滞 5,依次 Evaluate 喂 79/81/83/79/77/76,在 AlarmTriggered/Cleared 里打印。数一数:触发 1 次(83)、恢复 1 次(76)。再把回滞改 0 重喂,看报警次数爆炸——**亲手摸到回滞的价值**。

---

## 第八站 · 界面呈现:UI 三件套(376 行合计,前端人的主场)

> 文件:`ViewModels/RelayCommand.cs`(29行)+ `ViewModels/MainViewModel.cs`(284行)+ `MainWindow.xaml/.cs`(108+55行)

> 📚 **对应讲义**:[M8 · 工程化收尾](M8_工程化收尾_深度版.md)(MVVM 思想/RelayCommand——本站主讲的完整版)· [M8.5 · Prism 企业级 MVVM](M8.5_Prism企业级MVVM_深度版.md)(企业级替代:不再手写 RelayCommand)· [M14 · WinForm + 自定义控件](M14_WinForm与自定义控件_深度版.md)(GaugeControl/StatusDot 自绘)· [M5 · 实时可视化](M5_实时可视化_深度版.md)(ChartView/LiveCharts2)· [WPF/XAML 速查](WPF_XAML_速查_深度版.md)(前端类比版)

### 🎯 一句话

**MVVM 三角:Model(第 2~7 站的一切)——ViewModel(把数据翻译成界面能绑定的形状 + 接住所有按钮点击)——View(纯 XAML 长相,零业务)。** 等价于 Vue:**XAML=template,ViewModel=setup() 返回的东西,Binding=:绑定,INotifyPropertyChanged=响应式**。

### 🔬 前端人一秒版:WPF MVVM ↔ Vue 对照表(💼 面试可主动甩出)

| WPF | Vue/React 对应 | 说明 |
|---|---|---|
| `MainWindow.xaml` | `<template>` | 声明长相,零逻辑 |
| `MainViewModel` | `setup()` / 组件逻辑 | 全部状态和方法 |
| `{Binding Points}` | `:data="points"` | 数据绑定(单向:VM→V) |
| `INotifyPropertyChanged` | `ref()/reactive()` | **手动版响应式**:WPF 没有代理魔法,改值要**自己喊一嗓子** `OnChanged()` |
| `ICommand` | `@click="handler"` | 按钮点击的绑定目标 |
| `ObservableCollection` | 无敌版:响应式数组 | Add/Remove 自动通知 UI 刷新列表(**改元素内部属性不通知**,所以还要 PointView 自己 INPC) |
| `DataTemplate` | 组件插槽/scoped slot | "每个元素长这样"的模板 |
| `IValueConverter` | filter / computed | 值转换(如 bool→文字) |

**唯一需要重新理解的点**:Vue 的响应式是框架自动的,WPF 的响应式是**手动的**——每个属性 setter 里必须调 `OnChanged()`,忘了调 = 界面不刷新,这是 WPF 新手第一大坑。

### 8.1 RelayCommand —— 按钮的翻译官(29 行)

**它是什么**:WPF 按钮只认 `ICommand` 接口,不懂"方法"。RelayCommand 把任意方法包装成 ICommand——一个通用的适配器,写一次全项目用。

```csharp
09  public class RelayCommand : ICommand
10  {
11      private readonly Action<object?> _execute;              // 按下时执行的方法
12      private readonly Func<object?, bool>? _canExecute;      // 能不能按(可空:没传=永远能)
13
14      public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
15      {
16          _execute = execute;
17          _canExecute = canExecute;
18      }
19
20      public event EventHandler? CanExecuteChanged            // 🔧 特殊事件:转接给 WPF 全局"重新问一遍"机制
21      {
22          add { CommandManager.RequerySuggested += value; }
23          remove { CommandManager.RequerySuggested -= value; }
24      }
25
26      public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
27
28      public void Execute(object? parameter) => _execute(parameter);
29  }
```

| 行 | 讲解 |
|---|---|
| 11-12 | 🔧 `Action<object?>` = 无返回值的方法引用;`Func<object?,bool>` = 有返回值(布尔)的方法引用。等同 JS 把函数当参数传 |
| 20-24 | 🔧 **CanExecuteChanged 的把戏**:WPF 想知道"现在按钮能不能按"(灰色还是亮色)。这里不自己维护订阅名单,而是**转接给 CommandManager.RequerySuggested**(WPF 的全局重询机制:焦点变化等时机自动全量重问一遍)。背下来,这是简化版 MVVM 框架的经典写法(Prism/CommunityToolkit 内部更精细) |
| 27 | `??` = 左边为 null 用右边:没提供 canExecute 就永远返回 true(总能按) |
| 28 | 按下按钮 → 调你传入的方法。**整个类就是"按钮点击 → 方法调用"的一根线** |

### 8.2 MainViewModel —— 界面总指挥(284 行)

文件分三段:PointView(展示模型)→ 属性与命令 → 事件处理。逐段吃。

**第一段:PointView(26-44 行)——为什么需要"展示模型"**

```csharp
26  public class PointView : INotifyPropertyChanged      // 🔧 实现这个接口 = "我是可通知的"
27  {
28      private int _id;                                 // 私有字段(真正存值的地方)
34      public int Id { get => _id; set { _id = value; OnChanged(); } }   // 属性 = 字段 + 通知
39      public AlarmLevel Level { get => _level; set { _level = value; OnChanged(); } }
41
42      public event PropertyChangedEventHandler? PropertyChanged;                            // WPF 监听这个
43      private void OnChanged([CallerMemberName] string? n = null)
44          => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
44  }
```

| 行 | 讲解 |
|---|---|
| 26 | 🧠 **为什么不直接绑 SensorPoint**:①SensorPoint 是 struct,赋值是**复印**,WPF 绑定引擎跟踪不了复印过程——值变了 UI 不知道 ②没有属性通知能力。所以要造一个 class 版、每个属性 setter 都喊话的**展示模型(ViewModel 层的模型)**。这层薄包装是 MVVM 的日常动作,前端类比:**interface Pet { raw } → 组件里的 reactive 包装** |
| 34 | 🔧 **INPC 属性模板**(背到肌肉记忆):`public T X { get => _x; set { _x = value; OnChanged(); } }` —— 存值 + 喊话,一式两份。**忘了 OnChanged() = 这个属性永远不刷新界面** |
| 43 | 🔧 `[CallerMemberName]` = 编译器魔法:谁调用我,自动把"调用者的名字"填进来(不用手写 `OnChanged(nameof(Id))`,自动就是 "Id")。**自动报出"是谁变了"**,WPF 拿名字去精准刷新对应绑定 |
| 44 | `?.Invoke` 又来了(第 3 站讲过:有人听才广播) |

**第二段:MainViewModel 的状态与命令(50-166 行,挑骨架讲)**

```csharp
50  public class MainViewModel : INotifyPropertyChanged          // VM 自己也要可通知
51  {
52      private readonly PointStore _store;                      // 私有字段们:构造时注入,全程只读
53      private readonly AcquisitionPipeline _pipeline;
54      private readonly AlarmEngine _alarms;
55      private readonly IDevice _device;
59      private readonly Dictionary<int, AlarmLevel> _levels = new();   // 每个点位当前报警级别(给控件变色)
60      private bool _running;                                    // 是否采集中
61      private DateTime _from = DateTime.Today;                  // 报表起始时间(默认今天 0 点)
62      private DateTime _to = DateTime.Now;
63      private ChartView? _chart;                                // 曲线页引用(AttachChart 注入)
64
65      public ObservableCollection<PointView> Points { get; } = new();     // 界面点位表的数据源
66      public ObservableCollection<string> AlarmLog { get; } = new();      // 报警日志列表
68      public ICommand StartCommand { get; }                    // 启动按钮绑定
69      public ICommand StopCommand { get; }                     // 停止按钮绑定
70      public ICommand ExportReportCommand { get; }
71      public ICommand LogoutCommand { get; }
```

| 行 | 讲解 |
|---|---|
| 52-55 | 🧠 **依赖注入的消费者侧**:7 个私有字段 = "本 VM 需要哪些服务"的清单。全部 `readonly` = 构造后不换人。**VM 不 new 服务,只接收服务**——方便测试时塞假货 |
| 65-66 | 🔧 `ObservableCollection<T>` = **会喊话的 List**:Add/Remove/Insert 时自动通知 UI 增删行。注意局限:**改里面元素的字段它不管**(所以元素是 PointView,自带 INPC) |
| 68-71 | 4 个命令属性,XAML 里 `{Binding StartCommand}` 绑到按钮。**按钮永远绑命令,不绑方法**——WPF 不认识方法 |

```csharp
80      public bool IsRunning                                     // 采集中?
81      {
82          get => _running;
83          private set                                           // private set:只有 VM 自己能改
84          {
85              _running = value;
86              OnChanged();                                      // 通知 IsRunning 自己
88              OnChanged(nameof(CanStartAcquisition));          // 连带通知两个派生属性
89              OnChanged(nameof(CanStopAcquisition));
90          }
91      }
111     public bool CanStartAcquisition
112         => _current.HasPermission(Permissions.AcquisitionStart) && !IsRunning;
115     public bool CanStopAcquisition
116         => _current.HasPermission(Permissions.AcquisitionStop) && IsRunning;
```

| 行 | 讲解 |
|---|---|
| 85-89 | 🧠 **联动通知**:IsRunning 一变,"启动按钮能不能按""停止按钮能不能按"全跟着变——所以一个 setter 喊三次话。WPF 没有自动依赖追踪(Vue 的 computed 自动算依赖),**依赖关系要自己手动广播**。这是 WPF 版"computed 变更要手动触发" |
| 111-116 | 🧠 **权限 && 状态 双条件**:能启动 = (有权限)且(现在没在跑)。工业软件的按钮不是"给不给点",是"这个角色在这个状态下该不该点"——权限体系(M17)就这样渗进每个按钮。XAML 里 `IsEnabled="{Binding CanStartAcquisition}"` 直接消费 |

**第三段:构造函数(137-166 行)——装配 + 订阅**

```csharp
137     public MainViewModel(ServiceProvider services)
138     {
139         _store = services.GetRequiredService<PointStore>();        // 点菜七连
145         _current = services.GetRequiredService<ICurrentUserService>();
147         StartCommand = new RelayCommand(_ => Start());              // 命令 = 方法包一层
149         ExportReportCommand = new RelayCommand(_ => ExportReport(), _ => CanExportReport);
153         Recipes = new RecipeManagementViewModel(...);               // 子 VM(配方页)
158         Motion = new MotionControlViewModel(...);                   // 子 VM(运控页)
161         _pipeline.BatchReady += OnBatchReady;                       // ★订阅管道:一批数据到了
162         _alarms.AlarmTriggered += OnAlarmTriggered;                 // ★订阅报警:变坏了
163         _alarms.AlarmCleared += OnAlarmCleared;                     // ★订阅报警:变好了
165         _diag.RecordInfo("应用启动,DI 容器已装配。");
166     }
```

| 行 | 讲解 |
|---|---|
| 139-145 | 🔧 构造函数点菜(与 App.xaml.cs 同款)。**构造即声明依赖**:一看构造函数就知道这个 VM 依赖谁 |
| 147-150 | 🔧 命令装配标准姿势:`new RelayCommand(_ => 方法())`——Lambda 包住方法调用。带第二参数的就是"能不能按"的判断 |
| 161-163 | 🧠 **全项目数据流的最后一根线**:VM 订阅管道的 BatchReady 和报警引擎的两个事件。至此闭环完成:设备→管道→(VM)→界面。**第 5 站埋的"订阅方自行 Dispatcher"在这里兑现** |

**第四段:OnBatchReady —— 金线的收网处(177-209 行,★全项目最值得断点的方法)**

```csharp
177     private void OnBatchReady(object? _, IReadOnlyList<SensorPoint> batch)
178     {
180         var sw = Stopwatch.StartNew();                            // 秒表:量这批处理花了几毫秒
181         Application.Current.Dispatcher.Invoke(() =>
182         {
183             foreach (var p in batch)
184             {
185                 _store.AddOrUpdate(p);                             // ① 入库(内存同步+SQLite排队)
186                 _alarms.Evaluate(p);                               // ② 过报警规则(命中才广播)
187
188                 PointView? row = Points.FirstOrDefault(x => x.Id == p.Id);
189                 if (row is null)
190                 {
191                     row = new PointView { Id = p.Id, Value = p.Value, Timestamp = p.Timestamp, State = p.State };
192                     Points.Add(row);                               // ③ 新点位:建行
193                 }
194                 else
195                 {
196                     row.Value = p.Value;                           // 老点位:原位刷新(setter 喊话→界面自动变)
199                 }
201                 if (_levels.TryGetValue(p.Id, out var lv)) row.Level = lv;   // ④ 报警级别同步给表盘
203                 _chart?.Push(p);                                   // ⑤ 曲线页喂一口
204             }
205             OnChanged(nameof(DiagnosticsSummary));                // ⑥ 诊断摘要刷新
206         });
207         sw.Stop();
208         _diag.RecordBatch(batch.Count, sw.ElapsedMilliseconds);   // ⑦ 本批耗时上报诊断面板
209     }
```

| 行 | 讲解 |
|---|---|
| 177 | 签名 = 事件订阅方标准形状(第 5 站 23 行广播的接听方)。`_` = 不关心谁发的 |
| 180 | 🧠 **Stopwatch 计时**:给"一批处理耗时"计时并上报诊断面板(第 10 站)——**卡顿排查的第一指标就是它**。生产级思维的细节:不为功能,为可观测性 |
| 181 | 🔧 **Dispatcher.Invoke = 跨线程刷 UI 的唯一合法通道**(全项目最重要的一行固定写法,背下来)。背景:BatchReady 在**后台线程**触发(第 5 站警告过),而 WPF 铁律"只有 UI 线程能碰界面"。`Invoke` = 把 Lambda **快递到 UI 线程执行**,当前线程等它干完。类比:你在后厨(后台线程),想上菜必须传菜员(UI 线程)——后厨直接冲进餐厅会被打出来(InvalidOperationException) |
| 181-206 | 🧠 **为什么整个 foreach 包一个 Invoke,而不是每行一个**:Invoke 有跨线程开销(排队+上下文切换)。一批 50 条数据,包一起 = 1 次快递送 50 个菜;每条一个 = 50 次快递。💼 高频必考:"Dispatcher.Invoke 包整个批量而不是逐条" |
| 185-186 | 每条数据两件事:入库 + 过报警。**顺序**:先存再评——存是事实,评是判断,判断可以晚但事实不能丢 |
| 188-199 | 🧠 **表格的增量维护**:FirstOrDefault 找现有行(又是 LINQ,同 JS 的 find),没有则 Add 新行,有则改属性。**改属性即刷屏**:row.Value = p.Value 触发 PointView 的 setter → OnChanged → WPF 自动只刷新那一格。这就是响应式的回报——**你只管改数据,界面自己知道该干嘛** |
| 201 | `_levels` 字典记着每个点位当前报警级别(报警事件处理器写入,见 216 行),每次批量刷新同步给表盘——保证**新数据刷过来时,红色不会意外变回蓝**(报警级别是"状态",不是每条数据自带的) |
| 208 | 批量耗时上报:诊断面板的"末批 XXms"就是它 |

**报警两个处理器(211-232 行)**:

```csharp
211     private void OnAlarmTriggered(object? _, AlarmEvent e)
212     {
213         Application.Current.Dispatcher.Invoke(() =>
214         {
215             AlarmLog.Insert(0, $"[{DateTime.Now:HH:mm:ss}] 点位 {e.PointId} → {e.Level} 报警,值 = {e.Value}");
216             _levels[e.PointId] = e.Level;                     // 记住级别
218             var row = Points.FirstOrDefault(x => x.Id == e.PointId);
219             if (row is not null) row.Level = e.Level;         // 表盘立即变橙/红
220         });
221         _diag.RecordAlarm(e.PointId, e.Level.ToString(), e.Value);   // 诊断计数
222     }
```

| 行 | 讲解 |
|---|---|
| 215 | 🔧 `Insert(0, ...)` = 插到列表头 → **最新报警永远在最上面**(操作工不用滚动找最新)。细节之处见工业 UX:报警是紧急信息,新信息必须零滚动可见 |
| 213 | 又见 Dispatcher——报警事件同样来自后台线程。**凡是事件处理器碰 UI,必包 Dispatcher**,这个项目里你会看到它出现 4 次,全是同一个道理 |

**启停与登出(234-261 行)**:

```csharp
234     private void Start()
235     {
236         if (IsRunning) return;                                  // 幂等守卫(同第4站)
237         if (_device is SimulatedDevice sd) sd.Start(TimeSpan.FromMilliseconds(100));   // 模式匹配:是模拟设备才调 Start
239         IsRunning = true;                                       // 状态翻转 → 按钮自动变灰/亮
240     }
```

| 行 | 讲解 |
|---|---|
| 237 | 🔧 `is 类型 变量名` = **模式匹配**:判断"是不是 SimulatedDevice",是则顺便转成变量 sd。为什么需要判断:Start(开始产数)是模拟设备**特有**方法(第 4 站说过不在 IDevice 合同里)——换成真 PLC 后这行自然跳过,PLC 数据本来就在流。💡 这也是"合同内共性/合同外个性"设计的 consumption 侧证据 |

ExportReport(267-279)三步:弹保存对话框 → ReportService 按时间窗聚合 → ClosedXML 写 Excel。串联了存储(M4)→聚合→导出(M10),扫一眼即可。

### 8.3 MainWindow.xaml —— 长相说明书(108 行)

XAML 是声明式 UI(同 HTML),只讲结构和绑定,挑主骨架:

```xml
01  <Window x:Class="DaqMonitor.UI.MainWindow"           <!-- 🔧 这份 xml 对应哪个 C# 类 -->
02          xmlns="...presentation"                       <!-- 🔧 WPF 控件默认命名空间 -->
04          xmlns:vm="clr-namespace:DaqMonitor.UI.ViewModels"     <!-- 🔧 起别名:引用 VM 命名空间 -->
05          xmlns:ctrl="clr-namespace:DaqMonitor.UI.Controls"     <!-- 自定义控件 -->
08          Title="DAQ Monitor · 工业数据采集监控" Height="560" Width="880">
09      <Grid Margin="12">                                 <!-- Grid = WPF 的 div,主容器 -->
10          <Grid.RowDefinitions>                          <!-- 🔧 网格布局:先声明行(同 grid-template-rows) -->
11              <RowDefinition Height="Auto" />            <!-- 顶部:按内容高 -->
12              <RowDefinition Height="*" />               <!-- 中间:吃掉剩余全部(*=1fr) -->
13              <RowDefinition Height="Auto" />            <!-- 底部:按内容高 -->
14          </Grid.RowDefinitions>
```

| 行 | 讲解 |
|---|---|
| 02-07 | 🔧 xmlns = xml namespace,起别名引类型。WPF 的"import 语句" |
| 10-14 | 🔧 **Grid 三段布局**(Auto/*/Auto):顶部工具栏+主内容+底部说明,工业界面祖传结构(README 原型图同款)。`*` = 剩余空间全给我,等同 CSS `flex: 1` / grid 的 1fr |

```xml
25          <Button Content="启动采集" Command="{Binding StartCommand}" Width="90" Height="30"
26                  IsEnabled="{Binding CanStartAcquisition}" />
```

| 行 | 讲解 |
|---|---|
| 25-26 | 🔧 **按钮绑定双件套**:Command 绑命令(点了干嘛),IsEnabled 绑布尔(能不能点)。大厂工业规范:**关键操作按钮 ≥ 48px 高**(盲操作友好,这里的 30 是学习版) |

```xml
29          <TextBlock VerticalAlignment="Center" Margin="16,0,0,0">
30              <Run Text="状态:" />
31              <Run Text="{Binding IsRunning, Converter={StaticResource RunningText}}" FontWeight="Bold" />
32          </TextBlock>
```

| 行 | 讲解 |
|---|---|
| 31 | 🔧 `Run` = 文本片段(一 行 TextBlock 里拼多段不同样式)。`Converter` = 值转换器(IsRunning 的 true/false 转成"采集中/已停止"文字),转换器本体在 xaml.cs(下面讲) |

```xml
56          <DataGrid ItemsSource="{Binding Points}" AutoGenerateColumns="False" IsReadOnly="True"
57                    CanUserAddRows="False" FontSize="13">
58              <DataGrid.Columns>
59                  <DataGridTextColumn Header="点位" Binding="{Binding Id}" Width="55" />
61                  <DataGridTemplateColumn Header="数值(仪表)" Width="130">
62                      <DataGridTemplateColumn.CellTemplate>
63                          <DataTemplate>
64                              <ctrl:GaugeControl Value="{Binding Value}" Min="0" Max="150" Level="{Binding Level}"
65                                                     Label="{Binding Id, StringFormat=P{0}}" Height="84" />
66                          </DataTemplate>
67                      </DataGridTemplateColumn.CellTemplate>
68                  </DataGridTemplateColumn>
```

| 行 | 讲解 |
|---|---|
| 56 | 🔧 **DataGrid = 表格**,ItemsSource 绑 ObservableCollection<PointView> 就自动出全表。**绑定上下文切换**:外层绑 VM 的 Points,列内部 `{Binding Id}` 绑的是**每个元素(PointView)的 Id**——等同 Vue 的 v-for 内部作用域 |
| 61-68 | 🔧 **TemplateColumn = 自定义单元格**(scoped slot 的 WPF 版):不用文本,塞一个仪表盘控件。`GaugeControl` 是项目自绘的表盘(值=指针角度,Level=环的颜色:蓝/橙/红)——报警变红的数据流终点:AlarmEngine → OnAlarmTriggered → row.Level → 表盘红环 |
| 65 | `StringFormat=P{0}` = 格式化字符串(P1/P2/P3 显示点位号,同 JS 模板串) |

```xml
84          <TabControl Grid.Row="1" Margin="8,0,0,0">
85              <TabItem Header="报警日志">
86                  <ListBox ItemsSource="{Binding AlarmLog}" FontSize="12" />      <!-- 报警列表,Insert(0)的最新在顶 -->
88              <TabItem Header="📋 配方管理">
89                  <TabItem x:Name="RecipeTab" />                                   <!-- 空 Tab:内容由 code-behind 填 -->
94              <TabItem Header="实时曲线">
95                  <views:ChartView x:Name="ChartTab" />                            <!-- 曲线页(LiveCharts2) -->
97              <TabItem Header="诊断 / 调试">
98                  <diag:DiagnosticsPanel />                                        <!-- 诊断面板(第10站) -->
99          </TabControl>
```

| 行 | 讲解 |
|---|---|
| 84-99 | 🔧 TabControl = 页签容器。五个页签 = 五个功能区。89/92 行的空 TabItem 带个 `x:Name`(给元素起名,C# 里能引用到)——**内容为什么不在 XAML 里写死**:配方/运控 View 需要 VM 传入才能构造,而 XAML 不会自动找 VM,所以留空由 code-behind 手动填(见下面) |

### 8.4 MainWindow.xaml.cs —— 胶水层(55 行)

```csharp
09  /// <summary>把 bool 取反,给按钮的 IsEnabled 用。</summary>
10  public class InverseBoolConverter : IValueConverter
11  {
12      public object Convert(object value, ...) => value is bool b ? !b : true;   // true→false,false→true
14      public object ConvertBack(...) => Binding.DoNothing;                        // 单向转换:回程不管
15  }
17  /// <summary>把采集状态显示成文字。</summary>
18  public class RunningTextConverter : IValueConverter
21      public object Convert(object value, ...) => value is bool b && b ? "采集中" : "已停止";
```

| 行 | 讲解 |
|---|---|
| 10-23 | 🔧 **IValueConverter 模板**:Convert 正向(数据→界面),ConvertBack 逆向(界面→数据,单向绑定用 DoNothing 占位)。`value is bool b` 又是模式匹配。类比:Vue 的 filter / pipe。两个转换器:布尔取反、状态转文字 |

```csharp
25  public partial class MainWindow : Window
26  {
27      public MainWindow()
28      {
29          Resources.Add("InverseBool", new InverseBoolConverter());    // 🔧 转换器先注册进资源字典
30          Resources.Add("RunningText", new RunningTextConverter());    // XAML 里 {StaticResource RunningText} 才能找到
31          InitializeComponent();                                        // 🔧 编译 XAML 并加载(固定第一梯队调用)
32          DataContextChanged += MainWindow_DataContextChanged;         // 订阅"VM 被注入"事件
33      }
34
37      private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
38      {
39          if (DataContext is MainViewModel vm)
40          {
41              if (ChartTab is not null) vm.AttachChart(ChartTab);              // ① 曲线页接线
44              if (RecipeTab is not null && vm.Recipes is not null)
45              {
46                  RecipeTab.Content = new RecipeManagementView(vm.Recipes);    // ② 手动造配方页填进空 Tab
47              }
49              if (MotionTab is not null && vm.Motion is not null)
50              {
51                  MotionTab.Content = new MotionControlView(vm.Motion);        // ③ 运控页同理
52              }
53          }
54      }
55  }
```

| 行 | 讲解 |
|---|---|
| 29-31 | 🔧 三连:注册资源 → InitializeComponent(每个 XAML 窗口的固定第一行,**忘了它窗口是空白的**) |
| 32-54 | 🧠 **为什么需要 DataContextChanged 回合**:App.xaml.cs 是"先造 VM → 再造 Window → 再赋 DataContext",赋值那一刻本类才拿到 VM。拿到后干三件接线活:曲线页、配方页、运控页。**code-behind 的本分**:只做"接线"这种纯胶水,业务一概不写——写了就破坏 MVVM |

### 🎤 面试一句话(第八站)

> "MVVM 三层:View 是纯 XAML;ViewModel 持全部状态与命令,通过 INotifyPropertyChanged 手动驱动刷新(相当于手写响应式),ObservableCollection 驱动列表增删;跨线程数据经 Dispatcher.Invoke 包住整批刷新而不是逐条。View 不写业务,code-behind 只做接线;VM 通过 DI 接收服务,订阅管道 BatchReady 完成数据流闭环。"

### ✂️ 自己改一处(教练计划 L6 的简历锚点)

三选一:① 报警表加"按级别筛选"下拉 ② 点位表加搜索框 ③ 启动按钮加确认对话框。改完 `dotnet build` + 跑起来验证 + `dotnet test` 仍 85 绿 → git commit。**这一步做完,项目才算"你的"**。

---

## 第九站 · 总装配车间:Bootstrapper.cs(249 行)

> 文件:`src/DaqMonitor.Core/AppServices/Bootstrapper.cs`

> 📚 **对应讲义**:[M9 · 工程素养](M9_工程素养_测试DI容错_深度版.md)(DI 容器/生命周期/组合根——本站主讲的完整版)· [M8.5 · Prism](M8.5_Prism企业级MVVM_深度版.md)(Prism DryIoc 版组合根)· [M17](M17_工业安全与MES对接_深度版.md)(账号/审计的种子)· [M18](M18_配方管理_深度版.md)(配方种子)

### 🎯 一句话

**开机时把所有零件按依赖顺序装进 DI 容器、注册默认账号和报警规则的地方。** 工业术语:**组合根(Composition Root)**——全项目唯一允许"知道所有具体类"的地方。大白话:总装车间,别的车间(管道/存储/UI)只管要零件,不关心零件哪造的。

### 🔬 掰开揉碎:DI 是什么(30 秒版,前端人秒懂)

没有 DI:每个类自己 `new` 依赖 → 类之间焊死,换零件要拆一片,测试没法塞假货。
有 DI:**所有类只声明"我需要什么"(构造参数),由一个中央装配员统一 new 好递进去**。等同:你别自己 new axios 实例了,统一从 `provide/inject` 拿——全项目一个装配点,谁换谁知道。

三个生命周期(💼 必考):**Singleton**(全应用一个实例:PointStore/AlarmEngine 这种全局状态)、**Transient**(每次要新造一个)、**Scoped**(某个作用域内单例,Web 请求专用,本项目没用到)。

### 逐段讲解(249 行按功能分五段)

**第一段:数据库准备(31-42 行)**

```csharp
29      public static ServiceProvider Build()
30      {
31          var services = new ServiceCollection();               // 🔧 空的服务清单(登记簿)
32
35          var dbPath = System.IO.Path.Combine(
36              Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
37              "DaqMonitor", "daq.db");
38          var dbDir = System.IO.Path.GetDirectoryName(dbPath);
39          if (!string.IsNullOrEmpty(dbDir)) System.IO.Directory.CreateDirectory(dbDir);
40
41          services.AddDbContextFactory<AppDb>(opt =>
42              opt.UseSqlite($"Data Source={dbPath}"));
```

| 行 | 讲解 |
|---|---|
| 35-39 | 🧠 **数据库文件放哪是个决策**:放程序目录?Program Files 没写权限;放文档?用户会误删。`LocalApplicationData`(C:\Users\你\AppData\Local)是 Windows 官方答案:用户可写、按用户隔离、卸载清理不碰。**这类"放哪"的问题面试官爱问,考的是真实部署经验** |
| 38-39 | 建目录:第一次运行 LocalAppData 里还没有 DaqMonitor 文件夹,先造出来 |
| 41-42 | 🔧 注册 DbContext 工厂(第 6 站 31 行的 IDbContextFactory 从这来):`AddDbContextFactory` 是 EF Core 的标准注册方法,内部自动配成 Singleton 工厂 |

**第二段:单例服务登记(44-77 行,全是同款套路)**

```csharp
45      services.AddSingleton<PointStore>();                     // 仓储:全局一个
46      services.AddSingleton<AlarmEngine>();                    // 报警引擎:全局一个
48      services.AddSingleton<DiagnosticsService>();             // 诊断:全局一个
51      services.AddSingleton<ICurrentUserService, CurrentUserService>();   // 接口→实现:注册接口,取的时候点接口的名
53      services.AddSingleton<AuthService>();
56      services.AddSingleton<RecipeService>();
60      services.AddSingleton<IAxisController>(_ => new SimulatedAxisController(new AxisConfiguration { ... }));
77      services.AddSingleton<AcquisitionPipeline>(_ => new AcquisitionPipeline(TimeSpan.FromMilliseconds(200)));
```

| 行 | 讲解 |
|---|---|
| 45 等 | 🔧 `AddSingleton<T>()` = "T 这种零件,全应用共用一个"。**容器自动分析构造函数**:PointStore 要 IDbContextFactory,容器发现 41 行注册过,自动注入——**依赖链自动装配,这是 DI 的魔法所在** |
| 51 | 🔧 **接口注册双参数版**:`AddSingleton<接口, 实现类>` = "登记在接口名下"。取的时候 `GetRequiredService<ICurrentUserService>()` 拿到的是 CurrentUserService 实例——**上层只点接口的名**(和第 3 站 IDevice 同一哲学) |
| 60-69 | 🧠 **同一个接口注册两次**(X 轴、Y 轴都是 IAxisController):后取时用 `GetServices<IAxisController>()`(复数)拿到全部实例数组。工厂 Lambda 写法 `_ => new XXX()` = "要的时候现造,造法我指定" |
| 77 | 🧠 管道的 200ms 在这定死——**所有"魔数"集中在组合根,改配置只来这一个文件**(改这里做第 5 站的实验) |

**第三段:设备注册(79-124 行,★可插拔的证据现场)**

```csharp
110     services.AddSingleton<IDevice>(_ => new SimulatedDevice(1, "Sim-01", 1, 2, 3));
```

| 行 | 讲解 |
|---|---|
| 110 | 🧠 **全项目架构的支点**。注册的是"接口 IDevice 名下,一个 SimulatedDevice 实例"。App.xaml.cs 38 行、MainViewModel 142 行取的都是 IDevice——**把这一行换成下面注释里的任何一行(串口/PLC/Modbus/TCP/CAN),全项目自动改用新设备,其他代码零改动**。79-109 行的巨型注释就是"替换菜单":每种设备怎么换、参数怎么填,全写在现场。面试讲"可插拔架构",就背这一行的故事 |

```csharp
117     services.AddSingleton<Func<string, int, IEnumerable<TcpDevice.TcpMap>, bool, TcpDevice>>(
118         _ => (host, port, maps, simulate) => new TcpDevice(...));     // 工厂注入:运行时才能定参数的东西,注入"造它的函数"
127     services.AddSingleton<Func<string, int, TcpDevice>>(
128         sp => (host, port) => sp.GetRequiredService<Func<...>>()...   // 简化版工厂:内部取完整版再给默认参
```

| 行 | 讲解 |
|---|---|
| 117-129 | 🧠 **工厂注入模式**:TCP 设备的 host/port 要运行时用户输入才知道,没法开机注册死。于是注册"**造 TcpDevice 的函数**"本身,谁需要谁调用工厂现造。`Func<A,B,C,D>` = "吃 A B C D 吐 D 的函数"类型。面试聊"运行时参数的服务怎么办",答工厂注入 |

**第四段:建库 + 种子数据(138-154 行)**

```csharp
138     var provider = services.BuildServiceProvider();          // 🔧 清单登记完毕 → 真正造出容器
142     using (var scope = provider.CreateScope())
143     {
144         var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDb>>();
145         using var db = factory.CreateDbContext();
146         db.Database.EnsureCreated();                          // 按第6站图纸建表(已有库则跳过)
147         SeedDefaultUsers(db);                                 // 种子:3 个默认账号
148         SeedDefaultRecipes(db);                               // 种子:2 个示例配方
149     }
152     var alarms = provider.GetRequiredService<AlarmEngine>();
153     alarms.Add(new AlarmRule { PointId = 1, Threshold = 100, IsHigh = true, Level = AlarmLevel.Critical, Hysteresis = 2 });
154     alarms.Add(new AlarmRule { PointId = 2, Threshold = 100, IsHigh = true, Level = AlarmLevel.Warning, Hysteresis = 2 });
```

| 行 | 讲解 |
|---|---|
| 138 | 🔧 登记与开工的分界线:之前全是往清单写,这行才真正造容器 |
| 146 | 🧠 `EnsureCreated()` = 没库建库没表建表,已有就跳过(幂等)。生产升级场景改用 EF Migrations(代码注释也交代了)。**第一次跑程序生成 daq.db 的就是这一行** |
| 147-148 | 种子(Seed)数据:预置 3 个账号(admin/engineer/operator,BCrypt 加密)和 2 个配方,让 UI 一打开就有东西可看可登录。159-249 行是两个 Seed 方法的实体构造(同款对象初始化器堆叠,扫读即可),核心一句:**`if (db.Users.Any()) return;` 幂等守卫——种过就不重种**(否则每次启动密码重置,用户改名全丢) |
| 152-154 | 🧠 **报警规则在此真相大白**:点位 1 超 100 → Critical(红)、点位 2 超 100 → Warning(橙),回滞都是 2。对照第 4 站:模拟设备 10% 概率产 95~120 的值 → 大约每十几秒点位 1 冲过 102 → 一次红色报警。**你现在能从数据源头到表盘变红,完整讲出每一站发生了什么** |

### 🎤 面试一句话(第九站)

> "组合根在 Bootstrapper.Build:按'数据库工厂→单例服务→设备→管道'顺序装配 DI 容器;设备注册在 IDevice 接口名下,换真实串口或 PLC 只改一行注册,上层零改动,这是可插拔架构的支点;启动期 EnsureCreated 建库并幂等地种子默认账号、配方与报警规则,容器随窗口关闭统一 Dispose。"

### ✂️ 自己改一处(教练计划 L7 的毕业实验)

把 110 行换成:`services.AddSingleton<IDevice>(_ => new SimulatedDevice(1, "Sim-01", 5, 6));`(改点位号)——同时把 153 行报警规则的 PointId 改成 5 → 跑起来:表里出现 5、6 号点位,5 号会报警。**一行注册改全局,亲证"可插拔"**。

---

## 第十站 · 周边模块一页图(不逐行,给地图)

这些模块全部是**你已学套路的复用**,每个给你"是什么 + 用的哪个套路 + 深入去哪":

| 模块 | 一句话 | 用的套路(=哪一站) | 深入 |
|---|---|---|---|
| **LoginWindow/LoginViewModel** | 模态登录,BCrypt 验密,三角色 | MVVM 三角(第 8 站) | 第 1 站已讲流程;BCrypt = 密码单向哈希,存"指纹"不存原文 |
| **AuthService/CurrentUserService** | 登录态 + 当前用户 + 按钮级权限 | 单例 + 接口注册(第 9 站 51 行) | HasPermission 被每个 CanXxx 属性消费(第 8 站 111 行) |
| **DiagnosticsService/DiagnosticsPanel** | 采样/报警/批次耗时统计 + 环形日志 | 观察者(事件) + INPC(第 8 站) | OnBatchReady 208 行喂它数据;排卡顿先看它 |
| **ChartView** | LiveCharts2 实时曲线,点位 1/2 分流双线 | 第三方图表库 + Push 模式 | 第 8 站 203 行 `_chart?.Push(p)` 是唯一接缝 |
| **RecipeService/RecipeManagementView** | 配方(工艺参数包)版本化+快照回滚 | 双写思想(第 6 站) + MVVM | ISA-88 批次控制标准;改前自动存快照 |
| **MotionControl(SimulatedAxisController)** | 模拟 X/Y 两轴运控,令牌防并发 | 接口 + 多实例注册(第 9 站 60 行) | 真项目接固高/雷赛板卡,换实现即可 |
| **MqttPublisher** | 采集数据上云,MQTTnet | 又一个"Channel+后台泵"(第 6 站同款) | Enqueue 进队,后台批量 Publish——和写泵一个模子 |
| **TcpDevice** | 心跳探活 + 指数退避重连 + 粘包帧解析 | IDevice 实现(第 3/4 站) + 工厂注入(第 9 站 117 行) | 面试高频:Socket.Connected 为何不可信、退避为何加抖动 |
| **SerialDevice/ModbusDevice/PlcDevice/CanDevice/UsbHidDevice** | 5 种真实协议设备 | 全是 IDevice 合同的第 4 站变奏 | 每个对应 M1/M2/M3/M16 讲义;字节序问题见 ByteOrderLab |
| **GaugeControl/StatusDot** | 自绘表盘/状态灯(扇形弧+颜色环) | WPF 自定义控件(OnRender 绘图) | Level 驱动变色的终点(第 8 站 64 行) |
| **ReportService/ReportExporter** | LINQ 聚合 + ClosedXML 导 Excel | LINQ(第 6 站)+ 第三方库 | 第 8 站 ExportReport 已串过 |

**看懂这页图的标志**:你发现每个"新模块"都没有新魔法——**接口抽象、事件广播、Channel 排队、批量处理、双写、MVVM 六板斧反复组合**。这就是"吃透"的终极形态。

> 📚 **本站讲义直达**:[M1 串口](M1_串口通信_深度版.md) · [M2 Modbus](M2_Modbus_深度版.md) · [M3 PLC](M3_PLC_深度版.md) · [M7 MQTT](M7_OPCUA_MQTT_深度版.md) · [M10 报表](M10_报表_深度版.md) · [M11 TCP](M11_TCP_Socket_深度版.md) · [M16 CAN/USB-HID](M16_更多工业总线_CAN_USB_深度版.md) · [M17 安全/MES](M17_工业安全与MES对接_深度版.md) · [M18 配方](M18_配方管理_深度版.md) · [M19 调试](M19_问题排查与调试_深度版.md)

---

## 附录 A · 面试串联(把九站拧成一股绳)

### 30 秒版(简历上那句话的口头版)

> "我完整做了一个工业数据采集监控系统:设备层用 IDevice 统一抽象 7 种设备,采集层用 Channel 做生产者-消费者管道、200ms 批量放行防 UI 洪泛,存储层内存索引加 SQLite 异步串行双写,报警引擎带边沿触发和回滞带,上层 WPF 用 MVVM + Dispatcher 批量刷新,DI 组合根装配全部服务,85 个单元测试全绿。"

### 2 分钟版(面试官说"详细讲讲你的项目")

按**一条数据的旅程**讲(顺序即第 2~8 站):
1. **数据形状**:SensorPoint 结构体,值+时间戳不可分,struct 防高频 GC(第 2 站)
2. **设备抽象**:IDevice 四动作一事件,DeviceBase 固化状态机与广播口,可插拔可 mock(第 3 站)
3. **数据源头**:模拟设备 Task.Run 循环产数,CancellationToken 控启停(第 4 站)
4. **管道**:回调只 TryWrite,后台攒批,满 500 或 200ms 放行,锁内换引用锁外广播(第 5 站)
5. **双写**:内存索引服务实时,Channel 单消费者串行落 SQLite 满足单写者,断电只丢毫秒级样本(第 6 站)
6. **报警**:规则数据分离 + 边沿触发 + 回滞带,防报警泛滥(第 7 站)
7. **界面**:MVVM 手动响应式,Dispatcher 包整批,权限&&状态双条件按钮(第 8 站)
8. **装配**:组合根一行注册换设备全局生效(第 9 站)

### 面试官连环追问 Top 10(每题 15 秒速答)

| 追问 | 答案钩子(都在正文) |
|---|---|
| Channel 和 BlockingCollection 区别? | 异步唤醒不占线程 vs 线程阻塞等待;Delay vs Sleep 同理(第 4 站 64 行) |
| 为什么 TryWrite 不是 WriteAsync? | Unbounded 永不满,同步 TryWrite 纳秒级返回,回调必须极轻(第 5 站 41 行) |
| Dispatcher.Invoke 和 BeginInvoke? | 同步等结果 vs 异步发完就走;批量场景用哪个看要不要背压(第 8 站 181 行) |
| 双写不一致怎么办? | 实时路径只信内存,历史查询走库,两者受众不同;强一致另有 WAL 方案(第 6 站追问) |
| struct 和 class 怎么选? | 高频小数据 struct 防_GC;EF 实体/UI 绑定必须 class(第 2/6/8 站三处对照) |
| 事件泄漏怎么防? | += 必须配 -=,管道 Dispose 逐设备退订(第 5 站 77 行) |
| 为什么 UI 不直接订阅设备? | 100Hz×多设备直接打爆 UI 线程;管道批量节流(第 5 站"病与药") |
| 阈值附近抖动怎么办? | 回滞带:触发 102/恢复 98,施密特触发器(第 7 站灵魂段) |
| async void 什么时候合法? | 仅事件回调,且内部全包 try-catch(第 1 站追问 1) |
| 换成真 PLC 要改多少? | 一行:组合根注册换类,上层零改动(第 9 站 110 行) |

---

## 附录 B · 工业术语 ↔ 大白话总对照表(40 词)

**通信协议类**
| 术语 | 大白话 | 本文首次出现 |
|---|---|---|
| 上位机/下位机 | 监控软件/车间设备 | 第 2 站 |
| 点位(Point/Tag) | 一个被监控的测量点 | 第 2 站 |
| 采样 | 定时读一次数 | 第 2 站 |
| 寄存器地址 | 设备内存的格子编号 | 第 3 站 27 行 |
| 轮询(Polling) | 上位机反复主动问 | 第 3 站(对照事件推模式) |
| 帧(Frame) | 一次通信的完整字节包(头+数据+校验) | 第 10 站指路 |
| 粘包/半包 | 两次发的连成一次到/一次发的分两次到 | 第 10 站指路 |
| CRC 校验 | 帧尾防篡改指纹 | M2 讲义 |
| 字节序(大小端/CDAB) | 多字节值的排列顺序 | ByteOrderLab |
| 报文/协议 | 说什么话/说话的格式 | M2 讲义 |

**数据与存储类**
| 术语 | 大白话 | 本文 |
|---|---|---|
| 双写 | 柜台价目牌+仓库流水账 | 第 6 站 |
| 内存索引 | 柜台那块牌(List+Dictionary) | 第 6 站 |
| 持久化/落库 | 写进硬盘文件 | 第 6 站 |
| 单写者 | SQLite 同一时刻只让一个人写 | 第 6 站 |
| 幂等 | 重复执行不产生副作用 | 第 4/9 站 |
| 领域模型/持久化模型 | 工装/西装 | 第 6 站 |
| 仓储模式(Repository) | 存储细节全关仓库里,柜台只出货 | 第 6 站 |
| 种子数据(Seed) | 首次运行预置的初始数据 | 第 9 站 |

**报警与控制类**
| 术语 | 大白话 | 本文 |
|---|---|---|
| 阈值 | 报警的及格线 | 第 7 站 |
| 回滞(Hysteresis) | 空调式迟滞:触发恢复两条线 | 第 7 站 |
| 边沿触发 | 只在状态翻转瞬间响 | 第 7 站 |
| 报警泛滥(Alarm Flood) | 警报响到没人理 | 第 7 站 |
| IEC 60073 | 界面颜色国际标准(红黄绿灰) | 第 2 站 |
| 审计日志 | 谁何时干了什么,法律级登记本 | 第 1 站 |
| 配方(Recipe) | 工艺参数包,版本化可回滚 | 第 10 站 |
| 插补 | 两轴配合走出斜线/曲线 | M5/MC 讲义 |

**软件架构类**
| 术语 | 大白话 | 本文 |
|---|---|---|
| 组合根(Composition Root) | 总装车间,唯一知道所有具体类的地方 | 第 9 站 |
| 依赖注入(DI) | 中央装配员统一发零件 | 第 9 站 |
| 生命周期(Singleton 等) | 零件是共用一个还是每次新造 | 第 9 站 |
| 开闭原则(OCP) | 加新设备写新类,不改老代码 | 第 3 站 |
| 生产者-消费者 | 一边生产一边消费,中间排队 | 第 5 站 |
| 背压(Backpressure) | 下游跟不上时缓冲层保命 | 第 5 站 |
| 节流/攒批 | 大巴坐满发车+到点发车 | 第 5 站 |
| MVVM | 数据和长相彻底分家 | 第 8 站 |
| Dispatcher | 后厨上菜必须走的传菜通道 | 第 8 站 |
| 响应式(INPC) | 手动版 Vue reactive | 第 8 站 |
| fire-and-forget | 派出去就不管了(要自己兜异常) | 第 5 站 30 行 |
| 快照(Snapshot) | 拍张照片再处理,真身随便动 | 第 6/7 站 |

---

## 附录 C · 自测 30 问(合上文档,白纸作答;按站分组)

**第 1 站**:① 程序入口方法名?为什么没有 StartupUri?② 登录取消后为什么要 return?
**第 2 站**:③ SensorPoint 为什么是 struct?④ 时间戳为什么由采集源统一打?
**第 3 站**:⑤ IDevice 的 4+1 个成员?⑥ State 为什么 protected set?⑦ RaiseData 为什么不做成 public?
**第 4 站**:⑧ Start 的幂等守卫在哪行,防什么?⑨ Stop 为什么要 Wait(500) 而不是无限等?⑩ Cancel 是怎么"叫醒"Task.Delay 的?
**第 5 站**:⑪ 事件回调里为什么只做一件事?⑫ 攒批的两条放行路径?⑬ 为什么 BatchReady 的 Invoke 放锁外?⑭ Dispose 退订为什么必要?
**第 6 站**:⑮ 双写两层各服务谁?⑯ 写泵为什么 SingleReader?⑰ AsNoTracking 什么时候用?⑱ 断电丢数据的面试话术?
**第 7 站**:⑲ 回滞解决什么病?施密特触发器是什么类比?⑳ 边沿触发靠哪个集合实现?㉑ 规则遍历为什么先拍快照?
**第 8 站**:㉒ PointView 为什么必须是 class+INPC?㉓ Dispatcher.Invoke 为什么包整批?㉔ IsRunning 一变,为什么喊三次 OnChanged?㉕ ConvertBack 为什么 DoNothing?
**第 9 站**:㉖ 组合根是什么,放 Core 还是 UI,为什么?㉗ 换真 PLC 改哪一行?㉘ Seed 为什么先查 Any()?㉙ 数据库文件为什么放 LocalApplicationData?
**全景**:㉚ 白纸画出一条数据的完整旅程(从 SimulatedDevice 到表盘变红),标出经过的每个类和方法名。

**及格线**:㉚ 必须对 + 前 29 问 ≥ 22 对 = 项目吃透,去投简历。

---

## 附录 D · 高频知识点易错点急救手册(面试 + 排错双用)

> **怎么用**:每个点四段式——💥现场(真实报错/症状,可直接当搜索词)→ 🔍根因(大白话)→ ❌✅写法对照 → 🎤面试官怎么问。
> 这 10 个点覆盖本项目 90% 的翻车现场;面试官"会出什么问题"类追问,八成从这里出。

> 📚 **每个点的讲义出处**(想系统学,点过去):
> D1 跨线程 → [M0 Day7](M0_每日讲义_深度版.md)/[M8](M8_工程化收尾_深度版.md) · D2 async 陷阱 → [C# 陷阱](C#_陷阱_前端转上位机必看_深度版.md)/[速查 §8](CSharp语法速查_前端视角.md) · D3 泄漏 → [M9.5 压测](M9.5_性能压测与长跑稳定性_深度版.md)/[M9](M9_工程素养_测试DI容错_深度版.md) · D4 struct → [M0](M0_每日讲义_深度版.md)/[速查](CSharp语法速查_前端视角.md) · D5 绑定 → [M8](M8_工程化收尾_深度版.md)/[WPF 速查](WPF_XAML_速查_深度版.md) · D6/D7 EF+SQLite → [M4](M4_数据持久化_深度版.md) · D8 粘包 → [M11](M11_TCP_Socket_深度版.md)/[M1](M1_串口通信_深度版.md) · D9 字节序 → [M2](M2_Modbus_深度版.md) · D10 资源释放 → [M1](M1_串口通信_深度版.md)/[M9](M9_工程素养_测试DI容错_深度版.md)

### D1 · 跨线程访问 UI(Dispatcher)—— 翻车率第一名

💥 **现场**:
```
System.InvalidOperationException: 调用线程无法访问此对象,因为另一个线程拥有该对象。
```
触发场景:在设备事件回调/Task.Run/Timer 回调里直接改 UI 控件(本项目 OnBatchReady 若不包 Dispatcher 就是这个下场)。

🔍 **根因**:WPF 规定**界面上每个控件只能被创建它的线程(UI 线程)访问**。后台线程直接摸控件 = 闯进别人家里改东西,直接抛异常。前端类比:浏览器不允许 worker 线程直接操作 DOM,必须 postMessage 回主线程——一模一样的保护机制。

❌ **错误写法**:
```csharp
_pipeline.BatchReady += (s, batch) =>
{
    Points.Add(...);            // ❌ BatchReady 在后台线程触发,当场崩
};
```
✅ **正确写法**(本项目 MainViewModel.cs 181 行):
```csharp
_pipeline.BatchReady += (s, batch) =>
{
    Application.Current.Dispatcher.Invoke(() =>   // ✅ 快递到 UI 线程执行
    {
        Points.Add(...);
    });
};
```

🩹 **变种坑——Dispatcher 死锁**:后台线程 `Dispatcher.Invoke`(同步等 UI 线程干完),而 UI 线程此刻正在 `.Wait()` 等这个后台任务 → 互相等,程序卡死无报错。**解法**:UI 线程永远不要同步等后台;后台刷 UI 用 `BeginInvoke`(发完就走)或确保 UI 线程不被占。

🎤 **面试官怎么问**:"Invoke 和 BeginInvoke 区别?"——同步等结果 vs 异步发完就走;"怎么批量刷新?"——包整批不是逐条(第 8 站)。

### D2 · async/await:同步等异步(.Result/.Wait())= 死锁

💥 **现场**:程序**无报错卡死**,断点发现停在 `await` 之后的代码永远不执行。典型触发:UI 线程里写 `var data = LoadAsync().Result;`

🔍 **根因**(经典死锁四步):①UI 线程调 `.Result` 同步等待 ②异步方法内部 `await` 完成后想**回到 UI 线程**继续(WPF 的 await 默认捕获 UI 上下文)③可 UI 线程正被 `.Result` 占着,谁也不让谁 ④死锁。前端类比:你在单线程 JS 里"同步等待一个 Promise"——根本不允许,所以 JS 程序员没见过这种死锁,转 C# 第一个大坑。

❌ **错误写法**:
```csharp
private void Button_Click(object s, EventArgs e)
{
    var data = LoadDataAsync().Result;   // ❌ UI 线程同步等异步 = 死锁
}
```
✅ **正确写法**:
```csharp
private async void Button_Click(object s, EventArgs e)
{
    var data = await LoadDataAsync();    // ✅ 异步一路到底
}
```

🩹 **变种**:`async void` 方法里不 try-catch → 异常直接崩进程(事件回调是 async void 唯一合法场景,见第 1 站追问);`Task.Delay` 写成 `Thread.Sleep` → 线程抱着睡,UI 卡(第 4 站 64 行讲过)。

🎤:"为什么 .Result 危险?"——按上面四步讲;"async void 什么时候能用?"——只有事件回调,且全包 try-catch。

### D3 · 事件订阅泄漏(+ 内存泄漏四类)

💥 **现场**:程序越跑内存越大(dotMemory 里对象几万个);或**同一条数据,处理逻辑被执行了 N 次**(窗口开关几次后回调叠加);或对象早就该没了还一直活着。

🔍 **根因**:`obj.Event += handler` 之后,**发布事件的长命对象会一直抓着订阅者不放**(订阅链表有它一份引用)。订阅者是短命对象(比如一个窗口)→ 窗口关了,事件源还拽着它 → GC 不敢回收 → 泄漏 + 僵尸回调。前端类比:addEventListener 忘了 removeEventListener,单页应用里组件反复挂载就重复触发——同一个病。

❌ **错误写法**:
```csharp
var win = new DetailWindow();
_pipeline.BatchReady += win.OnBatch;   // ❌ 窗口关了,订阅还在
```
✅ **正确写法**(本项目 AcquisitionPipeline.cs 77 行的规矩):
```csharp
// 订阅与退订必须成对出现(窗口 Closed 时 / 管道 Dispose 时)
_pipeline.BatchReady += win.OnBatch;
win.Closed += (_, _) => _pipeline.BatchReady -= win.OnBatch;   // ✅ 善终
```

🩹 **泄漏四类速查**(M9.5 主题,面试可整题背):①**静态集合**只加不减(List 永远长大)②**事件订阅**不退订(本条)③**Timer/线程**不停(定时器抓着回调对象)④**非托管资源**不 Dispose(串口/文件/Bitmap)。排查工具:dotMemory / `dotnet-counters monitor`。

🎤:"你项目怎么防泄漏?"——背第 5 站 Dispose 五连 + 订阅成对。

### D4 · struct 值拷贝:改了值"没生效"

💥 **现场**:从存储里取出点位,改了 `Value`,再查——**没变**。无报错,纯逻辑错,最阴险。

🔍 **根因**:struct 赋值 = **复印**(第 2 站)。`var p = store.Get(id)` 拿到的是复印件,你改的是复印件,原件(存储里的)纹丝不动。前端类比:JS 里 `const a = {x:1}; const b = {...a}; b.x = 2`——b 变了 a 不变,因为展开是拷贝。区别是 **struct 的拷贝是自动且无声的**,你不知道哪一下赋值就复印了。

❌ **错误写法**:
```csharp
var p = _store.Get(1);      // struct:拿到的是复印件
p.Value = 99;               // ❌ 改的是复印件,存储里还是旧值
```
✅ **正确写法**:
```csharp
var p = _store.Get(1);
p.Value = 99;
_store.AddOrUpdate(p);      // ✅ 整体写回去(复印件签收盖新章)
// 或者:该类型需要频繁就地修改 → 一开始就设计成 class
```

🩹 **变种**:`foreach` 迭代变量也是只读的,循环里改结构体成员直接编译错;struct 里放 `List<T>` 字段 = 灾难(浅拷贝后两个结构体共享同一个 List,一改都改)。

🎤:"SensorPoint 为什么 struct?struct 有什么坑?"——高频 GC 场景用 struct(第 2 站),坑就是值拷贝语义,所以本项目对它只"整体替换"从不"原地改"(PointStore 70-73 行)。

### D5 · WPF 绑定不刷新三连(界面"死了")

💥 **现场**:数据明明变了,界面纹丝不动。**无任何报错**(或输出窗口里一条静默 Binding error,新手根本不看输出窗口)。

🔍 **根因**(三种,对症下药):
| 症状 | 根因 | 药 |
|---|---|---|
| 属性变了不刷 | 属性 setter 忘了调 `OnChanged()` | INPC 模板(第 8 站 8.2) |
| 列表增删不刷 | 用了 `List<T>` 而不是 `ObservableCollection<T>` | 换 ObservableCollection |
| 列表里**元素属性**变了不刷 | ObservableCollection 只管"行增删",**不管行内属性** | 元素类自己实现 INPC(PointView 存在的全部意义) |

❌ **错误写法**:
```csharp
public class BadPoint                       // ❌ 普通类直接绑
{
    public double Value { get; set; }       // ❌ 无通知,改了白改
}
Points.Add(badPoint);  badPoint.Value = 99; // 界面:不动
```
✅ **正确写法**:
```csharp
public double Value
{
    get => _value;
    set { _value = value; OnChanged(); }    // ✅ 喊话,界面自动刷新那一格
}
```

🩹 **变种**:DataContext 赋值时机晚于 InitializeComponent 之后的初始化逻辑 → 绑定暂时找不到源(本项目 MainWindow 用 DataContextChanged 事件接住 VM,第 8 站 8.4,就是防这个)。

🎤:"INotifyPropertyChanged 相当于前端什么?"——手动版响应式:Vue 用 Proxy 拦截自动触发,WPF 要你在 setter 里手动喊话;"ObservableCollection 呢?"——自动通知增删的响应式数组,但元素属性还得元素自己通知。

### D6 · EF Core:DbContext 线程不安全 + 生命周期

💥 **现场**:
```
System.InvalidOperationException: A second operation was started on this context
instance before a previous operation completed. This is usually caused by different
threads concurrently using the same instance of DbContext.
```

🔍 **根因**:DbContext **不是线程安全的**(设计如此),两个线程同时用一个实例查询/保存就抛上面这个。且它设计是**短命的**(一个工作单元一个实例),长期持有还会把跟踪的实体全攒在内存里。新人最自然的设计——"单例 DbContext 全项目共用"——恰好两头全踩。

❌ **错误写法**:
```csharp
services.AddSingleton<AppDb>();             // ❌ 单例 DbContext:线程炸 + 内存涨
```
✅ **正确写法**(本项目 PointStore/Bootstrapper 的方案):
```csharp
services.AddDbContextFactory<AppDb>(...);   // ✅ 注册工厂
await using var db = await _dbFactory.CreateDbContextAsync();   // ✅ 用完即弃
// 写入高频场景:再用 Channel 把写操作串行化(第 6 站写泵)
```

🩹 **变种坑**:①纯读查询忘 `AsNoTracking()` → 内存和 CPU 白烧(第 6 站 106 行)②LINQ 写了不执行,`ToList()` 才翻译成 SQL(IQueryable 是"查询计划",不是结果)③返回 `IQueryable` 给上层 = 上层一个 `Count()` 又打一次数据库,查询逃逸出仓储层。

🎤:"DbContext 生命周期怎么管理?"——短命 + 工厂 + 高频写串行化,本项目三件套答案。

### D7 · SQLite:database is locked

💥 **现场**:
```
Microsoft.Data.Sqlite.SqliteException: SQLite Error 5: 'database is locked'.
```
高频触发:多线程同时 SaveChanges;或 UI 在查、后台在写。

🔍 **根因**:SQLite 是**文件级单写者**数据库——同一时刻只允许一个写连接。它不是 bug,是 SQLite 的本性(轻量的代价)。MySQL/SQL Server 有行级锁没这问题,但轻量场景(单机工控)选 SQLite 就得遵守它的规矩。

❌ **错误写法**:
```csharp
// 多个线程各自 CreateDbContext 然后 SaveChangesAsync   // ❌ 撞车:database is locked
```
✅ **正确写法**(本项目第 6 站写泵全套):
```csharp
// ① 所有写请求进同一个 Channel(排队取号)
_writeQueue.Writer.TryWrite(rec);
// ② 单消费者按 FIFO 顺序写(SingleReader = true)——天然错不开
await foreach (var rec in _writeQueue.Reader.ReadAllAsync())
    { /* 一个窗口办业务 */ }
// ③ 补充弹药(连接串加):  Cache=Shared;Default Timeout=3  → 锁等待而非立刻炸
```

🎤:"SQLite 并发写怎么办?"——先说约束(单写者)再说方案(写队列串行化/WAL 模式/busy_timeout),能说出"这是架构约束不是 bug"就赢了。

### D8 · 串口/TCP 粘包半包:数据"偶尔"错

💥 **现场**:最折磨人的症状——**大部分时候对,偶尔解析出乱值/丢一条/两条并一条**。测试环境怎么都复现不了,现场偶发。串口报 CRC 校验失败;TCP 收到的 buff 长度忽长忽短。

🔍 **根因**:你以为"对方发一次,你收一次"。**实际**:串口/TCP 只是**字节流管道**,不保证边界。对方连发两条短消息可能被你一次 Read 全收走(粘包);一条长消息可能分两次到(半包)。网络一抖、串口一缓冲,边界就乱。前端类比:WebSocket 收 message 有边界是因为协议帮你切好了;C# 裸 Socket/SerialPort 给你的是"水管",自己拿桶接。

❌ **错误写法**:
```csharp
port.DataReceived += (s, e) =>
{
    var data = new byte[sp.BytesToRead];
    sp.Read(data, 0, data.Length);
    var point = Parse(data);        // ❌ 假设"一次到达 = 一条完整消息"
};
```
✅ **正确写法**(帧协议三件套:头 + 长度 + 校验,配状态机):
```csharp
// 约定帧: [帧头 AA 55][长度 N][数据×N][CRC×2]
_buffer.Append(newBytes);                    // ① 新字节先进缓冲区,永远不丢
while (true)
{
    if (!_buffer.StartsWith(HEAD)) { _buffer.TrimToNextHead(); continue; }  // ② 找头
    if (_buffer.Count < MIN_FRAME_LEN) break;            // ③ 不够一帧 → 等下一次
    int len = _buffer[2];
    if (_buffer.Count < 3 + len + 2) break;              // ③ 半包 → 留着继续攒
    if (!Crc16.Verify(_buffer, 0, 3 + len + 2)) { _buffer.RemoveHead(); continue; }  // ④ 校验坏→丢头重同步
    yield return _buffer.TakeFrame();                    // ⑤ 完整一帧才交出去
}
```

🎤:"TCP 粘包怎么解决?"——标准答案:**应用层定义消息边界**(定长/分隔符/长度前缀),接收端缓冲 + 按边界切,本项目 TcpFrameParser 就是长度前缀方案(第 10 站指路)。追问"为什么 TCP 有粘包 UDP 没有"——TCP 是字节流协议,UDP 数据报天然保边界。

### D9 · 字节序错乱:数值离谱(0.0001 或巨大数)

💥 **现场**:Modbus 读浮点温度,**显示 0.0000305 或 -1325843.75** 这种离谱值;或两台"一样"的设备,一台读出来正常一台离谱。整数有时正常,浮点必乱。

🔍 **根因**:一个 float 占 4 字节(2 个保持寄存器),**这 4 个字节的排列顺序(字节序)没有全球统一标准**——厂商 A 发 ABCD,厂商 B 发 CDAB,还有 BADC/CBAB。库/解析按错的序拼字节 = 拼出一个合法但完全错误的 float。这是**数据能对上但值离谱**类问题的第一嫌疑犯。ByteOrderLab(你 L0 做过的实验)就是为它建的。

❌ **错误写法**:
```csharp
var value = BitConverter.ToSingle(bytes, 0);   // ❌ 默认序解析,厂商是 CDAB 就翻车
```
✅ **正确写法**:
```csharp
// ① 抓帧/用测试从站拿到真实字节(如 42 C8 00 00 应为 100.0)
// ② 对照排查:手册标 ABCD?实物发的什么序?
// ③ 显式按正确序拼:
Array.Reverse(bytes, 0, 2);                    // 交换两个寄存器(CDAB→ABCD)
Array.Reverse(bytes, 2, 2);
var value = BitConverter.ToSingle(bytes, 0);   // ✅ 100.0
// 本项目 ModbusFrameParser.ByteOrder 枚举可配,抓帧确认后指定
```

🎤:"两台 Modbus 设备读浮点,一台对一台错,查什么?"——先答字节序(ABCD/CDAB),再说抓帧确认、解析器可配置。这题是**真实现场题**,答出"抓帧"两个字面试官就知道你干过活。

### D10 · 资源不释放:端口占用/程序杀不死

💥 **现场**(三个变种,同一个病根):
```
System.UnauthorizedAccessException: Access to the port 'COM3' is denied   ← 串口没关,再开报这个
窗口全关了,进程还在(VS 调试不结束)                                          ← 后台线程/Timer 没停
程序退出后 SQLite 文件还被占用,删不掉                                        ← 连接没释放
```

🔍 **根因**:串口句柄、Socket、文件句柄、Timer、后台线程都是**非托管/长命资源**,GC 只管内存不管它们。窗口关了 ≠ 资源放了;局部变量出作用域 ≠ 端口关了。**必须显式 Dispose/Close**。前端类比:addEventListener 忘 remove、setInterval 忘 clear——JS 里只泄漏内存,C# 里还锁着硬件。

❌ **错误写法**:
```csharp
var port = new SerialPort("COM3");  port.Open();
// ...用完不管了                                                    // ❌ 端口被占,下次 Open 抛异常
```
✅ **正确写法**:
```csharp
using var port = new SerialPort("COM3");       // ✅ 离开作用域自动 Close(哪怕中途 return/抛异常)
port.Open();
// 长命资源(Timer/CTS/事件订阅)→ IDisposable 里统一善后(本项目管道 Dispose 五连,第 5 站 72-79 行)
```

🩹 **排查口诀**:进程杀不死 → 任务管理器看线程数,99% 是前台线程(不带 IsBackground=true 的线程或没停的 Timer)还活着。

🎤:"using 的本质?"——语法糖,展开成 try-finally 保证 Dispose;"你的程序怎么保证资源不泄漏?"——引用第 5 站 Dispose 五连 + DI 容器统一销毁链(App.xaml.cs 53 行)。

---

### 这 10 个点的共同规律(比点本身更重要)

数一遍:D1/D2 是**线程模型**不懂,D3/D10 是**资源生命周期**不管,D6/D7 是**外部系统的约束**不熟,D4/D5 是**语言机制**理解浅,D8/D9 是**物理世界的真相**(字节流无边界/字节序无标准)。

> **上位机工程师的核心能力,就是替 UI、替数据库、替硬件把这些"不守规矩的东西"守好规矩。** 面试官问易错点,本质在问:你知道这行的世界有多乱吗?你现在知道了。

---

## 附录 E · 项目操作速查(防尴尬手册——面试前 10 分钟最后翻这页)

> **为什么有这页**:面试官验证"项目是不是你做的",第一问往往不是架构,是**操作层事实**——怎么启动/入口在哪/账号多少/数据库文件在哪。知识点背得再熟,这些答不上当场露馅。这页把"一个真跑过项目的人闭着眼都知道的事"一次备齐。
> **检验标准**:下面每一条,你要能**不看不查脱口而出**。

### E1 · 项目怎么启动(3 种方式,至少背熟 2 种)

```bash
# 方式①命令行(在 DAQMonitor/ 目录下)
dotnet build DaqMonitor.sln                    # 编译
dotnet run --project src/DaqMonitor.UI         # 启动(最常用)

# 方式② Visual Studio / VS Code
#   打开 DaqMonitor.sln → DaqMonitor.UI 设为启动项目 → F5(调试)/ Ctrl+F5(不调试)

# 方式③ 直接跑 exe(已编译过)
#   src/DaqMonitor.UI/bin/Debug/net8.0-windows/DaqMonitor.UI.exe
```
> 💡 框架是 **net8.0-windows + RollForward Major**——机器上装了 .NET 8/9/10 桌面运行时都能跑(这就是内网机器能直接跑 exe 的原因)。

### E2 · 项目入口在哪里(必考 + 一个"你知道吗")

- **代码入口**:`App.xaml.cs` 的 `OnStartup`(导读第 1 站逐行讲过):装配 DI → 弹登录窗 → 注册设备 → 开主窗。
- **为什么没有 Program.cs / Main?**——WPF 的 Main 是**编译器根据 App.xaml 自动生成**的(`App.xaml` 里没写 StartupUri,所以启动逻辑全在 OnStartup 手动控制)。**这一问答出来,面试官立刻知道你真写过 WPF。**
- 启动链条一句话:"App.OnStartup → Bootstrapper.Build 装配容器 → 登录窗拦截 → pipeline.Register(device) + Connect → MainViewModel/MainWindow 显示"。

### E3 · 解决方案里几个项目,各自干嘛

| 项目 | 类型 | 角色 |
|---|---|---|
| **DaqMonitor.Core** | 类库 | 大脑:设备/管道/存储/报警/DI 组合根,**不依赖 UI** |
| **DaqMonitor.UI** | WinExe(WPF) | 脸面:App 入口 + MVVM + 视图 |
| **DaqMonitor.Tests** | xUnit | 85 个测试的守护线 |

依赖方向只有一条:UI → Core,Tests → 两者。(Tests 本文档不讲,但**你要知道它有、有几个、怎么跑**。)

### E4 · 登录账号(启动后第一件事,忘了直接卡死)

| 账号 | 密码 | 角色 | 能干嘛 |
|---|---|---|---|
| **admin** | admin123 | 管理员 | 全部权限 |
| engineer | engineer123 | 工程师 | 改配方/参数 + 导报表 |
| operator | operator123 | 操作工 | 只读 + 启停采集 |

> 账号从哪来:Bootstrapper 的 `SeedDefaultUsers`(首次运行自动种入,BCrypt 加密,幂等)。

### E5 · 数据库文件在哪(第 6 站 ✂️ 实验的答案,直接背)

```
C:\Users\<你>\AppData\Local\DaqMonitor\daq.db
```
- 位置由 Bootstrapper 决定(`LocalApplicationData`:用户可写/按用户隔离/卸载不误删)
- SQLite 单文件,VSCode 装 SQLite 插件即可打开,主表 `sensor_record`(蛇形命名)
- 首次运行自动建库建表(`EnsureCreated`),删了它重启程序会重建(种子数据回来,历史数据没了)

### E6 · 测试怎么跑 + 数字

```bash
cd DAQMonitor && dotnet test     # 输出: 失败 0,通过 85
```
背三个数:**85 个测试 / 0 失败 / 覆盖协议解析(CRC/粘包)、报警回滞、双写持久化、重试、心跳**。

### E7 · 依赖了哪些包(被问"用了什么库"时报得出来)

| 层 | 包 | 干嘛 |
|---|---|---|
| Core | EF Core Sqlite 8 | 持久化 |
| Core | MQTTnet 4.3 | 上云 |
| Core | System.IO.Ports | 串口 |
| Core | BCrypt.Net-Next | 密码哈希 |
| Core | Microsoft.Extensions.DI | 容器 |
| UI | LiveCharts2(rc4.5) | 曲线 |
| UI | ClosedXML | Excel 报表 |
| Tests | xUnit + Moq | 测试 |

> 一句话版本:"EF Core + MQTTnet + System.IO.Ports + LiveCharts2 + ClosedXML + xUnit/Moq,全免费开源,无任何付费控件"——最后半句是加分项(工业界付费框架多,你刻意选了免费栈)。

### E8 · 30 秒现场演示剧本(面试官说"给我看看"时照做)

1. `dotnet run --project src/DaqMonitor.UI` → 弹登录窗 → 输 **admin/admin123** 回车
2. 主窗出现 → 点 **「启动采集」** → 实时点位表开始跳,仪表盘指针动
3. 等 10~20 秒 → 某点位值冲过 100 → **仪表盘变橙/红 + 报警日志 Tab 出现记录**(回滞:值回落几秒后自动复绿)
4. 切「实时曲线」Tab 看波形;点「停止采集」数据停;「导出报表」选桌面 → 出 Excel
5. 收尾一句:"底层是 100Hz 模拟设备 → Channel 缓冲 → 200ms 批量刷新,换成真实 Modbus 设备只改 Bootstrapper 一行注册。"

### E9 · "所有权 10 连问"速答表(自测:能秒答几个?)

| 问 | 答(一句话) |
|---|---|
| 怎么启动? | E1 方式① |
| 入口在哪? | App.xaml.cs 的 OnStartup,WPF 的 Main 由 App.xaml 生成 |
| 几个项目? | 3 个:Core(领域)/UI(界面)/Tests(85 测试) |
| 数据库在哪/什么库? | E5 路径,SQLite,EF Core + EnsureCreated |
| 测试几个怎么跑? | dotnet test,85/85 绿 |
| 框架什么版本? | .NET 8(LTS),net8.0-windows,RollForward Major |
| git 地址? | github.com/LuQuanLong0806/scada-learning(README 有三步运行说明) |
| 怎么打包发布? | `dotnet publish` 出绿色目录可分发;MSIX 安装包在计划中(M8) |
| 项目哪来的/多久? | 见下方话术 |
| 设备是真是假? | "演示用模拟设备(可测试性设计);真实 Modbus 接入验证过,Bootstrapper 换一行注册" |

**≥8 个秒答 = 过关;哪个卡壳,今天就打开项目亲手做一遍那个动作。**

### E10 · 项目来源怎么讲(诚实话术,别踩坑)

- ✅ **这么说**:"系统学习上位机技术后,按企业规范开发的个人项目——分层架构/接口抽象/测试都是对标真实工程做法,85 个测试守护,代码在 GitHub。"
- ❌ **别说**:"公司项目"(追问公司业务/同事/上线细节,两问穿帮,且诚信污点比技术不足严重十倍)。
- 个人项目减分吗?——**对初级岗不减分,加分的是工程完成度**(能跑/有测试/文档全/敢开源)。面试官见过太多"公司项目"讲不清,一个能现场跑的个人项目反而稀缺。

---

## 结语:读码的顺序就是数据的顺序

九站走完,你发现规律了吗:**这份导读的站序 = 一条数据的诞生到谢幕**。以后读任何陌生工程,也用这把钥匙:
1. 先找**入口**(Program/App/Startup)→ 2. 找**数据形状**(Models)→ 3. 找**抽象合同**(接口)→ 4. 顺着**一条数据**走到黑。

> 配套打卡:每读完一站做✂️实验;九站做完做附录 C;附录 C 及格 → 对照《1v1教练_项目吃透计划》L7 模拟面试 → 投简历。

*本文基于 2026-08-25 版本代码(85 测试全绿基线)逐行核对写成。若工程后续演进(如 Prism 重构、TcpDevice 落地),以代码为准,术语与套路不变。*
