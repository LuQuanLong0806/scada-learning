# R1 · 工程骨架 + 领域模型(把空房子立起来)

> **定位**:入职第一件事——把解决方案立起来,定义全项目共用的数据类型。
> **前置**:R0 精读 + 环境检查通过。**预计敲码**:40 分钟。
> **产出**:三项目解决方案 + Models,`dotnet build` 0 错误 0 警告。

---

## 🎯 本篇交付物

```
DaqMonitor/
├─ DaqMonitor.sln(x)            # 解决方案
└─ src/
   ├─ DaqMonitor.Core/          # 领域层:所有业务逻辑(和 UI 无关)
   │  └─ Models/SensorPoint.cs  # 全项目的数据货币
   ├─ DaqMonitor.UI/            # WPF 展示层(R8 才长出血肉)
   └─ DaqMonitor.Tests/         # xUnit 测试层(R2 起每个迭代往里加)
```

## 📋 需求单(先自己想怎么做,再往下看参考实现)

| # | 需求 | 验收 |
|---|---|---|
| FR1-1 | 解决方案含三个项目:Core(类库)/UI(WPF)/Tests(xUnit),统一 net8.0 | `dotnet sln list` 三项齐全 |
| FR1-2 | UI 引用 Core;Tests 引用 Core(UI 不被任何人引用) | 引用关系正确 |
| FR1-3 | 本机只装了 .NET 10 运行时的,net8.0 工程要能跑起来 | `dotnet build` 不报运行时错误 |
| FR1-4 | 领域模型:设备状态枚举、报警级别枚举、采样点结构体、报警记录结构体 | 代码可编译 |
| FR1-5 | 删干净模板垃圾(Class1.cs / UnitTest1.cs) | 无僵尸文件 |

**先自己想 2 分钟**:领域模型里"一个采样点"你准备用什么类型?class 还是 [struct](kp:struct-vs-class)?为什么?

## 📚 本篇知识点

- [struct vs class](kp:struct-vs-class) —— 为什么 SensorPoint 用 struct
- 分层:Core 不依赖 UI(编译方向决定耦合方向,面试常问)

## 🛠️ 参考实现

### ① 建解决方案 + 三项目

```bash
# 在你的工作目录执行(别在讲义仓库里建!)
mkdir DaqMonitor && cd DaqMonitor
dotnet new sln -n DaqMonitor

mkdir src && cd src
dotnet new classlib -n DaqMonitor.Core
dotnet new wpf     -n DaqMonitor.UI
dotnet new xunit   -n DaqMonitor.Tests
cd ..
```

> ⚠️ **SDK 10 生成的是 `DaqMonitor.slnx` 不是 `.sln`**——新格式(XML),和 .sln 完全等价,所有 `dotnet sln` 命令照用,不用管。
> ⚠️ **别加 `-f net8.0`**:新版 SDK 的 classlib/xunit 模板已不提供 net8.0 选项(会报"无效选项")。先按默认建,下一步改 csproj——真实开发也常这么干。

### ② 挂进解决方案 + 引用关系

```bash
dotnet sln add src/DaqMonitor.Core src/DaqMonitor.UI src/DaqMonitor.Tests
dotnet add src/DaqMonitor.UI reference src/DaqMonitor.Core
dotnet add src/DaqMonitor.Tests reference src/DaqMonitor.Core
```

**期望输出**(节选):
```
已将项目“src/DaqMonitor.Core”添加到解决方案中。 ×3
已添加引用 “..\DaqMonitor.Core\DaqMonitor.Core.csproj” ×2
```

### 🏗️ 为什么这样设计:为什么是三个项目、依赖只能从外向内?

**当时面临的选择**:

| 方案 | 优点 | 代价 |
|---|---|---|
| 单项目全塞一起 | 建得快、不用管引用 | UI 和业务编译耦合,以后换皮/跑无界面测试都要大改 |
| Core/UI/Tests 三项目,UI、Tests 引用 Core(选定) | 多一步 `dotnet add reference` | 依赖方向被**编译器强制**成单向 |

**为什么选它**:Core 不知道 UI 存在——不是"约定不许引用",是**编译上引用不了**(没加项目引用就 using 不了)。业务逻辑天然可以被任何壳复用:今天是 WPF,明天换 WinForms、换成 Windows 服务跑在无头工控机上,Core 一行不改。测试项目只引用 Core,跑测试不用加载任何 UI 程序集。

**不这样会怎样**:单项目里把采集逻辑写进 MainWindow.xaml.cs,三个月后想给核心逻辑加自动化测试——测试项目必须引用整个 WPF 工程,测试宿主要加载 XAML,又慢又脆。

**🎤 面试一句话**:"我分层的原则是编译方向=依赖方向:UI 引用 Core、Core 谁也不引用。Core 可以被 WPF、WinForms 或无界面服务任意复用,测试也只测 Core 不碰 UI。"

### ③ FR1-1/FR1-3:改 csproj 目标框架为 net8.0 + RollForward

> 📂 三个 `src/*/*.csproj` 的 `<PropertyGroup>` 里,把 TargetFramework 改成 net8.0,再加一行 RollForward
> 💡 模板默认生成 net10.0;本项目对齐参考工程用 net8.0(企业里常见的 LTS 目标)。本机只装了 .NET 10 运行时,`RollForward` 让 net8.0 应用用高版本运行时跑——生产常用技巧,面试可讲

**Core / Tests**(两个文件同样改法):
```xml
    <TargetFramework>net8.0</TargetFramework>
    <RollForward>Major</RollForward>
```

**UI**(注意带 `-windows` 后缀):
```xml
    <TargetFramework>net8.0-windows</TargetFramework>
    <RollForward>Major</RollForward>
```

如果你的机器装了 .NET 8 运行时,RollForward 可省(加了也无害)。

> ⚠️ **漏加的典型症状**(R2 跑 `dotnet test` 才会爆):`You must install or use .NET 8.0 ... framework_version=8.0.0`——build 能过(不加载运行时),test 过不去(测试宿主要真跑 net8.0)。报这个错就回三个 csproj 检查 RollForward。

### 🏗️ 为什么这样设计:为什么目标 net8.0 而不是本机最新的 net10.0?

**当时面临的选择**:

| 方案 | 优点 | 代价 |
|---|---|---|
| net10.0(本机 SDK 默认) | 零配置 | STS 版 18 个月就 EOL;客户工控机装的多是 LTS 运行时 |
| net8.0 + `RollForward.Major`(选定) | LTS 长支持;机器只有更高版本运行时也能跑 | 本机多写一行配置 |

**为什么选它**:工业软件部署在**客户的工控机**,不是你的开发机——现场装什么运行时版本你说了不算。net8.0 是企业主流 LTS;`RollForward.Major` 的语义是"用 8 编译,允许用 9/10 运行时跑",同时拿到"目标 LTS"和"本机能跑"两个好处。前端类比:类似 `browserslist` 目标旧浏览器 + 现代构建——**目标旧、运行新永远可以;目标新、运行旧必炸**。

**不这样会怎样**:目标 net10.0 交付到只装 .NET 8 运行时的客户机器,启动直接报"找不到框架";反过来目标 net8.0 不加 RollForward,在你这台只有 .NET 10 的机器上 `dotnet test` 就过不去(上面的 ⚠️ 就是这个)。

**🎤 面试一句话**:"目标框架选 LTS 的 net8.0 对齐企业部署习惯,本机高版本运行时靠 RollForward.Major 向前兼容——.NET 的兼容方向是单向的,旧目标能跑在新运行时上,反之不行。"

### ④ 删模板垃圾

```bash
rm src/DaqMonitor.Core/Class1.cs
rm src/DaqMonitor.Tests/UnitTest1.cs
```
(UI 的 MainWindow.xaml 留着,R8 会重写它。)

### ⑤ FR1-4:领域模型(全项目的"数据货币")

> 📂 `src/DaqMonitor.Core/Models/SensorPoint.cs` · namespace `DaqMonitor.Core.Models`
> 🔧 无 NuGet(纯 C#)
> 💡 与参考工程一字不差;注意是**公开字段 + 无构造函数**的 struct,统一用对象初始化器赋值

```csharp
using System;

namespace DaqMonitor.Core.Models;

/// <summary>设备状态机:离线→连接中→在线</summary>
public enum DeviceState { Offline, Connecting, Online }

/// <summary>报警级别(注意是 Normal 不是 Info)</summary>
public enum AlarmLevel { Normal, Warning, Critical }

/// <summary>
/// 采集点位——上位机最基础的数据载体。
/// struct:小而高频传递,GC 压力小;字段直接公开,赋值用对象初始化器:
///   var p = new SensorPoint { Id = 1, Value = 20.5, Timestamp = DateTime.Now };
/// </summary>
public struct SensorPoint
{
    public int Id;                  // 点位号
    public double Value;            // 工程量
    public DeviceState State;       // 点位质量(来自设备)
    public DateTime Timestamp;      // 采样时间戳(采集源统一打,下游共用)
}

/// <summary>报警记录(R2-R5 会用到,先立类型)</summary>
public struct Alarm
{
    public int PointId;
    public AlarmLevel Level;
    public double Value;
}
```

> ⚠️ **不写 Timestamp 会怎样**:`new SensorPoint { Id = 1, Value = 10 }` 的 Timestamp 落 `default(DateTime)` = `0001-01-01`。所以后面凡是事件转 SensorPoint,必须抄时间戳——这是本项目最常翻车的点,记住它。

### 🏗️ 为什么这样设计:SensorPoint 为什么是 struct、字段为什么直接公开?

**当时面临的选择**:

| 方案 | 优点 | 代价 |
|---|---|---|
| class | 引用语义,`list[0].Value = 1` 直接生效 | 100Hz × 多点位持续堆分配,Gen0 GC 频繁抖动;到处要判 null |
| struct + 公开字段(选定) | 栈分配零 GC;值拷贝=天然快照 | `list[i].Value = 1` 改的是副本,必须整取整存;字段忘赋是默认值 |

**为什么选它**:采样点是"小、短命、海量"的数据——struct 的主场。每秒几百个点在管道里传递,若是堆对象,Gen0 GC 一分钟触发上千次。而且值拷贝在这里是**优点**:传出去的就是采样那一瞬间的快照,下游改不动原始数据。字段直接公开省掉 `{ get; set; }` 样板——纯数据载体没有需要守护的不变量,属性封装是 ceremony(框架源码里 `KeyValuePair`、`DateTime` 同样思路)。

**代价要认**:struct 的坑在本项目里真实存在——R2 的事件转发必须显式抄 Timestamp、`List<T>` 改字段必须整取整存。**选型 = 认清它的坑并管理它,不是假装没有坑**——面试时主动讲坑,比回避坑加分。

**🎤 面试一句话**:"SensorPoint 是 4 字段 struct:采集高频,栈分配避免 Gen0 GC 抖动;值语义天然是快照。struct 的两个坑我也踩过——List 索引器返回副本、字段忘赋是默认值,项目里 Timestamp 必须显式抄就是第二个坑的对策。"

### 🏗️ 为什么这样设计:设备状态为什么是三态枚举而不是 bool IsOnline?

**当时面临的选择**:

| 方案 | 优点 | 代价 |
|---|---|---|
| bool IsOnline | 最简单 | "正在连接"无法表达,UI 只能闪 离线→在线 |
| enum 三态 Offline/Connecting/Online(选定) | 多写一个枚举 | UI 要处理三个分支 |

**为什么选它**:串口握手/Modbus 连接是几百毫秒到几秒的真实过程,这段**中间态必须让用户看见**——R8 的状态灯在 Connecting 时黄灯闪烁,就是它驱动的。三态还是状态机的最小完备集:任何时刻设备必居其一,不存在"又在线又离线"的非法组合(两个 bool 就会出现非法组合)。

**不这样会怎样**:bool 版在握手期间 UI 显示"离线",现场操作员以为没点上,再点一次连接按钮——开出第二个连接任务,两个任务抢同一个串口,经典事故。

**🎤 面试一句话**:"设备状态我做 Offline/Connecting/Online 三态而不是 bool:握手是用户必须看得见的中间态,UI 状态灯的黄灯闪烁就是它驱动的。用 bool 会把'正在连'显示成'离线',诱导操作员连点重连、双开连接任务。"

## ✅ 验证(必做,贴着敲)

```bash
dotnet build
```
**期望输出(关键行)**:
```
已成功生成。 → 0 个警告
              0 个错误
```

```bash
dotnet sln list
```
**期望输出**:三个项目路径都在列。

## ✅ 验收清单

- [ ] `dotnet build` 0 错误 0 警告
- [ ] Core 里只有 Models/SensorPoint.cs,没有 Class1.cs
- [ ] Tests 引用 Core,UI 引用 Core,没有反向引用
- [ ] 能说出:为什么 UI 引用 Core 而不是 Core 引用 UI?(编译方向=依赖方向,Core 不知道 UI 存在,才能被任何 UI 复用)
- [ ] git commit -m "R1: 工程骨架+领域模型"

## 🎤 面试怎么讲这一篇

> "解决方案分三层:Core 放领域逻辑,不依赖任何 UI;UI 是 WPF 壳;Tests 独立引用 Core。核心数据类型 SensorPoint 是 4 字段 struct——采集频率高,struct 栈分配零 GC 压力;时间戳由采集源统一打,避免下游各盖各的。依赖方向永远从外向内,这样 Core 可以被换皮(WPF→WinForm→服务)复用。"

**✅ 打卡[ ]**
