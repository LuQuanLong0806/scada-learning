using DaqMonitor.Core.Models;
using DaqMonitor.Core.Reporting;
using System;
using System.Collections.Generic;
using Xunit;

namespace DaqMonitor.Tests;

public class ReportServiceTests
{
    private static List<SensorPoint> Sample()
    {
        var t0 = new DateTime(2026, 7, 23, 8, 0, 0);
        return new List<SensorPoint>
        {
            new() { Id = 1, Value = 10, Timestamp = t0.AddMinutes(0) },
            new() { Id = 1, Value = 20, Timestamp = t0.AddMinutes(1) },
            new() { Id = 1, Value = 30, Timestamp = t0.AddMinutes(2) },
            new() { Id = 2, Value = 5,  Timestamp = t0.AddMinutes(0) },
            new() { Id = 2, Value = 15, Timestamp = t0.AddMinutes(1) },
            // 落在窗外的点：应被过滤
            new() { Id = 1, Value = 999, Timestamp = t0.AddHours(-5) },
        };
    }

    [Fact]
    public void Aggregate_GroupsByPoint_AndFiltersWindow()
    {
        var from = new DateTime(2026, 7, 23, 8, 0, 0);
        var to = from.AddMinutes(10);
        var stats = new ReportService().Aggregate(Sample(), from, to);

        Assert.Equal(2, stats.Count); // 点位1+点位2，窗外 999 已过滤

        var p1 = stats.Single(s => s.PointId == 1);
        Assert.Equal(3, p1.Count);
        Assert.Equal(10, p1.Min);
        Assert.Equal(30, p1.Max);
        Assert.Equal(20, p1.Avg); // (10+20+30)/3

        var p2 = stats.Single(s => s.PointId == 2);
        Assert.Equal(2, p2.Count);
        Assert.Equal(5, p2.Min);
        Assert.Equal(15, p2.Max);
        Assert.Equal(10, p2.Avg);
    }

    [Fact]
    public void Aggregate_EmptyWindow_ReturnsEmpty()
    {
        var stats = new ReportService().Aggregate(
            Sample(), new DateTime(2000, 1, 1), new DateTime(2000, 1, 2));
        Assert.Empty(stats);
    }
}
