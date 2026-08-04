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
