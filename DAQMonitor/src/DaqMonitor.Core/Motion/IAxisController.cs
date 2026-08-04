namespace DaqMonitor.Core.Motion;

/// <summary>
/// 运动控制轴抽象接口。
///
/// 设计哲学(对标固高/雷赛/正运动/Beckhoff):
///   接口只暴露"运动控制通用动作",不暴露厂商 API 差异。
///   真实硬件来了换一个实现,业务代码零改动 —— 这就是依赖倒置原则(DIP)的胜利。
///
/// 同类接口在工业界的标杆:
///   - PLCopen Motion(IEC 61131-3 标准):MC_MoveAbsolute / MC_MoveRelative / MC_Home / MC_Stop
///   - Beckhoff TwinCAT:TcMC2 库,实现 PLCopen 标准
///   - 固高/雷赛:厂商私有 API,通常包一层 wrapper 转成 PLCopen
///
/// 命名约定:跟 PLCopen 对齐(MoveAbsolute/MoveRelative/Home/Stop),面试好讲。
/// </summary>
public interface IAxisController
{
    // —— 标识 + 配置 ——
    int AxisId { get; }
    string Name { get; }
    AxisConfiguration Configuration { get; }

    // —— 状态 ——
    AxisState State { get; }
    double CurrentPosition { get; }     // mm/°(用户单位,不是脉冲)
    double CurrentVelocity { get; }     // mm/s,有方向(负=反向)
    bool IsHomed { get; }               // 是否已回零(未回零时定位命令应拒绝)

    // —— 事件(状态机变化时触发,UI/业务订阅) ——
    event EventHandler<AxisState>? StateChanged;
    event EventHandler<double>? PositionChanged;     // 持续刷新(UI 表盘)
    event EventHandler<string>? AlarmRaised;          // 报警信息(超限/超速/驱动故障)

    // —— 运动命令(PLCopen 兼容) ——

    /// <summary>
    /// 回原点(Homing)。机械装好后,轴的真实位置是未知的(编码器只能记录相对位移),
    /// 必须先回零确定基准点。流程通常是:轴以 HomeVelocity 慢速移动,直到碰到回零开关,
    /// 然后反向脱离一小段距离,记录当前位置作为 0 点。
    /// </summary>
    Task HomeAsync(CancellationToken ct = default);

    /// <summary>
    /// 绝对定位(到指定坐标)。如 MoveAbsoluteAsync(100, 50) = "以 50mm/s 移动到 X=100mm"。
    /// 未回零时拒绝执行(否则绝对坐标无意义)。
    /// </summary>
    Task MoveAbsoluteAsync(double position, double velocity, CancellationToken ct = default);

    /// <summary>
    /// 相对定位(走指定增量)。如 MoveRelativeAsync(10, 50) = "从当前位置往正向走 10mm"。
    /// 相对定位不强制要求 IsHomed(有些场景只关心增量),但工业现场一般也要求先回零。
    /// </summary>
    Task MoveRelativeAsync(double distance, double velocity, CancellationToken ct = default);

    /// <summary>
    /// 点动(操作工按住按钮就动)。velocity 为正/负控制方向,持续运动直到 JogStop / Stop。
    /// 用于示教/调试:操作工手动把轴调到大致位置,再保存为配方点位。
    /// </summary>
    Task JogAsync(double velocity, CancellationToken ct = default);

    /// <summary>减速停止(运动命令的中断)。</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// 紧急停止(立即停,IEC 60204-1 安全要求)。
    /// 跟 StopAsync 的区别:E-Stop 不走减速曲线,瞬时切断伺服输出(可能丢位置),
    /// 但能最快避免机械碰撞。故障/危险时优先用 E-Stop,正常停止用 Stop。
    /// </summary>
    Task EmergencyStopAsync();

    /// <summary>清除报警(Alarm → Idle),需要操作工主动确认。</summary>
    Task ResetAlarmAsync();
}
