using DaqMonitor.Core.Alarms;
using DaqMonitor.Core.Models;
using Xunit;

namespace DaqMonitor.Tests;

public class AlarmEngineTests
{
    [Fact]
    public void Evaluate_FiresOnlyOnRisingEdge()
    {
        var engine = new AlarmEngine();
        engine.Add(new AlarmRule { PointId = 3, Threshold = 100, Level = AlarmLevel.Critical, IsHigh = true });
        int count = 0;
        engine.AlarmTriggered += (s, e) => count++;

        engine.Evaluate(new SensorPoint { Id = 3, Value = 200 });
        engine.Evaluate(new SensorPoint { Id = 3, Value = 200 });   // 同点仍超阈值：不应重复报
        Assert.Equal(1, count);
    }

    [Fact]
    public void Evaluate_WithHysteresis_DoesNotChatter()
    {
        var engine = new AlarmEngine();
        engine.Add(new AlarmRule { PointId = 3, Threshold = 100, Hysteresis = 5, Level = AlarmLevel.Warning, IsHigh = true });
        int count = 0;
        engine.AlarmTriggered += (s, e) => count++;

        engine.Evaluate(new SensorPoint { Id = 3, Value = 120 });   // 越界，触发
        engine.Evaluate(new SensorPoint { Id = 3, Value = 102 });   // 仍在回滞带(95~105)内：不报
        engine.Evaluate(new SensorPoint { Id = 3, Value = 90 });    // 回到正常区：复位
        engine.Evaluate(new SensorPoint { Id = 3, Value = 120 });   // 再次越界：再报
        Assert.Equal(2, count);
    }
}
