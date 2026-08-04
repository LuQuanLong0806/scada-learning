using System.ComponentModel.DataAnnotations;

namespace DaqMonitor.Core.Auth;

/// <summary>
/// 用户实体(EF Core 持久化到 SQLite users 表)。
///
/// 字段说明:
///   - Username:登录名,唯一索引,大小写敏感(SQLite 默认)
///   - PasswordHash:BCrypt 哈希($2a$11$... 格式,内含盐 + workFactor)
///     不存明文 + 不存 MD5/SHA(易被彩虹表破解)。BCrypt 自带盐,每次哈希不同。
///   - Role:角色枚举,存字符串(HasConversion&lt;string&gt;),便于人工查库
///   - IsActive:软删除标志(禁用账号而不删除,保留审计完整性)
///   - CreatedAt / LastLoginAt:审计用
///
/// 为什么不用 ASP.NET Core Identity:
///   Identity 太重(30+ 表 + 外键 + 撤销令牌),学习项目过度设计。
///   我们用极简版:1 个 User 表 + 1 个 AuditLog 表,够讲清"权限+审计"两件事。
/// </summary>
public class User
{
    public int Id { get; set; }

    [Required, MaxLength(64)]
    public string Username { get; set; } = string.Empty;

    /// <summary>BCrypt 哈希字符串,格式 $2a$11$salt(22字符)hash(31字符)。</summary>
    [Required, MaxLength(120)]
    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Operator;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }

    /// <summary>显示名(可选,默认同 Username)。</summary>
    [MaxLength(64)]
    public string DisplayName { get; set; } = string.Empty;
}
