using System.ComponentModel.DataAnnotations;

namespace DaqMonitor.Core.Auth;

/// <summary>
/// 审计日志实体(EF Core 持久化到 audit_logs 表)。
///
/// 工业软件审计的法规定位:
///   - FDA 21 CFR Part 11(医药/食品):电子记录必须可追溯"谁/何时/改了什么"
///   - GxP/GAMP:任何影响产品质量的操作必须落审计
///   - IEC 62443(工业安全): incident response 必须有日志支撑
///
/// 审计日志的 3 个铁律:
///   ① 只追加(Append-Only),不允许改 / 删 — 这里通过业务层不暴露 Update/Delete 方法保证
///   ② 必须有"操作人"(UserId + Username)— 没有操作人的审计等于没审计
///   ③ 必须有"前后值"(BeforeValue/AfterValue)— 才能复盘事故
///
/// 真实生产还会加:TraceId(跨线程关联)、SourceIp、MachineName、ImmutableHash(防篡改)。
/// </summary>
public class AuditLog
{
    public long Id { get; set; }

    /// <summary>动作类型,约定:模块.动作(recipe.activate / user.login / device.config)。</summary>
    [Required, MaxLength(64)]
    public string Action { get; set; } = string.Empty;

    /// <summary>操作人 Id(外键到 User)。匿名操作(如登录失败)为 null。</summary>
    public int? UserId { get; set; }

    /// <summary>操作人用户名(冗余存,即使用户被删除也能查审计)。</summary>
    [MaxLength(64)]
    public string Username { get; set; } = string.Empty;

    /// <summary>操作目标(如"配方 产品A-V1.5"、"用户 admin")。</summary>
    [MaxLength(128)]
    public string Target { get; set; } = string.Empty;

    /// <summary>操作前值(JSON 字符串,可选)。</summary>
    public string? BeforeValue { get; set; }

    /// <summary>操作后值(JSON 字符串,可选)。</summary>
    public string? AfterValue { get; set; }

    /// <summary>结果:success / failure。</summary>
    [Required, MaxLength(16)]
    public string Result { get; set; } = "success";

    /// <summary>失败原因或额外说明(可选)。</summary>
    [MaxLength(256)]
    public string? Detail { get; set; }

    /// <summary>操作时间(UTC,显示时再转本地)。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
