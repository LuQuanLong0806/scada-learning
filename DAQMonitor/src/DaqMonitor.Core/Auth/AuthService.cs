using DaqMonitor.Core.Store;
using Microsoft.EntityFrameworkCore;

namespace DaqMonitor.Core.Auth;

/// <summary>
/// 认证服务:登录验证 + 用户 CRUD + BCrypt 密码哈希。
///
/// 关键决策(对比前端):
///   ① 密码哈希用 BCrypt(workFactor=11),不用 MD5/SHA — 前端很多老系统用 MD5+salt,
///      BCrypt 自带 salt + 可调 workFactor,抗 GPU/ASIC 暴力破解强 100 倍
///   ② 不用 JWT(模式 B 单机不需要) — JWT 是网络 token,这里只在本机验证
///   ③ 不存 Session — 每次启动重新登录(操作工换班需要)
///   ④ 软删除(IsActive=false)而不 Delete — 审计完整性(历史日志还要引用用户名)
///
/// 性能注意:
///   BCrypt workFactor=11 ≈ 单次验证 200ms。故意慢(防爆破),登录是低频操作可接受。
///   不要把 workFactor 调到 15+(单次 3 秒+,UI 卡顿)。
/// </summary>
public class AuthService
{
    private readonly IDbContextFactory<AppDb> _dbf;
    private readonly ICurrentUserService _current;
    private readonly AuditService _audit;

    public AuthService(IDbContextFactory<AppDb> dbf, ICurrentUserService current, AuditService audit)
    {
        _dbf = dbf;
        _current = current;
        _audit = audit;
    }

    /// <summary>
    /// 登录验证。
    /// 返回 (success, errorMessage)。
    /// 失败统一返回"用户名或密码错误",不区分用户名错/密码错(防账号枚举攻击)。
    /// </summary>
    public async Task<(bool Success, string Error)> LoginAsync(
        string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return (false, "请输入用户名和密码");

        using var db = await _dbf.CreateDbContextAsync(ct);
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username, ct);

        // 时序安全:用户不存在也要跑一次 BCrypt.Verify,防止"用户不存在秒返回,存在慢返回"侧信道
        if (user is null)
        {
            _ = BCrypt.Net.BCrypt.Verify("dummy", "$2a$11$dummyhashdummyhashdummyhashdummyhashdummyhashdum");
            await _audit.LogAsync("user.login", null, username, target: username, result: "failure", detail: "用户不存在", ct: ct);
            return (false, "用户名或密码错误");
        }

        if (!user.IsActive)
        {
            await _audit.LogAsync("user.login", user.Id, user.Username, target: user.Username, result: "failure", detail: "账号已禁用", ct: ct);
            return (false, "账号已禁用,请联系管理员");
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            await _audit.LogAsync("user.login", user.Id, user.Username, target: user.Username, result: "failure", detail: "密码错误", ct: ct);
            return (false, "用户名或密码错误");
        }

        // 登录成功:更新 LastLoginAt + 写入当前用户
        await using (var db2 = await _dbf.CreateDbContextAsync(ct))
        {
            var u = await db2.Users.FirstAsync(u => u.Id == user.Id, ct);
            u.LastLoginAt = DateTime.UtcNow;
            await db2.SaveChangesAsync(ct);
        }
        // 写一个副本,避免外部误改影响审计一致性
        _current.SetUser(new User
        {
            Id = user.Id,
            Username = user.Username,
            Role = user.Role,
            IsActive = user.IsActive,
            DisplayName = user.DisplayName,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt
        });
        await _audit.LogAsync("user.login", user.Id, user.Username, target: user.Username, result: "success", ct: ct);
        return (true, string.Empty);
    }

    /// <summary>登出 + 写审计。</summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        var u = _current.User;
        if (u is not null)
            await _audit.LogAsync("user.logout", u.Id, u.Username, target: u.Username, ct: ct);
        _current.Clear();
    }

    /// <summary>创建用户(仅 Admin 应调,由调用方保证权限)。</summary>
    public async Task<User> CreateUserAsync(
        string username, string password, UserRole role, string displayName = "", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("用户名和密码不能为空");
        if (username.Length > 64) throw new ArgumentException("用户名超过 64 字符");
        if (password.Length < 6) throw new ArgumentException("密码至少 6 位");

        using var db = await _dbf.CreateDbContextAsync(ct);
        if (await db.Users.AnyAsync(u => u.Username == username, ct))
            throw new InvalidOperationException($"用户名 {username} 已存在");

        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11),
            Role = role,
            DisplayName = string.IsNullOrEmpty(displayName) ? username : displayName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var actor = _current.User;
        await _audit.LogAsync(
            "user.create", actor?.Id, actor?.Username ?? "system",
            target: username,
            afterValue: $"{{\"role\":\"{role}\"}}", ct: ct);
        return user;
    }

    /// <summary>修改密码(需提供旧密码,防止 session 劫持后改密码)。</summary>
    public async Task<(bool Success, string Error)> ChangePasswordAsync(
        int userId, string oldPassword, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            return (false, "新密码至少 6 位");

        using var db = await _dbf.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return (false, "用户不存在");
        if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash))
            return (false, "旧密码错误");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: 11);
        await db.SaveChangesAsync(ct);

        var actor = _current.User;
        await _audit.LogAsync(
            "user.password.change", actor?.Id, actor?.Username ?? user.Username,
            target: user.Username, ct: ct);
        return (true, string.Empty);
    }

    /// <summary>禁用/启用账号(软删除)。</summary>
    public async Task SetActiveAsync(int userId, bool active, CancellationToken ct = default)
    {
        using var db = await _dbf.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return;
        user.IsActive = active;
        await db.SaveChangesAsync(ct);

        var actor = _current.User;
        await _audit.LogAsync(
            "user.active.toggle", actor?.Id, actor?.Username ?? "system",
            target: user.Username, afterValue: $"{{\"active\":{active.ToString().ToLowerInvariant()}}}", ct: ct);
    }

    /// <summary>列出全部用户(仅 Admin / Engineer 应调)。</summary>
    public async Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken ct = default)
    {
        using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync(ct);
    }
}
