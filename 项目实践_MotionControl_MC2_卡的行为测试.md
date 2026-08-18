# MC2 · 卡的行为测试(12 个测试钉死 MC1 的全部行为)

> **系列导航**:[MC1 骨架与模拟卡](项目实践_MotionControl_MC1_工程骨架与模拟卡.md) → **MC2 卡的行为测试** → [MC3 WinForms 主界面](项目实践_MotionControl_MC3_WinForms主界面.md) → [MC4 UI 冒烟验收](项目实践_MotionControl_MC4_UI冒烟验收.md) → [MC5 两轴直线插补(可选)](项目实践_MotionControl_MC5_两轴直线插补.md) → [MC6 轨迹可视化(可选)](项目实践_MotionControl_MC6_轨迹可视化.md)
> **定位**:MC1 交付的模拟卡,行为"对不对"不能靠嘴说 —— 本篇用 12 个 xUnit 测试逐条钉死。**测试就是升级版的验收标准**:两轴并发、急停冻结、软限位、回零、报警,每一条行为都有一个测试作证;以后任何重构,跑一遍测试就知道有没有把行为改坏(这就是回归测试)。
> **前置**:MC1(工程三件套 + MockMotionCard 能 build 0 错 0 警)。
> **预计开发时长**:跟敲 0.5 天。**先只看「📋 需求单」自己写,卡住再看「🛠️ 参考实现」对答案。**

---

## 🎯 本篇交付物

1. 测试文件 `MockMotionCardTests.cs`:12 个测试,覆盖 MC1 全部 7 条 FR;
2. `dotnet test` 全绿(12/12),实测 4 秒跑完 —— 不拖慢开发节奏的测试才会被天天跑;
3. 每一个 v1 的坑都有一个对应测试把修复"钉死"在代码库里。

---

## 📋 需求单(测试经理视角 —— 验收标准本身就是需求)

### 测试清单:12 个测试各钉死什么

| # | 测试名(直接用中文命名) | 钉死哪条行为 | 对应 MC1 |
|---|---|---|---|
| T01 | Connect_空IP_应返回参数错误 | 空串/全空格在门口挡掉(v1 前导空格坑) | FR-M02 |
| T02 | 未连接就发运动指令_应全部返回未连接 | 未连接一切指令被拒;重复 Connect 幂等 | FR-M02 |
| T03 | 连接但未使能就运动_应返回轴未使能 | 检查链顺序正确;**读位置不受使能限制** | FR-M02 |
| T04 | 两轴同时点动_互不干扰 | **v1 头号 bug 的回归测试** | FR-M03 |
| T05 | 绝对定位_短距离_应精确到达目标 | 3mm 短定位不瞬移不除零,精确贴目标 | FR-M03 |
| T06 | 绝对定位_零距离_应立即成功且不算运动 | 已在目标位 = 立即 Ok | FR-M03 |
| T07 | 绝对定位_目标超软限位_应返回参数错误 | 正/反向超限拒收;速度非法拒收 | FR-M06 |
| T08 | 急停_运动中途位置就地冻结 | 冻结(6 位小数级别不多走)+ 事件必触发 | FR-M04 |
| T09 | 回零_从任意位置精确回零位 | 120 → 0.000 | FR-M05 |
| T10 | 报警阻断运动_清报警后恢复 | AlarmActive 拒指令;清警后真能动 | FR-M07 |
| T11 | 点动撞正软限位_应自动停止并报警 | 位置夹在限位上,报警文本含"软限位" | FR-M06 |
| T12 | 断开连接_所有运动被取消 | Disconnect 后 IsMoving 全 false,指令全拒 | 连接管理 |

**先自己想**(参考实现之前,把这四个答案写在草稿上):
① 运动是异步的、要几百毫秒才到位,测试怎么"等它到位" —— 固定 `Thread.Sleep(2000)` 有什么问题?
② 一轮测试 12 个用例、每个都真等几秒,整套测试要跑一分钟,开发时谁还愿意跑?给你一个提示:模拟卡构造函数里那个 `tickMs` 是干嘛的?
③ T08 急停测试怎么证明"急停后 1mm 都不多走"?直接比较两次读数就够严谨吗?
④ 事件(`EmergencyStopped`)怎么断言"确实触发了"?

---

## 📚 本篇知识点(不懂再点回去学)

- [📖 xUnit 单元测试基础](kp:unit-test) —— Fact / Assert / 测试类的组织方式
- [📖 CancellationToken 协作式取消](kp:cancel-token) —— T04/T08/T12 测的全是"取消语义"
- [📖 event / EventHandler 事件机制](kp:event-delegate) —— T04/T08 里对事件的订阅与断言

---

## 🛠️ 参考实现(卡住/写完再看)

**设计思路一句**:一个测试文件、三个 helper(NewCard/ReadyCard/WaitUntil)+ 12 个测试;`tickMs: 10` 让仿真快进 10 倍,`WaitUntil` 轮询等异步运动完成 —— 测试既稳又不慢。

```csharp
// 📂 文件:src/MotionControl.Tests/MockMotionCardTests.cs(本篇先写到"生命周期"为止;
// 插补测试 MC5 再加,UI 冒烟测试 MC4 再加,都在文件末尾追加)
using MotionControlProject.Device;
using System.Diagnostics;

namespace MotionControl.Tests;

/// <summary>
/// MockMotionCard 单元测试 —— 模拟卡的"行为契约"。
/// tickMs 传 10(默认 100):仿真节拍快 10 倍,几秒内跑完全部运动场景。
/// 这些测试就是升级的"验收标准":两轴并发、急停、软限位、回零,全部有据可查。
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

    // ———— 参数与状态检查 ————

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

    // ———— 两轴并发(v1 的头号 bug 的回归测试) ————

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

    // ———— 绝对定位 ————

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

    // ———— 急停 ————

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

    // ———— 回零 ————

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

    // ———— 报警链路 ————

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

    // ———— 生命周期 ————

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

💡 **这份测试文件的五个门道**:

- **`tickMs: 10` = 仿真时间快进 10 倍**:节拍越小,同样的"运动时长"折算成的真实毫秒越少。整套 12 个用例 4 秒跑完 —— 测试慢一秒,开发者就多一分借口不跑它。真卡测试做不了这件事(电机不会快进),所以**能在模拟层测的行为全部在模拟层测**,这是接口化的又一红利;
- **`WaitUntil` 轮询 + 超时,不是固定 Sleep**:固定等 2 秒 = 最慢用例永远耗 2 秒,且机器慢时还会偶发失败(等够了时间但没等到结果);轮询"条件成立即返回"又快又稳,`TimeoutException` 兜底保证测试永远不会卡死 —— 和采集项目里"管道心跳"是同一个思想:**轮询 + 超时是对付异步的标准姿势**;
- **浮点断言必须带 precision**:`Assert.Equal(3.0, pos, precision: 3)`。浮点累加有误差,`==` 级别的比较是自找 flaky;precision 取几位,取决于业务上"多准算准"(定位 3 位 = 微米级,远超实际需要);
- **事件断言的套路 = bool 标志位**:订阅时 `card.EmergencyStopped += (s, e) => stopped = true;`,动作后 `Assert.True(stopped)`。闭包捕获局部变量,事件一触发标志就翻 —— 简单,但在多线程下有个隐含前提:断言前事件已经触发完(WaitUntil 已经保证了时间上的先后);
- **`await Task.CompletedTask` 是个占位**:T02 其实没有异步等待,但测试方法是 `async Task` —— 保留它,以后往里加 await 不用改签名;xUnit 对 `async void` 测试无法正确等待,`async Task` 才是正确姿势。

🔧 **为什么测试类要分 NewCard / ReadyCard 两层准备**:大多数用例需要"已连接 + 已使能"的前置状态,少数用例恰恰要测"没连接/没使能"的拒绝路径 —— 两个 helper 各管一种起点,测试体内只剩"动作 + 断言",一眼读完。

---

## ✅ 验证(沙盒实测输出,你可以逐字对)

```bash
dotnet test
```
```
已通过! - 失败:     0，通过:    12，已跳过:     0，总计:    12，持续时间: 4 s - MotionControl.Tests.dll (net8.0)
```

故意破坏实验(强烈建议做一次):把 `MockMotionCard.cs` 里 `StartMotionLocked` 的 `_cts[axis]?.Cancel();` 注释掉,再跑测试 —— T04"两轴同时点动"立刻红给你看,这就是"回归测试钉死行为"的体感。看完记得改回来。

---

## ✅ 验收清单(对着需求单逐条勾)

- [ ] T01-T03:参数与状态检查 3 个测试通过
- [ ] T04:两轴并发回归测试通过(v1 头号 bug 已钉死)
- [ ] T05-T07:绝对定位 3 个测试通过(短距离/零距离/超软限位)
- [ ] T08:急停冻结 + 事件触发通过
- [ ] T09:回零精确到 0.000 通过
- [ ] T10-T11:报警阻断/恢复 + 点动撞软限位通过
- [ ] T12:断开连接取消所有运动通过
- [ ] 做过一次"故意破坏"实验,亲眼看测试变红再改回绿

---

## 🎤 面试怎么讲这一篇

> **一句话开场**:"设备的每一条行为契约我都有测试钉着 —— 两轴并发、急停冻结、软限位、回零、报警,12 个用例 4 秒跑完,重构随便动,行为改坏立刻红。"

**追问弹药库**:
- **"异步运动你怎么测?不怕 flaky 吗?"** —— 轮询等待 + 超时兜底,条件成立立即返回,不用固定 Sleep;浮点断言带 precision,不给偶发误差留机会;
- **"测试怎么做到 4 秒跑完的?"** —— 模拟卡节拍 `tickMs=10` 快进 10 倍。启发自"仿真时间可以和真实时间解耦"——能在模拟层验证的行为绝不上真机测,真机只测模拟层覆盖不了的(脉冲、机械);
- **"这些测试的价值在哪?"** —— 回归保护。v1 的两轴互踩 bug 修掉后,我写了专门的回归测试,以后谁改坏"每轴独立令牌"这个设计,T04 当场红;
- **"测试名为什么用中文?"** —— 测试名就是行为规格,`两轴同时点动_互不干扰` 一行顶三行注释;CI 挂掉时,失败列表直接就是"哪条行为坏了"的人话报告。

下一篇:[MC3 · WinForms 主界面 —— 控件数组 + 集中状态刷新 + 跨线程事件](项目实践_MotionControl_MC3_WinForms主界面.md)
