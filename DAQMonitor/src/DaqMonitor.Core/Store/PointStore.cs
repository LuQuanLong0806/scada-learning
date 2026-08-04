using DaqMonitor.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace DaqMonitor.Core.Store;

/// <summary>
/// 点位存储：双索引（列表 + 字典）+ LINQ 查询（Day 3 练习落地）。
/// 列表保序用于展示，字典用于按 Id 快速查设备。
/// </summary>
public class PointStore
{
    private readonly List<SensorPoint> _points = new();
    private readonly Dictionary<int, SensorPoint> _byId = new();

    public void AddOrUpdate(SensorPoint p)
    {
        _byId[p.Id] = p;
        var idx = _points.FindIndex(x => x.Id == p.Id);
        if (idx >= 0) _points[idx] = p;
        else _points.Add(p);
    }

    public SensorPoint? Get(int id) => _byId.TryGetValue(id, out var p) ? p : null;

    public IReadOnlyList<SensorPoint> GetAll() => _points;

    /// <summary>返回超阈值的点（实时报警直接复用，Day 6 / M6 用）</summary>
    public IReadOnlyList<SensorPoint> GetAlarms(double threshold)
        => _points.Where(p => p.Value > threshold).ToList();
}
