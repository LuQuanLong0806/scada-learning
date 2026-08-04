namespace DaqMonitor.Core.Motion;

/// <summary>
/// 轴配置(从机械工艺参数推导,机器装好后基本不变)。
///
/// 真实工程来源:
///   - 行程(MinPosition/MaxPosition):机械设计图 + 现场实测(留 5% 安全余量)
///   - MaxVelocity:电机额定转速 × 螺距 / 减速比(比如 3000rpm × 5mm/rev / 60s = 250mm/s)
///   - HomeVelocity:回零慢速,通常是 MaxVelocity 的 10-20%(慢速找零位开关精度高)
///   - CountsPerUnit:编码器脉冲 → 用户单位的转换系数(如 10000 脉冲/mm)
///
/// 类比前端:这就是"用户设置页面"的常量,跟主题色、字体大小同性质。
/// </summary>
public class AxisConfiguration
{
    /// <summary>轴逻辑 ID(0/1/2...)。真实卡上对应物理轴号。</summary>
    public int AxisId { get; init; }

    /// <summary>轴名(给人看,如 "X" / "Y" / "Z" / "R1 旋转")。</summary>
    public string Name { get; init; } = "";

    /// <summary>软限位最小位置(mm 或 °)。运动到 < MinPosition 拒绝执行并报警。</summary>
    public double MinPosition { get; init; } = -200;

    /// <summary>软限位最大位置(mm 或 °)。</summary>
    public double MaxPosition { get; init; } = 200;

    /// <summary>最大速度(mm/s 或 °/s)。MoveAbsoluteAsync/Jog 的速度不能超过这个。</summary>
    public double MaxVelocity { get; init; } = 200;

    /// <summary>默认回零速度(mm/s,慢速确保精度)。</summary>
    public double HomeVelocity { get; init; } = 20;

    /// <summary>默认运动速度(给 UI 用,MoveAbsolute 时如果不传 velocity 用这个)。</summary>
    public double DefaultVelocity { get; init; } = 100;

    /// <summary>编码器转换系数(脉冲/单位,模拟用不到,接真卡时关键)。</summary>
    public double CountsPerUnit { get; init; } = 10000;

    /// <summary>是否反转方向(机械装配决定,某些电机正反转方向反)。</summary>
    public bool IsReversed { get; init; } = false;
}
