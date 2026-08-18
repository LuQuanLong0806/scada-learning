# MC1 · 工程骨架与模拟卡(net8 迁移 + IMotionCard + 两轴并发仿真)

> **系列导航**:**MC1 骨架与模拟卡** → [MC2 卡的行为测试](项目实践_MotionControl_MC2_卡的行为测试.md) → [MC3 WinForms 主界面](项目实践_MotionControl_MC3_WinForms主界面.md) → [MC4 UI 冒烟验收](项目实践_MotionControl_MC4_UI冒烟验收.md) → [MC5 两轴直线插补(可选)](项目实践_MotionControl_MC5_两轴直线插补.md) → [MC6 轨迹可视化(可选)](项目实践_MotionControl_MC6_轨迹可视化.md)
> **定位**:把你已经能跑的 WinForms 两轴模拟运控 v1(模拟设备 + 点动 + 绝对定位 + 日志,骨架完全正确)升级成 v2 的第一步:搭 net8 工程 + 设备抽象接口 + 行为正确的模拟卡。整个系列做完,面试里"讲不完的项目"变成"讲不完的亮点"。
> **前置**:DaqMonitor R1-R8(或同等基础:接口/事件/Task.Run/WinForms 基本控件)。
> **预计开发时长**:跟敲 0.5-1 天。**先只看「📋 需求单」自己写,卡住再看「🛠️ 参考实现」对答案。**

---

## 🎯 本篇交付物

1. 一个 net8.0-windows 的解决方案 `MotionControl`(主程序 + xUnit 测试工程,能互相引用);
2. 设备抽象接口 **IMotionCard** + 返回码枚举 **MotionResult**(对齐真卡 SDK 习惯);
3. 一张**行为正确的模拟卡 MockMotionCard**:两轴同时点动互不打断、短距离定位不瞬移、急停就地冻结、回零、软限位、报警阻断 —— 本篇只要求 `dotnet build` 0 错 0 警,**卡的行为验收在 [MC2](项目实践_MotionControl_MC2_卡的行为测试.md) 用 12 个测试逐条钉死**。

最终工程长这样(整个系列的结构,本篇完成后 Device/Common 两个目录里就有完整内容):

```
MotionControl/
├── MotionControl.sln
└── src/
    ├── MotionControl/            ← 主程序(WinForms,net8.0-windows)
    │   ├── Device/               ← MC1:接口 + 模拟卡
    │   ├── Common/               ← MC3:线程安全日志
    │   ├── UI/                   ← MC3:窗体(MC6 再加轨迹面板 TrajectoryPanel)
    │   ├── Program.cs            ← MC3
    │   └── MotionControl.csproj
    └── MotionControl.Tests/      ← MC2 起:测试
        ├── MockMotionCardTests.cs
        └── MotionControl.Tests.csproj
```

---

## 📋 需求单(产品经理视角 —— 先自己想怎么做)

### v1 的 10 个坑(整个系列的升级动机,先对号入座)

| # | v1 的坑 | 现场后果 | 在哪一篇修 |
|---|---|---|---|
| ① | 全局一个 `_isJogging`/`_jogCts` 管两根轴 | 按住轴 1 点动再按轴 2,轴 1 直接停 | **MC1**(每轴一个令牌) |
| ② | `totalSteps = moveTime / 100`,短距离算出 0 步 | 除零崩 / 目标很近时"瞬移" | **MC1**(步数公式) |
| ③ | 没有急停 | 程序失控时操作员只能拔电源 | **MC1**(StopAll) |
| ④ | 没有回零 | 每次开机坐标基准都不确定 | **MC1**(HomeAxis) |
| ⑤ | 没有软限位 | 点动按住不放,坐标飞到无穷大 | **MC1**(±1000 软限位) |
| ⑥ | 速度写死 50 | 想快想慢都做不到 | MC3(速度输入 + 防呆) |
| ⑦ | `btnMoveAbs1.Enabled` 设两次,`btnMoveAbs2` 一次都没设 | 按钮状态靠缘分 | MC3(集中刷新) |
| ⑧ | 清报警按钮没绑 Click 事件 | 报了警永远清不掉 | **MC1**(报警链路)+ MC3(按钮) |
| ⑨ | 复制粘贴 handler,轴 2 按钮日志打"轴 1" | 日志和操作对不上 | MC3(控件数组) |
| ⑩ | 事件里 Thread.Sleep 卡界面;日志写文件不加锁;IP 框藏前导空格 | 界面假死 / 日志乱码 / 连接莫名失败 | **MC1**(IP)+ MC3(其余) |

### 本篇功能需求 FR 表

| 编号 | 需求 | 验收标准 |
|---|---|---|
| FR-M01 | net8.0-windows 工程:解决方案 + 主程序 + xUnit 测试工程 | `dotnet build` 0 错 0 警;测试工程能引用主程序 |
| FR-M02 | 设备抽象 `IMotionCard` + 返回码枚举 `MotionResult` | 上层代码只依赖接口;0=Ok、负数=各类失败,对齐真卡 SDK 习惯 |
| FR-M03 | 模拟卡两轴并发仿真 | 轴 1、轴 2 同时点动互不打断;短距离定位精确到位;新指令可打断在途运动 |
| FR-M04 | 急停 `StopAll` | 任意运动中急停,所有轴位置就地冻结(1mm 都不多走),触发事件 |
| FR-M05 | 回零 `HomeAxis` | 从任意位置精确回到 0.000 |
| FR-M06 | 软限位 ±1000mm | 目标超限的定位指令被拒绝;点动顶到限位自动停 + 报警 |
| FR-M07 | 报警链路 | 报警时该轴一切运动被拒(AlarmActive);清除后恢复;事件可订阅 |

**先自己想**:① 接口要哪些方法和事件?("卡能做什么"和"卡会主动说什么"分开列)② "两轴同时点动互不打断",v1 的全局变量思路错在哪?③ 急停要能打断"正在走的一段运动",你想到了 C# 的哪个机制?④ "步数 = 距离 ÷ 速度 ÷ 节拍"这个公式,短距离时怎么防 0 步?

---

## 📚 本篇知识点(不懂再点回去学)

- [📖 IDevice 设备统一抽象](kp:idevice) —— 为什么 IMotionCard 和采集项目的 IDevice 是同一个思想
- [📖 event / EventHandler 事件机制](kp:event-delegate) —— 卡的位置/报警事件怎么往上抛
- [📖 Task.Run / async-await](kp:taskrun) —— 仿真循环就是 `Task.Run` 里一段可取消的 await 循环
- [📖 CancellationToken 协作式取消](kp:cancel-token) —— 急停/松手/新指令打断,全靠它

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

SDK 风格工程的好处:csproj 自动把目录下所有 .cs 编进去,不用像老 csproj 那样逐个登记。

### 步骤 2:设备抽象层 —— IMotionCard + MotionResult

**设计思路一句**:把"运动控制卡能做什么"抽成接口 —— 上位机行业现实是你开发时手上没有真卡,模拟卡/真卡各做一个实现,上层(窗体/测试)只认接口,这和采集项目 IDevice 一模一样(见 [📖 IDevice 设备统一抽象](kp:idevice))。

先想清楚**返回码**:真卡 SDK(固高/雷赛/正运动)几乎都返回 int,0 成功负数失败。用枚举把"魔数"变成可读名字,同时保留负数值:

```csharp
// 📂 文件:src/MotionControl/Device/IMotionCard.cs
namespace MotionControlProject.Device;

/// <summary>
/// 运动指令返回码 —— 对齐真实板卡 SDK 的习惯:0 = 成功,负数 = 各种失败原因。
/// 真卡 SDK(Googol/雷赛/正运动…)几乎都返回 int 错误码,这里用枚举把"魔数"变成可读名字,
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

    /// <summary>某轴是否正在运动(点动 / 定位 / 回零都算)。</summary>
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

    // ———— 模拟卡专用(真卡没有) ————

    /// <summary>人为注入一条报警 —— 用来在没真故障的情况下测试报警链路。</summary>
    void SimulateAlarm(int axis, string message);
}
```

> 两轴直线插补 `MoveLinear` 是 [MC5](项目实践_MotionControl_MC5_两轴直线插补.md) 的可选加餐,到时候往这个接口里加声明。

💡 **接口设计的三个门道**:
- **读和动分开**:读位置/读报警不加前置条件(现实里编码器位置任何时候都读得到);运动指令才要求"已连接 + 已使能 + 无报警"。v1 把这些搅在一起,报错信息也说不清;
- **事件承载"卡主动说"**:位置变化、报警、急停是卡 → 上层的通知流;方法调用是上层 → 卡的命令流。两个方向分开,界面就不会变成轮询大杂烩(见 [📖 event/EventHandler](kp:event-delegate));
- **SimulateAlarm 是模拟卡专属**:放在接口里是妥协(为了演示方便),真卡实现里它就是个空方法或直接不实现 —— 文档诚实标注,面试也能讲这层取舍。

### 步骤 3:模拟卡 v2 —— MockMotionCard(本系列的心脏)

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
/// 2. v1 点动/定位共用状态互相打架 → v2 点动、定位、回零统一走"取消旧的、启动新的";
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

> 直线插补 `MoveLinear` 的实现也是往这个类里加方法,放在 [MC5](项目实践_MotionControl_MC5_两轴直线插补.md)。

🗺️ **新手读码地图 —— MockMotionCard 怎么走读**:
1. **先看字段**:`_positions/_enabled/_alarms/_cts/_moving` 五个数组,下标就是轴号 —— "每轴一组状态"这句话落到了内存布局上;
2. **再追一条指令**:以 `MoveAbsolute` 为例 —— `CheckMotionLocked`(五连检查,每种失败一个错误码)→ 目标超软限位拒收 → 已在目标位直接 Ok → `StartMotionLocked` 启动仿真;
3. **钻进 StartMotionLocked**(全文件最核心的 40 行):取消旧令牌 → 建新令牌 → 算步数和步长 → `Task.Run` 里 for 循环:每步 `Task.Delay(节拍, token)` → 推进位置 → 触发事件 →(点动)查软限位;循环外精确贴目标;`catch (OperationCanceledException)` = 被打断就地冻结;`finally` 里复位 `_moving`;
4. **对照 v1 想一遍**:v1 的 `_isJogging` 全局一个 → 现在 `_cts[axis]` 每轴一个;v1 的 `totalSteps = moveTime/100` → 现在 `Math.Max(1, Ceiling(…))` + 完成贴目标;v1 点动/定位打架 → 现在都走"取消旧的启动新的",天然互斥;
5. **前端类比**:像组件里每个 tab 一个自己的 abortController —— 谁的请求谁取消,互不连坐。

---

## ✅ 验证(沙盒实测输出,你可以逐字对)

```bash
dotnet build
```
```
MotionControl -> F:\00_project\MotionControlV2\src\MotionControl\bin\Debug\net8.0-windows\MotionControl.dll
已成功生成。
    0 个警告
    0 个错误
```

本篇到此为止 —— 卡的行为对不对,**下一篇用测试说话**。

---

## ✅ 验收清单(对着需求单逐条勾)

- [ ] FR-M01 三件套工程建好,`dotnet build` 0 错 0 警
- [ ] FR-M02 IMotionCard + MotionResult 完成,方法/事件/返回码齐全(MoveLinear 留 MC5)
- [ ] FR-M03 每轴独立 CancellationToken;步数公式带 `Math.Max(1, …)`;新指令取消旧令牌 —— **行为验收见 MC2 测试**
- [ ] FR-M04 StopAll 取消所有令牌 + 触发 EmergencyStopped —— **MC2 验证**
- [ ] FR-M05 HomeAxis 走回 0 —— **MC2 验证**
- [ ] FR-M06 超软限位拒收;点动撞限位夹住 + 报警 —— **MC2 验证**
- [ ] FR-M07 报警时 CheckMotionLocked 返回 AlarmActive;ClearAlarm 恢复 + 发清除事件 —— **MC2 验证**

---

## 🎤 面试怎么讲这一篇

> **一句话开场**:"我做的模拟运控上位机,设备层是一套接口 + 两张卡:IMotionCard 定义卡的全部能力和事件,MockMotionCard 用可取消的后台任务仿真运动过程,真卡来了再写一个实现,上层零改动。"

**追问弹药库**:
- **"多轴同时动怎么实现?"** —— 每轴独立的 CancellationTokenSource + 独立仿真任务。v1 我用全局变量管两根轴,按轴 2 会停轴 1;改成"每轴一个令牌"后天然互不干扰,后面还写了回归测试钉死这个行为;
- **"急停怎么保证'就地冻结'?"** —— 运动循环里 `await Task.Delay(节拍, token)`,取消时在下个节拍抛 OperationCanceledException,catch 住什么都不做,位置停在当前值;
- **"为什么接口化?"** —— 上位机行业现实是开发期经常没有真卡(卡在客户产线上)。模拟卡先跑通全流程,真卡来了实现同一个 IMotionCard,窗体一行不改;
- **"返回码为什么用枚举不用异常?"** —— 对齐真卡 SDK 的 int 返回码习惯(0 成功负数失败);设备指令失败是**业务常态**不是异常路径,用返回码让调用方强制处理。

下一篇:[MC2 · 卡的行为测试 —— 12 个测试钉死本篇的全部行为](项目实践_MotionControl_MC2_卡的行为测试.md)
