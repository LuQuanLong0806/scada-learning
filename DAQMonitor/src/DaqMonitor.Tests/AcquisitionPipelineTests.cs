using DaqMonitor.Core.Acquisition;
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Models;
using Xunit;

namespace DaqMonitor.Tests;

public class AcquisitionPipelineTests
{
    private sealed class FakeDevice : DeviceBase
    {
        public FakeDevice() : base(1, "fake") { }
        public override void Connect() { }
        public override void Disconnect() { }
        public override double Read(int addr) => 0;
        public override void Write(int addr, double value) { }
        public void Emit(int pointId, double value) => RaiseData(pointId, value);
    }

    [Fact]
    public async Task Pipeline_BatchesPoints_IntoBatchReady()
    {
        using var pipeline = new AcquisitionPipeline(TimeSpan.FromMilliseconds(50));
        var received = new List<SensorPoint>();
        var done = new TaskCompletionSource();

        pipeline.BatchReady += (s, batch) =>
        {
            received.AddRange(batch);
            if (received.Count >= 3) done.TrySetResult();
        };

        var dev = new FakeDevice();
        pipeline.Register(dev);
        dev.Emit(1, 10);
        dev.Emit(2, 20);
        dev.Emit(3, 30);

        await Task.WhenAny(done.Task, Task.Delay(2000));
        Assert.Equal(3, received.Count);
    }
}
