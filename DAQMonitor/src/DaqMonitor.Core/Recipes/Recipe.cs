using System.ComponentModel.DataAnnotations;

namespace DaqMonitor.Core.Recipes;

/// <summary>
/// 配方实体(EF Core 持久化到 recipes 表)。
///
/// 什么是配方:
///   工业现场生产不同产品(比如 iPhone15 壳 vs Samsung S24 壳),工艺参数不同。
///   把"一组参数"打包存好、命名、版本化,换产品时"激活"对应配方即可,
///   不用现场改参数(改错概率高、追溯难)。这就是配方管理。
///
/// 类比前端:
///   Recipe = Figma Component Variant;iPhone15 一个 Variant,S24 另一个。
///   Activate = 当前选中的 Variant;同时只能一个 active。
///
/// 设计取舍:
///   ① 参数用 JSON 列(ParametersJson)而不是单独的 RecipeParameter 表
///      —— 工业配方参数通常 10-50 个,JSON 列查询/导出最简单,EF Core 8 原生支持。
///      真要按参数维度做"全局查询所有配方中温度=180 的"才需要拆表(罕见)。
///   ② 软删除(IsDeleted) —— 配方被历史审计引用,物理删会断链
///   ③ 版本号(Version)自增 —— 每次 Update 自动 +1,配合 Snapshot 支持回滚
/// </summary>
public class Recipe
{
    public int Id { get; set; }

    [Required, MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(256)]
    public string Description { get; set; } = string.Empty;

    /// <summary>版本号,每次 Update 自增。</summary>
    public int Version { get; set; } = 1;

    /// <summary>是否为当前激活配方(全局唯一,激活时其他配方自动置 false)。</summary>
    public bool IsActive { get; set; }

    /// <summary>软删除标志(审计完整性)。</summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// 参数 JSON 列(List&lt;RecipeParameter&gt; 序列化)。
    /// EF Core 8 可用 Owned primitive collection,这里直接用 string + System.Text.Json 更直观。
    /// </summary>
    [Required]
    public string ParametersJson { get; set; } = "[]";

    /// <summary>创建者用户 ID(审计追溯)。</summary>
    public int CreatedByUserId { get; set; }

    [MaxLength(64)]
    public string CreatedByUsername { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>最近一次激活时间(便于现场看"上次换型是什么时候")。</summary>
    public DateTime? ActivatedAt { get; set; }
}
