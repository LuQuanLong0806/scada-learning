namespace DaqMonitor.Core.Auth;

/// <summary>
/// 用户角色(枚举式 RBAC 简化版)。
///
/// 设计取舍:不做成"角色表 + 权限表 + 角色权限关联表"完整 RBAC,
/// 因为学习项目要追求每一行都能讲清。3 个角色够覆盖 95% 工业现场场景:
///   - Operator  操作工:只能看 + 操作(不能改配方/参数)
///   - Engineer  工程师:能改配方/参数 + 看审计
///   - Admin     管理员:能改用户 + 全部权限
///
/// 真实工程要扩到完整 RBAC 时,把 UserRole 改成实体表 + 中间关联即可,
/// 业务代码用 ICurrentUserService.HasPermission(...) 这层抽象,不直接耦合枚举。
/// </summary>
public enum UserRole
{
    /// <summary>操作工:只读 + 基础操作(启停采集、确认报警)。</summary>
    Operator = 0,

    /// <summary>工程师:操作工权限 + 改配方/参数 + 看审计日志。</summary>
    Engineer = 1,

    /// <summary>管理员:全部权限 + 用户管理 + 系统设置。</summary>
    Admin = 2,
}

/// <summary>
/// 权限点(细粒度操作权限,用于按钮级 IsEnabled 控制)。
/// 命名约定:模块.动作,方便 grep 查找。
/// </summary>
public static class Permissions
{
    public const string AcquisitionStart = "acquisition.start";        // 启动采集
    public const string AcquisitionStop = "acquisition.stop";          // 停止采集
    public const string RecipeEdit = "recipe.edit";                    // 编辑配方
    public const string RecipeActivate = "recipe.activate";            // 激活配方
    public const string DeviceConfig = "device.config";                // 修改设备参数
    public const string AuditView = "audit.view";                      // 查看审计日志
    public const string UserManage = "user.manage";                    // 用户管理
    public const string ReportExport = "report.export";                // 导出报表(含敏感生产数据)

    /// <summary>角色 → 权限映射(启动时加载到字典)。Admin 拥有全部,不再单独列。</summary>
    public static readonly IReadOnlyDictionary<UserRole, IReadOnlySet<string>> ByRole =
        new Dictionary<UserRole, IReadOnlySet<string>>
        {
            [UserRole.Operator] = new HashSet<string>
            {
                AcquisitionStart, AcquisitionStop, RecipeActivate
            },
            [UserRole.Engineer] = new HashSet<string>
            {
                AcquisitionStart, AcquisitionStop, RecipeActivate,
                RecipeEdit, DeviceConfig, AuditView, ReportExport
            },
            // Admin 通过 HasPermission 短路返回 true,不查这个表
        }.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<string>)kv.Value);
}
