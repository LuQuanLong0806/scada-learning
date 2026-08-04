namespace DaqMonitor.Core.Motion;

/// <summary>
/// 轴状态机。
///
/// 状态转移:
///   Disabled → (Enable) → Idle
///   Idle     → (Move/Jog/Home) → Moving / Homing
///   Moving/Homing → (运动完成) → Idle
///   Moving/Homing → (Stop) → Idle
///   任意态 → (EmergencyStop) → Idle(立即停)
///   任意态 → (超软限位/超速/驱动器报警) → Alarm(锁死,需 ResetAlarm 才能恢复)
///
/// 工业现场铁律:Alarm 态必须人工确认才能恢复(不能自动 reset 跑机器),
/// 因为触发 Alarm 通常意味着机械碰撞/电气故障/超程,继续运动会损坏设备。
/// </summary>
public enum AxisState
{
    /// <summary>未使能(伺服 off,轴自由)。</summary>
    Disabled = 0,

    /// <summary>已使能 + 空闲(可接受运动命令)。</summary>
    Idle = 1,

    /// <summary>正在执行点位运动(MoveAbsolute/MoveRelative/Jog)。</summary>
    Moving = 2,

    /// <summary>正在回原点(Homing 序列执行中)。</summary>
    Homing = 3,

    /// <summary>报警态(超限/超速/驱动器故障,锁死)。</summary>
    Alarm = 4,
}
