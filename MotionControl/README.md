# MotionControl —— 两轴运动控制模拟上位机(net8.0-windows + xUnit)

DAQMonitor 的姊妹项目:把 `项目实践_MotionControl_MC1` ~ `MC6` 六篇教程文档还原成一个真实可跑的工程。设备层是 `IMotionCard` 接口 + `MockMotionCard` 模拟卡(每轴独立 CancellationToken、急停就地冻结、回零、±1000 软限位、报警链路、两轴直线插补),界面是 WinForms(控件数组、InvokeRequired+BeginInvoke、定时器边沿检测、自绘 X-Y 轨迹面板)。零第三方运行时依赖,全部 BCL/自绘。

## 目录结构

```
MotionControl/
├── MotionControl.sln
└── src/
    ├── MotionControl/                 ← 主程序(WinForms, net8.0-windows)
    │   ├── Device/
    │   │   ├── IMotionCard.cs         ← MC1:MotionResult + 事件参数 + 接口(含 MC5 MoveLinear 声明)
    │   │   └── MockMotionCard.cs      ← MC1:模拟卡 + MC5:MoveLinear 等比插补
    │   ├── Common/
    │   │   └── LogHelper.cs           ← MC3:线程安全文件日志
    │   ├── UI/
    │   │   ├── MainForm.cs            ← MC3 主逻辑 + MC5 插补按钮 + MC6 轨迹采样
    │   │   ├── MainForm.Designer.cs   ← MC3 三栏布局 + MC5/MC6 改四栏(轴1|轴2|轨迹|报警日志)
    │   │   └── TrajectoryPanel.cs     ← MC6:自绘轨迹面板(双缓冲/mm→像素等比映射)
    │   ├── Program.cs                 ← MC3:入口
    │   └── MotionControl.csproj
    └── MotionControl.Tests/           ← xUnit 测试
        ├── MockMotionCardTests.cs     ← MC2 12 个行为测试 + MC4 UI 冒烟 + MC5 插补测试,共 14 个
        └── MotionControl.Tests.csproj
```

## 怎么跑

```bash
# 编译(0 错误 0 警告为目标)
dotnet build MotionControl.sln

# 跑全部测试(14 个:MC2×12 + MC4 冒烟×1 + MC5 插补×1)
dotnet test MotionControl.sln

# 运行 WinForms 主程序
dotnet run --project src/MotionControl/MotionControl.csproj
```

> 测试工程 NuGet 包(与 DAQMonitor.Tests 同版本,内网离线缓存已覆盖):Microsoft.NET.Test.Sdk 17.11.1、xunit 2.9.2、xunit.runner.visualstudio 2.8.2。**主程序零第三方包**(纯 BCL/自绘)。

## 内网使用(离线环境)

- **零新增包**:主程序零第三方依赖;测试包版本刻意与 DAQMonitor.Tests 对齐 → DAQMonitor 能离线 restore 的机器,**本工程直接可用,不需要任何额外准备**。
- NuGet.config 沿用 DAQMonitor 那份(拷进 `MotionControl\` 文件夹即可,相对路径 `..\nuget-packages` 同样适用——保持离线学习包目录结构时零修改;目录结构不同则改 value 路径)。
- 验证命令:`dotnet restore MotionControl.sln && dotnet test`(应见 14/14 绿)。
- 跑界面:`dotnet run --project src/MotionControl/MotionControl.csproj`,或直接双击 `src\MotionControl\bin\Debug\net8.0-windows\MotionControl.exe`(需 .NET 桌面运行时 8/9/10,RollForward 已配)。

## 测试清单(14 个)

| 来源 | 测试名 | 钉死的行为 |
|---|---|---|
| MC2 T01 | Connect_空IP_应返回参数错误 | 空串/全空格挡在门口 |
| MC2 T02 | 未连接就发运动指令_应全部返回未连接 | NotConnected + 重复 Connect 幂等 |
| MC2 T03 | 连接但未使能就运动_应返回轴未使能 | AxisDisabled;读位置不受使能限制 |
| MC2 T04 | 两轴同时点动_互不干扰 | v1 头号 bug 的回归测试 |
| MC2 T05 | 绝对定位_短距离_应精确到达目标 | 3mm 不瞬移不除零 |
| MC2 T06 | 绝对定位_零距离_应立即成功且不算运动 | 已在目标位立即 Ok |
| MC2 T07 | 绝对定位_目标超软限位_应返回参数错误 | 正/反向超限、速度非法拒收 |
| MC2 T08 | 急停_运动中途位置就地冻结 | 冻结 + EmergencyStopped 事件 |
| MC2 T09 | 回零_从任意位置精确回零位 | 120 → 0.000 |
| MC2 T10 | 报警阻断运动_清报警后恢复 | AlarmActive → ClearAlarm → 恢复 |
| MC2 T11 | 点动撞正软限位_应自动停止并报警 | 位置夹在限位 + 报警文案 |
| MC2 T12 | 断开连接_所有运动被取消 | Disconnect 取消一切、指令全拒 |
| MC4 | UI冒烟_窗体全流程不崩溃 | STA 线程真窗体全流程(连接→使能→双轴点动→定位→注障→清警→急停→断开) |
| MC5 | 直线插补_两轴等比推进且同时到位 | 中段 X:Y ≈ 5:3 + 双轴 precision:3 到位 |

## 与 MC1-MC6 文档的对应表

| 文档 | 本工程落点 |
|---|---|
| MC1 工程骨架与模拟卡 | sln/csproj 双工程、`Device/IMotionCard.cs`、`Device/MockMotionCard.cs` |
| MC2 卡的行为测试 | `Tests/MockMotionCardTests.cs` 的 T01-T12(tickMs=10 快进 + WaitUntil 轮询) |
| MC3 WinForms 主界面 | `Common/LogHelper.cs`、`Program.cs`、`UI/MainForm.cs`、`UI/MainForm.Designer.cs` |
| MC4 UI 冒烟验收 | `Tests/MockMotionCardTests.cs` 末尾 `UI冒烟_窗体全流程不崩溃`(STA + DoEvents 泵) |
| MC5 两轴直线插补 | `IMotionCard.MoveLinear` 声明、`MockMotionCard.MoveLinear` 实现、插补测试、btnLinear 按钮 |
| MC6 轨迹可视化 | `UI/TrajectoryPanel.cs`、Designer 四栏布局、Timer1_Tick 中 trajPanel.Sample 双轴同拍采样 |

## 与文档的出入(实现取舍)

- Designer 布局尺寸:MC3 原版三栏(1200 宽)在叠加 MC6 四栏改造后,按 MC6 的"窗体 1520 宽、四栏 26/26/24/24"为准,各 GroupBox/文本框宽度相应微调(如 txtAlarm 336→314),不影响任何行为与测试。
- 急停按钮 Location:按 1520 宽窗体把 Anchor 右上的 btnEstop 挪到 (1370, 14),保证贴右上角。
- 主程序命名空间为 `MotionControlProject`(MC1 指定 RootNamespace),测试命名空间为 `MotionControl.Tests`(MC2 指定)。
