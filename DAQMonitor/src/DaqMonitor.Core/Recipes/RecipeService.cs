using System.Text.Json;
using DaqMonitor.Core.Auth;
using DaqMonitor.Core.Store;
using Microsoft.EntityFrameworkCore;

namespace DaqMonitor.Core.Recipes;

/// <summary>
/// 配方服务:CRUD + 激活 + 导入导出 + 历史快照回滚。
///
/// 关键设计决策(对标工业标准 FDA 21 CFR Part 11):
///   ① 激活互斥:全局只能一个 IsActive=true,用 DB 事务保证原子
///   ② 软删除:IsDeleted=true 而非物理删(审计链不断)
///   ③ 自动快照:每次 Update 前自动 Snapshot,可 rollback 任意版本
///   ④ 全部操作落 AuditLog(配合 M17 工业安全)
///   ⑤ 权限点(调用方 UI 检查,Service 信任调用方已校验):
///      - Operator:可 Activate(换产品时)
///      - Engineer+:可 Create/Update/Import/Export
///      - Admin:可物理删除快照(合规慎用)
///
/// 性能注意:
///   - 单次 Update 写 2 张表(recipes + recipe_snapshots),都在一个事务里
///   - 历史快照累积会影响查询,生产可定期归档(>1 年的 snapshot 移到冷库)
/// </summary>
public class RecipeService
{
    private readonly IDbContextFactory<AppDb> _dbf;
    private readonly ICurrentUserService _current;
    private readonly AuditService _audit;
    private readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = false };

    public RecipeService(IDbContextFactory<AppDb> dbf, ICurrentUserService current, AuditService audit)
    {
        _dbf = dbf;
        _current = current;
        _audit = audit;
    }

    /// <summary>列出全部配方(默认不含软删的)。</summary>
    public async Task<IReadOnlyList<Recipe>> ListAsync(bool includeDeleted = false, CancellationToken ct = default)
    {
        using var db = await _dbf.CreateDbContextAsync(ct);
        var q = db.Recipes.AsNoTracking();
        if (!includeDeleted) q = q.Where(r => !r.IsDeleted);
        return await q.OrderByDescending(r => r.IsActive).ThenBy(r => r.Name).ToListAsync(ct);
    }

    /// <summary>获取当前激活配方(null = 未激活任何配方)。</summary>
    public async Task<Recipe?> GetActiveAsync(CancellationToken ct = default)
    {
        using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.Recipes.AsNoTracking().FirstOrDefaultAsync(r => r.IsActive, ct);
    }

    /// <summary>创建配方(Engineer+ 权限,UI 调用前校验)。</summary>
    public async Task<Recipe> CreateAsync(
        string name, string description, IReadOnlyList<RecipeParameter> parameters, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("配方名不能为空");
        if (name.Length > 64) throw new ArgumentException("配方名超过 64 字符");

        var actor = _current.User ?? throw new InvalidOperationException("未登录用户不能创建配方");

        using var db = await _dbf.CreateDbContextAsync(ct);
        if (await db.Recipes.AnyAsync(r => r.Name == name && !r.IsDeleted, ct))
            throw new InvalidOperationException($"配方 {name} 已存在");

        var recipe = new Recipe
        {
            Name = name,
            Description = description ?? string.Empty,
            Version = 1,
            ParametersJson = JsonSerializer.Serialize(parameters, _jsonOpts),
            CreatedByUserId = actor.Id,
            CreatedByUsername = actor.Username,
            CreatedAt = DateTime.UtcNow
        };
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            "recipe.create", actor.Id, actor.Username,
            target: name, afterValue: $"{{\"params\":{parameters.Count}}}", ct: ct);
        return recipe;
    }

    /// <summary>
    /// 更新配方:自动写快照(支持回滚)+ 版本号自增。
    /// </summary>
    public async Task<Recipe> UpdateAsync(
        int recipeId, string name, string description, IReadOnlyList<RecipeParameter> parameters, CancellationToken ct = default)
    {
        var actor = _current.User ?? throw new InvalidOperationException("未登录用户不能改配方");

        using var db = await _dbf.CreateDbContextAsync(ct);
        var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == recipeId, ct)
            ?? throw new InvalidOperationException($"配方 ID={recipeId} 不存在");

        // 改前先存快照(before_update)
        var snapshot = new RecipeSnapshot
        {
            RecipeId = recipe.Id,
            Version = recipe.Version,
            Name = recipe.Name,
            ParametersJson = recipe.ParametersJson,
            SnapshotByUsername = actor.Username,
            CreatedAt = DateTime.UtcNow,
            Reason = "before_update"
        };
        db.RecipeSnapshots.Add(snapshot);

        var oldParams = recipe.ParametersJson;
        recipe.Name = name;
        recipe.Description = description ?? string.Empty;
        recipe.ParametersJson = JsonSerializer.Serialize(parameters, _jsonOpts);
        recipe.Version += 1;

        await db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            "recipe.update", actor.Id, actor.Username,
            target: name, beforeValue: oldParams, afterValue: recipe.ParametersJson, ct: ct);
        return recipe;
    }

    /// <summary>
    /// 激活配方(先全部置 false,再置目标 true —— 用事务保证全局唯一)。
    /// 这是"换产品/换型"的标准动作,Operator 也能调。
    /// </summary>
    public async Task ActivateAsync(int recipeId, CancellationToken ct = default)
    {
        var actor = _current.User ?? throw new InvalidOperationException("未登录用户不能激活配方");

        using var db = await _dbf.CreateDbContextAsync(ct);
        using var tx = await db.Database.BeginTransactionAsync(ct);

        var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == recipeId, ct)
            ?? throw new InvalidOperationException($"配方 ID={recipeId} 不存在");
        if (recipe.IsDeleted) throw new InvalidOperationException("已删除的配方不能激活");

        // 激活前先快照(便于回滚"我激活错了")
        if (recipe.IsActive)
        {
            // 已是激活态,no-op
            return;
        }

        // 互斥:把所有其他配方 IsActive=false
        var allActive = await db.Recipes.Where(r => r.IsActive).ToListAsync(ct);
        var previouslyActive = allActive.Count > 0 ? string.Join(",", allActive.Select(r => r.Name)) : "(none)";
        foreach (var r in allActive) r.IsActive = false;

        recipe.IsActive = true;
        recipe.ActivatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await _audit.LogAsync(
            "recipe.activate", actor.Id, actor.Username,
            target: recipe.Name,
            beforeValue: previouslyActive, afterValue: recipe.Name, ct: ct);
    }

    /// <summary>软删除配方(同时取消激活态)。</summary>
    public async Task DeleteAsync(int recipeId, CancellationToken ct = default)
    {
        var actor = _current.User ?? throw new InvalidOperationException("未登录用户不能删除配方");

        using var db = await _dbf.CreateDbContextAsync(ct);
        var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == recipeId, ct)
            ?? throw new InvalidOperationException($"配方 ID={recipeId} 不存在");

        // 删前快照
        db.RecipeSnapshots.Add(new RecipeSnapshot
        {
            RecipeId = recipe.Id,
            Version = recipe.Version,
            Name = recipe.Name,
            ParametersJson = recipe.ParametersJson,
            SnapshotByUsername = actor.Username,
            CreatedAt = DateTime.UtcNow,
            Reason = "before_delete"
        });

        recipe.IsDeleted = true;
        recipe.IsActive = false;
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("recipe.delete", actor.Id, actor.Username, target: recipe.Name, ct: ct);
    }

    /// <summary>导出配方为 JSON 字符串(可文件存盘 / 跨设备复制)。</summary>
    public async Task<string> ExportAsync(int recipeId, CancellationToken ct = default)
    {
        var actor = _current.User;
        using var db = await _dbf.CreateDbContextAsync(ct);
        var recipe = await db.Recipes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == recipeId, ct)
            ?? throw new InvalidOperationException($"配方 ID={recipeId} 不存在");

        var dto = new
        {
            schema = "daq-monitor.recipe.v1",
            name = recipe.Name,
            description = recipe.Description,
            version = recipe.Version,
            parameters = JsonSerializer.Deserialize<List<RecipeParameter>>(recipe.ParametersJson, _jsonOpts)
        };
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });

        if (actor is not null)
            await _audit.LogAsync("recipe.export", actor.Id, actor.Username, target: recipe.Name, ct: ct);
        return json;
    }

    /// <summary>
    /// 从 JSON 导入配方(命名冲突时自动加后缀 "_imported")。
    /// </summary>
    public async Task<Recipe> ImportAsync(string json, CancellationToken ct = default)
    {
        var actor = _current.User ?? throw new InvalidOperationException("未登录用户不能导入配方");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var name = root.GetProperty("name").GetString() ?? "imported";
        var description = root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
        var parameters = root.TryGetProperty("parameters", out var p)
            ? p.Deserialize<List<RecipeParameter>>() ?? new()
            : new();

        using var db = await _dbf.CreateDbContextAsync(ct);
        // 命名冲突 → 加 _imported 后缀
        var finalName = name;
        while (await db.Recipes.AnyAsync(r => r.Name == finalName && !r.IsDeleted, ct))
            finalName = $"{name}_imported_{DateTime.UtcNow:HHmmss}";

        var recipe = new Recipe
        {
            Name = finalName,
            Description = description,
            Version = 1,
            ParametersJson = JsonSerializer.Serialize(parameters, _jsonOpts),
            CreatedByUserId = actor.Id,
            CreatedByUsername = actor.Username,
            CreatedAt = DateTime.UtcNow
        };
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("recipe.import", actor.Id, actor.Username,
            target: finalName, afterValue: $"{{\"params\":{parameters.Count}}}", ct: ct);
        return recipe;
    }

    /// <summary>列出某个配方的全部历史快照(按时间倒序)。</summary>
    public async Task<IReadOnlyList<RecipeSnapshot>> ListSnapshotsAsync(int recipeId, CancellationToken ct = default)
    {
        using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.RecipeSnapshots.AsNoTracking()
            .Where(s => s.RecipeId == recipeId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// 回滚到指定快照:把配方恢复到快照当时的状态(版本号 +1,表示"回滚后的新版本")。
    /// </summary>
    public async Task RollbackAsync(int snapshotId, CancellationToken ct = default)
    {
        var actor = _current.User ?? throw new InvalidOperationException("未登录用户不能回滚配方");

        using var db = await _dbf.CreateDbContextAsync(ct);
        var snap = await db.RecipeSnapshots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == snapshotId, ct)
            ?? throw new InvalidOperationException($"快照 ID={snapshotId} 不存在");
        var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == snap.RecipeId, ct)
            ?? throw new InvalidOperationException($"配方 ID={snap.RecipeId} 不存在(可能已被物理删除)");

        // 回滚前也要存当前状态快照(便于"撤销撤销")
        db.RecipeSnapshots.Add(new RecipeSnapshot
        {
            RecipeId = recipe.Id,
            Version = recipe.Version,
            Name = recipe.Name,
            ParametersJson = recipe.ParametersJson,
            SnapshotByUsername = actor.Username,
            CreatedAt = DateTime.UtcNow,
            Reason = $"before_rollback_to_v{snap.Version}"
        });

        var beforeParams = recipe.ParametersJson;
        recipe.Name = snap.Name;
        recipe.ParametersJson = snap.ParametersJson;
        recipe.Version += 1;
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            "recipe.rollback", actor.Id, actor.Username,
            target: recipe.Name, beforeValue: beforeParams, afterValue: snap.ParametersJson,
            detail: $"rollback to snapshot #{snapshotId} (v{snap.Version})", ct: ct);
    }
}
