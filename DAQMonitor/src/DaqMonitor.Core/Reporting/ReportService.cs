using DaqMonitor.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DaqMonitor.Core.Reporting;

/// <summary>
/// 报表聚合服务：把原始点位序列按"点位 + 时间窗"聚合成统计行（Min/Max/Avg/Count/起止时间）。
/// 纯计算、无 IO，方便单测；真正的 Excel/PDF 导出在 UI 层（见 M10 讲义 Day3）。
/// 知识关联：M4 把点位存进历史库 → 这里按窗聚合 → M5 把聚合结果画成报表/趋势图 → M6 报警可并入统计。
/// </summary>
public sealed class ReportService
{
    /// <summary>按点位分组，统计给定时间窗内的极值与均值；窗外点自动过滤。</summary>
    public IReadOnlyList<PointStat> Aggregate(IEnumerable<SensorPoint> points, DateTime from, DateTime to)
    {
        return points
            .Where(p => p.Timestamp >= from && p.Timestamp <= to)
            .GroupBy(p => p.Id)
            .Select(g => new PointStat(
                PointId: g.Key,
                Count: g.Count(),
                Min: g.Min(p => p.Value),
                Max: g.Max(p => p.Value),
                Avg: g.Average(p => p.Value),
                First: g.Min(p => p.Timestamp),
                Last: g.Max(p => p.Timestamp)))
            .OrderBy(s => s.PointId)
            .ToList();
    }
}

/// <summary>单点位在某时间窗内的统计快照</summary>
public sealed record PointStat(
    int PointId,
    int Count,
    double Min,
    double Max,
    double Avg,
    DateTime First,
    DateTime Last);
