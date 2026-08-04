namespace DaqMonitor.Core.Models;

/// <summary>设备状态（工业设备生命周期，对标 enum）</summary>
public enum DeviceState { Offline, Connecting, Online }

/// <summary>报警级别</summary>
public enum AlarmLevel { Normal, Warning, Critical }

/// <summary>
/// 一个传感器读数点位。值类型 struct，适合高频小数据（Day 2 练习落地）。
/// </summary>
public struct SensorPoint
{
    public int Id;
    public double Value;
    public DeviceState State;
    /// <summary>采样时间戳。统一由采集源打，下游(曲线/历史库/上云)共用，避免各自取时间不一致。</summary>
    public DateTime Timestamp;
}

/// <summary>报警记录（Day 2 练习落地）</summary>
public struct Alarm
{
    public int PointId;
    public AlarmLevel Level;
    public double Value;
}
