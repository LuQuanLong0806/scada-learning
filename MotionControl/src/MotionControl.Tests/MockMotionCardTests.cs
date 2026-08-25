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

    // ———— 两轴直线插补(可选篇,MC5 再加) ————

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
}
