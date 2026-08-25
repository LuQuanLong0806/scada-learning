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
