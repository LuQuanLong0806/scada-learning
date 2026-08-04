using DaqMonitor.Core.Models;

namespace DaqMonitor.Core.Devices;

/// <summary>数据到达事件参数（Day 6 事件机制落地）</summary>
public class DataEventArgs : EventArgs
{
    public int PointId { get; init; }
    public double Value { get; init; }
    /// <summary>采样时间戳，由采集源统一打，下游共用。</summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;
}

/// <summary>
/// 设备统一接口：UI / 采集层只认它，不关心底层是串口 / 网口 / PLC。
/// 这是面向接口编程的核心，也是 M3 / M4 多设备可插拔架构的地基（Day 5 项目任务）。
/// </summary>
public interface IDevice
{
    int Id { get; }
    string Name { get; }
    DeviceState State { get; }

    void Connect();
    void Disconnect();
    double Read(int addr);
    void Write(int addr, double value);

    /// <summary>采集层拿到数据后单向通知订阅方（UI 刷新用）</summary>
    event EventHandler<DataEventArgs>? DataReceived;
}

/// <summary>
/// 设备基类：复用通用状态与事件触发逻辑。
/// 串口 / 网口 / PLC 设备都继承它（M1 / M3 / M4 落地具体实现）。
/// </summary>
public abstract class DeviceBase : IDevice
{
    public int Id { get; }
    public string Name { get; }
    public DeviceState State { get; protected set; } = DeviceState.Offline;

    public event EventHandler<DataEventArgs>? DataReceived;

    protected DeviceBase(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public abstract void Connect();
    public abstract void Disconnect();
    public abstract double Read(int addr);
    public abstract void Write(int addr, double value);

    /// <summary>采集线程拿到数据后调用，单向推给订阅方（UI），实现层与展示层解耦</summary>
    protected void RaiseData(int pointId, double value)
        => DataReceived?.Invoke(this, new DataEventArgs { PointId = pointId, Value = value, Timestamp = DateTime.Now });
}
