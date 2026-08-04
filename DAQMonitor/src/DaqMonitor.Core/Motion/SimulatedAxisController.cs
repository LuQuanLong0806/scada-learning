using System.Diagnostics;

namespace DaqMonitor.Core.Motion;

/// <summary>
/// 模拟轴控制器 —— 无硬件验证整条运动控制链路。
///
/// 模拟策略:
///   每 10ms tick 一次,根据当前速度推进位置:
///     newPosition = currentPosition + currentVelocity * dt
///   到达目标 → 进入 Idle;超出软限位 → 进入 Alarm
///
/// 真实硬件对比:
///   - 固高:GT_GetPrfPos(轴号, ref pos) 取位置,GT_SetPos / GT_Update 下命令
///   - 雷赛:dcm_get_position() / dcm_pmove()
///   - Beckhoff:读 PLC symbol "Axis.NcToPlc.ActPos" / 调 TC method "MoveAbsolute"
///
/// 模拟器故意不模拟这些细节(用户单位 vs 脉冲 / 加减速曲线 / 编码器噪声),
/// 因为学习项目要的是"接口契约正确",真硬件来了实现细节会变,接口不变。
///
/// 线程模型:
///   用 System.Timers.Timer 10ms 触发,内部用 lock 保护状态(防止命令和 tick 同时改位置)。
///   真实工程可能用 PLC 扫描周期(典型 1-4ms),我们用 10ms 平衡 CPU 与平滑度。
/// </summary>
public sealed class SimulatedAxisController : IAxisController, IDisposable
{
    private readonly object _lock = new();
    private readonly System.Timers.Timer _timer;

    private AxisState _state = AxisState.Disabled;
    private double _position;
    private double _velocity;         // 当前实际运动速度(有方向,带符号)
    private bool _isHomed;
    private double _targetPosition;   // 点位运动的目标(Moving 态用)
    private bool _disposed;

    public SimulatedAxisController(AxisConfiguration config)
    {
        Configuration = config;
        _timer = new System.Timers.Timer(interval: 10) { AutoReset = true };
        _timer.Elapsed += (_, _) => Tick(TimeSpan.FromMilliseconds(10));
        _timer.Start();
        // 启动后默认使能(Idle 态),真实卡需要先 Enable 伺服
        _state = AxisState.Idle;
    }

    public int AxisId => Configuration.AxisId;
    public string Name => Configuration.Name;
    public AxisConfiguration Configuration { get; }

    public AxisState State { get { lock (_lock) return _state; } }
    public double CurrentPosition { get { lock (_lock) return _position; } }
    public double CurrentVelocity { get { lock (_lock) return _velocity; } }
    public bool IsHomed { get { lock (_lock) return _isHomed; } }

    public event EventHandler<AxisState>? StateChanged;
    public event EventHandler<double>? PositionChanged;
    public event EventHandler<string>? AlarmRaised;

    public Task HomeAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_state == AxisState.Alarm)
                throw new InvalidOperationException($"轴 {Name} 处于报警态,先 ResetAlarm");
            if (_state != AxisState.Idle)
                throw new InvalidOperationException($"轴 {Name} 正在 {_state},无法回零");

            // 回零 = 慢速向 0 方向运动,到达 0 后停
            _targetPosition = 0;
            _velocity = _position > 0 ? -Configuration.HomeVelocity : Configuration.HomeVelocity;
            _state = AxisState.Homing;
            RaiseStateChanged();
        }
        return Task.CompletedTask;
    }

    public Task MoveAbsoluteAsync(double position, double velocity, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_state == AxisState.Alarm)
                throw new InvalidOperationException($"轴 {Name} 报警中,先 ResetAlarm");
            if (_state != AxisState.Idle)
                throw new InvalidOperationException($"轴 {Name} 正在 {_state}");
            if (!_isHomed)
                throw new InvalidOperationException($"轴 {Name} 未回零,绝对定位无意义");
            if (position < Configuration.MinPosition || position > Configuration.MaxPosition)
                throw new ArgumentOutOfRangeException(nameof(position),
                    $"目标 {position} 超出软限位 [{Configuration.MinPosition}, {Configuration.MaxPosition}]");
            if (Math.Abs(velocity) > Configuration.MaxVelocity)
                throw new ArgumentOutOfRangeException(nameof(velocity),
                    $"速度 {velocity} 超过最大 {Configuration.MaxVelocity}");

            _targetPosition = position;
            var dir = position > _position ? 1 : -1;
            _velocity = dir * Math.Abs(velocity);
            _state = AxisState.Moving;
            RaiseStateChanged();
        }
        return Task.CompletedTask;
    }

    public Task MoveRelativeAsync(double distance, double velocity, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_state != AxisState.Idle)
                throw new InvalidOperationException($"轴 {Name} 正在 {_state}");
            // 相对定位换算成绝对定位(统一走一套逻辑)
            return MoveAbsoluteAsync(_position + distance, velocity, ct);
        }
    }

    public Task JogAsync(double velocity, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_state == AxisState.Alarm)
                throw new InvalidOperationException($"轴 {Name} 报警中,先 ResetAlarm");
            if (_state != AxisState.Idle && _state != AxisState.Moving)
                throw new InvalidOperationException($"轴 {Name} 处于 {_state}");
            if (Math.Abs(velocity) > Configuration.MaxVelocity)
                throw new ArgumentOutOfRangeException(nameof(velocity),
                    $"Jog 速度 {velocity} 超过最大 {Configuration.MaxVelocity}");

            // Jog 没有 target,靠速度方向一直走,直到 Stop 或撞软限位
            _targetPosition = velocity > 0 ? double.PositiveInfinity : double.NegativeInfinity;
            _velocity = velocity;
            _state = AxisState.Moving;
            RaiseStateChanged();
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            // 减速停止 = 立即把速度设为 0,下一 tick 自然停在当前位置
            _velocity = 0;
            _targetPosition = _position;
            if (_state == AxisState.Moving || _state == AxisState.Homing)
            {
                _state = AxisState.Idle;
                RaiseStateChanged();
            }
        }
        return Task.CompletedTask;
    }

    public Task EmergencyStopAsync()
    {
        lock (_lock)
        {
            _velocity = 0;
            _targetPosition = _position;
            _state = AxisState.Idle;   // E-Stop 后回 Idle(不是 Alarm;Alarm 表示故障,E-Stop 是正常安全动作)
            RaiseStateChanged();
        }
        return Task.CompletedTask;
    }

    public Task ResetAlarmAsync()
    {
        lock (_lock)
        {
            if (_state != AxisState.Alarm) return Task.CompletedTask;
            _state = AxisState.Idle;
            RaiseStateChanged();
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 模拟核心:推进一个 dt 时间步。
    /// 内部 timer 每 10ms 调一次;测试也可以直接调以确定性推进。
    /// </summary>
    internal void Tick(TimeSpan dt)
    {
        if (_disposed) return;

        // 在 lock 内做读改写,但事件触发放到 lock 外(避免订阅者回调里再 lock 造成死锁)
        AxisState newState;
        double newPosition;
        var raiseAlarm = false;
        string? alarmMsg = null;
        var raisePosition = false;

        lock (_lock)
        {
            if (_state != AxisState.Moving && _state != AxisState.Homing)
            {
                return;   // 空闲/报警/未使能 → 不推进
            }

            newPosition = _position + _velocity * dt.TotalSeconds;

            // 软限位检查(Jog 到 ±Infinity 时靠这个停)
            if (newPosition < Configuration.MinPosition)
            {
                newPosition = Configuration.MinPosition;
                raiseAlarm = true;
                alarmMsg = $"轴 {Name} 撞负向软限位 ({Configuration.MinPosition})";
            }
            else if (newPosition > Configuration.MaxPosition)
            {
                newPosition = Configuration.MaxPosition;
                raiseAlarm = true;
                alarmMsg = $"轴 {Name} 撞正向软限位 ({Configuration.MaxPosition})";
            }

            // 检查是否到达目标(点位运动;Jog 是 ±Infinity 不会到)
            if (_state == AxisState.Moving || _state == AxisState.Homing)
            {
                var reached = _velocity > 0
                    ? newPosition >= _targetPosition
                    : newPosition <= _targetPosition;
                if (reached && !double.IsInfinity(_targetPosition))
                {
                    newPosition = _targetPosition;   // 防止越过
                    _velocity = 0;
                    if (_state == AxisState.Homing)
                    {
                        _isHomed = true;
                        _position = 0;   // 回零完成的标志:把当前位置定为 0
                        newPosition = 0;
                    }
                    _state = AxisState.Idle;
                    newState = _state;
                }
                else
                {
                    newState = _state;
                }
            }
            else
            {
                newState = _state;
            }

            _position = newPosition;
            raisePosition = true;
        }

        // —— 事件触发(在 lock 外) ——
        if (raisePosition) PositionChanged?.Invoke(this, newPosition);
        if (raiseAlarm)
        {
            lock (_lock) _state = AxisState.Alarm;
            StateChanged?.Invoke(this, AxisState.Alarm);
            AlarmRaised?.Invoke(this, alarmMsg ?? "");
        }
        else if (newState != default)
        {
            StateChanged?.Invoke(this, newState);
        }
    }

    private void RaiseStateChanged()
    {
        var s = State;
        StateChanged?.Invoke(this, s);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
    }
}
