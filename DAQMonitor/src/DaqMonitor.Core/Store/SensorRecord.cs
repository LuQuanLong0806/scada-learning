using DaqMonitor.Core.Models;

namespace DaqMonitor.Core.Store;

/// <summary>
/// SensorPoint 的持久化形态（EF Core 实体）。
/// SensorPoint 是 struct（高频采集场景下避免装箱开销），EF Core 对 struct 实体不友好：
///   - 无法方便地配置主键/索引；
///   - 跟踪变更语义不清晰。
/// 因此落地到 SQLite 时转成这个 class（经典“领域模型 ↔ 持久化模型”分离）。
///
/// 设计取舍：
///   - 自增主键 Id：避免把业务字段硬塞成主键，写入无锁竞争更小；
///   - 业务字段 PointId + Time 上加复合索引：历史查询“按点位 + 时间窗”最频繁；
///   - Time 用 UTC（SQLite 存字符串排序也正确）。
/// </summary>
public class SensorRecord
{
    /// <summary>自增主键（持久化用，与业务 PointId 不是一回事）。</summary>
    public int Id { get; set; }

    /// <summary>点位 ID（业务键，来自 SensorPoint.Id）。</summary>
    public int PointId { get; set; }

    /// <summary>采样值。</summary>
    public double Value { get; set; }

    /// <summary>设备状态。</summary>
    public DeviceState State { get; set; }

    /// <summary>采样时间戳（来自 SensorPoint.Timestamp）。</summary>
    public DateTime Time { get; set; }

    /// <summary>从领域 struct 转换为持久化实体（纯映射，零副作用）。</summary>
    public static SensorRecord FromPoint(in SensorPoint p) => new()
    {
        PointId = p.Id,
        Value = p.Value,
        State = p.State,
        Time = p.Timestamp
    };

    /// <summary>从持久化实体还原为领域 struct。</summary>
    public SensorPoint ToPoint() => new()
    {
        Id = PointId,
        Value = Value,
        State = State,
        Timestamp = Time
    };
}
