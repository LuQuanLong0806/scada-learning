using DaqMonitor.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DaqMonitor.Core.Store;

/// <summary>
/// EF Core DbContext：把 SensorPoint 历史样本落到 SQLite。
///
/// 为什么不直接 DbSet&lt;SensorPoint&gt;：
///   SensorPoint 是 struct，EF Core 8 对 owned struct / keyless struct 实体支持仍不顺畅，
///   配置索引与变更跟踪也麻烦。改用一个等价的 class SensorRecord 作持久化模型，
///   领域层（采集 / 报警 / UI）继续用 struct，互转只在 Store 边界发生 —— 仓储模式的好处。
///
/// 索引策略：
///   - (PointId, Time) 复合索引：覆盖“按点位 + 时间窗查历史”的主查询路径；
///   - PointId 单列索引：覆盖“按点位统计最新值/聚合”的次要路径；
///   - Time 单列索引：覆盖“全点位按时间窗扫描”。
/// </summary>
public class AppDb : DbContext
{
    public AppDb(DbContextOptions<AppDb> options) : base(options) { }

    /// <summary>历史样本表（按时间追加，几乎不更新）。</summary>
    public DbSet<SensorRecord> Records => Set<SensorRecord>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        var e = mb.Entity<SensorRecord>();
        e.ToTable("sensor_record");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedOnAdd();

        e.Property(x => x.PointId).HasColumnName("point_id").IsRequired();
        e.Property(x => x.Value).HasColumnName("value").IsRequired();
        e.Property(x => x.State).HasColumnName("state").HasConversion<string>().IsRequired();
        e.Property(x => x.Time).HasColumnName("time").IsRequired();

        // 主查询路径：按点位 + 时间窗。SQLite 的 ASC 索引同时支持 ASC / DESC 查询。
        e.HasIndex(x => new { x.PointId, x.Time }).HasDatabaseName("ix_record_point_time");
        e.HasIndex(x => x.PointId).HasDatabaseName("ix_record_point");
        e.HasIndex(x => x.Time).HasDatabaseName("ix_record_time");
    }
}
