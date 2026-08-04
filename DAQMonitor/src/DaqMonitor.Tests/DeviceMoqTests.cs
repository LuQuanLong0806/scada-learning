using DaqMonitor.Core.Acquisition;
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Models;
using Moq;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 用 Moq 造“虚拟设备”，验证统一采集管道能从任意 IDevice 收到数据。
/// 单元测试铁律：不碰真实串口/PLC/MQTT 等外部依赖，全部用 Mock 替身。
/// </summary>
public class DeviceMoqTests
{
    [Fact]
    public async Task Pipeline_ReceivesData_FromMockedDevice()
    {
        var device = new Mock<IDevice>();
        device.Setup(d => d.Id).Returns(1);
        device.Setup(d => d.Name).Returns("mock");

        using var pipeline = new AcquisitionPipeline(TimeSpan.FromMilliseconds(50));
        var done = new TaskCompletionSource();
        var received = new List<SensorPoint>();
        pipeline.BatchReady += (s, batch) =>
        {
            received.AddRange(batch);
            if (received.Count >= 1) done.TrySetResult();
        };

        pipeline.Register(device.Object);
        // 用 Moq 的 Raise 模拟“设备收到一帧数据并触发事件”
        device.Raise(d => d.DataReceived += null,
            new DataEventArgs { PointId = 7, Value = 42, Timestamp = DateTime.Now });

        await Task.WhenAny(done.Task, Task.Delay(2000));
        Assert.Single(received);
        Assert.Equal(7, received[0].Id);
        Assert.Equal(42, received[0].Value);
    }
}
