using System.Text.Json;
using DaqMonitor.Core.Auth;
using DaqMonitor.Core.Recipes;
using DaqMonitor.Core.Store;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 配方服务测试:覆盖 M18 的核心契约。
///   ① 创建/激活互斥
///   ② 版本号自增 + 自动快照
///   ③ 导出/导入 roundtrip(数据不丢)
///   ④ 回滚(撤销修改)
///   ⑤ 命名冲突检测
/// </summary>
public class RecipeTests
{
    // 帮手:每个测试独立 SQLite 文件 + 已登录的 Engineer 用户
    private static (RecipeService recipes, AuditService audit, ICurrentUserService current, TestDb db) Build()
    {
        var db = TestDb.Create();
        var factory = db.FactoryInstance;
        var audit = new AuditService(factory);
        var current = new CurrentUserService();
        current.SetUser(new User { Id = 1, Username = "tester", Role = UserRole.Engineer, IsActive = true });
        var recipes = new RecipeService(factory, current, audit);
        return (recipes, audit, current, db);
    }

    private static List<RecipeParameter> SampleParams(double temp = 180) => new()
    {
        new() { Key = "温度", Value = temp.ToString(), Unit = "℃", Type = "float", Min = "150", Max = "220" },
        new() { Key = "压力", Value = "5.5", Unit = "MPa", Type = "float" },
        new() { Key = "速度", Value = "120", Unit = "mm/s", Type = "float" },
    };

    [Fact]
    public async Task CreateAsync_PersistsRecipe_WithParameters()
    {
        var (recipes, _, _, db) = Build();
        using var _ = db;

        var r = await recipes.CreateAsync("iPhone15", "标准配方", SampleParams());

        Assert.Equal("iPhone15", r.Name);
        Assert.Equal(1, r.Version);
        Assert.False(r.IsActive);
        // 参数序列化进 JSON 列
        var ps = JsonSerializer.Deserialize<List<RecipeParameter>>(r.ParametersJson)!;
        Assert.Equal(3, ps.Count);
        Assert.Equal("180", ps[0].Value);
    }

    [Fact]
    public async Task ActivateAsync_OnlyOneActiveAtAnyTime()
    {
        var (recipes, _, _, db) = Build();
        using var _ = db;

        var a = await recipes.CreateAsync("A", "", SampleParams());
        var b = await recipes.CreateAsync("B", "", SampleParams(temp: 200));

        await recipes.ActivateAsync(a.Id);
        Assert.True((await recipes.GetActiveAsync())?.Name == "A");

        await recipes.ActivateAsync(b.Id);  // 激活 B 应该自动取消 A
        var active = await recipes.GetActiveAsync();
        Assert.Equal("B", active?.Name);

        // 全局只能一个 active
        using var ctx = db.CreateDbContext();
        var activeCount = await ctx.Recipes.CountAsync(r => r.IsActive);
        Assert.Equal(1, activeCount);
    }

    [Fact]
    public async Task UpdateAsync_IncrementsVersion_And_WritesSnapshot()
    {
        var (recipes, _, _, db) = Build();
        using var _ = db;

        var r = await recipes.CreateAsync("v1-test", "", SampleParams(temp: 180));
        var updated = await recipes.UpdateAsync(r.Id, "v1-test", "改了温度", SampleParams(temp: 200));

        Assert.Equal(2, updated.Version);  // 版本号 +1

        var snaps = await recipes.ListSnapshotsAsync(r.Id);
        Assert.Single(snaps);  // 改前自动写了 1 个快照
        Assert.Equal("before_update", snaps[0].Reason);
        Assert.Contains("180", snaps[0].ParametersJson);   // 快照里是改前的值(180)
        Assert.DoesNotContain("200", snaps[0].ParametersJson);
    }

    [Fact]
    public async Task ExportImport_Roundtrip_PreservesParameters()
    {
        var (recipes, _, _, db) = Build();
        using var _ = db;

        var original = await recipes.CreateAsync("export-test", "原始描述", SampleParams());
        var json = await recipes.ExportAsync(original.Id);

        // 删掉原配方(模拟跨设备),再导入
        await recipes.DeleteAsync(original.Id);
        var imported = await recipes.ImportAsync(json);

        Assert.Equal("export-test", imported.Name);  // 名字保留
        Assert.Equal("原始描述", imported.Description);
        var ps = JsonSerializer.Deserialize<List<RecipeParameter>>(imported.ParametersJson)!;
        Assert.Equal(3, ps.Count);
        Assert.Equal("180", ps[0].Value);
    }

    [Fact]
    public async Task ImportAsync_DuplicateName_GetsImportedSuffix()
    {
        var (recipes, _, _, db) = Build();
        using var _ = db;

        var existing = await recipes.CreateAsync("dup-name", "", SampleParams());
        var json = await recipes.ExportAsync(existing.Id);

        var imported = await recipes.ImportAsync(json);
        Assert.NotEqual("dup-name", imported.Name);  // 不能撞名
        Assert.Contains("imported", imported.Name);
    }

    [Fact]
    public async Task RollbackAsync_RestoresPreviousParameters()
    {
        var (recipes, _, _, db) = Build();
        using var _ = db;

        var r = await recipes.CreateAsync("rollback-test", "", SampleParams(temp: 180));
        await recipes.UpdateAsync(r.Id, r.Name, "", SampleParams(temp: 999));  // 改成 999(假设是错误值)

        var snaps = await recipes.ListSnapshotsAsync(r.Id);
        Assert.Single(snaps);
        await recipes.RollbackAsync(snaps[0].Id);  // 回滚到改前(v1, 温度=180)

        using var ctx = db.CreateDbContext();
        var current = ctx.Recipes.First(x => x.Id == r.Id);
        Assert.Contains("180", current.ParametersJson);  // 参数回到 180
        Assert.DoesNotContain("999", current.ParametersJson);
        Assert.Equal(3, current.Version);  // 回滚也是一次修改,版本号继续 +1
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletes_PreservesAuditChain()
    {
        var (recipes, _, _, db) = Build();
        using var _ = db;

        var r = await recipes.CreateAsync("to-delete", "", SampleParams());
        await recipes.DeleteAsync(r.Id);

        // 默认 ListAsync 不返回软删的
        var visible = await recipes.ListAsync();
        Assert.DoesNotContain(visible, x => x.Id == r.Id);

        // includeDeleted=true 才能看到
        var withDeleted = await recipes.ListAsync(includeDeleted: true);
        Assert.Contains(withDeleted, x => x.Id == r.Id);
        Assert.True(withDeleted.First(x => x.Id == r.Id).IsDeleted);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_Throws()
    {
        var (recipes, _, _, db) = Build();
        using var _ = db;

        await recipes.CreateAsync("unique-name", "", SampleParams());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            recipes.CreateAsync("unique-name", "", SampleParams()));
    }
}
