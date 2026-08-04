using DaqMonitor.Core.Motion;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 模拟运动控制器测试:覆盖 IAxisController 的核心契约。
///
/// 测试策略:
///   控制器内部有 10ms 真实 timer,等待运动完成用 Task.Delay + 状态轮询。
///   单测运行时长 < 500ms,稳定不抖。
///
/// 覆盖契约:
///   ① 回零后 IsHomed = true,位置 = 0
///   ② 绝对定位到指定坐标
///   ③ 相对定位增量
///   ④ 软限位拒绝越界命令 + Jog 撞限位进入 Alarm
///   ⑤ 急停立即转 Idle
///   ⑥ 未回零时拒绝绝对定位
/// </summary>
public class MotionControlTests : IDisposable
{
    private readonly SimulatedAxisController _axis;

    public MotionControlTests()
    {
        _axis = new SimulatedAxisController(new AxisConfiguration
        {
            AxisId = 0,
            Name = "X-test",
            MinPosition = -100,
            MaxPosition = 100,
            MaxVelocity = 200,
            HomeVelocity = 50,
            DefaultVelocity = 100
        });
    }

    public void Dispose() => _axis.Dispose();

    /// <summary>等轴进入 Idle,超时 2 秒。</summary>
    private async Task WaitIdleAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (_axis.State != AxisState.Idle && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.Equal(AxisState.Idle, _axis.State);
    }

    [Fact]
    public async Task HomeAsync_SetsHomed_And_ZeroesPosition()
    {
        // 没回零前 IsHomed=false
        Assert.False(_axis.IsHomed);

        await _axis.HomeAsync();
        await WaitIdleAsync();

        Assert.True(_axis.IsHomed);
        Assert.Equal(0, _axis.CurrentPosition, precision: 3);
    }

    [Fact]
    public async Task MoveAbsolute_ReachesTarget_AfterHoming()
    {
        await _axis.HomeAsync();
        await WaitIdleAsync();

        await _axis.MoveAbsoluteAsync(position: 50, velocity: 100);
        await WaitIdleAsync();

        Assert.Equal(50, _axis.CurrentPosition, precision: 0);   // 整数 mm 级别足够
    }

    [Fact]
    public async Task MoveRelative_AddsDistance_ToCurrentPosition()
    {
        await _axis.HomeAsync();
        await WaitIdleAsync();

        await _axis.MoveRelativeAsync(distance: 20, velocity: 100);
        await WaitIdleAsync();
        Assert.Equal(20, _axis.CurrentPosition, precision: 0);

        await _axis.MoveRelativeAsync(distance: -5, velocity: 100);
        await WaitIdleAsync();
        Assert.Equal(15, _axis.CurrentPosition, precision: 0);
    }

    [Fact]
    public async Task MoveAbsolute_OutsideSoftLimit_Throws()
    {
        await _axis.HomeAsync();
        await WaitIdleAsync();

        // 软限位 100,要求移动到 200 应被拒绝
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _axis.MoveAbsoluteAsync(position: 200, velocity: 100));
    }

    [Fact]
    public async Task Jog_IntoSoftLimit_RaisesAlarm_And_Locks()
    {
        await _axis.HomeAsync();
        await WaitIdleAsync();

        // 先回到接近正向限位的位置
        await _axis.MoveAbsoluteAsync(95, 200);
        await WaitIdleAsync();

        var alarms = new List<string>();
        _axis.AlarmRaised += (_, msg) => alarms.Add(msg);

        // Jog 向正方向,应该撞到 MaxPosition=100 报警
        await _axis.JogAsync(velocity: 50);
        await Task.Delay(500);   // 等 Jog 走到限位

        // 进入 Alarm 态
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (_axis.State != AxisState.Alarm && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.Equal(AxisState.Alarm, _axis.State);
        Assert.NotEmpty(alarms);
        Assert.Contains(alarms, a => a.Contains("软限位"));

        // 报警态下任何运动命令都应该拒绝
        await Assert.ThrowsAsync<InvalidOperationException>(() => _axis.MoveAbsoluteAsync(0, 50));
    }

    [Fact]
    public async Task EmergencyStop_AbortsMotion_Immediately()
    {
        await _axis.HomeAsync();
        await WaitIdleAsync();

        // 发起一个长距离运动(到 50,100mm/s → 大约 0.5 秒)
        await _axis.MoveAbsoluteAsync(50, 100);
        await Task.Delay(50);   // 让运动开始
        Assert.Equal(AxisState.Moving, _axis.State);

        // 急停
        await _axis.EmergencyStopAsync();
        Assert.Equal(AxisState.Idle, _axis.State);

        // 急停后位置应该是中间某个值(没到 50)
        Assert.True(_axis.CurrentPosition < 50);
        Assert.True(_axis.CurrentPosition > 0);
    }

    [Fact]
    public async Task MoveAbsolute_BeforeHome_Throws()
    {
        // 不回零直接绝对定位 — 应该拒绝
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _axis.MoveAbsoluteAsync(position: 10, velocity: 50));
    }

    [Fact]
    public async Task ResetAlarm_ClearsAlarmState()
    {
        await _axis.HomeAsync();
        await WaitIdleAsync();

        await _axis.MoveAbsoluteAsync(95, 200);
        await WaitIdleAsync();

        await _axis.JogAsync(50);
        await Task.Delay(500);
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (_axis.State != AxisState.Alarm && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.Equal(AxisState.Alarm, _axis.State);

        await _axis.ResetAlarmAsync();
        Assert.Equal(AxisState.Idle, _axis.State);
    }
}
