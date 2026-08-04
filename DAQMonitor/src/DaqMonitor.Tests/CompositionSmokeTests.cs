using DaqMonitor.Core.Acquisition;
using DaqMonitor.Core.AppServices;
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 无界面集成冒烟测试：证明“企业级项目能跑起来”不靠肉眼看窗口。
/// 直接用组合根 Bootstrapper 装配整套服务，跑真实采集链路，断言有数据产出。
/// </summary>
public class CompositionSmokeTests
{
    [Fact]
    public async Task Bootstrapper_Wires_Device_Pipeline_Store_And_Produces_Points()
    {
        using var provider = Bootstrapper.Build();
        var device = provider.GetRequiredService<IDevice>();
        var pipeline = provider.GetRequiredService<AcquisitionPipeline>();
        var store = provider.GetRequiredService<PointStore>();

        pipeline.Register(device);
        device.Connect();

        // 测试在这里扮演“消费者”角色（真实项目里是 ViewModel/历史库订阅 BatchReady）
        var gotBatch = new TaskCompletionSource<bool>();
        var received = new List<SensorPoint>();
        var gate = new object();
        pipeline.BatchReady += (_, batch) =>
        {
            lock (gate)
            {
                foreach (var p in batch) { store.AddOrUpdate(p); received.Add(p); }
                if (batch.Count > 0) gotBatch.TrySetResult(true);
            }
        };

        // 启动模拟设备（真实设备同理，只是数据来源不同）
        ((SimulatedDevice)device).Start(TimeSpan.FromMilliseconds(20));

        // 等最多 3 秒，必须收到至少一个批次
        var completed = await Task.WhenAny(gotBatch.Task, Task.Delay(3000));
        ((SimulatedDevice)device).Stop();

        Assert.True(completed == gotBatch.Task, "管道在 3 秒内未产出任何批次——采集链路未跑通");
        Assert.NotEmpty(received);
        Assert.NotEmpty(store.GetAll());
        Assert.All(received, p => Assert.True(p.Timestamp > DateTime.MinValue));
    }
}
