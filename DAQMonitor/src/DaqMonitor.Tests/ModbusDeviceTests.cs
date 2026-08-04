using DaqMonitor.Core.Acquisition;
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Protocol;
using DaqMonitor.Core.Store;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 验证 M2 的 ModbusDevice 真落工程、且"换设备 UI 零改动"成立。
/// 用 simulate 模式（零硬件）跑通：设备产生数据 → 经统一采集管道 → 写入存储。
/// </summary>
public class ModbusDeviceTests
{
    [Fact]
    public void SimulateMode_RaisesData_ForMappedPoints()
    {
        var dev = new ModbusDevice(1, "MB", slave: 1,
            new[] { new ModbusDevice.RegisterMap(1, 0, "float"), new ModbusDevice.RegisterMap(2, 1, "word") },
            simulate: true);
        var got = new System.Collections.Generic.List<int>();
        dev.DataReceived += (_, e) => got.Add(e.PointId);

        dev.Connect();
        System.Threading.Thread.Sleep(700);     // 等至少一个轮询周期(500ms)
        dev.Disconnect();

        Assert.Contains(1, got);
        Assert.Contains(2, got);
    }

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

    [Fact]
    public void PlcDevice_SimulateMode_RaisesData()
    {
        var dev = new PlcDevice(2, "PLC", new[] { new PlcDevice.PlcMap(3, "DB1.DBW0") }, simulate: true);
        var got = new System.Collections.Generic.List<int>();
        dev.DataReceived += (_, e) => got.Add(e.PointId);

        dev.Connect();
        System.Threading.Thread.Sleep(700);
        dev.Disconnect();

        Assert.Contains(3, got);
    }
}
