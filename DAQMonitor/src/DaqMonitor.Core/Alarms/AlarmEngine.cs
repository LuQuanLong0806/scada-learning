using DaqMonitor.Core.Models;
using System.Collections.Generic;

namespace DaqMonitor.Core.Alarms;

/// <summary>
/// 报警引擎：规则与数据分离。数据流入逐条匹配，命中即发 AlarmTriggered。
/// 生产级特性：
/// ① 线程安全——规则可在运行时增删；
/// ② 边沿触发——只在“未报警→报警”的上升沿通知，避免每条越界数据都刷屏；
/// ③ 回滞(hysteresis)——值在阈值附近抖动时不反复触发/恢复。
/// </summary>
public class AlarmEngine
{
    private readonly List<AlarmRule> _rules = new();
    private readonly HashSet<int> _active = new();   // 当前已处于报警状态的点位
    private readonly object _gate = new();

    public event EventHandler<AlarmEvent>? AlarmTriggered;
    /// <summary>报警恢复（下降沿）：点位从报警区间回到正常区间时触发，UI 据此把表盘颜色复位。</summary>
    public event EventHandler<AlarmEvent>? AlarmCleared;

    public void Add(AlarmRule r) { lock (_gate) _rules.Add(r); }
    public void Clear() { lock (_gate) { _rules.Clear(); _active.Clear(); } }

    public void Evaluate(SensorPoint p)
    {
        List<AlarmRule> snapshot;
        lock (_gate) snapshot = _rules.ToList();

        foreach (var r in snapshot)
        {
            if (r.PointId != p.Id) continue;
            bool breach = r.IsHigh ? p.Value > r.Threshold : p.Value < r.Threshold;
            bool inBand = r.Hysteresis > 0 && Math.Abs(p.Value - r.Threshold) <= r.Hysteresis;

            if (breach && !inBand)
            {
                bool wasActive;
                lock (_gate) wasActive = !_active.Add(p.Id);
                if (!wasActive)   // 仅上升沿触发
                    AlarmTriggered?.Invoke(this, new AlarmEvent { PointId = p.Id, Level = r.Level, Value = p.Value });
            }
            else if (!breach && r.Hysteresis > 0)
            {
                bool wasActive;
                lock (_gate) wasActive = _active.Remove(p.Id);   // 回到正常区间，复位，下次越界再报
                if (wasActive)   // 仅下降沿通知 UI 复位表盘
                    AlarmCleared?.Invoke(this, new AlarmEvent { PointId = p.Id, Level = r.Level, Value = p.Value });
            }
        }
    }
}
