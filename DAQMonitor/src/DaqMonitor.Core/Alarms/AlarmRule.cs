using DaqMonitor.Core.Models;

namespace DaqMonitor.Core.Alarms;

/// <summary>报警规则：点位 + 阈值 + 级别 + 方向（超上限 / 低于下限）。规则与数据分离，改配置即可加报警点。</summary>
public class AlarmRule
{
    public int PointId { get; set; }
    public double Threshold { get; set; }
    public AlarmLevel Level { get; set; }
    public bool IsHigh { get; set; } = true;     // true: 超过阈值报警；false: 低于阈值报警
    /// <summary>回滞带宽：值在 [Threshold-带宽, Threshold+带宽] 内视为“已恢复”，不再反复触发（生产必做，防止阈值附近抖动狂报）。</summary>
    public double Hysteresis { get; set; }
}
