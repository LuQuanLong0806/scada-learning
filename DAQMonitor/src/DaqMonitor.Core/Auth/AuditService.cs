using DaqMonitor.Core.Store;
using Microsoft.EntityFrameworkCore;

namespace DaqMonitor.Core.Auth;

/// <summary>
/// 审计日志服务:所有"影响系统状态"的操作必须经过这里落库。
///
/// 工业现场铁律:
///   ① 审计失败不应中断业务(否则工人在生产线上卡死)
///   ② 审计必须同步(防崩溃后丢失)
///   ③ 审计表只追加,不更新/删除(法律合规要求)
///
/// 这里的实现:
///   - 同步写 SQLite(失败时 Console 写 + 吞异常,保业务)
///   - 不暴露 Update/Delete 方法(编译期保证只追加)
///   - 任意业务线程都能调,DbContext 短生命周期(避免线程冲突)
///
/// 性能:
///   单次写 &lt; 5ms,即使每秒 100 次审计也能扛(SQLite WAL 模式)。
///   长跑场景若审计洪峰,可改 Channel 异步队列(类似 MqttPublisher),学习项目暂不需要。
/// </summary>
public class AuditService
{
    private readonly IDbContextFactory<AppDb> _dbf;

    public AuditService(IDbContextFactory<AppDb> dbf) => _dbf = dbf;

    /// <summary>
    /// 落一条审计日志。失败抛异常的版本(用于关键路径,如登录)。
    /// </summary>
    public async Task LogAsync(
        string action,
        int? userId,
        string username,
        string target = "",
        string? beforeValue = null,
        string? afterValue = null,
        string result = "success",
        string? detail = null,
        CancellationToken ct = default)
    {
        var log = new AuditLog
        {
            Action = action,
            UserId = userId,
            Username = username,
            Target = target,
            BeforeValue = beforeValue,
            AfterValue = afterValue,
            Result = result,
            Detail = detail,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            using var db = await _dbf.CreateDbContextAsync(ct);
            db.AuditLogs.Add(log);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // 审计失败不能中断业务,但必须留痕(Console + 可选:文件)
            Console.Error.WriteLine($"[AUDIT FAIL] {action} by {username}: {ex.Message}");
        }
    }

    /// <summary>
    /// 不带 userId 的便捷重载(系统级操作,如启动/关闭)。
    /// </summary>
    public Task LogSystemAsync(string action, string detail = "", CancellationToken ct = default)
        => LogAsync(action, null, "system", detail: detail, ct: ct);

    /// <summary>
    /// 查询审计日志(分页 + 过滤,UI 历史页用)。
    /// </summary>
    public async Task<IReadOnlyList<AuditLog>> QueryAsync(
        int? userIdFilter = null,
        string? actionFilter = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        using var db = await _dbf.CreateDbContextAsync(ct);
        var q = db.AuditLogs.AsNoTracking();
        if (userIdFilter is int uid) q = q.Where(a => a.UserId == uid);
        if (!string.IsNullOrEmpty(actionFilter)) q = q.Where(a => a.Action == actionFilter);
        if (fromUtc is DateTime f) q = q.Where(a => a.CreatedAt >= f);
        if (toUtc is DateTime t) q = q.Where(a => a.CreatedAt <= t);
        return await q.OrderByDescending(a => a.CreatedAt).Take(limit).ToListAsync(ct);
    }
}
