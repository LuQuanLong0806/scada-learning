# R6 · 持久化:内存 + SQLite 双写(数据不再怕重启)

> **定位**:车间断电/重启后,历史数据必须还在。这一篇做 **PointStore 双写**:内存索引管实时,SQLite 管历史,一条 `AddOrUpdate` 同时喂两头。
> **前置**:R5 全绿。**预计敲码**:90 分钟。
> **产出**:SensorRecord/AppDb/PointStore + TestDb,8 个新测试(累计 50)。

---

## 🎯 本篇交付物

```
src/DaqMonitor.Core/Store/
├─ SensorRecord.cs    # 持久化实体(struct SensorPoint 的 class 形态)
├─ AppDbContext.cs    # EF Core DbContext(R6 版只有 sensor_record 表;R9+ 加用户/审计/配方表)
└─ PointStore.cs      # 双写存储:内存索引(旧 API 不变)+ 异步串行落库 + 历史查询
src/DaqMonitor.Tests/
├─ TestDb.cs                      # 每实例独立 SQLite 文件的测试工厂
├─ PointStoreTests.cs             # 3 测试(内存路径)
├─ PointStorePersistenceTests.cs  # 3 测试(落库+时间窗查询)
├─ SerialDeviceTests.cs           # +1 穿管道测试(R4 埋的)
└─ ModbusDeviceTests.cs           # +1 穿管道测试
```

## 📋 需求单(先自己设计,再对照)

| # | 需求 | 验收 |
|---|---|---|
| FR6-1 | 持久化实体 SensorRecord:class、自增主键、(PointId, Time) 复合索引 | FromPoint/ToPoint 互转零丢失 |
| FR6-2 | DbContext(AppDb):sensor_record 表、State 存字符串、三个索引 | EnsureCreated 建库成功 |
| FR6-3 | PointStore 双写:**旧 API(AddOrUpdate/Get/GetAll/GetAlarms)语义不变**,既有调用方零改动;每次 AddOrUpdate 内存同步 + SQLite **异步追加**(不覆盖历史) | R5 的测试一行不改照样绿 |
| FR6-4 | 落库走 Channel 串行队列([双写](kp:dual-write)):采集线程不等盘;SQLite 单写者约束满足 | FlushAsync 后数据全在库 |
| FR6-5 | 落盘失败不丢内存数据、不打断采集(catch 静默,真实工程接日志) | 写库异常内存索引照常服务 |
| FR6-6 | 历史查询 QueryHistoryAsync(点位+时间窗,升序)走 SQLite | 乱序喂入按时间升序返回 |
| FR6-7 | 无参构造兼容:new PointStore() 内部自建临时库工厂——测试不用改 | PointStoreTests 3/3 绿 |
| FR6-8 | 穿管道证明:SerialDevice / ModbusDevice 经管道 BatchReady 写进 store | R4 埋的 2 个测试回填通过 |

**自己先想 15 分钟**:
1. 为什么不直接 `DbSet<SensorPoint>`?(struct 实体 EF Core 8 支持不顺,主键/变更跟踪都别扭——领域模型 ≠ 持久化模型)
2. AddOrUpdate 为什么不 await SaveChanges?(采集管道在 BatchReady 里同步调用,改 async 会污染整条链路;所以 fire-and-forget + 串行泵)
3. SQLite 为什么要串行写?(单写者数据库,并发写直接 locked 异常)
4. 测试库为什么用独立文件而不用 `:memory:`?(EF 每次 CreateDbContext 开新连接,:memory: 库随连接销毁,跨上下文丢表)

## 📚 本篇知识点

- [EF Core](kp:efcore) · [IDbContextFactory](kp:dbfactory) · [双写:内存+库](kp:dual-write) · [xUnit 单元测试](kp:unit-test)

## 🛠️ 参考实现

### ⓪ 装包(Core 和 Tests 都要)

```bash
dotnet add src/DaqMonitor.Core package Microsoft.EntityFrameworkCore.Sqlite --version 8.0.10
dotnet add src/DaqMonitor.Tests package Microsoft.EntityFrameworkCore.Sqlite --version 8.0.10
```
> 💡 断网车间友好:SQLite 单文件零安装。8.0.10 与参考工程对齐(8.0.x LTS 内任意补丁版都能跑)。

### ① SensorRecord —— 持久化实体

> 📂 `src/DaqMonitor.Core/Store/SensorRecord.cs` · namespace `DaqMonitor.Core.Store`
> 🔧 依赖 EF Sqlite(⓪ 已装)
> 💡 "领域模型 ↔ 持久化模型"分离:内存里跑 struct,库里存 class,互转只在 Store 边界

```csharp
using DaqMonitor.Core.Models;

namespace DaqMonitor.Core.Store;

/// <summary>
/// SensorPoint 的持久化形态(EF Core 实体)。
/// SensorPoint 是 struct(高频采集场景下避免装箱开销),EF Core 对 struct 实体不友好:
///   - 无法方便地配置主键/索引;
///   - 跟踪变更语义不清晰。
/// 因此落地到 SQLite 时转成这个 class(经典"领域模型 ↔ 持久化模型"分离)。
///
/// 设计取舍:
///   - 自增主键 Id:避免把业务字段硬塞成主键,写入无锁竞争更小;
///   - 业务字段 PointId + Time 上加复合索引:历史查询"按点位 + 时间窗"最频繁;
///   - Time 用 UTC(SQLite 存字符串排序也正确)。
/// </summary>
public class SensorRecord
{
    /// <summary>自增主键(持久化用,与业务 PointId 不是一回事)。</summary>
    public int Id { get; set; }

    /// <summary>点位 ID(业务键,来自 SensorPoint.Id)。</summary>
    public int PointId { get; set; }

    /// <summary>采样值。</summary>
    public double Value { get; set; }

    /// <summary>设备状态。</summary>
    public DeviceState State { get; set; }

    /// <summary>采样时间戳(来自 SensorPoint.Timestamp)。</summary>
    public DateTime Time { get; set; }

    /// <summary>从领域 struct 转换为持久化实体(纯映射,零副作用)。</summary>
    public static SensorRecord FromPoint(in SensorPoint p) => new()
    {
        PointId = p.Id,
        Value = p.Value,
        State = p.State,
        Time = p.Timestamp
    };

    /// <summary>从持久化实体还原为领域 struct。</summary>
    public SensorPoint ToPoint() => new()
    {
        Id = PointId,
        Value = Value,
        State = State,
        Timestamp = Time
    };
}
```

> ⚠️ 又是时间戳:FromPoint/ToPoint 双向都抄 Timestamp/Time——R1 的铁律第三次出现。

### ② AppDb —— DbContext(R6 版)

> 📂 `src/DaqMonitor.Core/Store/AppDbContext.cs` · 类名是 `AppDb`
> 💡 参考工程的同名文件里还有用户/审计/配方表(R9+ 的内容),R6 版先只留 sensor_record——**R9+ 做到那篇时再往 OnModelCreating 里加表**,种子代码结构不变

```csharp
using DaqMonitor.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DaqMonitor.Core.Store;

/// <summary>
/// EF Core DbContext:把 SensorPoint 历史样本落到 SQLite。
///
/// 为什么不直接 DbSet<SensorPoint>:
///   SensorPoint 是 struct,EF Core 8 对 owned struct / keyless struct 实体支持仍不顺畅,
///   配置索引与变更跟踪也麻烦。改用一个等价的 class SensorRecord 作持久化模型,
///   领域层(采集/报警/UI)继续用 struct,互转只在 Store 边界发生——仓储模式的好处。
///
/// 索引策略:
///   - (PointId, Time) 复合索引:覆盖"按点位 + 时间窗查历史"的主查询路径;
///   - PointId 单列索引:覆盖"按点位统计最新值/聚合"的次要路径;
///   - Time 单列索引:覆盖"全点位按时间窗扫描"。
/// </summary>
public class AppDb : DbContext
{
    public AppDb(DbContextOptions<AppDb> options) : base(options) { }

    /// <summary>历史样本表(按时间追加,几乎不更新)。</summary>
    public DbSet<SensorRecord> Records => Set<SensorRecord>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        var e = mb.Entity<SensorRecord>();
        e.ToTable("sensor_record");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedOnAdd();

        e.Property(x => x.PointId).HasColumnName("point_id").IsRequired();
        e.Property(x => x.Value).HasColumnName("value").IsRequired();
        e.Property(x => x.State).HasColumnName("state").HasConversion<string>().IsRequired();
        e.Property(x => x.Time).HasColumnName("time").IsRequired();

        // 主查询路径:按点位 + 时间窗。SQLite 的 ASC 索引同时支持 ASC / DESC 查询。
        e.HasIndex(x => new { x.PointId, x.Time }).HasDatabaseName("ix_record_point_time");
        e.HasIndex(x => x.PointId).HasDatabaseName("ix_record_point");
        e.HasIndex(x => x.Time).HasDatabaseName("ix_record_time");
    }
}
```

### ③ PointStore —— 双写存储(本篇主角)

> 📂 `src/DaqMonitor.Core/Store/PointStore.cs`
> 💡 三层人格:**旧 API 纯内存(实时永不阻塞)** + **Channel 串行写泵(满足 SQLite 单写者)** + **历史查询走库**

```csharp
using DaqMonitor.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;

namespace DaqMonitor.Core.Store;

/// <summary>
/// 点位存储:内存索引(列表 + 字典,UI / 报警实时用)+ SQLite 持久化(历史查询用)。
///
/// 设计要点:
///   - 旧 API(AddOrUpdate / Get / GetAll / GetAlarms)保持不变——既有调用方零改动。
///     这一层只读内存索引,保证实时路径永不阻塞在 IO 上。
///   - 每次 AddOrUpdate 同时把一条记录追加到 SQLite,采集线程不等待落盘(fire-and-forget
///     + 内部串行队列,避免并发写冲突)。SQLite 单写者特性决定了必须串行化写入。
///   - QueryHistoryAsync / QueryHistory 走 SQLite,支持"按点位 + 时间窗"历史回放。
///
/// 为什么写盘是异步串行而不是直接 await:
///   AddOrUpdate 是同步 API(采集管道在 BatchReady 里同步喂入),改成 Task 会污染整条链路;
///   用一个 Channel 把写任务串行化,既不阻塞采集,又满足 SQLite 的单写者约束。
/// </summary>
public class PointStore : IDisposable
{
    // ===== 内存索引(旧 API 用,保持原语义不变) =====
    private readonly List<SensorPoint> _points = new();
    private readonly Dictionary<int, SensorPoint> _byId = new();
    private readonly object _gate = new();

    // ===== SQLite 持久化 =====
    private readonly IDbContextFactory<AppDb> _dbFactory;
    private readonly bool _ownsFactory;
    // 串行化所有写库操作(SQLite 单写者)。Channel 比 lock + SemaphoreSlim 更稳:
    // 写任务不会堆积在 ThreadPool,且按 FIFO 顺序执行。
    private readonly System.Threading.Channels.Channel<SensorRecord> _writeQueue =
        System.Threading.Channels.Channel.CreateUnbounded<SensorRecord>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Task _writePump;
    private volatile bool _disposed;

    /// <summary>
    /// 兼容旧调用方的无参构造:内部建一个本地文件 SQLite 工厂。
    /// 既有测试都是 new PointStore(),不需要改一行就能继续跑——同时还免费拿到 SQLite 持久化能力。
    /// </summary>
    public PointStore()
        : this(CreateDefaultFactory(), ownsFactory: true)
    {
    }

    /// <summary>DI 构造:由 Bootstrapper 注入工厂(生产路径)。</summary>
    public PointStore(IDbContextFactory<AppDb> factory) : this(factory, ownsFactory: false) { }

    private PointStore(IDbContextFactory<AppDb> factory, bool ownsFactory)
    {
        _dbFactory = factory;
        _ownsFactory = ownsFactory;
        // 启动写库泵(单消费者,串行执行 SaveChanges,天然满足 SQLite 单写者)
        _writePump = Task.Run(PumpWritesAsync);
    }

    // ===================== 旧 API(不变) =====================

    /// <summary>更新或插入当前点位(内存索引同步刷新;SQLite 异步追加一行)。</summary>
    public void AddOrUpdate(SensorPoint p)
    {
        // 1) 内存索引同步更新(旧语义)
        lock (_gate)
        {
            _byId[p.Id] = p;
            var idx = _points.FindIndex(x => x.Id == p.Id);
            if (idx >= 0) _points[idx] = p;
            else _points.Add(p);
        }

        // 2) 异步落盘:仅追加(历史库要保留全部时序样本,不因"更新当前值"而丢历史)
        if (!_disposed)
        {
            _writeQueue.Writer.TryWrite(SensorRecord.FromPoint(p));
        }
    }

    public SensorPoint? Get(int id)
    {
        lock (_gate) return _byId.TryGetValue(id, out var p) ? p : null;
    }

    public IReadOnlyList<SensorPoint> GetAll()
    {
        lock (_gate) return _points.ToList();
    }

    /// <summary>返回超阈值的点(实时报警直接复用)。</summary>
    public IReadOnlyList<SensorPoint> GetAlarms(double threshold)
    {
        lock (_gate) return _points.Where(p => p.Value > threshold).ToList();
    }

    // ===================== 新 API:历史查询走 SQLite =====================

    /// <summary>按点位 + 时间窗查历史(含闭区间 [from, to])。结果按时间升序返回。</summary>
    public async Task<List<SensorPoint>> QueryHistoryAsync(
        int pointId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Records.AsNoTracking()
            .Where(r => r.PointId == pointId && r.Time >= from && r.Time <= to)
            .OrderBy(r => r.Time)
            .ToListAsync(ct);
        return rows.ConvertAll(r => r.ToPoint());
    }

    /// <summary>同步版本(少量遗留调用方或测试用)。</summary>
    public List<SensorPoint> QueryHistory(int pointId, DateTime from, DateTime to)
        => QueryHistoryAsync(pointId, from, to).GetAwaiter().GetResult();

    /// <summary>把队列里残留的写任务全部落盘(停机 / 测试结束前可调用,避免丢点)。</summary>
    public Task FlushAsync(TimeSpan? timeout = null)
    {
        // 让 writer 完成 → pump 把剩余记录写完 → 等 pump 退出
        _writeQueue.Writer.TryComplete();
        return _writePump.WaitAsync(timeout ?? TimeSpan.FromSeconds(5));
    }

    // ===================== 内部实现 =====================

    private async Task PumpWritesAsync()
    {
        var reader = _writeQueue.Reader;
        try
        {
            await foreach (var rec in reader.ReadAllAsync())
            {
                if (_disposed) break;
                try
                {
                    await using var db = await _dbFactory.CreateDbContextAsync();
                    db.Records.Add(rec);
                    await db.SaveChangesAsync();
                }
                catch
                {
                    // 落盘失败不阻断采集——实时路径已用内存索引服务。
                    // 真实工程可注入 ILogger 在此上报。
                }
            }
        }
        catch
        {
            // pump 异常不应冒泡到 finalizer
        }
    }

    /// <summary>默认工厂:系统临时目录下的 daq-{guid}.db(并行测试不互相串库)。</summary>
    private static IDbContextFactory<AppDb> CreateDefaultFactory()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"daq-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AppDb>()
            .UseSqlite($"Data Source={path}")
            .Options;

        // 轻量工厂:每次 CreateDbContext 返回新实例(SQLite 连接随 DbContext 释放而关闭)
        var factory = new InlineFactory<AppDb>(() => new AppDb(options));

        // 建库(同步,构造期一次性——旧 API 是同步的,不能让 Get/GetAll 等测试因为库未建而失败)
        using (var init = factory.CreateDbContext())
        {
            init.Database.EnsureCreated();
        }
        return factory;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // 通知 pump 收尾
        _writeQueue.Writer.TryComplete();
        try { _writePump.Wait(TimeSpan.FromSeconds(2)); } catch { /* 忽略超时 */ }
        if (_ownsFactory && _dbFactory is IDisposable d) d.Dispose();
    }

    /// <summary>极简 IDbContextFactory 实现:用委托拼装(options 已闭包进委托里)。</summary>
    private sealed class InlineFactory<TCtx> : IDbContextFactory<TCtx> where TCtx : DbContext
    {
        private readonly Func<TCtx> _factory;
        public InlineFactory(Func<TCtx> factory) => _factory = factory;
        public TCtx CreateDbContext() => _factory();
    }
}
```

### ④ TestDb —— 测试库工厂

> 📂 `src/DaqMonitor.Tests/TestDb.cs`
> 💡 用独立文件不用 `:memory:`:EF 每次 CreateDbContext 开新连接,:memory: 库随连接销毁,跨上下文丢表

```csharp
using DaqMonitor.Core.Store;
using Microsoft.EntityFrameworkCore;

namespace DaqMonitor.Tests;

/// <summary>
/// 测试用 AppDb 工厂:本地文件 SQLite(每实例一个唯一文件,并行测试不互相串库)。
///
/// 用法:
///   using var fixture = TestDb.Create();
///   using var store = new PointStore(fixture);   // 走 DI 构造(TestDb 直接实现 IDbContextFactory)
/// </summary>
public sealed class TestDb : IDisposable, IDbContextFactory<AppDb>
{
    /// <summary>测试用工厂(每实例对应一个唯一文件 SQLite)。</summary>
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

    // 直接实现 IDbContextFactory<AppDb>,让 new PointStore(fixture) 不需要隐式转换
    public AppDb CreateDbContext() => FactoryInstance.CreateDbContext();
    public Task<AppDb> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => FactoryInstance.CreateDbContextAsync(cancellationToken);

    public void Dispose()
    {
        try { if (System.IO.File.Exists(_file)) System.IO.File.Delete(_file); } catch { /* 忽略 */ }
    }
}
```

### ⑤ 测试(6 个新文件测试 + 2 个回填)

> 📂 `src/DaqMonitor.Tests/PointStoreTests.cs`(无参构造,纯内存语义)

```csharp
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Store;
using Xunit;

namespace DaqMonitor.Tests;

public class PointStoreTests
{
    [Fact]
    public void AddOrUpdate_InsertsThenUpdatesSameId()
    {
        var store = new PointStore();
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 10 });
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 99 });

        Assert.Single(store.GetAll());
        Assert.Equal(99d, store.GetAll().First().Value);
    }

    [Fact]
    public void Get_ReturnsNullForUnknownId()
    {
        var store = new PointStore();
        Assert.Null(store.Get(999));
    }

    [Fact]
    public void GetAlarms_ReturnsOnlyAboveThreshold()
    {
        var store = new PointStore();
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 10 });
        store.AddOrUpdate(new SensorPoint { Id = 2, Value = 200 });

        var alarms = store.GetAlarms(100);
        Assert.Single(alarms);
        Assert.Equal(2, alarms[0].Id);
    }
}
```

> 📂 `src/DaqMonitor.Tests/PointStorePersistenceTests.cs`(注入 TestDb,验持久化)

```csharp
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Store;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// PointStore 持久化路径测试:验证 AddOrUpdate 后,QueryHistoryAsync 能从 SQLite 查到历史样本。
/// 覆盖"双写(内存 + SQLite)+ 时间窗查询"的核心契约。
/// </summary>
public class PointStorePersistenceTests
{
    [Fact]
    public async Task QueryHistoryAsync_Returns_PersistedSamples_InTimeOrder()
    {
        // Arrange:用 TestDb(每实例独立 SQLite 文件),并行测试不互相串库
        using var fixture = TestDb.Create();
        using var store = new PointStore(fixture);   // 走 DI 构造(注入工厂)

        var t0 = new DateTime(2026, 8, 4, 10, 0, 0, DateTimeKind.Utc);
        var windowStart = t0.AddSeconds(-1);
        var windowEnd = t0.AddMinutes(10);

        // 三条样本:1 条在窗外、2 条在窗内(乱序喂入,验证结果按时间升序)
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 1.0, Timestamp = t0 });
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 3.0, Timestamp = t0.AddSeconds(2) });
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 0.5, Timestamp = t0.AddSeconds(-30) }); // 窗外

        // 等异步写库泵把队列写完
        await store.FlushAsync();

        // Act
        var history = await store.QueryHistoryAsync(1, windowStart, windowEnd);

        // Assert:2 条窗内样本,按时间升序
        Assert.Equal(2, history.Count);
        Assert.Equal(1.0d, history[0].Value);
        Assert.Equal(3.0d, history[1].Value);
    }

    [Fact]
    public async Task QueryHistoryAsync_FiltersByPointId()
    {
        using var fixture = TestDb.Create();
        using var store = new PointStore(fixture);

        var t = DateTime.UtcNow;
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 10, Timestamp = t });
        store.AddOrUpdate(new SensorPoint { Id = 2, Value = 20, Timestamp = t });
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 30, Timestamp = t.AddSeconds(1) });
        await store.FlushAsync();

        var onlyPoint1 = await store.QueryHistoryAsync(1, t.AddSeconds(-1), t.AddSeconds(10));
        Assert.Equal(2, onlyPoint1.Count);
        Assert.All(onlyPoint1, p => Assert.Equal(1, p.Id));
    }

    [Fact]
    public async Task Memory_And_Sqlite_StayConsistent_AfterAddOrUpdate()
    {
        // 内存索引(实时)与 SQLite(历史)的值要一致——这是双写模式的基本契约
        using var fixture = TestDb.Create();
        using var store = new PointStore(fixture);

        var t = DateTime.UtcNow;
        store.AddOrUpdate(new SensorPoint { Id = 5, Value = 42.5, Timestamp = t });
        await store.FlushAsync();

        var memCurrent = store.Get(5);
        Assert.NotNull(memCurrent);
        Assert.Equal(42.5d, memCurrent!.Value.Value);

        var hist = await store.QueryHistoryAsync(5, t.AddSeconds(-1), t.AddSeconds(1));
        Assert.Single(hist);
        Assert.Equal(42.5d, hist[0].Value);
    }
}
```

> 📂 `SerialDeviceTests.cs` — **文件末尾追加** R4 埋的穿管道测试(文件头补 `using DaqMonitor.Core.Acquisition; using DaqMonitor.Core.Models; using DaqMonitor.Core.Store;`)
> 📂 `ModbusDeviceTests.cs` — **同样追加**(using 同步补)

```csharp
    [Fact]
    public async Task SerialDevice_ThroughPipeline_ProducesPoints_InStore()
    {
        // 最强证明:SerialDevice 直接挂到统一采集管道 + 存储,整条链路不碰 UI、不碰真实硬件
        var ch = new LoopbackSerialChannel();
        var dev = new SerialDevice(1, "SER", ch);
        using var pipeline = new AcquisitionPipeline(TimeSpan.FromMilliseconds(50));
        var store = new PointStore();

        pipeline.Register(dev);
        dev.Connect();
        ch.Write(FrameParser.Build(1, 123.5));

        var done = new TaskCompletionSource<bool>();
        pipeline.BatchReady += (_, b) =>
        {
            foreach (var p in b) store.AddOrUpdate(p);
            if (b.Count > 0) done.TrySetResult(true);
        };

        await Task.WhenAny(done.Task, Task.Delay(2000));
        dev.Disconnect();

        Assert.True(store.GetAll().Any(p => p.Id == 1 && Math.Abs(p.Value - 123.5) < 1e-6),
            "SerialDevice 经管道写入存储失败——'换设备 UI 零改动'未成立");
    }
```

```csharp
    [Fact]
    public async Task ModbusDevice_ThroughPipeline_ProducesPoints_InStore()
    {
        var dev = new ModbusDevice(1, "MB", slave: 1,
            new[] { new ModbusDevice.RegisterMap(1, 0, "float") }, simulate: true);
        using var pipeline = new AcquisitionPipeline(TimeSpan.FromMilliseconds(50));
        var store = new PointStore();

        pipeline.Register(dev);
        dev.Connect();

        var done = new TaskCompletionSource<bool>();
        pipeline.BatchReady += (_, b) =>
        {
            foreach (var p in b) store.AddOrUpdate(p);
            if (b.Count > 0) done.TrySetResult(true);
        };

        await Task.WhenAny(done.Task, Task.Delay(2000));
        dev.Disconnect();

        Assert.True(store.GetAll().Any(p => p.Id == 1), "ModbusDevice 经管道写入存储失败——'换设备 UI 零改动'未成立");
    }
```

## ✅ 验证(必做)

```bash
dotnet build
dotnet test
```
**期望输出(关键行)**:
```
已成功生成。 → 0 个警告 0 个错误
已通过! - 失败: 0,通过: 50 ... DaqMonitor.Tests.dll
```
(50 = 之前 42 + 本篇 8)

## ✅ 验收清单

- [ ] build 0 错 0 警,test 50/50 绿
- [ ] 能回答:为什么 SensorPoint 是 struct 而 SensorRecord 是 class?各自服务谁?(采集高频/GC;EF 持久化)
- [ ] 能回答:AddOrUpdate 里"内存同步、SQLite 异步",为什么这么分?(实时路径不能等盘;历史允许秒级延迟)
- [ ] 能回答:为什么 AddOrUpdate 对 SQLite 是"追加"不是"更新"?(历史库保留全部时序样本,当前值更新只发生在内存索引)
- [ ] 打开 %TEMP% 目录能看到 daq-*.db 文件——用 `sqlite3` 打开看 sensor_record 表(可选,装了 sqlite 工具的话)
- [ ] git commit -m "R6: 持久化双写 EF Core+SQLite+8测试"

## 🎤 面试怎么讲这一篇

> "存储层是双写:PointStore 的实时 API 只碰内存索引,UI 和报警永不阻塞在 IO;同时每条样本进 Channel 串行队列,由后台泵逐条落 SQLite——串行是关键,SQLite 是单写者数据库,并发写会锁冲突。领域模型 SensorPoint 是 struct,持久化模型 SensorRecord 是 class,EF Core 对 struct 实体支持不好,这层转换放在仓储边界。查询按点位加时间窗走 (PointId,Time) 复合索引。落库失败静默降级到只有内存,不让磁盘问题打断采集。测试用独立临时文件库,不用 :memory:,因为 EF 每次开新连接而内存库随连接销毁。"

**✅ 打卡[ ]**
