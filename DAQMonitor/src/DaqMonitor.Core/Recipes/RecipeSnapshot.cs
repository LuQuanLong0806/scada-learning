using System.ComponentModel.DataAnnotations;

namespace DaqMonitor.Core.Recipes;

/// <summary>
/// 配方快照:每次 Update 前自动存档一份,支持 rollback。
///
/// 为什么需要快照:
///   FDA 21 CFR Part 11(医药/食品/化妆品 GxP)要求"任何配方变更可追溯且可回滚"。
///   即"3 个月前那个 bug 是哪天引入的、引入前的状态是什么、能不能回去"。
///
/// 类比前端:Git commit。每次 Update 自动 commit 一次,可 git checkout 回任意版本。
/// </summary>
public class RecipeSnapshot
{
    public int Id { get; set; }

    public int RecipeId { get; set; }

    /// <summary>对应 Recipe.Version(快照当时版本号)。</summary>
    public int Version { get; set; }

    [Required, MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    /// <summary>当时的参数 JSON 全量副本(快照本质:不可变记录)。</summary>
    [Required]
    public string ParametersJson { get; set; } = "[]";

    [MaxLength(64)]
    public string SnapshotByUsername { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>快照原因:"before_update" / "before_delete" / "before_activate" 等。</summary>
    [MaxLength(32)]
    public string Reason { get; set; } = "before_update";
}
