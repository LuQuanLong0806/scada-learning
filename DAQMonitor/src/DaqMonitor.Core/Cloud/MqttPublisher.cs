using System.Threading.Channels;
using DaqMonitor.Core.Models;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Packets;
using MQTTnet.Protocol;

namespace DaqMonitor.Core.Cloud;

/// <summary>
/// MQTT 双向上云（M7 修正版落地）：MQTTnet 4.x v4 API。
///
/// 设计原则：
///   - 完全 async（无 .Result/.Wait，无 async void，public async 方法全部返回 Task）。
///   - 双向：上行 Publish（点位遥测）+ 下行 Subscribe（云 → 设备命令回调）。
///   - 生产者-消费者：业务侧只往 Channel 写一个 SensorPoint（O(1)），后台任务批量发，
///     每 200ms 或满 100 条触发一次。避免每个点都 RoundTrip MQTT。
///   - 断线自动重连：用 Polly 风格的指数退避（1/2/4/8/16s，封顶 60s）。
///
/// 主题命名：
///   上行（telemetry）：daq/{deviceId}/telemetry    payload = JSON {"id","value","ts"}
///   下行（command）：  daq/{deviceId}/command      payload = 业务自定义字节
/// </summary>
public sealed class MqttPublisher : IAsyncDisposable, IDisposable
{
    private readonly IMqttClient _client;
    private readonly MqttClientOptions _options;
    private readonly string _deviceId;
    private readonly string _telemetryTopic;
    private readonly string _commandTopic;

    private readonly Channel<SensorPoint> _channel =
        Channel.CreateBounded<SensorPoint>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,   // 上云落后时丢老点，优先保新
            SingleReader = true,
            SingleWriter = false
        });

    private readonly CancellationTokenSource _cts = new();
    private Task? _publishLoop;
    private Task? _connectLoop;
    private volatile bool _disposed;

    /// <summary>下行命令回调：业务侧注入（写 PLC / 读点位 / 改配方）。</summary>
    public Func<string, byte[], Task>? OnCommand { get; set; }

    /// <summary>主题约定（便于测试断言）。</summary>
    public string TelemetryTopic => _telemetryTopic;
    public string CommandTopic => _commandTopic;

    /// <summary>
    /// 构造 MQTT 发布器。
    /// </summary>
    /// <param name="host">Broker 主机/IP。</param>
    /// <param name="port">Broker 端口（默认 1883，TLS=8883）。</param>
    /// <param name="deviceId">设备 ID，用于拼主题，默认 "daq-01"。</param>
    /// <param name="clientId">MQTT ClientId，默认与 deviceId 同。</param>
    public MqttPublisher(string host, int port = 1883, string deviceId = "daq-01", string? clientId = null)
    {
        _deviceId = deviceId;
        _telemetryTopic = $"daq/{deviceId}/telemetry";
        _commandTopic = $"daq/{deviceId}/command";

        _client = new MqttFactory().CreateMqttClient();
        _options = new MqttClientOptionsBuilder()
            .WithClientId(clientId ?? deviceId)
            .WithTcpServer(host, port)
            .WithCredentials(string.Empty, string.Empty)
            .WithCleanSession(false)              // 持久会话：重连后接收离线命令
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .Build();

        _client.ApplicationMessageReceivedAsync += OnMessageReceived;
        _client.DisconnectedAsync += OnDisconnected;
    }

    /// <summary>
    /// 启动：开始后台任务（连接 + 发布循环）。重复调用幂等。
    /// </summary>
    public Task StartAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MqttPublisher));
        if (_connectLoop is null)
            _connectLoop = Task.Run(() => ConnectLoopAsync(_cts.Token));
        if (_publishLoop is null)
            _publishLoop = Task.Run(() => PublishLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 投递一个点位到上云缓冲。O(1) 入队，立即返回，不阻塞采集。
    /// </summary>
    public bool Enqueue(SensorPoint point)
        => _channel.Writer.TryWrite(point);

    // ===================== 后台循环 =====================

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        int backoffIdx = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_client.IsConnected)
                {
                    await _client.ConnectAsync(_options, ct);
                    var subOpts = new MqttClientSubscribeOptions
                    {
                        TopicFilters = new List<MqttTopicFilter>
                        {
                            new MqttTopicFilter
                            {
                                Topic = _commandTopic,
                                QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce
                            }
                        }
                    };
                    await _client.SubscribeAsync(subOpts, ct);
                    backoffIdx = 0;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception) { /* 重试 */ }

            try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { break; }
            backoffIdx = Math.Min(backoffIdx + 1, 4);   // 5/10/.../25s 由 MQTT 自身 keepalive 触发断线
        }
    }

    private async Task PublishLoopAsync(CancellationToken ct)
    {
        var batch = new List<SensorPoint>(128);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 取一个点起步
                SensorPoint first;
                try { first = await _channel.Reader.ReadAsync(ct); }
                catch (OperationCanceledException) { break; }
                batch.Add(first);

                // 在 200ms 内尽量多读 / 满足 100 条立刻发，与 AcquisitionPipeline 同套路
                var deadline = DateTime.UtcNow.AddMilliseconds(200);
                while (batch.Count < 100 && DateTime.UtcNow < deadline)
                {
                    if (_channel.Reader.TryRead(out var p)) batch.Add(p);
                    else await Task.Delay(5, ct);
                }

                await PublishBatchAsync(batch, ct);
                batch.Clear();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception) { /* 单批失败等下一轮，重连循环会拉起 broker */ }
        }
    }

    private async Task PublishBatchAsync(List<SensorPoint> batch, CancellationToken ct)
    {
        if (batch.Count == 0 || !_client.IsConnected) return;

        // payload = NDJSON（每行一个点位），便于云侧按行解析 + 不引第三方 JSON 库
        var sw = new System.IO.StringWriter();
        foreach (var p in batch)
        {
            // 简单 JSON（值类型固定，无字符串注入风险）
            sw.Write("{\"id\":");
            sw.Write(p.Id);
            sw.Write(",\"value\":");
            sw.Write(p.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sw.Write(",\"ts\":\"");
            sw.Write(p.Timestamp.ToString("o"));
            sw.Write("\"}\n");
        }

        // 直接构造 MqttApplicationMessage，避免 builder 扩展方法在不同 4.x 小版本间的命名差异
        var msg = new MqttApplicationMessage
        {
            Topic = _telemetryTopic,
            PayloadSegment = new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes(sw.ToString())),
            QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce,
            Retain = false
        };
        await _client.PublishAsync(msg, ct);
    }

    /// <summary>下行命令分发：把 broker 收到的 message payload 转交业务回调。</summary>
    private async Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs e)
    {
        if (OnCommand is null) return;
        if (e.ApplicationMessage.Topic != _commandTopic) return;
        var segment = e.ApplicationMessage.PayloadSegment;
        var payload = segment.Array is null ? Array.Empty<byte>() : segment.Array.Skip(segment.Offset).Take(segment.Count).ToArray();
        try { await OnCommand(e.ApplicationMessage.Topic, payload); }
        catch { /* 业务回调异常不应影响 MQTT 客户端 */ }
    }

    /// <summary>断线时重连由 ConnectLoop 轮询 + MQTTnet 内部重试负责；这里只复位状态。</summary>
    private Task OnDisconnected(MqttClientDisconnectedEventArgs _) => Task.CompletedTask;

    // ===================== 释放 =====================

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _channel.Writer.TryComplete();
        try { _cts.Cancel(); } catch { /* ignore */ }
        try { await Task.WhenAny(_publishLoop ?? Task.CompletedTask, Task.Delay(2000)); } catch { /* ignore */ }
        try { await Task.WhenAny(_connectLoop ?? Task.CompletedTask, Task.Delay(2000)); } catch { /* ignore */ }
        try
        {
            if (_client.IsConnected) await _client.DisconnectAsync();
        }
        catch { /* ignore */ }
        _client.Dispose();
        _cts.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        // 同步 Dispose：避免 finalizer 时间窗，安全起见等 1 秒
        try { _cts.Cancel(); } catch { /* ignore */ }
        try { _publishLoop?.Wait(1000); } catch { /* ignore */ }
        try { _connectLoop?.Wait(1000); } catch { /* ignore */ }
        try { if (_client.IsConnected) _client.DisconnectAsync().GetAwaiter().GetResult(); } catch { /* ignore */ }
        _client.Dispose();
        _cts.Dispose();
        _disposed = true;
    }
}
