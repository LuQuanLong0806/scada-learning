using DaqMonitor.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;

namespace DaqMonitor.Core.Store;

/// <summary>
/// 点位存储：内存索引（列表 + 字典，UI / 报警实时用） + SQLite 持久化（历史查询用）。
///
/// 设计要点（M4 存储 → 历史库落地）：
///   - 旧 API（AddOrUpdate / Get / GetAll / GetAlarms）保持不变 —— 既有调用方（采集管道、
///     MainViewModel、CompositionSmokeTests 等）零改动。这一层只读内存索引，保证实时路径
///     永不阻塞在 IO 上。
///   - 每次 AddOrUpdate 同时把一条记录追加到 SQLite，采集线程不等待落盘（fire-and-forget
///     + 内部串行队列，避免并发写冲突）。SQLite 单写者特性决定了必须串行化写入。
///   - 新增 QueryHistoryAsync / QueryHistory 走 SQLite，支持“按点位 + 时间窗”历史回放。
///
/// 为什么写盘是异步串行而不是直接 await：
///   AddOrUpdate 是同步 API（采集管道在 BatchReady 里同步喂入），改成 Task 会污染整条链路；
///   用一个 Channel 把写任务串行化，既不阻塞采集，又满足 SQLite 的单写者约束。
/// </summary>
public class PointStore : IDisposable
{
    // ===== 内存索引（旧 API 用，保持原语义不变） =====
    private readonly List<SensorPoint> _points = new();
    private readonly Dictionary<int, SensorPoint> _byId = new();
    private readonly object _gate = new();

    // ===== SQLite 持久化 =====
    private readonly IDbContextFactory<AppDb> _dbFactory;
    private readonly bool _ownsFactory;
    // 串行化所有写库操作（SQLite 单写者）。Channel 比 lock + SemaphoreSlim 更稳：
    // 写任务不会堆积在 ThreadPool，且按 FIFO 顺序执行。
    private readonly System.Threading.Channels.Channel<SensorRecord> _writeQueue =
        System.Threading.Channels.Channel.CreateUnbounded<SensorRecord>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Task _writePump;
    private volatile bool _disposed;

    /// <summary>
    /// 兼容旧调用方的无参构造：内部建一个本地文件 SQLite 工厂。
    /// 既有测试（PointStoreTests / ModbusDeviceTests / SerialDeviceTests）都是 new PointStore()，
    /// 不需要改一行就能继续跑 —— 同时还免费拿到 SQLite 持久化能力。
    /// </summary>
    public PointStore()
        : this(CreateDefaultFactory(), ownsFactory: true)
    {
    }

    /// <summary>DI 构造：由 Bootstrapper 注入工厂（生产路径）。</summary>
    public PointStore(IDbContextFactory<AppDb> factory) : this(factory, ownsFactory: false) { }

    private PointStore(IDbContextFactory<AppDb> factory, bool ownsFactory)
    {
        _dbFactory = factory;
        _ownsFactory = ownsFactory;
        // 启动写库泵（单消费者，串行执行 SaveChanges，天然满足 SQLite 单写者）
        _writePump = Task.Run(PumpWritesAsync);
    }

    // ===================== 旧 API（不变） =====================

    /// <summary>更新或插入当前点位（内存索引同步刷新；SQLite 异步追加一行）。</summary>
    public void AddOrUpdate(SensorPoint p)
    {
        // 1) 内存索引同步更新（旧语义）
        lock (_gate)
        {
            _byId[p.Id] = p;
            var idx = _points.FindIndex(x => x.Id == p.Id);
            if (idx >= 0) _points[idx] = p;
            else _points.Add(p);
        }

        // 2) 异步落盘：仅追加（历史库要保留全部时序样本，不因“更新当前值”而丢历史）
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

    /// <summary>返回超阈值的点（实时报警直接复用，Day 6 / M6 用）。</summary>
    public IReadOnlyList<SensorPoint> GetAlarms(double threshold)
    {
        lock (_gate) return _points.Where(p => p.Value > threshold).ToList();
    }

    // ===================== 新 API：历史查询走 SQLite =====================

    /// <summary>按点位 + 时间窗查历史（含闭区间 [from, to]）。结果按时间升序返回。</summary>
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

    /// <summary>同步版本（少量遗留调用方或测试用）。</summary>
    public List<SensorPoint> QueryHistory(int pointId, DateTime from, DateTime to)
        => QueryHistoryAsync(pointId, from, to).GetAwaiter().GetResult();

    /// <summary>把队列里残留的写任务全部落盘（停机 / 测试结束前可调用，避免丢点）。</summary>
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
                    // 落盘失败不阻断采集 —— 实时路径已用内存索引服务。
                    // 真实工程可注入 ILogger 在此上报。
                }
            }
        }
        catch
        {
            // pump 异常不应冒泡到 finalizer
        }
    }

    /// <summary>默认工厂：系统临时目录下的 daq-{guid}.db（并行测试不互相串库）。</summary>
    private static IDbContextFactory<AppDb> CreateDefaultFactory()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"daq-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AppDb>()
            .UseSqlite($"Data Source={path}")
            .Options;

        // 轻量工厂：每次 CreateDbContext 返回新实例（SQLite 连接随 DbContext 释放而关闭）
        var factory = new InlineFactory<AppDb>(() => new AppDb(options));

        // 建库（同步，构造期一次性 —— 旧 API 是同步的，不能让 Get/GetAll 等测试因为库未建而失败）
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

    /// <summary>极简 IDbContextFactory 实现：用委托拼装（options 已闭包进委托里）。</summary>
    private sealed class InlineFactory<TCtx> : IDbContextFactory<TCtx> where TCtx : DbContext
    {
        private readonly Func<TCtx> _factory;
        public InlineFactory(Func<TCtx> factory) => _factory = factory;
        public TCtx CreateDbContext() => _factory();
    }
}
