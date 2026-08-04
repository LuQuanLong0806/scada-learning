using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Models;
using System.Threading.Channels;

namespace DaqMonitor.Core.Acquisition;

/// <summary>
/// 统一采集管道（15K 标准架构）：所有设备事件 → 入 Channel 缓冲 → 后台消费 + 定时批量出队 → 一次性推给 UI。
/// 解决 M5③ / M7② 里“逐事件 Dispatcher.Invoke / 逐事件 PublishAsync”在 100Hz×多设备下冲垮 UI / 网络的问题。
/// 关键点：事件只做“入队”这一件极轻的事，重活（刷新/上云）由定时器批量处理。
/// </summary>
public sealed class AcquisitionPipeline : IDisposable
{
    private readonly Channel<SensorPoint> _channel = Channel.CreateUnbounded<SensorPoint>();
    private readonly List<IDevice> _devices = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Timer _flushTimer;
    private readonly object _gate = new();
    private List<SensorPoint> _pending = new();
    private readonly int _maxBatch;

    /// <summary>批量就绪事件：在后台线程触发，UI 订阅方需自行 Dispatcher 回 UI 线程再改界面。</summary>
    public event EventHandler<IReadOnlyList<SensorPoint>>? BatchReady;
    public event EventHandler<Exception>? Error;

    public AcquisitionPipeline(TimeSpan flushInterval, int maxBatch = 500)
    {
        _maxBatch = maxBatch;
        _flushTimer = new Timer(_ => Flush(), null, flushInterval, flushInterval);
        _ = ConsumeAsync();
    }

    /// <summary>注册一个设备：自动订阅它的 DataReceived，把点塞进缓冲。</summary>
    public void Register(IDevice device)
    {
        device.DataReceived += OnPoint;
        _devices.Add(device);
    }

    private void OnPoint(object? sender, DataEventArgs e)
        => _channel.Writer.TryWrite(new SensorPoint { Id = e.PointId, Value = e.Value, Timestamp = e.Timestamp });

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var p in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                List<SensorPoint>? batch = null;
                lock (_gate)
                {
                    _pending.Add(p);
                    if (_pending.Count >= _maxBatch) { batch = _pending; _pending = new(); }
                }
                if (batch is not null) BatchReady?.Invoke(this, batch);
            }
        }
        catch (OperationCanceledException) { /* 正常退出 */ }
        catch (Exception ex) { Error?.Invoke(this, ex); }
    }

    private void Flush()
    {
        List<SensorPoint>? batch = null;
        lock (_gate)
        {
            if (_pending.Count > 0) { batch = _pending; _pending = new(); }
        }
        if (batch is not null) BatchReady?.Invoke(this, batch);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _flushTimer.Dispose();
        _channel.Writer.TryComplete();
        foreach (var d in _devices) d.DataReceived -= OnPoint;
        _cts.Dispose();
    }
}
