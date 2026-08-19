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

> 🧩 **这篇可以"边贴边跑"**:测试方法彼此独立,每贴一组就能 `dotnet test` 看到新测试变绿 —— 贴 5 次看 5 次绿,比一口气贴完再跑的体感好得多。第 1 步贴文件骨架,第 2-5 步都是**贴到类的末尾(最后一个 `}` 之前)**;赶时间也可以直接展开文末折叠块整体粘贴,再回头按 5 步读。

#### 🏗️ 为什么这样设计:12 个测试为什么按"行为"组织,而不是"每个公共方法测一遍"?

**当时面临的选择**:

| 方案 | 优点 | 代价 |
|---|---|---|
| 按方法覆盖:每个 public 方法至少一个用例 | 覆盖率报表好看 | 方法能调通 ≠ 行为正确:MoveAbsolute 有用例,不代表"未使能时必拒"这条**规则**被验证 |
| 按行为/验收标准组织(T01-T12 每条钉死一条行为)(选定) | 测试名即文档 | 实现重构时部分测试要跟着改(这本就是该改的) |

**为什么选它**:模拟卡的正确性 = 一组**业务规则**("未连接必拒""急停必冻结""报警必锁轴"),不是"每个方法能跑通"。按行为组织,测试清单就是需求单(本篇需求单的标题就叫"验收标准本身就是需求");哪条规则被破坏,测试名直接告诉你**坏了什么**,而不是"某断言红了再去读代码"。测试还只调公共 API(黑盒):实现随便重构(节拍循环换算法),行为在就全绿——**测试钉住行为,才配得上"重构安全网"这个称号**。前端类比:Cypress 按用户故事写用例,不按"每个 reducer 分支写一个"。

**不这样会怎样**:方法级覆盖在参数校验重构进基类后一片假绿——方法全在、行数没少,但"没使能必拒"这条行为已经没人验证了。

**🎤 面试一句话**:"卡的测试我按行为组织不按方法覆盖:上位机的正确性是一组规则——未连接必拒、急停必冻结、报警必锁轴,一条规则一个测试,名字就是验收标准;实现怎么重构只要行为在就全绿,这才是重构安全网。"

**第 1 步 · 文件骨架 + 三个 helper**(贴法:新建 `MockMotionCardTests.cs`,以下整段贴入 —— 此时应能 build)

```csharp
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
}
```

📚 **知识点**
- **NewCard / ReadyCard 两层准备,各管一种起点**:大多数用例要"已连接 + 已使能"才好直接测运动;少数用例(T01-T03)恰恰要测"没连接/没使能"的拒绝路径 —— 两个 helper 各管一头,测试体内只剩"动作 + 断言",一眼读完。前端类比:给 `renderHook` 再封一个 `renderLoggedIn`,把重复的登录态铺底收敛到一个函数里。
- **`tickMs: 10` = 仿真时间快进 10 倍**:节拍越小,同样的运动时长折算的真实毫秒越少,12 个用例 4 秒跑完。**测试慢一秒,开发者就多一分借口不跑它**;真卡测试做不了这件事(电机不会快进),所以能在模拟层测的行为全部在模拟层测 —— 接口化的又一红利。类比:msw 把网络延迟调成 0,测试要能控制时间。
- **`WaitUntil` 轮询 + 超时,不是固定 Sleep**:固定等 2 秒 = 最慢用例永远耗 2 秒,且机器慢时等够了时间却没等到结果就偶发红;轮询"条件成立立即返回"又快又稳,`TimeoutException` 兜底保证永不卡死。这就是 Jest 的 `waitFor()` / react-query 轮询的思想:**对付异步的标准姿势是轮询条件 + 超时,不是死等时间**。

#### 🏗️ 为什么这样设计:等待为什么用"轮询条件 + 超时",时间为什么用 tickMs 快进 10 倍?

**当时面临的选择(等异步运动完成)**:

| 方案 | 优点 | 代价 |
|---|---|---|
| `Thread.Sleep(2000)` 固定等 | 一行,零思考 | 快机器白等、慢机器不够等 → **偶发红**(flaky);12 个用例全这样,套件又慢又不稳 |
| `WaitUntil(条件, 超时)` 轮询(选定) | 多写 8 行 helper | 无 |

**为什么选它**:运动是异步完成的,测试要等的是**条件**不是**时间**——条件成立立即返回(快),3 秒不成立抛超时(永不卡死)。两个方向的失败都被堵死:快机器不等冤枉时间,慢机器等到条件才走。这是 Jest `waitFor()` 的同款思想。**tickMs:10 快进**是另一半:模拟卡节拍周期从 50ms 缩到 10ms,一段 5 秒的运动 1 秒演完——**仿真时间可缩放**,测试既保留完整时序过程(急停测的仍是"运动中途"),又把套件总时长压回秒级。测试要的两个美德"稳"和"快",分别由这两个设计各管一个。

**不这样会怎样**:Sleep 版在 CI 慢机上随机红,团队第一反应是"再睡长点"——套件越改越慢,flaky 却没绝迹;真实速度跑仿真,12 个用例要一分多钟,没人愿意频繁跑测试。

**🎤 面试一句话**:"异步等待我用轮询条件加超时,不用固定 Sleep——条件成立立刻返回所以快,超时兜底所以永不假死,flaky 的根源'快机器白等、慢机器不够等'一次堵死;再加仿真节拍快进 10 倍,时序过程完整保留,套件秒级跑完。"
- **`Stopwatch` 而不是 `DateTime.Now`**:秒表是单调时钟(只往前走),系统时间会被授时/手动改 —— 测耗时一律用 Stopwatch,和前端用 `performance.now()` 而不是 `Date.now()` 同理。

**第 2 步 · T01-T03:参数与状态检查**(贴法:贴到 `WaitUntil` 方法后面、类的最后一个 `}` 之前)

```csharp
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
```

📚 **知识点**
- **`Assert.Equal(expected, actual)` 期望值在前**:参数顺序约定,写反了报错信息会"反着念"("期望 100,实际 ParamError"变成"期望 ParamError,实际 100"),排查时自己骗自己。
- **T01 边界成对测**:空串 `""` 和全空格 `"   "` 各断言一次 —— 同一类脏输入的两种形态,一个测试里测全,不用拆两个。
- **T02 顺带钉死"重复 Connect 幂等"**:连两次不报错、状态仍是已连接 —— 幂等性(同样操作做 N 次效果和做 1 次一样)是接口的隐性契约,类比:提交按钮连点两次不该炸。
- **`await Task.CompletedTask` 是占位**:T02 其实没有异步等待,但方法签名是 `async Task` —— 保留这行,以后往里加 await 不用改签名。真正的原因:xUnit 对 `async void` 测试**无法正确等待**(框架拿不到 Task,测试"看似通过"其实还没跑完),`async Task` 才是正确姿势 —— 和"React 事件处理函数别写 async 直接当 onClick"是同一类"签名即承诺"问题。
- **T03 最后一行断言是精华**:断了使能,`MoveAbsolute` 被拒 —— 但 `GetAxisPosition` 照样读得到 0。MC1 接口设计"读和动分开"在这里被测试**锁定**:以后谁给读位置加上使能前置条件,这个测试当场红。

**第 3 步 · T04-T07:两轴并发回归 + 定位三连**(贴法:接在上一步后面)

```csharp
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
```

📚 **知识点**
- **T04 是"回归测试"的教科书样本**:v1 的头号 bug(全局 `_isJogging`,轴 2 一动轴 1 被停)修掉后,把**复现步骤**写成永久测试 —— 以后谁改坏"每轴一个令牌"这个设计,不用等上机,CI 当场红。前端类比:修完 bug 补一条 E2E 用例,"这个坑我只掉一次"。
- **`WaitUntil` 的条件用 `&&` 合并两轴**:两轴都 >1mm 才算"同时在动",单一条件证明不了"并发"。
- **浮点断言必须带 precision**:`Assert.Equal(3.0, pos, precision: 3)`。0.1 加三次不等于 0.3,`==` 级比较是 flaky 之源;取几位取决于业务上"多准算准"(定位 3 位小数 = 微米级,远超机械实际需要)—— **precision 是业务决策,不是随手写的**。
- **T05 专挑 3mm 短距离**:v1 的步数除零就死在短距离上(步数算出 0),注释里写明"3mm @ 5mm/s = 600ms"。**测边界值**:0、极短、刚好超限 —— bug 的老巢全在边界。
- **T07 一测三断言可以,因为行为同类**:正向超、反向超、速度非法,都是"参数非法拒收"同一行为;若断言的行为不同(一个测拒收、一个测报错文案),就该拆成两个测试。

**第 4 步 · T08-T09:急停冻结 + 回零**(贴法:接在上一步后面)

```csharp
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
```

📚 **知识点**
- **事件断言的套路 = bool 标志位**:先 `var stopped = false;`,订阅时用 lambda 翻转 `stopped = true`,动作后 `Assert.True(stopped)` —— 闭包捕获局部变量,事件一触发标志就翻。这就是手写版 `jest.fn()` 调用记录:不关心参数细节时,一个布尔就是最小的 mock。隐含前提是**断言前事件已触发完** —— 这里 `WaitUntil` 已保证了时间先后。
- **T08 的严谨在"二次读数"**:急停后记下 `frozen`,再等 200ms(**两个节拍**,足以让任何"在途循环"再走一步),然后断言位置仍等于 frozen(precision 6,纳米级)—— 只读一次只能证明"停了",读两次才能证明"**停住不动了**",多走的 1mm 都藏不住。
- **速度参数是算出来的,不是拍脑袋**:`MoveAbsolute(0, 500, 5)` 全程 100 秒 —— 故意选个慢速度,保证 `WaitUntil` 等到 >2mm 时运动**还在中途**,急停测的才是"运动中冻结"而不是"停了以后再急停"。测试里的每个参数都要经得起"为什么是这个值"的追问。
- **T09 两段式铺真实起点**:先定位到 120、等停稳,再回零断言 0.000 —— 测"从任意位置回零",就别依赖"刚好在 0 附近"的默认起点。

**第 5 步 · T10-T12:报警链路 + 软限位 + 断开**(贴法:接在上一步后面 —— 贴完 12 个测试全齐)

```csharp
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

📚 **知识点**
- **T10 是一条因果链,不拆**:注障 → 运动被拒 → 清警 → 运动恢复 —— 三段行为互为因果,拆成三个测试反而要各自重复铺状态;**一条链一个测试,并列行为才分开测**。
- **`Assert.Contains("软限位", msg)` 连报错文案都钉死**:操作员看到的报警文本必须含"软限位"三个字,否则这条报警等于没报 —— 用户可见的行为(文案)和机器行为(错误码)一样值得断言。
- **T11 的注释就是算式**:3000mm/s @ 10ms 节拍 = 每步 30mm,约 0.34 秒撞到 +1000 —— 速度取多大,取决于"几步撞上、多久撞上"的期望。测试参数带算式注释,读的人才敢改。
- **T12 钉死 Disconnect 的完整语义**:两轴点动途中断开 → 两个 `IsMoving` 都为 false(取消一切)+ `IsConnected` 为 false + 再发指令返回 `NotConnected` —— MC1 里"断开先 CancelAllLocked"的实现,在这里整条被测试锁定。类比:组件卸载时 abort 所有在途请求,之后任何响应回来都不许再 setState。

<details markdown="1">
<summary>📄 完整文件 MockMotionCardTests.cs(对答案 / 整体粘贴用 —— 贴完等于上面 5 步全部完成)</summary>

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

</details>

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
