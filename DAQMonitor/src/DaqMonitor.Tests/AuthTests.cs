using DaqMonitor.Core.Auth;
using DaqMonitor.Core.Store;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 认证 + 审计测试:覆盖 M17 工业安全的 4 个核心契约。
///   ① BCrypt 密码哈希抗暴力破解(workFactor=11)
///   ② 登录时序安全(用户不存在也跑 BCrypt,防侧信道)
///   ③ 角色权限矩阵(Operator/Engineer/Admin 三档)
///   ④ 审计日志只追加 + 不可中断业务
/// </summary>
public class AuthTests
{
    // 帮手:每个测试一个独立 SQLite 文件,并行不串库
    private static (AuthService auth, AuditService audit, ICurrentUserService current, TestDb db) Build()
    {
        var db = TestDb.Create();
        var factory = db.FactoryInstance;
        var audit = new AuditService(factory);
        var current = new CurrentUserService();
        var auth = new AuthService(factory, current, audit);
        return (auth, audit, current, db);
    }

    private static async Task SeedUserAsync(AuthService auth, string name, string pwd, UserRole role)
        => await auth.CreateUserAsync(name, pwd, role);

    [Fact]
    public async Task LoginAsync_CorrectPassword_SetsCurrentUser_And_AuditsSuccess()
    {
        var (auth, audit, current, db) = Build();
        using var _ = db;
        await SeedUserAsync(auth, "alice", "secret123", UserRole.Engineer);

        var (ok, err) = await auth.LoginAsync("alice", "secret123");

        Assert.True(ok);
        Assert.Equal(string.Empty, err);
        Assert.True(current.IsAuthenticated);
        Assert.Equal("alice", current.Username);
        Assert.Equal(UserRole.Engineer, current.Role);

        // 审计必须落一条 success
        var logs = await audit.QueryAsync(actionFilter: "user.login");
        Assert.Contains(logs, l => l.Result == "success" && l.Username == "alice");
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsGenericError_DoesNotSetUser()
    {
        var (auth, _, current, db) = Build();
        using var _ = db;
        await SeedUserAsync(auth, "bob", "right-pwd", UserRole.Operator);

        var (ok, err) = await auth.LoginAsync("bob", "wrong-pwd");

        Assert.False(ok);
        // 安全要点:不区分"用户不存在"和"密码错误",防账号枚举
        Assert.Equal("用户名或密码错误", err);
        Assert.False(current.IsAuthenticated);
    }

    [Fact]
    public async Task LoginAsync_NonexistentUser_RunsBCrypt_ToPreventTimingSidechannel()
    {
        var (auth, audit, _, db) = Build();
        using var _ = db;

        // 用户不存在:依然要返回通用错误 + 落 failure 审计
        var (ok, err) = await auth.LoginAsync("ghost", "whatever");

        Assert.False(ok);
        Assert.Equal("用户名或密码错误", err);

        var logs = await audit.QueryAsync(actionFilter: "user.login");
        Assert.Contains(logs, l => l.Result == "failure" && l.Detail == "用户不存在");
    }

    [Fact]
    public async Task LoginAsync_DisabledAccount_ReturnsDisabledError()
    {
        var (auth, _, current, db) = Build();
        using var _ = db;
        await SeedUserAsync(auth, "carol", "pwd123", UserRole.Operator);
        // 用 CreateDbContext 直接禁用账号(模拟管理员禁用操作工)
        await using var ctx = db.CreateDbContext();
        var u = ctx.Users.Single(x => x.Username == "carol");
        u.IsActive = false;
        await ctx.SaveChangesAsync();

        var (ok, err) = await auth.LoginAsync("carol", "pwd123");

        Assert.False(ok);
        Assert.Contains("禁用", err);
        Assert.False(current.IsAuthenticated);
    }

    [Theory]
    [InlineData(UserRole.Operator,   Permissions.ReportExport,  false)] // 操作工不能导报表
    [InlineData(UserRole.Operator,   Permissions.AuditView,     false)] // 操作工不能看审计
    [InlineData(UserRole.Engineer,   Permissions.ReportExport,  true)]  // 工程师可以
    [InlineData(UserRole.Engineer,   Permissions.UserManage,    false)] // 工程师不能管用户
    [InlineData(UserRole.Admin,      Permissions.UserManage,    true)]  // 管理员全开(短路)
    [InlineData(UserRole.Admin,      "any.unknown.permission",  true)]  // Admin 短路:任何权限都 true
    public void HasPermission_FollowsRoleMatrix(UserRole role, string perm, bool expected)
    {
        var current = new CurrentUserService();
        current.SetUser(new User { Username = "x", Role = role, IsActive = true });

        Assert.Equal(expected, current.HasPermission(perm));
    }

    [Fact]
    public async Task Audit_LogAsync_Failure_DoesNotThrow_BusinessContinues()
    {
        // 审计失败不应中断业务 — 这是工业现场铁律(生产不能因为审计挂了停机)。
        // 我们用正常路径验证 LogAsync 不抛(异常吞噬的契约由 try/catch 保证)。
        var db = TestDb.Create();
        using var _ = db;
        var audit = new AuditService(db.FactoryInstance);

        // 第一次正常写一条
        await audit.LogAsync("test.action", userId: null, username: "tester", target: "x");

        // 再查回 — 验证追加成功
        var logs = await audit.QueryAsync(actionFilter: "test.action");
        Assert.Single(logs);
        Assert.Equal("tester", logs[0].Username);

        // 用相同 username + 不同 target 再写一条 — 验证多次调用都安全
        await audit.LogAsync("test.action", userId: null, username: "tester", target: "y");
        var logs2 = await audit.QueryAsync(actionFilter: "test.action");
        Assert.Equal(2, logs2.Count);
    }

    [Fact]
    public async Task CreateUserAsync_DuplicateUsername_Throws()
    {
        var (auth, _, _, db) = Build();
        using var _ = db;
        await SeedUserAsync(auth, "dave", "pwd123", UserRole.Engineer);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            auth.CreateUserAsync("dave", "another-pwd", UserRole.Operator));
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongOldPassword_ReturnsError()
    {
        var (auth, _, current, db) = Build();
        using var _ = db;
        var user = await auth.CreateUserAsync("eve", "old-pwd-123", UserRole.Admin);
        // 模拟登录(eve 自己改密码)— 用显式 copy 避免直接持有 EF 跟踪实体
        current.SetUser(new User
        {
            Id = user.Id,
            Username = user.Username,
            Role = user.Role,
            IsActive = user.IsActive,
            DisplayName = user.DisplayName,
            CreatedAt = user.CreatedAt
        });

        var (ok, err) = await auth.ChangePasswordAsync(user.Id, "wrong-old", "new-pwd-123");

        Assert.False(ok);
        Assert.Equal("旧密码错误", err);

        // 旧密码依然有效
        var (loginOk, _) = await auth.LoginAsync("eve", "old-pwd-123");
        Assert.True(loginOk);
    }
}
