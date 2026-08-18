# MC · 模拟运动控制 —— 升级篇(把你的 v1 升级成面试可讲的 v2)

> **定位**:你已经有一个能跑的 WinForms 两轴模拟运控 v1(模拟设备 + 点动 + 绝对定位 + 日志,骨架完全正确)。这一篇把它升级成 v2:**接口化 + 两轴并发 + 急停 + 回零 + 软限位 + 报警链路 + 直线插补 + 工业风界面 + 14 个单元测试** —— 面试里"讲不完"的项目变成"讲不完的亮点"。
> **前置**:DaqMonitor R1-R8(或同等基础:会用接口/事件/Task.Run/xUnit/WinForms 基本控件)。
> **预计开发时长**:跟敲 2-3 天(每天 2-3h);独立开发再对答案 4-5 天。
> **工作流**:和 R 系列一样 —— **先只看「📋 需求单」自己写**,卡住/写完再看「🛠️ 参考实现」对答案。

---

## 🎯 本篇交付物

做完这一篇,你会得到:

1. 一个 net8.0-windows 的 WinForms 工程 `MotionControl`(解决方案 + 主程序 + 测试,共 7 个代码文件,全部**带详细注释**);
2. 一张**行为正确的模拟卡**:两轴同时点动互不干扰、短距离定位不瞬移、急停就地冻结、回零精确到 0.000、点动撞软限位自动停 + 报警、两轴直线插补等比推进;
3. **14 个单元测试全绿**(含一个真跑 WinForms 界面消息泵的全流程冒烟测试);
4. 一块**工业风界面**:GroupBox 分区、唯一红色按钮留给急停、黑底等宽日志、淡黄报警框;
5. 一套面试话术:每个升级点都对应一个"现场会出什么事"的故事。

运行起来长这样(布局):
```
┌──────────────────────────────────────────────────────────────────────┐
│ ┌ 连接控制 ─────────────────────────────┐                 ┌────────┐ │
│ │ IP 地址: [127.0.0.1] [连接] [断开] ▪  │                 │急停 STOP│ │ ← 全窗体唯一红色按钮
│ └───────────────────────────────────────┘                 └────────┘ │
├──────────────────┬──────────────────┬────────────────────────────────┤
│ ┌ 轴 1 (X 轴) ──┐ │ ┌ 轴 2 (Y 轴) ──┐ │ ┌ 报警信息 ────────────────┐ │
│ │ 使能控制      │ │ │ (布局与轴1一致)│ │ │ [淡黄底/深红字报警列表]   │ │
│ │ [使能][失能]  │ │ │               │ │ │ [清除全部报警]            │ │
│ │ 点动(按住不放)│ │ │               │ ├────────────────────────────┤ │
│ │ [▲正转][▼反转]│ │ │               │ │ ┌ 运行日志 ──────────────┐ │ │
│ │ 当前位置      │ │ │               │ │ │ [黑底/浅绿等宽字日志]   │ │ │
│ │ 速度/目标位置 │ │ │               │ │ │ (同时落盘 logs\ 目录)  │ │ │
│ │ [绝对定位][回零⌂]│ │              │ │ └────────────────────────┘ │ │
│ └───────────────┘ │ └───────────────┘ └────────────────────────────┘ │
└──────────────────┴──────────────────┴────────────────────────────────┘
```

---

## 📋 需求单(产品经理视角 —— 先自己想怎么做)

### v1 的 10 个坑(升级动机,先对号入座)

| # | v1 的坑 | 现场后果 | v2 怎么修 |
|---|---|---|---|
| ① | 全局一个 `_isJogging`/`_jogCts` 管两根轴 | 按住轴 1 点动再按轴 2,轴 1 直接停 | 每轴一个 `CancellationTokenSource`,各动各的(FR-M03) |
| ② | `totalSteps = moveTime / 100`,短距离算出 0 步 | 除零崩 / 目标很近时"瞬移" | 步数公式 `Math.Max(1, Ceiling(距离/速度×1000/节拍))` + 完成后精确贴目标(FR-M03) |
| ③ | 没有急停 | 程序失控时操作员只能拔电源 | `StopAll()` 取消所有令牌 + 红色急停按钮(FR-M04) |
| ④ | 没有回零 | 每次开机坐标基准都不确定 | `HomeAxis` 低速走回绝对 0(FR-M05) |
| ⑤ | 没有软限位 | 点动按住不放,坐标飞到无穷大 | ±1000 软限位:超限指令拒绝,点动撞限位自动停+报警(FR-M06) |
| ⑥ | 速度写死 50 | 想快想慢都做不到 | 速度输入框 + 非法输入自动回退 50(FR-M08) |
| ⑦ | `btnMoveAbs1.Enabled` 设了两次,`btnMoveAbs2` 一次都没设 | 复制粘贴改号漏改,按钮状态靠缘分 | 所有按钮状态集中到一个 `RefreshUiState()` 统一算(FR-M11) |
| ⑧ | 清报警按钮没绑 Click 事件 | 摆设按钮,报了警永远清不掉 | 报警链路:注入→显示→阻断运动→清除→恢复(FR-M07) |
| ⑨ | 复制粘贴的 handler,轴 2 的按钮日志里打"轴 1" | 日志和操作对不上,排查两眼一黑 | 控件收进数组,一段循环统一订阅,日志带真实轴号(FR-M11) |
| ⑩ | 事件里 `Thread.Sleep` 卡界面;日志写文件不加锁;IP 框藏前导空格 | 界面假死 / 日志乱码 / 连接莫名失败 | Sleep 全部清除;`LogHelper` 加锁;输入进门就 Trim(FR-M08/M12) |

### 功能需求 FR 表

| 编号 | 需求 | 验收标准 |
|---|---|---|
| FR-M01 | net8.0-windows 工程:解决方案 + 主程序 + xUnit 测试工程 | `dotnet build` 0 错 0 警;测试工程能引用主程序 |
| FR-M02 | 设备抽象 `IMotionCard` + 返回码枚举 `MotionResult` | 上层代码只依赖接口;0=Ok、负数=各类失败,对齐真卡 SDK 习惯 |
| FR-M03 | 模拟卡两轴并发仿真 | **轴 1、轴 2 同时点动互不打断**;短距离定位精确到位;新指令可打断在途运动 |
| FR-M04 | 急停 `StopAll` | 任意运动中急停,所有轴位置就地冻结(1mm 都不多走),触发事件 |
| FR-M05 | 回零 `HomeAxis` | 从任意位置精确回到 0.000 |
| FR-M06 | 软限位 ±1000mm | 目标超限的定位指令被拒绝;点动顶到限位自动停 + 报警 |
| FR-M07 | 报警链路 | 报警时该轴一切运动被拒(AlarmActive);清除后恢复;事件上界面 |
| FR-M08 | 参数防呆 | 速度可输入,非法(空/非数字/越界)自动回退 50;IP 自动 Trim;未连接/未使能发指令返回明确错误码 |
| FR-M09 | 单元测试 | ≥13 个测试覆盖:并发/急停/回零/软限位/报警/插补/生命周期 |
| FR-M10 | UI 分区整容 | GroupBox 分区;急停是唯一红色按钮;报警淡黄底深红字;日志黑底浅绿等宽字 |
| FR-M11 | 界面状态集中管理 | 按钮可用性只在 `RefreshUiState()` 一处计算;两轴控件数组化 |
| FR-M12 | 线程安全日志 | 多线程写文件不乱码(lock);界面日志后台线程投递回 UI 线程;同时落盘 `logs\` |
| FR-M13 | (可选)两轴直线插补 `MoveLinear` | X:0→50、Y:0→30 过程中任意时刻 X:Y≈5:3,同时到位 |
| FR-M14 | UI 全流程冒烟测试 | STA 线程真跑窗体:连接→使能→双轴点动→定位→注报警→清警→急停→断开,0 异常 |

**先自己想**:别急着往下翻。拿张纸画出 —— ① `IMotionCard` 接口要哪些方法和事件?② "两轴同时点动互不打断"用 v1 的全局变量思路怎么改?③ 急停要能打断"正在走的一段运动",你想到了 C# 的哪个机制?④ WinForms 里按钮事件来自后台线程时怎么安全更新界面?

---

## 📚 本篇知识点(不懂再点回去学)

- [📖 IDevice 设备统一抽象](kp:idevice) —— 为什么 IMotionCard 和采集项目的 IDevice 是同一个思想
- [📖 event / EventHandler 事件机制](kp:event-delegate) —— 卡的位置/报警事件怎么往上抛
- [📖 Task.Run / async-await](kp:taskrun) —— 仿真循环就是 `Task.Run` 里一段可取消的 await 循环
- [📖 CancellationToken 协作式取消](kp:cancel-token) —— 急停/松手/新指令打断,全靠它(本篇新增)
- [📖 InvokeRequired / BeginInvoke 跨线程更新界面](kp:winforms-invoke) —— WinForms 版的 Dispatcher(本篇新增)
- [📖 xUnit 单元测试](kp:unit-test) —— 14 个测试怎么写、怎么轮询等待异步运动

---

## 🛠️ 参考实现(卡住/写完再看)

### 步骤 1:工程骨架 —— net8 迁移与三件套结构

**设计思路一句**:v1 是 .NET Framework 4.8 老式 csproj;v2 迁到 SDK 风格 net8.0-windows —— 以后加 NuGet 包、跑测试、上 CI 都是现代写法。

在任意目录(示例用 `F:\00_project\MotionControlV2`)执行:

```bash
dotnet new sln -n MotionControl
dotnet new winforms -n MotionControl -o src/MotionControl -f net8.0
dotnet new xunit -n MotionControl.Tests -o src/MotionControl.Tests -f net10.0
dotnet sln add src/MotionControl/MotionControl.csproj src/MotionControl.Tests/MotionControl.Tests.csproj
```

> ⚠️ 为什么 xunit 是 `-f net10.0`:本机 SDK 是 10,xunit 模板最低只肯生成 net10。**无所谓** —— 下一步整个 csproj 会被替换成 net8.0-windows。winforms 模板接受 net8.0,不受影响。

然后把两个 csproj **整体替换**为下面内容(两个要点:① 测试工程 TFM 必须和主程序一样是 `net8.0-windows`,否则引用不上;② `RollForward Major` 允许用 SDK 10 编译 net8 目标):

```xml
<!-- 📂 文件:src/MotionControl/MotionControl.csproj(整体替换) -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <!-- 沿用原工程的命名空间,一行代码都不用改命名空间就能迁移 -->
    <RootNamespace>MotionControlProject</RootNamespace>
    <!-- 本机 SDK 是 10,允许用新版 SDK 编译 net8 目标 -->
    <RollForward>Major</RollForward>
  </PropertyGroup>

</Project>
```

```xml
<!-- 📂 文件:src/MotionControl.Tests/MotionControl.Tests.csproj(整体替换) -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <!-- 测试要引用 WinExe 的 net8.0-windows 工程,TFM 必须一致 -->
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <UseWindowsForms>true</UseWindowsForms>
    <RollForward>Major</RollForward>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

csproj 替换完才能挂引用(csproj 没换之前 TFM 不匹配,`dotnet add reference` 会拒绝):

```bash
dotnet add src/MotionControl.Tests/MotionControl.Tests.csproj reference src/MotionControl/MotionControl.csproj
```

删掉模板文件,建好目录:

```bash
rm src/MotionControl/Form1.cs src/MotionControl/Form1.Designer.cs src/MotionControl.Tests/UnitTest1.cs
mkdir -p src/MotionControl/Device src/MotionControl/Common src/MotionControl/UI
```

最终结构(SDK 风格工程:csproj 自动把 .cs 全编进去,不用像老 csproj 那样逐个登记):

```
MotionControl/
├── MotionControl.sln
└── src/
    ├── MotionControl/            ← 主程序(WinForms)
    │   ├── Device/               ← 设备抽象层:接口 + 模拟卡
    │   ├── Common/               ← 日志等公共件
    │   ├── UI/                   ← 窗体
    │   ├── Program.cs
    │   └── MotionControl.csproj
    └── MotionControl.Tests/      ← xUnit 测试
        ├── MockMotionCardTests.cs
        └── MotionControl.Tests.csproj
```

### 步骤 2:设备抽象层 —— IMotionCard + MotionResult

**设计思路一句**:把"运动控制卡能做什么"抽成接口 —— 上位机行业现实是你开发时手上没有真卡,模拟卡/真卡各做一个实现,上层(窗体/测试)只认接口,这是和采集项目 IDevice 一模一样的套路(见 [📖 IDevice 设备统一抽象](kp:idevice))。

先想清楚**返回码**:真卡 SDK(Googol/雷赛/正运动)几乎都返回 int,0 成功负数失败。用枚举把"魔数"变成可读名字,同时保留负数值:

```csharp
// 📂 文件:src/MotionControl/Device/IMotionCard.cs
namespace MotionControlProject.Device;

/// <summary>
/// 运动指令返回码 —— 对齐真实板卡 SDK 的习惯:0 = 成功,负数 = 各种失败原因。
/// 真卡 SDK 几乎都返回 int 错误码,这里用枚举把"魔数"变成可读名字,
/// 但保留负数值,练习者将来看到真卡返回 -1 就知道该怎么对应。
/// </summary>
public enum MotionResult
{
    /// <summary>指令成功</summary>
    Ok = 0,
    /// <summary>卡未连接(网线没插 / Connect 没调 / 已断开)</summary>
    NotConnected = -1,
    /// <summary>轴号越界(只有 2 轴的卡,传了 axis=5)</summary>
    AxisIndexError = -2,
    /// <summary>参数非法:空 IP、速度 ≤ 0、目标位置超出软限位…</summary>
    ParamError = -3,
    /// <summary>轴未使能(伺服没上使能就让它动,真机上电机纹丝不动)</summary>
    AxisDisabled = -4,
    /// <summary>轴处于报警状态,必须先清报警才能再动</summary>
    AlarmActive = -5,
}

/// <summary>位置变化事件参数:哪根轴、现在走到哪(mm)。</summary>
public class PositionChangedEventArgs : EventArgs
{
    public int Axis { get; }
    public double Position { get; }

    public PositionChangedEventArgs(int axis, double position)
    {
        Axis = axis;
        Position = position;
    }
}

/// <summary>报警事件参数:哪根轴、报什么、是"发生报警"还是"报警清除"。</summary>
public class AlarmChangedEventArgs : EventArgs
{
    public int Axis { get; }
    public string Message { get; }
    /// <summary>true = 报警发生;false = 报警被清除。</summary>
    public bool IsActive { get; }

    public AlarmChangedEventArgs(int axis, string message, bool isActive)
    {
        Axis = axis;
        Message = message;
        IsActive = isActive;
    }
}

/// <summary>
/// 运动控制卡抽象 —— 整个工程的"插座"。
/// 上位机行业现实:你开发时手上往往没有真卡(卡在客户产线上),
/// 所以把"卡能做什么"抽象成接口,模拟卡和真卡各做一个实现,上层代码完全复用。
/// 这与采集项目的 IDevice 是同一个思路:面向接口编程,设备可替换。
/// </summary>
public interface IMotionCard
{
    // ———— 状态查询(属性) ————

    /// <summary>卡是否已连接。</summary>
    bool IsConnected { get; }

    /// <summary>卡控制几根轴。</summary>
    int AxisCount { get; }

    // ———— 事件:卡主动向上层"汇报" ————

    /// <summary>任一轴位置变化时触发(模拟卡每个仿真节拍发一次;真卡可由轮询线程发)。</summary>
    event EventHandler<PositionChangedEventArgs>? PositionChanged;

    /// <summary>报警发生 / 报警清除时触发。</summary>
    event EventHandler<AlarmChangedEventArgs>? AlarmChanged;

    /// <summary>急停(StopAll)生效时触发一次。</summary>
    event EventHandler? EmergencyStopped;

    // ———— 连接管理 ————

    /// <summary>连接卡。ipAddress 为空或全空格返回 ParamError(真卡会做 ping/握手)。</summary>
    MotionResult Connect(string ipAddress);

    /// <summary>断开连接,并取消所有进行中的运动。</summary>
    MotionResult Disconnect();

    // ———— 轴状态 ————

    /// <summary>使能 / 下使能某轴。使能 = 伺服上电锁轴,未使能一切运动指令都会被拒绝。</summary>
    MotionResult SetAxisEnable(int axis, bool enable);

    /// <summary>某轴是否已使能。</summary>
    bool IsAxisEnabled(int axis);

    /// <summary>某轴是否正在运动(点动 / 定位 / 回零 / 插补都算)。</summary>
    bool IsMoving(int axis);

    /// <summary>读某轴当前位置(mm)。注意:读位置永远允许,连没使能都能读。</summary>
    double GetAxisPosition(int axis);

    /// <summary>读某轴当前报警信息,空字符串 = 无报警。</summary>
    string GetAlarmMessage(int axis);

    // ———— 运动指令 ————

    /// <summary>
    /// 点动(JOG):按住按钮朝一个方向一直走,松手停。
    /// speed 单位 mm/s;forward = true 正转 / false 反转。
    /// </summary>
    MotionResult JogAxis(int axis, double speed, bool forward);

    /// <summary>停止某轴点动(松手时调用)。</summary>
    MotionResult StopJog(int axis);

    /// <summary>
    /// 绝对定位:走到"绝对坐标" position(mm),速度 speed(mm/s)。
    /// 若该轴正在运动,新指令会打断旧运动(真卡的常规语义:后到的指令赢)。
    /// </summary>
    MotionResult MoveAbsolute(int axis, double position, double speed);

    /// <summary>回零(回原点):走到机械零点位置 0。速度固定 100mm/s(简化的"回零速度")。</summary>
    MotionResult HomeAxis(int axis);

    /// <summary>急停:所有轴立即停止,位置就地冻结。触发 EmergencyStopped 事件。</summary>
    MotionResult StopAll();

    /// <summary>清除某轴报警。清完报警轴还要重新确认使能状态才能运动(与真卡一致)。</summary>
    MotionResult ClearAlarm(int axis);

    // ———— 可选进阶:两轴直线插补 ————

    /// <summary>
    /// 多轴直线插补:各轴同时启动、等比推进、同时到位,走出一条空间直线。
    /// 例:X 从 0→50,Y 从 0→30,任意时刻 X:Y 恒等于 5:3。
    /// </summary>
    MotionResult MoveLinear(int[] axes, double[] targets, double speed);

    // ———— 模拟卡专用(真卡没有) ————

    /// <summary>人为注入一条报警 —— 用来在没真故障的情况下测试报警链路。</summary>
    void SimulateAlarm(int axis, string message);
}
```

💡 **接口设计的三个门道**:
- **读和动分开**:读位置/读报警不加前置条件(现实里编码器位置任何时候都读得到);运动指令才要求"已连接 + 已使能 + 无报警"。v1 把这些搅在一起,报错信息也说不清;
- **事件承载"卡主动说"**:位置变化、报警、急停是卡 → 上层的通知流;方法调用是上层 → 卡的命令流。两个方向分开,界面就不会变成轮询大杂烩(见 [📖 event/EventHandler](kp:event-delegate));
- **SimulateAlarm 是模拟卡专属**:放在接口里是妥协(为了演示方便),真卡实现里它就是个空方法或直接不实现 —— 文档诚实标注,面试也能讲这层取舍。

### 步骤 3:模拟卡 v2 —— MockMotionCard(本篇的心脏)

**设计思路一句**:每段"运动"= 一个后台 `Task.Run` 循环,每节拍把位置向目标推进一步;急停/松手/新指令打断 = `CancellationToken` 取消循环,位置就地冻结(见 [📖 Task.Run/async-await](kp:taskrun)、[📖 CancellationToken](kp:cancel-token))。

```csharp
// 📂 文件:src/MotionControl/Device/MockMotionCard.cs
namespace MotionControlProject.Device;

/// <summary>
/// 模拟运动控制卡 —— 没有真卡也能把整个上位机调通。
///
/// 仿真原理:每个"运动"不再是瞬间改坐标(v1 的做法),而是启动一个后台任务,
/// 每 tickMs 毫秒把位置向目标推进一步,步长 = 速度 × 节拍 —— 位置随时间连续变化,
/// 和真电机"转起来要时间"的体验一致。运动可以被随时取消(急停/松手/新指令打断)。
///
/// v1 → v2 的三处结构性修复:
/// 1. v1 用一个全局 _isJogging/_jogCts 管两根轴,轴 2 一动就把轴 1 停了
///    → v2 每轴一个 CancellationTokenSource,各动各的;
/// 2. v1 点动/定位共用状态互相打架 → v2 点动、定位、回零、插补统一走"取消旧的、启动新的";
/// 3. v1 没有软限位/急停/回零 → v2 内建 ±softLimit 软限位、StopAll 急停、HomeAxis 回零。
/// </summary>
public class MockMotionCard : IMotionCard
{
    /// <summary>仿真节拍(毫秒)。默认 100ms;单元测试传 10ms 让运动快进 10 倍。</summary>
    private readonly int _tickMs;

    /// <summary>软限位(±mm)。超过就拒绝指令;点动撞上就停 + 报警 —— 保护"机械"的最后一道软件防线。</summary>
    private readonly double _softLimit;

    /// <summary>指令入口互斥锁:UI 线程发指令、后台任务改状态,都从这把锁过,防止读到半截状态。</summary>
    private readonly object _gate = new();

    // ———— 每轴一组状态:下标 0 = 轴 1,下标 1 = 轴 2 ————

    /// <summary>各轴当前位置(mm)。</summary>
    private readonly double[] _positions;

    /// <summary>各轴使能状态。</summary>
    private readonly bool[] _enabled;

    /// <summary>各轴报警信息;null = 无报警。</summary>
    private readonly string?[] _alarms;

    /// <summary>
    /// 每轴一个取消令牌 —— v2 的核心修复。
    /// 轴 1 的运动只握轴 1 的令牌,急停/打断也只取消对应轴,两轴互不影响。
    /// 插补时所有参与轴共用同一个令牌实例,一停俱停。
    /// </summary>
    private readonly CancellationTokenSource?[] _cts;

    /// <summary>各轴"是否在运动"标志(StartMotion 里置 true,任务 finally 里复位)。</summary>
    private readonly bool[] _moving;

    /// <summary>回零速度固定值(mm/s)。真卡回零有单独的低速段 + 原点开关,这里简化成低速走回 0。</summary>
    public const double HomeSpeed = 100;

    private bool _connected;

    public MockMotionCard(int axisCount = 2, int tickMs = 100, double softLimit = 1000)
    {
        if (axisCount <= 0) axisCount = 2;
        _tickMs = Math.Max(1, tickMs);
        _softLimit = softLimit;
        _positions = new double[axisCount];
        _enabled = new bool[axisCount];
        _alarms = new string?[axisCount];
        _cts = new CancellationTokenSource?[axisCount];
        _moving = new bool[axisCount];
    }

    // ———— IMotionCard:属性与事件 ————

    public bool IsConnected => _connected;
    public int AxisCount => _positions.Length;

    /// <summary>注意:这些事件都在后台仿真线程上触发,UI 订阅后必须自己 Invoke 切回 UI 线程(WinForms 规矩)。</summary>
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;
    public event EventHandler<AlarmChangedEventArgs>? AlarmChanged;
    public event EventHandler? EmergencyStopped;

    // ———— 连接管理 ————

    public MotionResult Connect(string ipAddress)
    {
        // v1 坑:IP 带个前导空格就直接连不上,还查不出原因。
        // 现在统一 Trim + 判空返回明确错误码,把"会发生的脏输入"在门口挡掉。
        if (string.IsNullOrWhiteSpace(ipAddress)) return MotionResult.ParamError;

        lock (_gate)
        {
            _connected = true;   // 模拟卡连谁都是通的;真卡这里会做 TCP 连接/握手,失败返回相应错误码
            return MotionResult.Ok;
        }
    }

    public MotionResult Disconnect()
    {
        lock (_gate)
        {
            CancelAllLocked();          // 断开前先停掉所有运动,后台任务干净退出
            _connected = false;
            return MotionResult.Ok;
        }
    }

    // ———— 轴状态查询 ————

    public MotionResult SetAxisEnable(int axis, bool enable)
    {
        lock (_gate)
        {
            if (!CheckIndex(axis)) return MotionResult.AxisIndexError;
            if (!_connected) return MotionResult.NotConnected;

            _enabled[axis] = enable;
            return MotionResult.Ok;
        }
    }

    public bool IsAxisEnabled(int axis) => CheckIndex(axis) && _enabled[axis];

    public bool IsMoving(int axis) => CheckIndex(axis) && _moving[axis];

    /// <summary>读位置不做"已连接/已使能"限制 —— 现实里编码器位置任何时候都读得到。</summary>
    public double GetAxisPosition(int axis)
    {
        lock (_gate) return CheckIndex(axis) ? _positions[axis] : 0;
    }

    public string GetAlarmMessage(int axis) => CheckIndex(axis) ? _alarms[axis] ?? "" : "";

    // ———— 运动指令 ————

    public MotionResult JogAxis(int axis, double speed, bool forward)
    {
        lock (_gate)
        {
            var r = CheckMotionLocked(axis, speed);
            if (r != MotionResult.Ok) return r;

            // 点动 = 朝软限位方向一直走:把"目标"设在限位上,
            // 走不走得到无所谓 —— 用户松手(StopJog)就会取消,只有一直按住才会撞上限位触发保护。
            var target = forward ? _softLimit : -_softLimit;
            StartMotionLocked(axis, target, speed, jog: true);
            return MotionResult.Ok;
        }
    }

    public MotionResult StopJog(int axis)
    {
        lock (_gate)
        {
            if (!CheckIndex(axis)) return MotionResult.AxisIndexError;
            _cts[axis]?.Cancel();      // 取消令牌 → 仿真循环在下个节拍抛 OperationCanceledException → 位置就地冻结
            return MotionResult.Ok;
        }
    }

    public MotionResult MoveAbsolute(int axis, double position, double speed)
    {
        lock (_gate)
        {
            var r = CheckMotionLocked(axis, speed);
            if (r != MotionResult.Ok) return r;
            if (Math.Abs(position) > _softLimit) return MotionResult.ParamError;   // 目标直接超软限位:拒绝,不搭理
            if (Math.Abs(position - _positions[axis]) < 1e-9) return MotionResult.Ok; // 已在目标位:立即成功(v1 除零坑的根治)

            StartMotionLocked(axis, position, speed, jog: false);
            return MotionResult.Ok;
        }
    }

    /// <summary>回零 = 以固定的低速走回绝对坐标 0。真卡回零走原点开关 + 反向找 Z 相,逻辑更绕但目的相同。</summary>
    public MotionResult HomeAxis(int axis)
    {
        lock (_gate)
        {
            var r = CheckMotionLocked(axis, HomeSpeed);
            if (r != MotionResult.Ok) return r;

            StartMotionLocked(axis, 0, HomeSpeed, jog: false);
            return MotionResult.Ok;
        }
    }

    /// <summary>急停:取消所有轴的运动令牌。已在途的运动循环在下个节拍内退出,位置停在当前值。</summary>
    public MotionResult StopAll()
    {
        lock (_gate) CancelAllLocked();
        EmergencyStopped?.Invoke(this, EventArgs.Empty);   // 事件放锁外:别在持锁时回调别人的代码
        return MotionResult.Ok;
    }

    public MotionResult ClearAlarm(int axis)
    {
        lock (_gate)
        {
            if (!CheckIndex(axis)) return MotionResult.AxisIndexError;
            if (!_connected) return MotionResult.NotConnected;

            _alarms[axis] = null;   // 只清报警,不动使能状态 —— 和真卡一致:清完报警使能是否还在要看驱动
        }
        AlarmChanged?.Invoke(this, new AlarmChangedEventArgs(axis, "", isActive: false));
        return MotionResult.Ok;
    }

    // ———— 直线插补(可选篇) ————

    public MotionResult MoveLinear(int[] axes, double[] targets, double speed)
    {
        // 入参形状先验一遍:轴号数组与目标数组必须一一对应且非空
        if (axes is null || targets is null || axes.Length == 0 || axes.Length != targets.Length)
            return MotionResult.ParamError;

        lock (_gate)
        {
            if (!_connected) return MotionResult.NotConnected;
            if (speed <= 0) return MotionResult.ParamError;

            // 逐轴检查:轴号 / 使能 / 报警 / 软限位,任何一轴不满足,整条插补指令拒绝
            // (插补是"绑腿跑",一个不能跑整队都不动 —— 真卡同理)
            foreach (var (axis, target) in axes.Zip(targets))
            {
                if (!CheckIndex(axis)) return MotionResult.AxisIndexError;
                if (!_enabled[axis]) return MotionResult.AxisDisabled;
                if (_alarms[axis] is not null) return MotionResult.AlarmActive;
                if (Math.Abs(target) > _softLimit) return MotionResult.ParamError;
            }

            foreach (var axis in axes) _cts[axis]?.Cancel();   // 插补优先:打断各轴在途运动

            // 一个令牌发给所有参与轴 —— 急停/新指令取消它,所有轴同时停(插补的命门:必须同起同停)
            var cts = new CancellationTokenSource();
            foreach (var axis in axes) { _cts[axis] = cts; _moving[axis] = true; }

            var froms = axes.Select(a => _positions[a]).ToArray();
            // 总步数按"走得最远的那根轴"算 —— 步数定了,每根轴再按各自距离等比分步,速度语义 = 最长轴的速度
            var maxDist = 0.0;
            for (var k = 0; k < axes.Length; k++)
                maxDist = Math.Max(maxDist, Math.Abs(targets[k] - froms[k]));

            Task.Run(async () =>
            {
                try
                {
                    var steps = Math.Max(1, (int)Math.Ceiling(maxDist / speed * 1000.0 / _tickMs));
                    for (var i = 1; i <= steps; i++)
                    {
                        await Task.Delay(_tickMs, cts.Token);
                        for (var k = 0; k < axes.Length; k++)
                        {
                            // 等比推进:第 i 步位置 = 起点 + 全程位移 × i/steps
                            // → 任意时刻各轴位移比例恒定,轨迹是空间直线,且同时到位
                            var p = froms[k] + (targets[k] - froms[k]) * i / steps;
                            lock (_gate) _positions[axes[k]] = p;
                            PositionChanged?.Invoke(this, new PositionChangedEventArgs(axes[k], p));
                        }
                    }
                    if (!cts.Token.IsCancellationRequested)
                    {
                        // 一步不多一步不少地精确落点(消除浮点累积误差)
                        for (var k = 0; k < axes.Length; k++)
                        {
                            lock (_gate) _positions[axes[k]] = targets[k];
                            PositionChanged?.Invoke(this, new PositionChangedEventArgs(axes[k], targets[k]));
                        }
                    }
                }
                catch (OperationCanceledException) { /* 急停/打断:各轴就地冻结 */ }
                finally
                {
                    lock (_gate)
                        for (var k = 0; k < axes.Length; k++)
                        {
                            _moving[axes[k]] = false;
                            if (ReferenceEquals(_cts[axes[k]], cts)) _cts[axes[k]] = null;
                        }
                }
            });
            return MotionResult.Ok;
        }
    }

    // ———— 模拟卡专用 ————

    /// <summary>注入报警:置报警串 + 立即取消该轴运动(真机上报警卡会自动封脉冲动不了,行为一致)。</summary>
    public void SimulateAlarm(int axis, string message)
    {
        lock (_gate)
        {
            if (!CheckIndex(axis)) return;
            _alarms[axis] = message;
            _cts[axis]?.Cancel();
        }
        AlarmChanged?.Invoke(this, new AlarmChangedEventArgs(axis, message, isActive: true));
    }

    // ———— 私有工具 ————

    /// <summary>轴号合法性(只查索引,不查连接/使能 —— 查询类方法用)。</summary>
    private bool CheckIndex(int axis) => axis >= 0 && axis < AxisCount;

    /// <summary>
    /// 运动前检查链,按"越靠前的越廉价"排序:轴号 → 连接 → 速度 → 使能 → 报警。
    /// 所有运动指令(点动/定位/回零)共用 —— v1 每个方法各查各的、漏了就出怪 bug 的根治。
    /// </summary>
    private MotionResult CheckMotionLocked(int axis, double speed)
    {
        if (!CheckIndex(axis)) return MotionResult.AxisIndexError;
        if (!_connected) return MotionResult.NotConnected;
        if (speed <= 0) return MotionResult.ParamError;
        if (!_enabled[axis]) return MotionResult.AxisDisabled;
        if (_alarms[axis] is not null) return MotionResult.AlarmActive;
        return MotionResult.Ok;
    }

    /// <summary>
    /// 启动一段"单轴匀速运动"仿真(点动/定位/回零最终都落到这)。
    /// 调用方必须已持 _gate 锁。
    /// </summary>
    private void StartMotionLocked(int axis, double target, double speed, bool jog)
    {
        // 新指令打断旧运动 —— 真卡语义:后到的指令赢。v1 里"运动中再按按钮"的行为是未定义的
        _cts[axis]?.Cancel();
        var cts = new CancellationTokenSource();
        _cts[axis] = cts;
        _moving[axis] = true;

        var from = _positions[axis];
        var dist = target - from;

        // 步数 = 距离 ÷ 速度 ÷ 节拍:走 100mm、50mm/s、100ms 节拍 → 100/50×1000/100 = 20 步,耗时恰 2 秒。
        // Math.Max(1, …) 兜底极短距离 —— v1 的 totalSteps = moveTime/100 在短距离时算出 0,
        // 再除以 totalSteps 就"瞬移"或除零,这是 v1 定位一按就"卡成瞬移"的直接死因。
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(dist) / speed * 1000.0 / _tickMs));
        var step = dist / steps;

        Task.Run(async () =>
        {
            try
            {
                for (var i = 1; i <= steps; i++)
                {
                    await Task.Delay(_tickMs, cts.Token);        // 被取消时这里抛 OperationCanceledException
                    var p = from + step * i;
                    lock (_gate) _positions[axis] = p;
                    PositionChanged?.Invoke(this, new PositionChangedEventArgs(axis, p));

                    // 点动专属保护:朝限位走时每步都检查,顶到软限位就夹住位置 + 报警 + 停
                    if (jog && Math.Abs(p) >= _softLimit - 1e-9)
                    {
                        var clamped = Math.Clamp(p, -_softLimit, _softLimit);
                        lock (_gate) { _positions[axis] = clamped; _alarms[axis] = $"触发{(p > 0 ? "正" : "负")}软限位 {_softLimit:F0}mm,已自动停止"; }
                        PositionChanged?.Invoke(this, new PositionChangedEventArgs(axis, clamped));
                        var msg = _alarms[axis]!;
                        AlarmChanged?.Invoke(this, new AlarmChangedEventArgs(axis, msg, isActive: true));
                        break;
                    }
                }

                // 定位/回零走完全程后,把位置精确贴到目标(消除浮点累积误差,保证 repeatability)
                if (!cts.Token.IsCancellationRequested && !jog)
                {
                    lock (_gate) _positions[axis] = target;
                    PositionChanged?.Invoke(this, new PositionChangedEventArgs(axis, target));
                }
            }
            catch (OperationCanceledException)
            {
                // 急停 / 松手 / 被新指令打断:什么都不做,位置停在当前值 —— "就地冻结"
            }
            finally
            {
                lock (_gate)
                {
                    _moving[axis] = false;
                    // 只有槽里还是"自己这个令牌"时才清空:防止把打断我的人刚放进来的新令牌误删
                    if (ReferenceEquals(_cts[axis], cts)) _cts[axis] = null;
                }
            }
        });
    }

    private void CancelAllLocked()
    {
        for (var i = 0; i < AxisCount; i++) _cts[i]?.Cancel();
    }
}
```

🗺️ **新手读码地图 —— MockMotionCard 怎么走读**:
1. **先看字段**:`_positions/_enabled/_alarms/_cts/_moving` 五个数组,下标就是轴号 —— "每轴一组状态"这句话落到了内存布局上;
2. **再追一条指令**:以 `MoveAbsolute` 为例 —— `CheckMotionLocked`(五连检查,每种失败一个错误码)→ 目标超软限位拒收 → 已在目标位直接 Ok → `StartMotionLocked` 启动仿真;
3. **钻进 StartMotionLocked**(全文件最核心的 40 行):取消旧令牌 → 建新令牌 → 算步数和步长 → `Task.Run` 里 for 循环:每步 `Task.Delay(节拍, token)` → 推进位置 → 触发事件 →(点动)查软限位;循环外精确贴目标;`catch (OperationCanceledException)` = 被打断就地冻结;`finally` 里复位 `_moving`;
4. **对照 v1 想一遍**:v1 的 `_isJogging` 全局一个 → 现在 `_cts[axis]` 每轴一个;v1 的 `totalSteps = moveTime/100` → 现在 `Math.Max(1, Ceiling(…))` + 完成贴目标;v1 点动/定位打架 → 现在都走"取消旧的启动新的",天然互斥;
5. **前端类比**:像组件里每个 tab 一个自己的 abortController —— 谁的请求谁取消,互不连坐。

### 步骤 4:单元测试 —— 14 个行为契约

**设计思路一句**:测试就是需求单的"可执行版" —— 两轴并发、急停冻结、回零、软限位、报警阻断、插补比例,每条 FR 一个测试,跑一遍 = 验收一遍(见 [📖 xUnit 单元测试](kp:unit-test))。

```csharp
// 📂 文件:src/MotionControl.Tests/MockMotionCardTests.cs
using MotionControlProject.Device;
using MotionControlProject.UI;
using System.Diagnostics;

namespace MotionControl.Tests;

/// <summary>
/// MockMotionCard 单元测试 —— 模拟卡的"行为契约"。
/// tickMs 传 10(默认 100):仿真节拍快 10 倍,几秒内跑完全部运动场景。
/// 这些测试就是升级的"验收标准":两轴并发、急停、软限位、回零、插补,全部有据可查。
/// </summary>
public class MockMotionCardTests
{
    /// <summary>新建一张快节拍模拟卡(不动它,各测试自己决定连接/使能到哪一步)。</summary>
    private static MockMotionCard NewCard() => new(axisCount: 2, tickMs: 10, softLimit: 1000);

    /// <summary>一步到位的卡:已连接 + 两轴都已使能,直接测运动逻辑。</summary>
    private static MockMotionCard ReadyCard()
    {
        var card = NewCard();
        card.Connect("127.0.0.1");
        card.SetAxisEnable(0, true);
        card.SetAxisEnable(1, true);
        return card;
    }

    /// <summary>轮询等待条件成立(每 10ms 查一次),超时抛异常 —— 测试异步运动的标准写法。</summary>
    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("等待条件超时,运动没有按预期发生");
            await Task.Delay(10);
        }
    }

    // ———— 1. 参数与状态检查 ————

    [Fact]
    public void Connect_空IP_应返回参数错误()
    {
        var card = NewCard();
        // 空串 / 全空格都要挡住 —— v1 里 IP 文本框藏前导空格连不上的坑,现在进门就报明确错误码
        Assert.Equal(MotionResult.ParamError, card.Connect(""));
        Assert.Equal(MotionResult.ParamError, card.Connect("   "));
        Assert.False(card.IsConnected);
    }

    [Fact]
    public async Task 未连接就发运动指令_应全部返回未连接()
    {
        var card = NewCard();   // 故意不 Connect
        Assert.Equal(MotionResult.NotConnected, card.JogAxis(0, 50, true));
        Assert.Equal(MotionResult.NotConnected, card.MoveAbsolute(0, 100, 50));
        Assert.Equal(MotionResult.NotConnected, card.HomeAxis(0));

        card.Connect("127.0.0.1");
        Assert.Equal(MotionResult.Ok, card.Connect("127.0.0.1"));   // 重复连接幂等,不报错
        Assert.True(card.IsConnected);
        await Task.CompletedTask;
    }

    [Fact]
    public void 连接但未使能就运动_应返回轴未使能()
    {
        var card = NewCard();
        card.Connect("127.0.0.1");   // 故意不 SetAxisEnable
        Assert.Equal(MotionResult.AxisDisabled, card.MoveAbsolute(0, 100, 50));
        Assert.Equal(MotionResult.AxisDisabled, card.JogAxis(0, 50, true));
        // 读位置不受使能限制 —— 编码器位置任何时候都读得到
        Assert.Equal(0, card.GetAxisPosition(0));
    }

    // ———— 2. 两轴并发(v1 的头号 bug 的回归测试) ————

    [Fact]
    public async Task 两轴同时点动_互不干扰()
    {
        var card = ReadyCard();

        // v1 复现:全局 _isJogging 导致按轴 2 的瞬间轴 1 被停。
        // v2 每轴一个取消令牌,两轴应同时前进
        Assert.Equal(MotionResult.Ok, card.JogAxis(0, 50, forward: true));
        Assert.Equal(MotionResult.Ok, card.JogAxis(1, 50, forward: true));

        await WaitUntil(() => card.GetAxisPosition(0) > 1 && card.GetAxisPosition(1) > 1);

        card.StopJog(0);
        card.StopJog(1);
        Assert.True(card.GetAxisPosition(0) > 1, "轴1 不该被轴2 的点动打断");
        Assert.True(card.GetAxisPosition(1) > 1, "轴2 自己也要在动");
    }

    // ———— 3. 绝对定位 ————

    [Fact]
    public async Task 绝对定位_短距离_应精确到达目标()
    {
        var card = ReadyCard();
        // 3mm @ 5mm/s = 600ms:v1 的步数除零就死在这种短距离上
        Assert.Equal(MotionResult.Ok, card.MoveAbsolute(0, 3, 5));

        await WaitUntil(() => !card.IsMoving(0));
        // 完成后精确贴目标(无浮点漂移),保留 3 位小数比对
        Assert.Equal(3.0, card.GetAxisPosition(0), precision: 3);
    }

    [Fact]
    public void 绝对定位_零距离_应立即成功且不算运动()
    {
        var card = ReadyCard();
        Assert.Equal(MotionResult.Ok, card.MoveAbsolute(0, 0, 50));
        Assert.False(card.IsMoving(0));
    }

    [Fact]
    public void 绝对定位_目标超软限位_应返回参数错误()
    {
        var card = ReadyCard();
        Assert.Equal(MotionResult.ParamError, card.MoveAbsolute(0, 2000, 50));   // 正向超
        Assert.Equal(MotionResult.ParamError, card.MoveAbsolute(0, -1500, 50));  // 反向超
        Assert.Equal(MotionResult.ParamError, card.MoveAbsolute(0, 100, 0));     // 速度非法
    }

    // ———— 4. 急停 ————

    [Fact]
    public async Task 急停_运动中途位置就地冻结()
    {
        var card = ReadyCard();
        var stopped = false;
        card.EmergencyStopped += (s, e) => stopped = true;

        Assert.Equal(MotionResult.Ok, card.MoveAbsolute(0, 500, 5));   // 全程 100s,足够中途急停
        await WaitUntil(() => card.GetAxisPosition(0) > 2);

        Assert.Equal(MotionResult.Ok, card.StopAll());
        var frozen = card.GetAxisPosition(0);
        Assert.True(stopped, "急停必须触发 EmergencyStopped 事件");

        await Task.Delay(200);   // 再等两个节拍,确认没有"惯性滑动"
        Assert.Equal(frozen, card.GetAxisPosition(0), precision: 6);
        Assert.False(card.IsMoving(0));
    }

    // ———— 5. 回零 ————

    [Fact]
    public async Task 回零_从任意位置精确回零位()
    {
        var card = ReadyCard();
        Assert.Equal(MotionResult.Ok, card.MoveAbsolute(0, 120, 200));
        await WaitUntil(() => !card.IsMoving(0));
        Assert.Equal(120.0, card.GetAxisPosition(0), precision: 3);

        Assert.Equal(MotionResult.Ok, card.HomeAxis(0));
        await WaitUntil(() => !card.IsMoving(0));
        Assert.Equal(0.0, card.GetAxisPosition(0), precision: 3);
    }

    // ———— 6. 报警链路 ————

    [Fact]
    public async Task 报警阻断运动_清报警后恢复()
    {
        var card = ReadyCard();
        card.SimulateAlarm(0, "模拟伺服过流");

        Assert.Equal(MotionResult.AlarmActive, card.MoveAbsolute(0, 100, 50));
        Assert.Equal("模拟伺服过流", card.GetAlarmMessage(0));

        Assert.Equal(MotionResult.Ok, card.ClearAlarm(0));
        Assert.Equal(MotionResult.Ok, card.MoveAbsolute(0, 100, 200));
        await WaitUntil(() => !card.IsMoving(0));
        Assert.Equal(100.0, card.GetAxisPosition(0), precision: 3);
    }

    [Fact]
    public async Task 点动撞正软限位_应自动停止并报警()
    {
        var card = ReadyCard();
        // 3000mm/s @ 10ms 节拍 = 每步 30mm,约 0.34s 撞到 +1000
        Assert.Equal(MotionResult.Ok, card.JogAxis(0, 3000, forward: true));

        await WaitUntil(() => card.GetAlarmMessage(0).Length > 0);
        Assert.Equal(1000.0, card.GetAxisPosition(0), precision: 2);   // 位置被夹在限位上,不多走一步
        Assert.False(card.IsMoving(0));
        Assert.Contains("软限位", card.GetAlarmMessage(0));
    }

    // ———— 7. 两轴直线插补(可选篇) ————

    [Fact]
    public async Task 直线插补_两轴等比推进且同时到位()
    {
        var card = ReadyCard();
        // X: 0→50, Y: 0→30,速度 25 → 全程 2s。任意时刻 X:Y 应恒为 5:3
        Assert.Equal(MotionResult.Ok, card.MoveLinear(new[] { 0, 1 }, new[] { 50.0, 30.0 }, 25));

        await Task.Delay(300);   // 走到中段抓一次比例
        var midX = card.GetAxisPosition(0);
        var midY = card.GetAxisPosition(1);
        Assert.True(midX > 1 && midY > 1, "两轴都应已在运动中");
        Assert.InRange(midX / midY, 5.0 / 3 - 0.1, 5.0 / 3 + 0.1);   // 比例恒定 = 直线

        await WaitUntil(() => !card.IsMoving(0) && !card.IsMoving(1));
        Assert.Equal(50.0, card.GetAxisPosition(0), precision: 3);
        Assert.Equal(30.0, card.GetAxisPosition(1), precision: 3);
    }

    // ———— 8. 生命周期 ————

    [Fact]
    public async Task 断开连接_所有运动被取消()
    {
        var card = ReadyCard();
        Assert.Equal(MotionResult.Ok, card.JogAxis(0, 50, true));
        Assert.Equal(MotionResult.Ok, card.JogAxis(1, 50, true));
        await WaitUntil(() => card.GetAxisPosition(0) > 0.5 && card.GetAxisPosition(1) > 0.5);

        Assert.Equal(MotionResult.Ok, card.Disconnect());
        await Task.Delay(100);

        Assert.False(card.IsConnected);
        Assert.False(card.IsMoving(0));
        Assert.False(card.IsMoving(1));
        // 断开后一切运动指令被拒
        Assert.Equal(MotionResult.NotConnected, card.JogAxis(0, 50, true));
    }
}
```

> 📌 注意:这里先写 13 个卡测试,**UI 冒烟测试(第 14 个)等步骤 6 界面写完再贴进这个类里**,否则编译不过。

**✅ 验证命令与期望输出**:

```bash
dotnet build
```
```
MotionControl -> F:\00_project\MotionControlV2\src\MotionControl\bin\Debug\net8.0-windows\MotionControl.dll
已成功生成。
    0 个警告
    0 个错误
```

```bash
dotnet test
```
```
已通过! - 失败:     0，通过:    13，已跳过:     0，总计:    13 - MotionControl.Tests.dll (net8.0-windows)
```

💡 **测试怎么读**:
- `WaitUntil` 是异步测试的命根子:运动是"过程",断言前必须**轮询等到条件成立**(每 10ms 查一次,3 秒超时兜底)。直接 `Thread.Sleep(500)` 再断言是新手写法 —— 机器一慢就偶发红;
- **急停测试的精髓**:冻结位置后**再等 200ms** 断言位置没变 —— 证明没有"惯性滑动",这正是真电机的"立即停止"语义;
- 两轴并发测试就是 v1 头号 bug 的**回归测试**:以后谁把令牌改回全局的,CI 立刻红给他看。

### 步骤 5:线程安全日志 —— LogHelper

**设计思路一句**:文件日志是上位机的"黑匣子",断网车间出了事全靠它 —— 多线程写文件必须加锁,否则两行日志穿插成乱码。

```csharp
// 📂 文件:src/MotionControl/Common/LogHelper.cs
namespace MotionControlProject.Common;

/// <summary>
/// 文件日志 —— 上位机的"黑匣子"。
/// 断网车间里出了问题,现场人员能给你的往往只有这个日志文件,所以关键动作必须落盘。
///
/// v1 坑:直接 File.AppendAllText 没加锁 —— 多线程同时写文件时,两行日志会穿插成乱码。
/// v2:所有写入包在 lock 里串行化,一行永远是一行。
/// </summary>
public static class LogHelper
{
    /// <summary>写文件互斥锁:静态类全局唯一,任何线程的写入排队通过。</summary>
    private static readonly object Gate = new();

    /// <summary>日志目录:exe 旁的 logs\,按天分文件,方便按日期回溯故障。</summary>
    private static string LogDir => Path.Combine(AppContext.BaseDirectory, "logs");

    /// <summary>
    /// 写一条日志并落盘。
    /// level 用 "INFO"/"WARN"/"ERROR"(对齐 -5 左对齐,日志列才整齐)。
    /// </summary>
    public static void Log(string message, string level = "INFO")
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level,-5}] {message}";
        lock (Gate)   // 没这把锁,后台线程和 UI 线程同时写就是乱码
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(
                Path.Combine(LogDir, $"motion_{DateTime.Now:yyyyMMdd}.txt"),
                line + Environment.NewLine);
        }
    }
}
```

### 步骤 6:UI v2 —— 窗体逻辑 + Designer 布局 + 入口

**设计思路一句**:窗体只做三件事(收集输入 → 调接口 → 刷事件到控件),两轴控件收进**数组**、事件订阅用**一段循环**、按钮可用性集中到**一个方法** —— 三招灭掉 v1 的所有复制粘贴病(见 [📖 InvokeRequired/BeginInvoke](kp:winforms-invoke))。

```csharp
// 📂 文件:src/MotionControl/Program.cs(整体替换模板生成的同名文件)
namespace MotionControlProject;

/// <summary>程序入口。net8 WinForms 模板写法:ApplicationConfiguration.Initialize() 负责高 DPI / 字体 / 默认样式。</summary>
internal static class Program
{
    [STAThread]   // WinForms 硬要求:UI 线程必须是 STA(剪贴板、文件对话框等 COM 组件依赖)
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        // 想接真卡时,只改这一行:new UI.MainForm(new Device.XxxRealCard("192.168.0.10"))
        Application.Run(new UI.MainForm());
    }
}
```

```csharp
// 📂 文件:src/MotionControl/UI/MainForm.cs
using MotionControlProject.Common;
using MotionControlProject.Device;

namespace MotionControlProject.UI;

/// <summary>
/// 主窗体 —— 只做三件事:收集用户输入 → 调 IMotionCard → 把卡的事件刷回界面。
/// 本文件不含任何运动算法:换一张真卡(实现 IMotionCard 即可),这里一行都不用改。
///
/// 相比 v1 的结构改进:
/// 1. 两轴控件收进数组,事件订阅用一段循环 —— v1 每个按钮复制粘贴一个 handler,改一处漏一处;
/// 2. 按钮可用性集中在一个 RefreshUiState() 里统一算 —— v1 里 btnMoveAbs1 被禁了两次、
///    btnMoveAbs2 一次都没禁过,就是"各改各的"的恶果;
/// 3. 卡的事件都在后台线程触发,统一用 InvokeRequired + BeginInvoke 切回 UI 线程再碰控件
///    (v1 已有此好习惯,v2 保留并发扬到所有事件)。
/// </summary>
public partial class MainForm : Form
{
    private readonly IMotionCard _card;

    // ———— 控件数组:下标 0 = 轴 1,下标 1 = 轴 2 ————
    private readonly Button[] _btnEnable;
    private readonly Button[] _btnDisable;
    private readonly Button[] _btnJogForward;
    private readonly Button[] _btnJogBackward;
    private readonly Button[] _btnMoveAbs;
    private readonly Button[] _btnHome;
    private readonly TextBox[] _txtPos;
    private readonly TextBox[] _txtSpeed;
    private readonly TextBox[] _txtAbs;

    /// <summary>"运动完成"检测:记录上一帧(100ms 前)是否在运动,状态从运动翻转为静止时打一条完成日志。</summary>
    private readonly bool[] _wasMoving = new bool[2];

    /// <summary>默认构造:生产环境用模拟卡。Designer 必须有无参构造才能在 VS 里打开设计器。</summary>
    public MainForm() : this(new MockMotionCard()) { }

    /// <summary>依赖注入入口:测试或接真卡时从这里塞入任意 IMotionCard 实现。</summary>
    public MainForm(IMotionCard card)
    {
        InitializeComponent();
        _card = card;

        // Designer 生成的两轴控件按轴序收进数组,后面所有"每轴逻辑"都能写成循环
        _btnEnable      = new[] { btnEnable1, btnEnable2 };
        _btnDisable     = new[] { btnDisable1, btnDisable2 };
        _btnJogForward  = new[] { btnJog1Forward, btnJog2Forward };
        _btnJogBackward = new[] { btnJog1Backward, btnJog2Backward };
        _btnMoveAbs     = new[] { btnMoveAbs1, btnMoveAbs2 };
        _btnHome        = new[] { btnHome1, btnHome2 };
        _txtPos         = new[] { txtPos1, txtPos2 };
        _txtSpeed       = new[] { txtSpeed1, txtSpeed2 };
        _txtAbs         = new[] { txtAbs1, txtAbs2 };

        // ———— 事件订阅全部集中在这里,Designer 文件只管"长相" ————

        btnConnect.Click    += (s, e) => Connect();
        btnDisconnect.Click += (s, e) => DisconnectCard();
        btnEstop.Click      += (s, e) => EmergencyStop();
        btnClearAlarm.Click += (s, e) => ClearAlarms();

        for (var i = 0; i < 2; i++)
        {
            var axis = i;   // 闭包捕获副本的经典坑:for 的 i 是所有循环共享的变量,
                            // 不复制一份,两个按钮的 lambda 里拿到的都会是循环结束后的 2

            _btnEnable[i].Click      += (s, e) => SetAxisEnabled(axis, true);
            _btnDisable[i].Click     += (s, e) => SetAxisEnabled(axis, false);

            // 点动:按下启动、松开停止 —— MouseDown/MouseUp 而不是 Click(v1 的正确直觉,v2 保留)
            _btnJogForward[i].MouseDown  += (s, e) => StartJog(axis, forward: true);
            _btnJogForward[i].MouseUp    += (s, e) => StopJog(axis);
            _btnJogBackward[i].MouseDown += (s, e) => StartJog(axis, forward: false);
            _btnJogBackward[i].MouseUp   += (s, e) => StopJog(axis);

            _btnMoveAbs[i].Click += (s, e) => MoveAbs(axis);
            _btnHome[i].Click    += (s, e) => Home(axis);
        }

        // 卡 → 界面:三个事件分别在"位置变化 / 报警变化 / 急停"时被后台线程触发
        _card.PositionChanged  += OnPositionChanged;
        _card.AlarmChanged     += OnAlarmChanged;
        _card.EmergencyStopped += OnEmergencyStopped;

        // 100ms 界面轮询:刷新按钮状态 + 检测"运动完成"。
        // 为什么用轮询而不是事件?模拟卡没有"运动完成"事件,真卡 SDK 也常常只有状态位 ——
        // "定时查状态 + 边沿检测"是上位机最常用、最稳的完成检测手段(采集项目的管道心跳同理)。
        // Tick 事件已在 Designer 里绑定,这里只设定周期并启动
        timer1.Interval = 100;
        timer1.Start();

        RefreshUiState();
        AppendLog($"系统就绪:模拟卡已加载,{_card.AxisCount} 轴。请先【连接】再【使能】。");
    }

    // ———— 连接区 ————

    private void Connect()
    {
        // v1 坑:txtIp 里带了个前导空格,连接永远失败还看不出来 —— Trim + 判空在门口挡掉
        var ip = txtIp.Text.Trim();
        var r = _card.Connect(ip);
        if (r != MotionResult.Ok) { Fail(r, "连接"); return; }
        AppendLog($"已连接模拟卡 {ip}(共 {_card.AxisCount} 轴)");
        RefreshUiState();
    }

    private void DisconnectCard()
    {
        var r = _card.Disconnect();
        if (r != MotionResult.Ok) { Fail(r, "断开"); return; }
        AppendLog("已断开连接,所有运动已停止");
        RefreshUiState();
    }

    /// <summary>急停:唯一一个红色按钮,点击立即执行、不做任何确认弹窗 —— 急停就该一按就停。</summary>
    private void EmergencyStop()
    {
        _card.StopAll();
        AppendLog("!! 急停触发:全部轴已停止,位置冻结 !!", "ERROR");
        RefreshUiState();
    }

    private void ClearAlarms()
    {
        // 两个轴都清一遍;无报警的轴清了也无副作用(幂等)
        _card.ClearAlarm(0);
        _card.ClearAlarm(1);
        AppendLog("已清除全部报警(v1 的 btnClearAlarm 根本没绑 Click 事件 —— 纯摆设,这里是修复)");
        RefreshUiState();
    }

    // ———— 每轴操作 ————

    private void SetAxisEnabled(int axis, bool enable)
    {
        var r = _card.SetAxisEnable(axis, enable);
        if (r != MotionResult.Ok) { Fail(r, $"轴{axis + 1} {(enable ? "使能" : "失能")}"); return; }
        AppendLog($"轴{axis + 1} {(enable ? "已使能" : "已下使能")}");
        RefreshUiState();
    }

    private void StartJog(int axis, bool forward)
    {
        var speed = SpeedOf(_txtSpeed[axis]);
        var r = _card.JogAxis(axis, speed, forward);
        if (r != MotionResult.Ok) { Fail(r, $"轴{axis + 1} 点动"); return; }
        AppendLog($"轴{axis + 1} 点动 {(forward ? "正转 ▲" : "反转 ▼")} @ {speed:F0} mm/s");
    }

    private void StopJog(int axis)
    {
        _card.StopJog(axis);   // 没连卡时也只是返回错误码,不值得刷屏,不打日志
    }

    private void MoveAbs(int axis)
    {
        // 目标位置解析失败要就地报出来,v1 是"按了没反应"式沉默失败
        if (!double.TryParse(_txtAbs[axis].Text.Trim(), out var target))
        {
            AppendLog($"✗ 轴{axis + 1} 目标位置不是数字:{_txtAbs[axis].Text}", "WARN");
            return;
        }
        var speed = SpeedOf(_txtSpeed[axis]);
        var r = _card.MoveAbsolute(axis, target, speed);
        if (r != MotionResult.Ok) { Fail(r, $"轴{axis + 1} 绝对定位"); return; }
        AppendLog($"轴{axis + 1} 绝对定位 → {target:F2} mm @ {speed:F0} mm/s");
    }

    private void Home(int axis)
    {
        var r = _card.HomeAxis(axis);
        if (r != MotionResult.Ok) { Fail(r, $"轴{axis + 1} 回零"); return; }
        AppendLog($"轴{axis + 1} 回零启动(固定 {MockMotionCard.HomeSpeed:F0} mm/s)…");
    }

    // ———— 卡事件 → 界面(全部先切回 UI 线程) ————

    private void OnPositionChanged(object? sender, PositionChangedEventArgs e)
    {
        // 事件来自后台仿真线程。WinForms 铁律:非 UI 线程碰控件就抛 InvalidOperationException
        if (InvokeRequired) { BeginInvoke(() => OnPositionChanged(sender, e)); return; }
        _txtPos[e.Axis].Text = e.Position.ToString("F3");
    }

    private void OnAlarmChanged(object? sender, AlarmChangedEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(() => OnAlarmChanged(sender, e)); return; }
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        if (e.IsActive)
            txtAlarm.AppendText($"[{stamp}] 轴{e.Axis + 1} 报警:{e.Message}\r\n");
        else
            txtAlarm.AppendText($"[{stamp}] 轴{e.Axis + 1} 报警已清除\r\n");
        RefreshUiState();
    }

    private void OnEmergencyStopped(object? sender, EventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(() => OnEmergencyStopped(sender, e)); return; }
        AppendLog("卡上报:EmergencyStop 已生效", "ERROR");
    }

    // ———— 定时器:状态轮询 + 运动完成边沿检测 ————

    private void Timer1_Tick(object? sender, EventArgs e)
    {
        for (var i = 0; i < 2; i++)
        {
            var moving = _card.IsMoving(i);
            // 上一帧在动、这一帧停了 = 运动刚完成(边沿检测,只报一次)
            if (_wasMoving[i] && !moving)
                AppendLog($"轴{i + 1} 运动完成,停在 {_card.GetAxisPosition(i):F3} mm");
            _wasMoving[i] = moving;
        }
        RefreshUiState();
    }

    // ———— 界面状态集中刷新 ————

    /// <summary>
    /// 所有按钮的 Enabled 状态只在这里计算。
    /// 规则一眼可读:连接区看 IsConnected;每轴操作区 = 已连接 && 已使能 && 无报警;
    /// 急停永远可用(急停按钮被禁掉本身就是事故)。
    /// v1 的教训:按钮状态散落在各 handler 里各改各的 → btnMoveAbs1 禁了两次、btnMoveAbs2 忘了禁。
    /// </summary>
    private void RefreshUiState()
    {
        if (IsDisposed) return;

        var connected = _card.IsConnected;
        btnConnect.Enabled = !connected;
        btnDisconnect.Enabled = connected;
        txtIp.Enabled = !connected;
        // 连接指示灯:绿 = 已连接,灰 = 未连接。颜色只用来表达状态,装饰色一概不用
        lblConnectStatus.BackColor = connected ? Color.MediumSeaGreen : Color.DarkGray;

        for (var i = 0; i < 2; i++)
        {
            var operable = connected
                           && _card.IsAxisEnabled(i)
                           && string.IsNullOrEmpty(_card.GetAlarmMessage(i));
            _btnEnable[i].Enabled = connected;          // 使能/失能只要求"卡在线"
            _btnDisable[i].Enabled = connected;
            _btnJogForward[i].Enabled = operable;       // 运动类操作才要求"已使能 + 无报警"
            _btnJogBackward[i].Enabled = operable;
            _btnMoveAbs[i].Enabled = operable;
            _btnHome[i].Enabled = operable;
        }
    }

    // ———— 小工具 ————

    /// <summary>
    /// 解析速度输入:非法(空/非数字/越界)就回退默认 50 并把文本框纠正过来 —— 永远给调用方一个可用值。
    /// 上位机处理手工输入的黄金法则:防呆 + 自愈,而不是抛异常崩给用户看。
    /// </summary>
    private static double SpeedOf(TextBox box)
    {
        if (double.TryParse(box.Text.Trim(), out var v) && v > 0 && v <= 5000) return v;
        box.Text = "50";
        return 50;
    }

    /// <summary>返回码 → 人话。真卡 SDK 给你的只有 int,把它翻译成操作员能看懂的句子是上位机的本职。</summary>
    private void Fail(MotionResult r, string what)
    {
        var msg = r switch
        {
            MotionResult.NotConnected   => "卡未连接",
            MotionResult.AxisIndexError => "轴号越界",
            MotionResult.ParamError     => "参数不合法(速度/目标位置超软限位)",
            MotionResult.AxisDisabled   => "轴未使能",
            MotionResult.AlarmActive    => "轴有报警,请先清报警",
            _ => r.ToString(),
        };
        AppendLog($"✗ {what}失败:{msg}(错误码 {(int)r})", "WARN");
    }

    /// <summary>
    /// 界面日志:黑底等宽字,一行一条;同时经 LogHelper 落盘。
    /// 线程安全:后台事件线程也会调它,InvokeRequired 判断后 BeginInvoke 投递回 UI 线程。
    /// </summary>
    private void AppendLog(string message, string level = "INFO")
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => AppendLog(message, level)); return; }
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n");
        LogHelper.Log(message, level);
    }

    /// <summary>关窗前断开卡:取消所有后台运动任务,进程才能干净退出,不会留僵尸线程占用日志文件。</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _card.Disconnect();
        base.OnFormClosing(e);
    }
}
```

**UI 整容篇 —— Designer 布局**(长文件,耐心敲完;轴 2 的 GroupBox 与轴 1 完全同构,只是控件名后缀是 2):

```csharp
// 📂 文件:src/MotionControl/UI/MainForm.Designer.cs
namespace MotionControlProject.UI;

/// <summary>
/// 控件布局(Designer 风格:只描述"长相",事件订阅全在 MainForm.cs 构造函数里)。
///
/// 布局思路(UI 整容篇):
/// - 顶栏 = 连接控制 + 急停(危险动作专属红色,全窗体唯一的彩色按钮);
/// - 主体三栏:轴1 | 轴2 | 报警/日志 —— 两轴 GroupBox 内部布局完全一致,对照着抄第二遍即可;
/// - 报警框淡黄底深红字(警示色),日志框黑底浅绿等宽字(终端风,一眼区分"信息"和"告警");
/// - 颜色只表达状态与危险等级,不做任何装饰 —— 工业界面的铁律。
/// </summary>
partial class MainForm
{
    /// <summary>必需的设计器变量。</summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>清理所有正在使用的资源。</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows 窗体设计器生成的代码

    /// <summary>设计器支持所需的方法 —— 不要修改。</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        panelTop = new Panel();
        gbConnect = new GroupBox();
        lblConnectStatus = new Panel();
        btnDisconnect = new Button();
        btnConnect = new Button();
        txtIp = new TextBox();
        lblIp = new Label();
        btnEstop = new Button();
        tableLayoutPanel1 = new TableLayoutPanel();
        gbAxis1 = new GroupBox();
        lblSoftLimit1 = new Label();
        btnHome1 = new Button();
        btnMoveAbs1 = new Button();
        txtAbs1 = new TextBox();
        lblTarget1 = new Label();
        txtSpeed1 = new TextBox();
        lblSpeed1 = new Label();
        txtPos1 = new TextBox();
        lblPos1 = new Label();
        btnJog1Backward = new Button();
        btnJog1Forward = new Button();
        lblJog1 = new Label();
        btnDisable1 = new Button();
        btnEnable1 = new Button();
        lblEnable1 = new Label();
        gbAxis2 = new GroupBox();
        lblSoftLimit2 = new Label();
        btnHome2 = new Button();
        btnMoveAbs2 = new Button();
        txtAbs2 = new TextBox();
        lblTarget2 = new Label();
        txtSpeed2 = new TextBox();
        lblSpeed2 = new Label();
        txtPos2 = new TextBox();
        lblPos2 = new Label();
        btnJog2Backward = new Button();
        btnJog2Forward = new Button();
        lblJog2 = new Label();
        btnDisable2 = new Button();
        btnEnable2 = new Button();
        lblEnable2 = new Label();
        tableLayoutPanel2 = new TableLayoutPanel();
        gbAlarm = new GroupBox();
        btnClearAlarm = new Button();
        txtAlarm = new RichTextBox();
        gbLog = new GroupBox();
        txtLog = new RichTextBox();
        timer1 = new System.Windows.Forms.Timer(components);
        panelTop.SuspendLayout();
        gbConnect.SuspendLayout();
        tableLayoutPanel1.SuspendLayout();
        gbAxis1.SuspendLayout();
        gbAxis2.SuspendLayout();
        tableLayoutPanel2.SuspendLayout();
        gbAlarm.SuspendLayout();
        gbLog.SuspendLayout();
        SuspendLayout();
        //
        // panelTop —— 顶栏:左边连接控制,右边急停
        //
        panelTop.Controls.Add(gbConnect);
        panelTop.Controls.Add(btnEstop);
        panelTop.Dock = DockStyle.Top;
        panelTop.Location = new Point(0, 0);
        panelTop.Name = "panelTop";
        panelTop.Size = new Size(1200, 80);
        panelTop.TabIndex = 0;
        //
        // gbConnect —— 连接控制分组
        //
        gbConnect.Controls.Add(lblIp);
        gbConnect.Controls.Add(txtIp);
        gbConnect.Controls.Add(btnConnect);
        gbConnect.Controls.Add(btnDisconnect);
        gbConnect.Controls.Add(lblConnectStatus);
        gbConnect.Location = new Point(12, 8);
        gbConnect.Name = "gbConnect";
        gbConnect.Size = new Size(500, 64);
        gbConnect.TabIndex = 0;
        gbConnect.TabStop = false;
        gbConnect.Text = "连接控制";
        //
        // lblIp
        //
        lblIp.AutoSize = true;
        lblIp.Location = new Point(16, 34);
        lblIp.Name = "lblIp";
        lblIp.Size = new Size(65, 17);
        lblIp.TabIndex = 0;
        lblIp.Text = "IP 地址:";
        //
        // txtIp —— 默认值不带空格(v1 里这里藏过一个前导空格)
        //
        txtIp.Font = new Font("Consolas", 10.5F);
        txtIp.Location = new Point(87, 30);
        txtIp.Name = "txtIp";
        txtIp.Size = new Size(140, 25);
        txtIp.TabIndex = 1;
        txtIp.Text = "127.0.0.1";
        //
        // btnConnect
        //
        btnConnect.Location = new Point(242, 28);
        btnConnect.Name = "btnConnect";
        btnConnect.Size = new Size(95, 30);
        btnConnect.TabIndex = 2;
        btnConnect.Text = "连接";
        btnConnect.UseVisualStyleBackColor = true;
        //
        // btnDisconnect
        //
        btnDisconnect.Location = new Point(352, 28);
        btnDisconnect.Name = "btnDisconnect";
        btnDisconnect.Size = new Size(95, 30);
        btnDisconnect.TabIndex = 3;
        btnDisconnect.Text = "断开";
        btnDisconnect.UseVisualStyleBackColor = true;
        //
        // lblConnectStatus —— 连接指示灯(小方片):绿=已连接,灰=未连接,代码里改颜色
        //
        lblConnectStatus.BackColor = Color.DarkGray;
        lblConnectStatus.BorderStyle = BorderStyle.FixedSingle;
        lblConnectStatus.Location = new Point(462, 33);
        lblConnectStatus.Name = "lblConnectStatus";
        lblConnectStatus.Size = new Size(18, 18);
        lblConnectStatus.TabIndex = 4;
        //
        // btnEstop —— 急停:全窗体唯一红色按钮。Anchor 右侧,窗口缩放也贴边
        //
        btnEstop.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnEstop.BackColor = Color.FromArgb(214, 64, 64);
        btnEstop.FlatAppearance.BorderSize = 0;
        btnEstop.FlatStyle = FlatStyle.Flat;
        btnEstop.Font = new Font("微软雅黑", 12F, FontStyle.Bold);
        btnEstop.ForeColor = Color.White;
        btnEstop.Location = new Point(1050, 14);
        btnEstop.Name = "btnEstop";
        btnEstop.Size = new Size(136, 50);
        btnEstop.TabIndex = 1;
        btnEstop.Text = "急停 STOP";
        btnEstop.UseVisualStyleBackColor = false;
        //
        // tableLayoutPanel1 —— 主体三栏:轴1 | 轴2 | 报警+日志
        //
        tableLayoutPanel1.ColumnCount = 3;
        tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
        tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
        tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
        tableLayoutPanel1.Controls.Add(gbAxis1, 0, 0);
        tableLayoutPanel1.Controls.Add(gbAxis2, 1, 0);
        tableLayoutPanel1.Controls.Add(tableLayoutPanel2, 2, 0);
        tableLayoutPanel1.Dock = DockStyle.Fill;
        tableLayoutPanel1.Location = new Point(0, 80);
        tableLayoutPanel1.Name = "tableLayoutPanel1";
        tableLayoutPanel1.Padding = new Padding(10, 8, 10, 10);
        tableLayoutPanel1.RowCount = 1;
        tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        tableLayoutPanel1.Size = new Size(1200, 700);
        tableLayoutPanel1.TabIndex = 1;
        //
        // gbAxis1 —— 轴 1 全部操作。内部纵排:使能 → 点动 → 位置 → 速度 → 目标 → 定位/回零
        //
        gbAxis1.Controls.Add(lblEnable1);
        gbAxis1.Controls.Add(btnEnable1);
        gbAxis1.Controls.Add(btnDisable1);
        gbAxis1.Controls.Add(lblJog1);
        gbAxis1.Controls.Add(btnJog1Forward);
        gbAxis1.Controls.Add(btnJog1Backward);
        gbAxis1.Controls.Add(lblPos1);
        gbAxis1.Controls.Add(txtPos1);
        gbAxis1.Controls.Add(lblSpeed1);
        gbAxis1.Controls.Add(txtSpeed1);
        gbAxis1.Controls.Add(lblTarget1);
        gbAxis1.Controls.Add(txtAbs1);
        gbAxis1.Controls.Add(btnMoveAbs1);
        gbAxis1.Controls.Add(btnHome1);
        gbAxis1.Controls.Add(lblSoftLimit1);
        gbAxis1.Dock = DockStyle.Fill;
        gbAxis1.Location = new Point(13, 11);
        gbAxis1.Name = "gbAxis1";
        gbAxis1.Size = new Size(399, 677);
        gbAxis1.TabIndex = 0;
        gbAxis1.TabStop = false;
        gbAxis1.Text = "轴 1(X 轴)";
        //
        // lblEnable1 —— 小节标题(加粗,视觉分区)
        //
        lblEnable1.AutoSize = true;
        lblEnable1.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
        lblEnable1.Location = new Point(20, 38);
        lblEnable1.Name = "lblEnable1";
        lblEnable1.Size = new Size(62, 17);
        lblEnable1.TabIndex = 0;
        lblEnable1.Text = "使能控制";
        //
        // btnEnable1
        //
        btnEnable1.Location = new Point(20, 66);
        btnEnable1.Name = "btnEnable1";
        btnEnable1.Size = new Size(110, 38);
        btnEnable1.TabIndex = 1;
        btnEnable1.Text = "使能";
        btnEnable1.UseVisualStyleBackColor = true;
        //
        // btnDisable1
        //
        btnDisable1.Location = new Point(145, 66);
        btnDisable1.Name = "btnDisable1";
        btnDisable1.Size = new Size(110, 38);
        btnDisable1.TabIndex = 2;
        btnDisable1.Text = "失能";
        btnDisable1.UseVisualStyleBackColor = true;
        //
        // lblJog1
        //
        lblJog1.AutoSize = true;
        lblJog1.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
        lblJog1.Location = new Point(20, 126);
        lblJog1.Name = "lblJog1";
        lblJog1.Size = new Size(120, 17);
        lblJog1.TabIndex = 3;
        lblJog1.Text = "点动(按住不放)";
        //
        // btnJog1Forward —— 点动不设彩色:危险等级低于急停,红色只留给急停
        //
        btnJog1Forward.Location = new Point(20, 154);
        btnJog1Forward.Name = "btnJog1Forward";
        btnJog1Forward.Size = new Size(170, 56);
        btnJog1Forward.TabIndex = 4;
        btnJog1Forward.Text = "▲ 正转";
        btnJog1Forward.UseVisualStyleBackColor = true;
        //
        // btnJog1Backward
        //
        btnJog1Backward.Location = new Point(205, 154);
        btnJog1Backward.Name = "btnJog1Backward";
        btnJog1Backward.Size = new Size(170, 56);
        btnJog1Backward.TabIndex = 5;
        btnJog1Backward.Text = "▼ 反转";
        btnJog1Backward.UseVisualStyleBackColor = true;
        //
        // lblPos1
        //
        lblPos1.AutoSize = true;
        lblPos1.Location = new Point(20, 232);
        lblPos1.Name = "lblPos1";
        lblPos1.Size = new Size(111, 17);
        lblPos1.TabIndex = 6;
        lblPos1.Text = "当前位置 (mm)";
        //
        // txtPos1 —— 只显示不输入:ReadOnly + 等宽字体,数字跳动不抖版式
        //
        txtPos1.BackColor = Color.White;
        txtPos1.Font = new Font("Consolas", 14.25F);
        txtPos1.Location = new Point(20, 256);
        txtPos1.Name = "txtPos1";
        txtPos1.ReadOnly = true;
        txtPos1.Size = new Size(190, 30);
        txtPos1.TabIndex = 7;
        txtPos1.Text = "0.000";
        txtPos1.TextAlign = HorizontalAlignment.Center;
        //
        // lblSpeed1
        //
        lblSpeed1.AutoSize = true;
        lblSpeed1.Location = new Point(20, 304);
        lblSpeed1.Name = "lblSpeed1";
        lblSpeed1.Size = new Size(103, 17);
        lblSpeed1.TabIndex = 8;
        lblSpeed1.Text = "速度 (mm/s)";
        //
        // txtSpeed1 —— v1 的速度写死 50;v2 可输入,非法输入由 SpeedOf() 兜底回 50
        //
        txtSpeed1.Font = new Font("Consolas", 12F);
        txtSpeed1.Location = new Point(20, 328);
        txtSpeed1.Name = "txtSpeed1";
        txtSpeed1.Size = new Size(120, 29);
        txtSpeed1.TabIndex = 9;
        txtSpeed1.Text = "50";
        //
        // lblTarget1
        //
        lblTarget1.AutoSize = true;
        lblTarget1.Location = new Point(20, 376);
        lblTarget1.Name = "lblTarget1";
        lblTarget1.Size = new Size(127, 17);
        lblTarget1.TabIndex = 10;
        lblTarget1.Text = "目标位置 (mm)";
        //
        // txtAbs1
        //
        txtAbs1.Font = new Font("Consolas", 12F);
        txtAbs1.Location = new Point(20, 400);
        txtAbs1.Name = "txtAbs1";
        txtAbs1.Size = new Size(120, 29);
        txtAbs1.TabIndex = 11;
        txtAbs1.Text = "100";
        //
        // btnMoveAbs1
        //
        btnMoveAbs1.Location = new Point(20, 450);
        btnMoveAbs1.Name = "btnMoveAbs1";
        btnMoveAbs1.Size = new Size(170, 48);
        btnMoveAbs1.TabIndex = 12;
        btnMoveAbs1.Text = "绝对定位";
        btnMoveAbs1.UseVisualStyleBackColor = true;
        //
        // btnHome1
        //
        btnHome1.Location = new Point(205, 450);
        btnHome1.Name = "btnHome1";
        btnHome1.Size = new Size(170, 48);
        btnHome1.TabIndex = 13;
        btnHome1.Text = "回零 ⌂";
        btnHome1.UseVisualStyleBackColor = true;
        //
        // lblSoftLimit1 —— 灰字提示:把"隐藏规则"写在界面上,操作员不用翻文档
        //
        lblSoftLimit1.AutoSize = true;
        lblSoftLimit1.ForeColor = Color.Gray;
        lblSoftLimit1.Location = new Point(20, 516);
        lblSoftLimit1.Name = "lblSoftLimit1";
        lblSoftLimit1.Size = new Size(311, 17);
        lblSoftLimit1.TabIndex = 14;
        lblSoftLimit1.Text = "软限位 ±1000 mm · 流程:连接 → 使能 → 运动";
        //
        // gbAxis2 —— 与 gbAxis1 布局完全一致,仅控件名后缀不同
        //
        gbAxis2.Controls.Add(lblEnable2);
        gbAxis2.Controls.Add(btnEnable2);
        gbAxis2.Controls.Add(btnDisable2);
        gbAxis2.Controls.Add(lblJog2);
        gbAxis2.Controls.Add(btnJog2Forward);
        gbAxis2.Controls.Add(btnJog2Backward);
        gbAxis2.Controls.Add(lblPos2);
        gbAxis2.Controls.Add(txtPos2);
        gbAxis2.Controls.Add(lblSpeed2);
        gbAxis2.Controls.Add(txtSpeed2);
        gbAxis2.Controls.Add(lblTarget2);
        gbAxis2.Controls.Add(txtAbs2);
        gbAxis2.Controls.Add(btnMoveAbs2);
        gbAxis2.Controls.Add(btnHome2);
        gbAxis2.Controls.Add(lblSoftLimit2);
        gbAxis2.Dock = DockStyle.Fill;
        gbAxis2.Location = new Point(418, 11);
        gbAxis2.Name = "gbAxis2";
        gbAxis2.Size = new Size(399, 677);
        gbAxis2.TabIndex = 1;
        gbAxis2.TabStop = false;
        gbAxis2.Text = "轴 2(Y 轴)";
        //
        // lblEnable2
        //
        lblEnable2.AutoSize = true;
        lblEnable2.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
        lblEnable2.Location = new Point(20, 38);
        lblEnable2.Name = "lblEnable2";
        lblEnable2.Size = new Size(62, 17);
        lblEnable2.TabIndex = 0;
        lblEnable2.Text = "使能控制";
        //
        // btnEnable2
        //
        btnEnable2.Location = new Point(20, 66);
        btnEnable2.Name = "btnEnable2";
        btnEnable2.Size = new Size(110, 38);
        btnEnable2.TabIndex = 1;
        btnEnable2.Text = "使能";
        btnEnable2.UseVisualStyleBackColor = true;
        //
        // btnDisable2
        //
        btnDisable2.Location = new Point(145, 66);
        btnDisable2.Name = "btnDisable2";
        btnDisable2.Size = new Size(110, 38);
        btnDisable2.TabIndex = 2;
        btnDisable2.Text = "失能";
        btnDisable2.UseVisualStyleBackColor = true;
        //
        // lblJog2
        //
        lblJog2.AutoSize = true;
        lblJog2.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
        lblJog2.Location = new Point(20, 126);
        lblJog2.Name = "lblJog2";
        lblJog2.Size = new Size(120, 17);
        lblJog2.TabIndex = 3;
        lblJog2.Text = "点动(按住不放)";
        //
        // btnJog2Forward
        //
        btnJog2Forward.Location = new Point(20, 154);
        btnJog2Forward.Name = "btnJog2Forward";
        btnJog2Forward.Size = new Size(170, 56);
        btnJog2Forward.TabIndex = 4;
        btnJog2Forward.Text = "▲ 正转";
        btnJog2Forward.UseVisualStyleBackColor = true;
        //
        // btnJog2Backward
        //
        btnJog2Backward.Location = new Point(205, 154);
        btnJog2Backward.Name = "btnJog2Backward";
        btnJog2Backward.Size = new Size(170, 56);
        btnJog2Backward.TabIndex = 5;
        btnJog2Backward.Text = "▼ 反转";
        btnJog2Backward.UseVisualStyleBackColor = true;
        //
        // lblPos2
        //
        lblPos2.AutoSize = true;
        lblPos2.Location = new Point(20, 232);
        lblPos2.Name = "lblPos2";
        lblPos2.Size = new Size(111, 17);
        lblPos2.TabIndex = 6;
        lblPos2.Text = "当前位置 (mm)";
        //
        // txtPos2
        //
        txtPos2.BackColor = Color.White;
        txtPos2.Font = new Font("Consolas", 14.25F);
        txtPos2.Location = new Point(20, 256);
        txtPos2.Name = "txtPos2";
        txtPos2.ReadOnly = true;
        txtPos2.Size = new Size(190, 30);
        txtPos2.TabIndex = 7;
        txtPos2.Text = "0.000";
        txtPos2.TextAlign = HorizontalAlignment.Center;
        //
        // lblSpeed2
        //
        lblSpeed2.AutoSize = true;
        lblSpeed2.Location = new Point(20, 304);
        lblSpeed2.Name = "lblSpeed2";
        lblSpeed2.Size = new Size(103, 17);
        lblSpeed2.TabIndex = 8;
        lblSpeed2.Text = "速度 (mm/s)";
        //
        // txtSpeed2
        //
        txtSpeed2.Font = new Font("Consolas", 12F);
        txtSpeed2.Location = new Point(20, 328);
        txtSpeed2.Name = "txtSpeed2";
        txtSpeed2.Size = new Size(120, 29);
        txtSpeed2.TabIndex = 9;
        txtSpeed2.Text = "50";
        //
        // lblTarget2
        //
        lblTarget2.AutoSize = true;
        lblTarget2.Location = new Point(20, 376);
        lblTarget2.Name = "lblTarget2";
        lblTarget2.Size = new Size(127, 17);
        lblTarget2.TabIndex = 10;
        lblTarget2.Text = "目标位置 (mm)";
        //
        // txtAbs2
        //
        txtAbs2.Font = new Font("Consolas", 12F);
        txtAbs2.Location = new Point(20, 400);
        txtAbs2.Name = "txtAbs2";
        txtAbs2.Size = new Size(120, 29);
        txtAbs2.TabIndex = 11;
        txtAbs2.Text = "100";
        //
        // btnMoveAbs2
        //
        btnMoveAbs2.Location = new Point(20, 450);
        btnMoveAbs2.Name = "btnMoveAbs2";
        btnMoveAbs2.Size = new Size(170, 48);
        btnMoveAbs2.TabIndex = 12;
        btnMoveAbs2.Text = "绝对定位";
        btnMoveAbs2.UseVisualStyleBackColor = true;
        //
        // btnHome2
        //
        btnHome2.Location = new Point(205, 450);
        btnHome2.Name = "btnHome2";
        btnHome2.Size = new Size(170, 48);
        btnHome2.TabIndex = 13;
        btnHome2.Text = "回零 ⌂";
        btnHome2.UseVisualStyleBackColor = true;
        //
        // lblSoftLimit2
        //
        lblSoftLimit2.AutoSize = true;
        lblSoftLimit2.ForeColor = Color.Gray;
        lblSoftLimit2.Location = new Point(20, 516);
        lblSoftLimit2.Name = "lblSoftLimit2";
        lblSoftLimit2.Size = new Size(311, 17);
        lblSoftLimit2.TabIndex = 14;
        lblSoftLimit2.Text = "软限位 ±1000 mm · 流程:连接 → 使能 → 运动";
        //
        // tableLayoutPanel2 —— 第三栏上下切:报警 52% / 日志 48%
        //
        tableLayoutPanel2.ColumnCount = 1;
        tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tableLayoutPanel2.Controls.Add(gbAlarm, 0, 0);
        tableLayoutPanel2.Controls.Add(gbLog, 0, 1);
        tableLayoutPanel2.Dock = DockStyle.Fill;
        tableLayoutPanel2.Location = new Point(823, 11);
        tableLayoutPanel2.Name = "tableLayoutPanel2";
        tableLayoutPanel2.RowCount = 2;
        tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 52F));
        tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 48F));
        tableLayoutPanel2.Size = new Size(374, 677);
        tableLayoutPanel2.TabIndex = 2;
        //
        // gbAlarm
        //
        gbAlarm.Controls.Add(txtAlarm);
        gbAlarm.Controls.Add(btnClearAlarm);
        gbAlarm.Dock = DockStyle.Fill;
        gbAlarm.Location = new Point(3, 3);
        gbAlarm.Name = "gbAlarm";
        gbAlarm.Size = new Size(368, 346);
        gbAlarm.TabIndex = 0;
        gbAlarm.TabStop = false;
        gbAlarm.Text = "报警信息";
        //
        // txtAlarm —— 淡黄底 + 深红字:一眼锁定告警(与日志的黑绿风彻底区分)
        //
        txtAlarm.BackColor = SystemColors.Info;
        txtAlarm.DetectUrls = false;
        txtAlarm.Font = new Font("Consolas", 9.75F);
        txtAlarm.ForeColor = Color.Firebrick;
        txtAlarm.Location = new Point(16, 40);
        txtAlarm.Name = "txtAlarm";
        txtAlarm.ReadOnly = true;
        txtAlarm.Size = new Size(336, 250);
        txtAlarm.TabIndex = 0;
        txtAlarm.Text = "";
        //
        // btnClearAlarm —— v1 的清报警按钮没绑事件,是摆设;v2 真正工作
        //
        btnClearAlarm.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnClearAlarm.Location = new Point(16, 296);
        btnClearAlarm.Name = "btnClearAlarm";
        btnClearAlarm.Size = new Size(180, 38);
        btnClearAlarm.TabIndex = 1;
        btnClearAlarm.Text = "清除全部报警";
        btnClearAlarm.UseVisualStyleBackColor = true;
        //
        // gbLog
        //
        gbLog.Controls.Add(txtLog);
        gbLog.Dock = DockStyle.Fill;
        gbLog.Location = new Point(3, 355);
        gbLog.Name = "gbLog";
        gbLog.Size = new Size(368, 319);
        gbLog.TabIndex = 1;
        gbLog.TabStop = false;
        gbLog.Text = "运行日志";
        //
        // txtLog —— 黑底浅绿等宽字,终端风;所有动作留痕,同时落盘 logs\
        //
        txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        txtLog.BackColor = Color.Black;
        txtLog.DetectUrls = false;
        txtLog.Font = new Font("Consolas", 9.75F);
        txtLog.ForeColor = Color.LightGreen;
        txtLog.Location = new Point(16, 40);
        txtLog.Name = "txtLog";
        txtLog.ReadOnly = true;
        txtLog.ScrollBars = RichTextBoxScrollBars.Vertical;
        txtLog.Size = new Size(336, 263);
        txtLog.TabIndex = 0;
        txtLog.Text = "";
        //
        // timer1 —— 100ms 界面轮询:按钮状态刷新 + 运动完成边沿检测(Interval 在 MainForm.cs 里设)
        //
        timer1.Tick += Timer1_Tick;
        //
        // MainForm
        //
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1200, 780);
        Controls.Add(tableLayoutPanel1);
        Controls.Add(panelTop);
        Font = new Font("微软雅黑", 9.75F);
        MinimumSize = new Size(1216, 819);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "运动控制平台 · 模拟版 v2(接口化 + 多轴并发 + 软限位 + 急停 + 回零)";
        panelTop.ResumeLayout(false);
        gbConnect.ResumeLayout(false);
        gbConnect.PerformLayout();
        tableLayoutPanel1.ResumeLayout(false);
        tableLayoutPanel1.PerformLayout();
        gbAxis1.ResumeLayout(false);
        gbAxis1.PerformLayout();
        gbAxis2.ResumeLayout(false);
        gbAxis2.PerformLayout();
        tableLayoutPanel2.ResumeLayout(false);
        tableLayoutPanel2.PerformLayout();
        gbAlarm.ResumeLayout(false);
        gbLog.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion

    private Panel panelTop;
    private GroupBox gbConnect;
    private Label lblIp;
    private TextBox txtIp;
    private Button btnConnect;
    private Button btnDisconnect;
    private Panel lblConnectStatus;
    private Button btnEstop;
    private TableLayoutPanel tableLayoutPanel1;
    private GroupBox gbAxis1;
    private Label lblEnable1;
    private Button btnEnable1;
    private Button btnDisable1;
    private Label lblJog1;
    private Button btnJog1Forward;
    private Button btnJog1Backward;
    private Label lblPos1;
    private TextBox txtPos1;
    private Label lblSpeed1;
    private TextBox txtSpeed1;
    private Label lblTarget1;
    private TextBox txtAbs1;
    private Button btnMoveAbs1;
    private Button btnHome1;
    private Label lblSoftLimit1;
    private GroupBox gbAxis2;
    private Label lblEnable2;
    private Button btnEnable2;
    private Button btnDisable2;
    private Label lblJog2;
    private Button btnJog2Forward;
    private Button btnJog2Backward;
    private Label lblPos2;
    private TextBox txtPos2;
    private Label lblSpeed2;
    private TextBox txtSpeed2;
    private Label lblTarget2;
    private TextBox txtAbs2;
    private Button btnMoveAbs2;
    private Button btnHome2;
    private Label lblSoftLimit2;
    private TableLayoutPanel tableLayoutPanel2;
    private GroupBox gbAlarm;
    private RichTextBox txtAlarm;
    private Button btnClearAlarm;
    private GroupBox gbLog;
    private RichTextBox txtLog;
    private System.Windows.Forms.Timer timer1;
}
```

🗺️ **新手读码地图 —— UI 怎么走读**:
1. **布局层级倒着看**:窗体 → `tableLayoutPanel1`(三栏,Dock=Fill 撑满 panelTop 以外的全部)→ 每栏一个 GroupBox;第三栏再套 `tableLayoutPanel2` 上下切。**Dock + 百分比** = 窗口怎么拉都不乱(v1 纯绝对坐标,一缩放就穿帮);
2. **对照 v1 的 TableLayoutPanel**:v1 已经用了表格布局(好底子),v2 只是把它从"5 列散放"改成"3 栏分区",并把急停从堆里拿出来钉在顶栏右侧;
3. **找一遍颜色**:红(急停,唯一)、绿/灰(连接状态灯)、淡黄+深红(报警)、黑+浅绿(日志)—— 每种颜色都**只表达状态**,没有任何"好看"的装饰色;
4. **前端类比**:像把无语义的 div 堆改成 flex 三栏 + 语义化分区;`RefreshUiState()` 就是你熟悉的"状态驱动渲染" —— 状态一变,按钮态集中重算,而不是每个 handler 手搓 DOM(控件)。

### 步骤 7:UI 全流程冒烟测试(第 14 个测试)

**设计思路一句**:数据采集项目的教训 —— **短暂启动冒烟测不出交互期 bug**(参考工程 3 个运行时崩溃全是这么漏掉的)。所以在 STA 线程上把整个操作流程真跑一遍:后台事件 → BeginInvoke 投递 → 消息泵(DoEvents)分发 → 控件更新 → 定时器 Tick,任何一环跨线程碰控件都会当场炸。

把下面这个测试**贴进 `MockMotionCardTests` 类里**(最后一个 `}` 之前):

```csharp
    // ———— 9. UI 全流程冒烟 ————

    [Fact]
    public void UI冒烟_窗体全流程不崩溃()
    {
        // 数据采集项目的教训:短暂启动冒烟测不出交互期 bug。
        // 这里在 STA 线程上把整个操作流程真跑一遍:
        // 后台线程事件 → BeginInvoke 投递 → DoEvents 消息泵分发 → 控件更新 → 定时器 Tick,
        // 任何一步跨线程碰控件都会当场抛异常。
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var card = new MockMotionCard(tickMs: 10);
                var form = new MainForm(card);
                form.Show();

                // 模拟操作员全流程:连接 → 使能 → 双轴同时点动 → 定位 → 注入报警 → 清报警 → 急停 → 断开
                card.Connect("127.0.0.1");
                card.SetAxisEnable(0, true);
                card.SetAxisEnable(1, true);
                card.JogAxis(0, 200, forward: true);     // 轴1 正转
                card.JogAxis(1, 200, forward: false);    // 轴2 反转(两轴并发,事件线程全在跑)
                Pump(50);                                 // 0.5s 消息泵,让位置事件刷到界面
                card.MoveAbsolute(0, 300, 400);           // 定位打断点动(打断语义)
                Pump(50);
                card.SimulateAlarm(1, "UI 冒烟注入报警");  // 报警事件 → 报警框
                Pump(30);
                card.ClearAlarm(1);
                Pump(20);
                card.StopAll();                           // 急停事件
                Pump(20);
                card.Disconnect();
                Pump(10);
                form.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);   // WinForms 控件必须活在 STA 线程
        thread.Start();
        thread.Join(60000);
        Assert.Null(error);
        return;

        // 手动消息泵:Application.Run 会阻塞测试,这里用 DoEvents 循环代替,
        // 每轮处理完队列里所有消息(包括 BeginInvoke 投递和 Timer 的 WM_TIMER)
        static void Pump(int loops)
        {
            for (var i = 0; i < loops; i++)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
        }
    }
```

💡 **为什么这么写**:
- WinForms 控件有**线程亲和性**,必须活在 STA 线程 —— `SetApartmentState(ApartmentState.STA)` 是硬要求;
- `Application.Run` 会阻塞到窗体关闭,测试里用不了;`Application.DoEvents()` 手动泵一轮消息,`BeginInvoke` 的投递和 `Timer` 的 WM_TIMER 就都被处理了 —— 这就是"迷你消息循环";
- 这个测试在沙盒里**真实抓出过** Designer/Invoke 链路的错误(比如事件里直接碰控件),比"启动 2 秒没崩"的裸冒烟强得多。

---

## ✅ 验证(沙盒实测输出,你可以逐字对)

沙盒路径 `F:\00_project\_mcverify`,按本文档字面执行(命令逐条跑、代码逐块粘),实测:

```bash
dotnet build
```
```
MotionControl -> F:\00_project\_mcverify\src\MotionControl\bin\Debug\net8.0-windows\MotionControl.dll
MotionControl.Tests -> F:\00_project\_mcverify\src\MotionControl.Tests\bin\Debug\net8.0-windows\MotionControl.Tests.dll
已成功生成。
    0 个警告
    0 个错误
```

```bash
dotnet test
```
```
已通过! - 失败:     0，通过:    14，已跳过:     0，总计:    14，持续时间: 9 s - MotionControl.Tests.dll (net8.0-windows)
```

**14 个测试 = 14 条验收**:空 IP / 未连接 / 未使能 / **两轴同时点动互不干扰** / 短距离精确定位 / 零距离 / 超软限位拒绝 / **急停就地冻结** / **回零精确到 0** / 报警阻断+清警恢复 / **点动撞限位自动停** / **插补等比同达** / 断开取消运动 / **UI 全流程冒烟 0 异常**。

**界面手动验收清单**(`dotnet run --project src/MotionControl`,亲自点一遍):

- [ ] 启动即见三栏分区 + 右上角红色"急停 STOP";日志区打出"系统就绪"
- [ ] 不连接直接点"使能" → 日志报"卡未连接(错误码 -1)";运动按钮全部灰
- [ ] 连接 → 指示灯变绿;使能两轴 → 点动/定位按钮变亮
- [ ] **按住轴 1 正转不放,再按住轴 2 反转 —— 两个位置框同时在跳**(v1 做不到)
- [ ] 定位到 100,运动中日志每隔一会儿无输出、位置平滑走,停后打"运动完成,停在 100.000 mm"
- [ ] 定位输入 2000 → 日志报"参数不合法"(软限位)
- [ ] 速度框输入 abc → 自动变回 50(防呆自愈)
- [ ] 按住正转不放约 3 秒(速度调 3000)→ 撞 +1000 自动停,报警框淡黄底出现"触发正软限位",运动按钮变灰
- [ ] 清除全部报警 → 报警框打"已清除",按钮恢复
- [ ] 运动中拍急停 → 位置立刻停住,日志红字"急停触发",再点回零 → 精确回 0.000
- [ ] 关窗重开,`bin\Debug\net8.0-windows\logs\motion_今天日期.txt` 里有全部操作记录

---

## ✅ 验收清单(对着需求单逐条勾)

- [ ] FR-M01 net8.0-windows 三件套,build 0 错 0 警,测试工程能引用主程序
- [ ] FR-M02 IMotionCard + MotionResult 枚举,上层只依赖接口
- [ ] FR-M03 两轴同时点动互不打断;短距离定位精确;新指令打断旧运动
- [ ] FR-M04 急停就地冻结 + EmergencyStopped 事件
- [ ] FR-M05 回零精确回 0.000
- [ ] FR-M06 目标超限拒收;点动撞限位自动停 + 报警
- [ ] FR-M07 报警阻断运动(AlarmActive),清除后恢复,界面报警框联动
- [ ] FR-M08 速度可输入非法自愈;IP Trim;错误码全程明确
- [ ] FR-M09 ≥13 个测试全绿(参考实现 14 个)
- [ ] FR-M10 GroupBox 分区;红色只属于急停;报警淡黄深红;日志黑底浅绿等宽
- [ ] FR-M11 按钮态只在 RefreshUiState() 计算;两轴控件数组化
- [ ] FR-M12 LogHelper 加锁落盘;界面日志后台线程安全投递
- [ ] FR-M13 (可选)插补任意时刻 X:Y≈5:3,同时到位
- [ ] FR-M14 UI 全流程冒烟测试通过

---

## 🎤 面试怎么讲这一篇

> **一句话开场**:"我做一个两轴模拟运控上位机,第一版只有点动和定位;我把它升级成了接口化、可测试、带完整安全链路的版本 —— 用每轴一个 CancellationToken 解决多轴并发,用取消令牌实现急停的'就地冻结',加了软限位和报警阻断,配了 14 个单元测试,其中一个是真跑 WinForms 消息泵的全流程冒烟。"

**追问弹药库**:
- **"多轴同时动怎么实现?"** —— 每轴独立的 CancellationTokenSource + 独立仿真任务。v1 我用全局变量管两根轴,按轴 2 会停轴 1;改成"每轴一个令牌"后天然互不干扰,还写了回归测试钉死这个行为;
- **"急停怎么保证'就地冻结'?"** —— 运动循环里 `await Task.Delay(节拍, token)`,取消时在下个节拍抛 OperationCanceledException,catch 住什么都不做,位置停在当前值;测试里冻结后等 200ms 再断言位置没变,证明没有"惯性滑动";
- **"为什么接口化?"** —— 上位机行业现实是开发期经常没有真卡(卡在客户产线上)。模拟卡先跑通全流程,真卡来了实现同一个 IMotionCard,窗体一行不改,只换构造函数里 new 的那个对象;
- **"跨线程更新界面出过什么问题?"** —— 卡的位置事件在后台仿真线程触发,直接碰控件 WinForms 会抛 InvalidOperationException;统一走 InvokeRequired + BeginInvoke 投递回 UI 线程(和 WPF 的 Dispatcher 同一个道理);
- **"返回码为什么用枚举不用异常?"** —— 对齐真卡 SDK 的 int 返回码习惯(0 成功负数失败);设备指令失败是**业务常态**不是异常路径,用返回码让调用方强制处理;
- **"UI 测试怎么做的?"** —— STA 线程 + `Application.DoEvents()` 手动泵消息,把"连接→使能→双轴点动→定位→注报警→清警→急停→断开"全流程真跑一遍。因为我之前踩过"短暂冒烟测不出交互期 bug"的坑 —— 界面能不能立起来和交互全流程跑不跑得通,是两码事。

**和采集项目串成一条线**:采集(DaqMonitor)讲的是"数据怎么进来、怎么处理、怎么落库";运控(MotionControl)讲的是"指令怎么下去、怎么被安全地执行、怎么被随时打断"。一进一出,面试官问哪个方向你都有整个工程托底。

---

> 下一篇(计划):**接真卡篇** —— 用雷赛/正运动的 C# SDK 实现 IMotionCard(真实回零的原点开关 + Z 相、跟随误差、伺服报警字),以及运动引擎的"指令队列 + 到位判断 + 超时保护"。
