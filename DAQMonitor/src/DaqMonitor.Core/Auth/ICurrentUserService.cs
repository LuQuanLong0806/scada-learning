namespace DaqMonitor.Core.Auth;

/// <summary>
/// 当前登录用户持有者(应用生命周期单例)。
///
/// 类比前端:这就是 Vue 的 Pinia user store / React 的 Context CurrentUser,
/// 登录后写入,登出清空,任何 VM 都能 inject 读当前用户。
///
/// 类比 ASP.NET Core:HttpContext.User / ClaimsPrincipal 的简化版,
/// 桌面应用没有 HttpContext,用一个单例对象承载"当前用户"。
///
/// 设计要点:
///   - 单例:整个应用同时只有一个登录用户(操作工换班 = 重新登录)
///   - 线程安全:User 属性读写要加锁(UI 线程写 / 后台线程读)
///   - 不持有 DbContext:User 是 POCO,EF 跟踪由 AuthService 负责
/// </summary>
public interface ICurrentUserService
{
    /// <summary>当前登录用户(null = 未登录)。返回的是副本,改了不会写库。</summary>
    User? User { get; }

    /// <summary>是否已登录。</summary>
    bool IsAuthenticated { get; }

    /// <summary>当前用户名(未登录返回空串,UI 绑定用)。</summary>
    string Username { get; }

    /// <summary>当前角色(未登录返回 null)。</summary>
    UserRole? Role { get; }

    /// <summary>检查权限点(Admin 永远 true,其他角色查 Permissions.ByRole)。</summary>
    bool HasPermission(string permission);

    /// <summary>登录成功后写入(由 AuthService 调用,业务代码不应直接调)。</summary>
    void SetUser(User user);

    /// <summary>登出 / 换班。</summary>
    void Clear();
}

/// <summary>
/// 默认实现:内存单例 + 锁保护读写。
///
/// 为什么用锁而不是 volatile / Interlocked:
///   User 是引用类型,"读-改-写"在多线程下需要原子语义,lock 最简单可靠。
///   频率很低(登录/登出/换班),锁竞争可忽略。
/// </summary>
public sealed class CurrentUserService : ICurrentUserService
{
    private readonly object _lock = new();
    private User? _user;

    public User? User
    {
        get { lock (_lock) return _user; }
    }

    public bool IsAuthenticated => User is not null;

    public string Username => User?.Username ?? string.Empty;

    public UserRole? Role => User?.Role;

    public bool HasPermission(string permission)
    {
        var u = User;
        if (u is null) return false;
        if (u.Role == UserRole.Admin) return true;  // Admin 短路
        return Permissions.ByRole.TryGetValue(u.Role, out var perms) && perms.Contains(permission);
    }

    public void SetUser(User user)
    {
        lock (_lock) _user = user;
    }

    public void Clear()
    {
        lock (_lock) _user = null;
    }
}
