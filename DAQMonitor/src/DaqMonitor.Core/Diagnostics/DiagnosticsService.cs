using System.Collections.ObjectModel;
using System.ComponentModel;

namespace DaqMonitor.Core.Diagnostics;

/// <summary>
/// 诊断 / 调试服务（放在 Core，与 UI 无关，纯逻辑）。
/// 工业现场 80% 的时间是“排查为什么没数据 / 数据不对 / 连不上”，
/// 所以这个服务把采集链路的**统计指标**和**结构化日志**集中起来，
/// 既能在界面上展示（DiagnosticsPanel），也能作为排查的第一手证据。
///
/// 设计要点：
/// ① 全部用 lock 保护计数，线程安全（后台采集线程 / UI 线程都会调）；
/// ② 日志用环形缓冲（最多 200 条），避免内存无限增长；
/// ③ 暴露 ReadOnlyObservableCollection，UI 直接绑、跨线程安全。
/// </summary>
public class DiagnosticsService
{
    private readonly object _gate = new();
    private int _totalSamples;
    private int _alarmCount;
    private int _batchCount;
    private long _lastBatchMs;
    private readonly DateTime _startTime = DateTime.Now;
    private readonly ObservableCollection<string> _log = new();
    private const int MaxLog = 200;

    /// <summary>累计采样点数（每批累加）。</summary>
    public int TotalSamples => _totalSamples;
    /// <summary>累计报警触发次数（上升沿计）。</summary>
    public int AlarmCount => _alarmCount;
    /// <summary>累计批量次数。</summary>
    public int BatchCount => _batchCount;
    /// <summary>最近一批的处理耗时（毫秒），排查“卡顿/丢点”看它。</summary>
    public long LastBatchMs => _lastBatchMs;
    /// <summary>已运行时长。</summary>
    public TimeSpan Uptime => DateTime.Now - _startTime;
    /// <summary>对外只读日志视图，UI 直接绑。</summary>
    public ReadOnlyObservableCollection<string> Log { get; }

    public DiagnosticsService() => Log = new ReadOnlyObservableCollection<string>(_log);

    /// <summary>记录一次批量采集：累加点数/批次数，并写一条 INFO 日志。</summary>
    public void RecordBatch(int sampleCount, long elapsedMs)
    {
        lock (_gate)
        {
            _totalSamples += sampleCount;
            _batchCount++;
            _lastBatchMs = elapsedMs;
        }
        Append("INFO", $"批量 #{_batchCount}: {sampleCount} 点, 耗时 {elapsedMs}ms");
    }

    /// <summary>记录一次报警触发（上升沿）。</summary>
    public void RecordAlarm(int pointId, string level, double value)
    {
        lock (_gate) _alarmCount++;
        Append("WARN", $"报警 点位{pointId} → {level}, 值={value}");
    }

    /// <summary>通用 INFO 记录（如设备连接/断开）。</summary>
    public void RecordInfo(string message) => Append("INFO", message);

    /// <summary>通用 WARN 记录（如重连/异常）。</summary>
    public void RecordWarn(string message) => Append("WARN", message);

    private void Append(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {level} {message}";
        lock (_gate)
        {
            // 新日志插到头部（最新在上）；超出上限从尾部丢弃
            _log.Insert(0, line);
            while (_log.Count > MaxLog) _log.RemoveAt(_log.Count - 1);
        }
    }
}
