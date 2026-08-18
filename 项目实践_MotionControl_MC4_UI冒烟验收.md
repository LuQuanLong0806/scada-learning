# MC4 · UI 冒烟验收(让测试替你把界面全流程跑一遍)

> **系列导航**:[MC1 骨架与模拟卡](项目实践_MotionControl_MC1_工程骨架与模拟卡.md) → [MC2 卡的行为测试](项目实践_MotionControl_MC2_卡的行为测试.md) → [MC3 WinForms 主界面](项目实践_MotionControl_MC3_WinForms主界面.md) → **MC4 UI 冒烟验收** → [MC5 两轴直线插补(可选)](项目实践_MotionControl_MC5_两轴直线插补.md) → [MC6 轨迹可视化(可选)](项目实践_MotionControl_MC6_轨迹可视化.md)
> **定位**:MC2 测的是卡,MC3 写的是界面 —— 但"界面 + 卡"合起来对不对,还没人验过。数据采集项目给过一个教训:**短暂启动式的冒烟测不出交互期的 bug**(跨线程事件在程序跑了三五秒后才开始乱飞)。本篇写一个真把全流程跑一遍的 UI 冒烟测试:连接 → 使能 → 双轴并发点动 → 定位 → 注入报警 → 清报警 → 急停 → 断开,全程跑在真窗体上,任何一步跨线程碰控件都会当场抛异常、当场红。
> **前置**:MC3(界面完整可用,手动验收清单已过)。
> **预计开发时长**:跟敲 0.5 天(代码不长,概念要啃:STA、消息泵、DoEvents)。**先只看「📋 需求单」自己写,卡住再看「🛠️ 参考实现」对答案。**

---

## 🎯 本篇交付物

1. 第 13 个测试 `UI冒烟_窗体全流程不崩溃`:在 STA 线程上创建真窗体、手动驱动消息泵、跑完操作员全流程;
2. `dotnet test` 13/13 全绿 —— **从此每次跑测试,都等于免费把界面全流程点了一遍**;
3. 搞懂三个 WinForms 底层概念:STA 线程、消息队列、消息泵(面试讲跨线程时的深度弹药)。

---

## 📋 需求单(测试经理视角 —— 先自己想怎么做)

| 编号 | 需求 | 验收标准 |
|---|---|---|
| FR-S01 | 无头环境跑完整 UI 流程 | 不弹真窗口、不需要人工点击,测试自己驱动 |
| FR-S02 | 流程覆盖操作员全动作 | 连接 → 使能 → **两轴同时点动** → 定位(打断点动)→ 注入报警 → 清报警 → 急停 → 断开 → 关窗 |
| FR-S03 | 每一步都给消息泵留分发时间 | BeginInvoke 投递的回调、Timer 的 Tick 真的被执行到 |
| FR-S04 | 任何异常 = 测试失败 | 线程里 catch 住异常带回主断言,不许吞 |

**先自己想**(这三个问题想明白,代码就是水到渠成):
① `Application.Run(form)` 会阻塞直到关窗 —— 测试里根本不能用,那怎么让窗体"活起来"、消息循环照样转?(提示:`Application.DoEvents()` 是什么?)
② WinForms 控件必须活在 STA 线程上,而 xUnit 跑测试的线程不是 —— 怎么办?
③ 卡的事件在后台线程触发,MainForm 用 BeginInvoke 把更新投递回 UI 线程 —— 投递过去的回调,**谁**来执行?测试线程 Sleep 的时候它会自己执行吗?

---

## 📚 本篇知识点(不懂再点回去学)

- [📖 WinForms 跨线程访问控件](kp:winforms-invoke) —— BeginInvoke 投递到哪、谁来执行
- [📖 xUnit 单元测试基础](kp:unit-test) —— 测试方法的组织与断言

---

## 🛠️ 参考实现(卡住/写完再看)

**设计思路一句**:开一个 STA 线程跑窗体,主测试流程"做一步操作 → 用 DoEvents 泵一会儿消息"交替推进,让界面事件真的分发;线程里任何异常都被捕获、带回断言。

对 `MockMotionCardTests.cs` 做**两处改动**:

**改动 1**:文件头补一个 using(测试里要构造 MainForm):

```csharp
using MotionControlProject.Device;
using MotionControlProject.UI;      // ← 新增:冒烟测试要 new MainForm
using System.Diagnostics;
```

**改动 2**:在类末尾(`断开连接_所有运动被取消` 测试之后、类的 `}` 之前)追加:

```csharp
    // ———— UI 全流程冒烟(MC4 再加) ————

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
        thread.Join(60000);                             // 等它跑完(上限 60s,防测试挂死)
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

💡 **这 60 行里的五个门道**:

- **为什么开新线程 + STA**:xUnit 的测试线程不是 STA,直接 new 控件会抛 `InvalidOperationException`(剪贴板/COM 组件要求单线程公寓)。`SetApartmentState(ApartmentState.STA)` 必须在 `Start()` **之前**调用 —— 这是 WinForms 控件能被创建的硬前提;
- **为什么不用 `Application.Run`**:它会进入消息循环直到窗体关闭,测试就永远停在那。替代方案:手动"泵"消息 —— `Application.DoEvents()` 每调一次,就把当前消息队列里的消息(按钮点击、BeginInvoke 回调、Timer 的 WM_TIMER、重绘)全部处理一遍再返回;
- **"做一步,Pump 一会儿"的节奏**:发指令是同步的,但它引发的**事件 → BeginInvoke → 控件更新**是异步的 —— Pump(50) = 50 轮 × 10ms,给投递出去的回调留足被 DoEvents 分发的时间。不 Pump 就直接断言/做下一步,事件回调可能还躺在队列里没执行,测试就漏掉了它本来要覆盖的路径;
- **异常怎么变成测试失败**:`Assert.Null(error)` —— 线程里抛的异常不会自动让 xUnit 测试失败(它在另一个线程),必须 catch 住存进闭包变量,带回测试线程断言。`thread.Join(60000)` 设上限,流程万一卡死,测试在 60 秒后失败而不是永远挂着;
- **`static void Pump` 是局部函数**(C# 7+):定义在方法体内、`return;` 之后 —— 它只服务于这一个测试,不该污染类的 API。`static` 让它不能捕获外部变量,编译器顺便帮你检查没有隐式闭包。

🔧 **为什么这个测试能抓住 MC2 抓不到的 bug**:MC2 测卡时不碰任何控件。假如某天有人把 `OnPositionChanged` 里的 `InvokeRequired` 判断删了 —— 卡的行为测试全绿,但界面一跑就炸;现在这个冒烟测试里,两轴并发点动每 10ms 刷两次位置事件,几十轮 Pump 之后,那次跨线程访问必然发生,**当场抛给你看**。这正是"数据采集项目里短暂冒烟没测出的 bug,拖到联调才爆"的针对性补防。

---

## ✅ 验证(沙盒实测输出,你可以逐字对)

```bash
dotnet test
```
```
已通过! - 失败:     0，通过:    13，已跳过:     0，总计:    13，持续时间: 6 s - MotionControl.Tests.dll (net8.0)
```

故意破坏实验(做完记得改回来):把 MainForm.cs 里 `OnPositionChanged` 的第一行 `if (InvokeRequired) …` 注释掉,再跑测试 —— `UI冒烟_窗体全流程不崩溃` 立刻红,报跨线程操作无效。这就是这个测试存在的意义。

---

## ✅ 验收清单(对着需求单逐条勾)

- [ ] using 补了,冒烟测试追加在类末尾,13/13 全绿
- [ ] 能说清:为什么 STA、为什么不用 Application.Run、DoEvents 干了什么
- [ ] 做过一次"删 InvokeRequired"的故意破坏实验,亲眼看冒烟测试红
- [ ] (理解题)`Pump(50)` 的 50 改成 2 会怎样?—— 想清楚再试,试完改回 50

---

## 🎤 面试怎么讲这一篇

> **一句话开场**:"我的 UI 没有靠人肉点出来验证 —— 有一个冒烟测试在 STA 线程上创建真实窗体、手动驱动消息泵,把操作员全流程跑一遍,任何跨线程错误当场红。"

**追问弹药库**:
- **"UI 怎么做自动化测试?WinForms 不是很难测吗?"** —— 核心三件套:STA 线程(`SetApartmentState`)满足控件的公寓要求;`Application.DoEvents()` 手动泵消息代替阻塞的 `Application.Run`;操作一步 Pump 一轮,给 BeginInvoke 回调留分发时间;
- **"BeginInvoke 之后发生了什么?"**(深度题,答出来就超过大多数候选人)—— 回调被包装成消息投递到创建控件线程的消息队列,由该线程的消息循环取出执行;`DoEvents` 就是在当前调用栈里"借"一次循环,把队列清一遍。所以 Sleep 本身不会让投递执行,**必须泵**;
- **"这测试值得吗?界面不是常变吗?"** —— 冒烟测试只测"全流程不炸"这一条不变量,不测具体样式;界面再怎么改,连接 → 运动 → 报警 → 急停的主干不变,测试就不用改 —— 投入产出比极高的一层;
- **"和 MC2 的 12 个测试什么关系?"** —— 分层:MC2 测设备行为(纯逻辑,毫秒级),MC4 测"设备 + 界面"的集成(真实线程模型)。底层快测定位根因,上层冒烟守住集成 —— 就是测试金字塔。

下一篇:[MC5 · 两轴直线插补(可选加餐)](项目实践_MotionControl_MC5_两轴直线插补.md)
