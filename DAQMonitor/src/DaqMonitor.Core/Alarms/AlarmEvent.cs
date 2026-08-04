using DaqMonitor.Core.Models;

namespace DaqMonitor.Core.Alarms;

/// <summary>报警触发事件参数。</summary>
public class AlarmEvent : EventArgs
{
    public int PointId { get; init; }
    public AlarmLevel Level { get; init; }
    public double Value { get; init; }
}
