using DaqMonitor.Core.Auth;
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Recipes;
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

    /// <summary>用户表(M17 工业安全:本地账号 + BCrypt)。</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>审计日志表(只追加,法律合规要求)。</summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>配方表(M18 工艺参数包)。</summary>
    public DbSet<Recipe> Recipes => Set<Recipe>();

    /// <summary>配方历史快照(改前自动存档,支持 rollback)。</summary>
    public DbSet<RecipeSnapshot> RecipeSnapshots => Set<RecipeSnapshot>();

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

        // ===== User 表 =====
        var u = mb.Entity<User>();
        u.ToTable("users");
        u.HasKey(x => x.Id);
        u.Property(x => x.Id).ValueGeneratedOnAdd();
        u.Property(x => x.Username).HasColumnName("username").IsRequired().HasMaxLength(64);
        u.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired().HasMaxLength(120);
        u.Property(x => x.Role).HasColumnName("role").HasConversion<string>().IsRequired();
        u.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        u.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        u.Property(x => x.LastLoginAt).HasColumnName("last_login_at");
        u.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(64);
        // 用户名唯一索引:防止并发创建重复账号
        u.HasIndex(x => x.Username).IsUnique().HasDatabaseName("ix_users_username");

        // ===== AuditLog 表 =====
        var a = mb.Entity<AuditLog>();
        a.ToTable("audit_logs");
        a.HasKey(x => x.Id);
        a.Property(x => x.Id).ValueGeneratedOnAdd();
        a.Property(x => x.Action).HasColumnName("action").IsRequired().HasMaxLength(64);
        a.Property(x => x.UserId).HasColumnName("user_id");
        a.Property(x => x.Username).HasColumnName("username").HasMaxLength(64);
        a.Property(x => x.Target).HasColumnName("target").HasMaxLength(128);
        a.Property(x => x.BeforeValue).HasColumnName("before_value");
        a.Property(x => x.AfterValue).HasColumnName("after_value");
        a.Property(x => x.Result).HasColumnName("result").IsRequired().HasMaxLength(16);
        a.Property(x => x.Detail).HasColumnName("detail").HasMaxLength(256);
        a.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        // 审计查询主路径:按时间倒序翻页 + 按 UserId 过滤
        a.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_audit_created");
        a.HasIndex(x => x.UserId).HasDatabaseName("ix_audit_user");
        a.HasIndex(x => x.Action).HasDatabaseName("ix_audit_action");

        // ===== Recipe 表(M18 配方管理) =====
        var r = mb.Entity<Recipe>();
        r.ToTable("recipes");
        r.HasKey(x => x.Id);
        r.Property(x => x.Id).ValueGeneratedOnAdd();
        r.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(64);
        r.Property(x => x.Description).HasColumnName("description").HasMaxLength(256);
        r.Property(x => x.Version).HasColumnName("version").IsRequired();
        r.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        r.Property(x => x.IsDeleted).HasColumnName("is_deleted").IsRequired();
        // 参数 JSON 列:SQLite 没有原生 JSON 类型,用 TEXT 存;EF Core 8 仍能查询 JSON path
        r.Property(x => x.ParametersJson).HasColumnName("parameters_json").IsRequired();
        r.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        r.Property(x => x.CreatedByUsername).HasColumnName("created_by_username").HasMaxLength(64);
        r.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        r.Property(x => x.ActivatedAt).HasColumnName("activated_at");
        // 配方名 + 软删 标识唯一(允许软删后重用同名)
        r.HasIndex(x => new { x.Name, x.IsDeleted }).HasDatabaseName("ix_recipes_name");
        r.HasIndex(x => x.IsActive).HasDatabaseName("ix_recipes_active");

        // ===== RecipeSnapshot 表 =====
        var rs = mb.Entity<RecipeSnapshot>();
        rs.ToTable("recipe_snapshots");
        rs.HasKey(x => x.Id);
        rs.Property(x => x.Id).ValueGeneratedOnAdd();
        rs.Property(x => x.RecipeId).HasColumnName("recipe_id").IsRequired();
        rs.Property(x => x.Version).HasColumnName("version").IsRequired();
        rs.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(64);
        rs.Property(x => x.ParametersJson).HasColumnName("parameters_json").IsRequired();
        rs.Property(x => x.SnapshotByUsername).HasColumnName("snapshot_by_username").HasMaxLength(64);
        rs.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        rs.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(32);
        rs.HasIndex(x => x.RecipeId).HasDatabaseName("ix_snapshots_recipe");
        rs.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_snapshots_created");
    }
}
