using DaqMonitor.Core.Store;
using Microsoft.EntityFrameworkCore;

namespace DaqMonitor.Tests;

/// <summary>
/// 测试用 AppDb 工厂：本地文件 SQLite（每实例一个唯一文件，并行测试不互相串库）。
/// 用文件而不是 :memory: —— 因为 EF Core 默认每次 CreateDbContext 都会开新连接，
/// 而 SQLite 的 :memory: 库生命周期跟连接绑定，跨上下文会丢表。
/// Mode=Memory&Cache=Shared 也能解决，但对 EF Core 8 仍有版本差异，文件最稳妥。
///
/// 用法：
///   using var fixture = TestDb.Create();
///   var store = new PointStore(fixture);   // 走 DI 构造（TestDb 直接实现 IDbContextFactory）
///   ...
/// </summary>
public sealed class TestDb : IDisposable, IDbContextFactory<AppDb>
{
    /// <summary>测试用工厂（每实例对应一个唯一文件 SQLite）。</summary>
    public sealed class Factory : IDbContextFactory<AppDb>
    {
        private readonly string _path;
        public Factory(string path)
        {
            _path = path;
            // 一次性建库建表
            using var init = CreateDbContext();
            init.Database.EnsureCreated();
        }
        public AppDb CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDb>()
                .UseSqlite($"Data Source={_path}")
                .Options;
            return new AppDb(options);
        }
        public Task<AppDb> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    public Factory FactoryInstance { get; }

    private TestDb(string file, Factory factory)
    {
        _file = file;
        FactoryInstance = factory;
    }
    private readonly string _file;

    public static TestDb Create()
    {
        var file = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"daq-test-{Guid.NewGuid():N}.db");
        var factory = new Factory(file);
        return new TestDb(file, factory);
    }

    // 直接实现 IDbContextFactory<AppDb>，让 new PointStore(fixture) 不需要隐式转换
    // (C# 不允许到接口的隐式转换，所以用直接实现替代原 implicit operator)
    public AppDb CreateDbContext() => FactoryInstance.CreateDbContext();
    public Task<AppDb> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => FactoryInstance.CreateDbContextAsync(cancellationToken);

    public void Dispose()
    {
        try { if (System.IO.File.Exists(_file)) System.IO.File.Delete(_file); } catch { /* 忽略 */ }
    }
}
