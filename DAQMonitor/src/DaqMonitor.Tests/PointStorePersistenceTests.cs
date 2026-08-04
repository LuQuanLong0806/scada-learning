using System.Threading.Tasks;
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Store;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// PointStore 持久化路径测试：验证 AddOrUpdate 后，QueryHistoryAsync 能从 SQLite 查到历史样本。
/// 覆盖“双写（内存 + SQLite） + 时间窗查询”的核心契约。
/// </summary>
public class PointStorePersistenceTests
{
    [Fact]
    public async Task QueryHistoryAsync_Returns_PersistedSamples_InTimeOrder()
    {
        // Arrange：用 TestDb（每实例独立 SQLite 文件），并行测试不互相串库
        using var fixture = TestDb.Create();
        using var store = new PointStore(fixture);   // 走 DI 构造（注入工厂）

        var t0 = new DateTime(2026, 8, 4, 10, 0, 0, DateTimeKind.Utc);
        var windowStart = t0.AddSeconds(-1);
        var windowEnd = t0.AddMinutes(10);

        // 三条样本：1 条在窗外、2 条在窗内（乱序喂入，验证结果按时间升序）
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 1.0, Timestamp = t0 });
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 3.0, Timestamp = t0.AddSeconds(2) });
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 0.5, Timestamp = t0.AddSeconds(-30) }); // 窗外

        // 等异步写库泵把队列写完
        await store.FlushAsync();

        // Act
        var history = await store.QueryHistoryAsync(1, windowStart, windowEnd);

        // Assert：2 条窗内样本，按时间升序
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
        // 内存索引（实时） 与 SQLite（历史） 的值要一致 —— 这是双写模式的基本契约
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
