# M7 — OPC UA / MQTT 上云（15K 加分项）

> **优先级定位**：🔴 必学（加分项）· 上云 OPC UA/MQTT（15K 加分，主流岗位常要）
> **技术来源**：🟧 第三方 NuGet `MQTTnet`（MQTT）、`OPCFoundation.NetStandard.Opc.Ua.Client`（OPC UA）。
> **给简历加的能力**：把数据推到云端 / 对接 SCADA —— 这是 13K→15K 的分水岭，体现"会联网"。
> **前置**：M0–M6（有完整采集/存储/报警链路）。

> ⏱️ **阅读路径**(按时间预算选入口)
> - **3 分钟**:看「模块目标」— 知道 MQTT 是轻量上云协议 / OPC UA 是工业互联标准
> - **30 分钟**:加看 Day 1 MQTT Publish + mosquitto_sub 接数据
> - **3 小时**:全文精读 + Day 2 MQTT 双向 Subscribe(下行命令)+ Day 3 OPC UA 客户端
> - 🎯 **面试高频**:**全程 async 不能 .Result(UI 死锁)** / MQTT QoS 0/1/2 / 持久会话 CleanSession=false / MQTTnet 4.x v4 API
> - 🔁 **配套复习**:[代码肌肉 B13 MQTT Publish+Subscribe 双向](代码肌肉训练手册_30天刷题版.md) · [Debug C1 async void 吞异常](代码肌肉训练手册_30天刷题版.md) · [间隔重复表](记忆与复习机制_间隔重复版.md)

> 📚 **前置语法**(M7 用到的,陌生请查 [C# 语法速查 — 前端视角](CSharp语法速查_前端视角.md))
> - `async Task` — 全程异步(MQTT/OPC UA 都是 IO 密集),速查 §8
> - `event Func<Task>?` — 异步事件(罕见但 M7 用),速查 §7/§8
> - `CancellationToken ct` — 取消令牌,速查 §8
> - `await Task.Delay(TimeSpan.FromSeconds(30), ct)` — 异步延迟
> - `class MqttClient : IDisposable` / `using var client = ...` — 必须 Dispose,速查 §13
> - `Interlocked.Exchange(ref _retryCount, 0)` — 原子重试计数,速查 §14

> 📦 **前置类型**(本模块示例代码用到的核心自定义类型)
> M7 示例引用 `SensorPoint` / `AcquisitionPipeline` 等类型 — 这些在 [📦 前置类型定义 · 学员粘贴版](前置类型定义_学员粘贴版.md) **集中定义**(`AcquisitionPipeline` 简化版在"四、采集管道简化版")。**遇到"找不到类型 XXX"报错,先去那份文档复制对应类型**,在项目里建 `_PredefinedTypes.cs` 粘进去就能跑。本模块会**新建** `MqttPublisher` / `CloudCommand`(下行命令 record)。

## 模块目标
把 DAQ Monitor 的实时数据通过 MQTT 发布到 Broker（如 EMQX / 本地 Mosquitto），可用手机/云端订阅查看；了解 OPC UA 客户端对接方式。

## Day 1 — MQTT 发布订阅 🟡

### 一句话讲清楚
MQTT = 物联网的"微信群"：设备往"主题(topic)"发消息，订阅同一主题的人都能收到。上位机把采集数据发到 `factory/line1/temp` 这种主题，云端/大屏订阅即可。

### 前端类比秒懂
| MQTT | 前端 |
|---|---|
| Broker | 消息服务器（如 RabbitMQ/NATS） |
| Topic | 频道 / 事件名 |
| Publish | `emitter.emit('topic', data)` |
| Subscribe | `emitter.on('topic', cb)` |
| QoS | 送达保证级别 |

### 分点精讲
**① 连接 + 发布**（🟧）

> 🔧 **必装 NuGet**(在 `src/DaqMonitor.Core/` 目录执行):
> ```bash
> cd src/DaqMonitor.Core
> dotnet add package MQTTnet
> ```
> 💡 MQTTnet 4.x 是 .NET 最流行的 MQTT 客户端/服务端库。装完才能 `using MQTTnet;` `using MQTTnet.Client;`

> 📂 语法演示可放 `Program.cs` 跑;**真实工程**放 `DaqMonitor.Core/Cloud/MqttPublisher.cs`(本节②会建)。

```csharp
using MQTTnet;
using MQTTnet.Client;

var factory = new MqttFactory();
var client = factory.CreateMqttClient();
await client.ConnectAsync(new MqttClientOptionsBuilder()
    .WithTcpServer("broker.emqx.io", 1883).Build());

var msg = new MqttApplicationMessageBuilder()
    .WithTopic("factory/line1/temp")
    .WithPayload($"{{\"point\":1,\"value\":{value},\"ts\":\"{DateTime.Now:o}\"}}")
    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
    .Build();
await client.PublishAsync(msg);
```

**② 批量发布（事件→入队→后台 consumer 串行消费，呼应 M9）**（🟧）

> 📂 `DaqMonitor.Core/Cloud/MqttPublisher.cs` · namespace `DaqMonitor.Core.Cloud`
> 🔧 已装 MQTTnet(本节①已装)
> 💡 用到 `SensorPoint` / `AcquisitionPipeline`(M9 定义)

> 🔥 **修正说明**：早期版本在 `DataReceived` 里 `await client.PublishAsync(...)` 逐点发布 —— 高频下会阻塞采集线程、MQTT 抖动。正确做法：从 **M9 的统一采集管道 `BatchReady`** 批量发布；且**事件处理器仅入队(同步)**，后台 consumer 异步消费(解决 `async void` 反模式)。

```csharp
// 正确版:BatchReady 仅入队(同步,绝不 async void),后台 consumer 异步消费
// 完整代码放 DaqMonitor.Core/Cloud/MqttPublisher.cs
using MQTTnet;
using MQTTnet.Client;
using System.Threading.Channels;
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Pipeline;

namespace DaqMonitor.Core.Cloud;

public class MqttPublisher
{
    private readonly IMqttClient _client;
    private readonly AcquisitionPipeline _pipeline;
    private readonly Channel<IReadOnlyList<SensorPoint>> _publishQ =
        Channel.CreateBounded<IReadOnlyList<SensorPoint>>(100);   // 有界,防 OOM

public MqttPublisher(IMqttClient client, AcquisitionPipeline pipeline)
{
    _client = client;
    _pipeline = pipeline;
    _pipeline.BatchReady += OnBatchReady;   // 同步方法
}

private void OnBatchReady(object? sender, IReadOnlyList<SensorPoint> batch)
{
    // 仅入队,不 await。满了就丢弃本批(报警但不停采集)
    if (!_publishQ.Writer.TryWrite(batch))
        _log?.LogWarning("MQTT 发布队列满,丢弃一批 {Count} 条", batch.Count);
}

public async Task StartAsync(CancellationToken ct)
{
    await ConnectWithRetryAsync(ct);   // 用 M9 的 Retry 重连
    _ = Task.Run(() => ConsumeLoopAsync(ct), ct);
}

private async Task ConsumeLoopAsync(CancellationToken ct)
{
    await foreach (var batch in _publishQ.Reader.ReadAllAsync(ct))
    {
        foreach (var p in batch)
        {
            try
            {
                await _client.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic($"factory/line1/point{p.Id}")
                    .WithPayload($"{{\"v\":{p.Value},\"t\":\"{p.Timestamp:o}\"}}")   // JSON 载荷,云端好解析
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build());
            }
            catch (Exception ex)
            {
                _log?.LogError(ex, "MQTT 发布失败 PointId={Id}", p.Id);
                // 不抛,继续下一条
            }
        }
    }
}
```
> 📌 断线重连用 **M9 的 `Retry`**（指数退避 + 抖动）：连接失败自动重试，而不是裸 `try/catch` 抛给用户。

### 🔬 掰开揉碎：为什么不能 `BatchReady += async (s, batch) => await ...`
早期讲义的写法 `_pipeline.BatchReady += async (s, batch) => { await ... }` 看似简洁，**其实是 `async void`**——因为 `BatchReady` 是 `EventHandler` 签名（返回 `void`）。后果：
- **① 异常被吞 + 进程崩**：`async void` 内部抛异常没有 `Task` 能 `await`/`catch`，会直接走 `AppDomain.UnhandledException` 把整个上位机进程拖崩，连采集都停了。
- **② 并发乱序**：`BatchReady` 每秒触发多次，上一次 `await PublishAsync` 还没完，下一次又进来，多条批同时跑 → topic 时序错乱、Broker 端看到的消息顺序不可预测。

**正确做法**：事件处理器**仅入队（同步 `TryWrite`，瞬时返回）**，由**后台 consumer 串行 `await`**消费——既不阻塞采集线程，又保证顺序。这是 .NET 高频场景的标准"生产者-消费者"模式。详 [C# 陷阱讲义 陷阱 4](C#_陷阱_前端转上位机必看_深度版.md)。

### ⚠️ 重点 & 易踩坑
| 项 | 说明 |
|---|---|
| ⭐ 用 JSON 载荷 | 云端好解析，字段对齐你的 `SensorPoint`（含 `Timestamp`） |
| ⭐ QoS | AtLeastOnce 至少一次，关键数据别用 AtMostOnce |
| 🔥 别逐点发布 | 必须从 `BatchReady` 批量发布（见上），别在 `DataReceived` 里逐点 `PublishAsync` |
| 🔥 别 `async void` 事件处理器 | `BatchReady += async (s,b)=>await...` 是 `async void`，异常崩进程 + 并发乱序。改用 `Channel<T>` 入队 + 后台 consumer（见上） |
| 🔥 断线重连 | Broker 会掉，用 M9 的 `Retry` 自动重连，别裸 `try/catch` |

### 🔴 知识点：MQTT 双向订阅（接收云端下发命令）

**1. 一句话讲清楚**
上位机不只是"上报数据"（`Publish`），还要"接收指令"（`Subscribe`）—— 云端/大屏下发"启停""改设定值"，上位机收到后调 PLC 的 `Write`。真实上云双向是标配，**光会 Publish 接不了活**。

**2. 真实代码**
```csharp
public async Task SubscribeCommandsAsync(CancellationToken ct)
{
    await _client.SubscribeAsync(new MqttTopicFilterBuilder()
        .WithTopic("factory/line1/cmd")
        .WithAtLeastOnceQoS()
        .Build());

    _client.ApplicationMessageReceivedAsync += async e =>
    {
        try
        {
            var json = e.ApplicationMessage.ConvertPayloadToString();
            var cmd = JsonSerializer.Deserialize<CloudCommand>(json);

            // 路由不同命令
            switch (cmd?.Action)
            {
                case "setpoint":
                    _plcDevice.Write(cmd.PointId, cmd.Value);   // 下发到 PLC
                    _log?.LogInformation("云端下发设定值: PointId={Id} Value={V}", cmd.PointId, cmd.Value);
                    break;
                case "start":
                    _pipelineRunCts = new CancellationTokenSource();
                    _ = _pipeline.RunAsync(_pipelineRunCts.Token);   // 后台启动采集(参考 AcquisitionPipeline.RunAsync)
                    break;
                case "stop":
                    _pipelineRunCts?.Cancel();                      // 取消 token = 停止采集
                    break;
            }
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "处理云端命令失败");
        }
    };
}

public record CloudCommand(string Action, int PointId, double Value);
```

**3. ⚠️ 坑点表**
| 坑 | 说明 |
|---|---|
| 异常吞 | `ApplicationMessageReceivedAsync` 是 `async void` 类事件处理器，**必须 try-catch**，否则异常崩进程 |
| QoS 选择 | 命令类用 AtLeastOnce（至少一次，可能重复，要幂等）；数据上报也用 AtLeastOnce |
| 命令幂等 | 收到同一命令多次应等价（前端类比：像 Redux 必须是 pure reducer） |
| 跨线程改 PLC | `_plcDevice.Write` 内部要加 `lock` 或 `ConcurrentQueue` 串行化，避免和采集线程抢设备 |

**4. 测试方法**
- 装 MQTTX 桌面版，连 `broker.emqx.io:1883`
- 订阅 `factory/line1/point1`（看上位机上报）
- 向 `factory/line1/cmd` 发 `{"action":"setpoint","pointId":1,"value":50}` 命令
- 看 PLC 设备的真值是否变 50

### 🟢 基础题
连公共 MQTT Broker（`broker.emqx.io:1883`），向 `test/daq` 主题发布一条 JSON 消息。

### 🟡 进阶题
把发布改成"批量"：订阅 M9 的 `AcquisitionPipeline.BatchReady`，每批点一次性发布（每点一个 topic），用 `AtLeastOnce` QoS。

### 🔴 挑战题
加"双向"：订阅 `factory/line1/cmd` 接收云端下发的设定值命令，反序列化成 `Command` 后调用 M3 的 `PlcDevice.Write` 下发给 PLC —— 构成「云端→上位机→设备」闭环。

**✅ 答案（基础题核心，topic 改 `test/daq`）**
```csharp
await client.PublishAsync(new MqttApplicationMessageBuilder()
    .WithTopic("test/daq")
    .WithPayload($"{{\"point\":1,\"value\":{value}}}").Build());
```

**🏗️ 项目任务**：DAQ Monitor 加 `Cloud/MqttPublisher.cs`（已修正版，`async void` 已用 `Channel<T>` 解决 + 双向订阅云端下发命令），订阅 `AcquisitionPipeline.BatchReady` 批量发布到配置的主题；订阅 `factory/line1/cmd` 接收云端命令并下发 PLC；用 M9 的 `Retry` 做断线重连。上云能力达标（15K 亮点）。

**🎓 工控导师说**：上云第一个踩的坑是"逐点 Publish 把 Broker 打挂"。工厂 100Hz × 50 设备 = 每秒 5000 条，MQTT Broker 不是为这个设计的。正确：**攒批 + 合理 QoS + 断线退避重连**。还有，上云前务必确认网络隔离——生产网和办公网通常不通，别在客户现场才发现连不上外网。

**💼 职业建议**：OPC UA/MQTT 是 15K 分水岭。面试不用精通，但要能讲清"MQTT 是发布订阅的物联网协议、OPC UA 解决跨厂商互通、上云要批量+重连"。这三点讲出，面试官就信你能做联网项目。

**✅ 打卡[ ]**

## Day 2 — OPC UA 客户端（对接 SCADA） 🟡

### 一句话讲清楚
OPC UA 是工业互联的"世界语"：上位机作为客户端连 OPC UA 服务器（PLC/SCADA 暴露的），读/写"节点(Node)"，跨厂商互通 —— 工厂级集成标配。

### 分点精讲
**① 连接 + 读节点**（🟧）
```csharp
using Opc.Ua;
using Opc.Ua.Client;

// 创建配置(证书、安全策略)
var cfg = await new ApplicationConfigurationBuilder
{
    ApplicationName = "DAQMonitor",
    ApplicationType = ApplicationType.Client,
    SecurityConfiguration = new SecurityConfiguration
    {
        AutoAcceptUntrustedCertificates = true   // 测试用,生产要正经证书
    }
}.BuildAsync();

// 异步连接(全程 await,绝不用 .Result)
var endpoint = new ConfiguredEndpoint(null,
    new EndpointDescription("opc.tcp://localhost:4840"),
    EndpointSelectionOptions.None);

using var session = await Session.Create(
    cfg, endpoint, false, "DAQMonitor", 60000, null, null);

// 异步读节点
var value = await session.ReadValueAsync("ns=2;s=Line1.Temp");
Console.WriteLine($"温度 = {value.Value}");
```

### 🔬 掰开揉碎：为什么绝不能用 `.Result` / `.Wait()`
**为什么危险**：`.Result` 会「阻塞当前线程等结果」。如果在 UI 线程调，UI 卡死；更隐蔽的是**死锁**——当异步方法内部要 `await` 回 UI 线程（WPF 的 `SynchronizationContext`），而 UI 线程正被 `.Result` 卡着等它，**两边互等 = 永久死锁**。**正确做法：全程 `async/await`**，不阻塞任何线程。M9 讲过 `async Task` 测试，这里同理。详 [C# 陷阱讲义 陷阱 4](C#_陷阱_前端转上位机必看_深度版.md)。

**反面教材（绝不这么写）**：
```csharp
// ❌ 死锁风险
using var session = Session.Create(cfg, ep, false, "DAQMonitor", 60000, null, null).Result;
var value = session.ReadValue("ns=2;s=Line1.Temp").Value;
```

### 🔬 掰开揉碎：MQTT 是「双向」的（上位机常要接收云端下发）
> 📌 **完整版已升级到 Day 1 主代码（"MQTT 双向订阅"知识点）**，含完整 try-catch + 命令路由 + 幂等坑点。这里仅保留简要提示。

讲义只发了（Publish），真实上云是**双向**——云端/大屏给上位机下发「设定值」「启停指令」：
```csharp
// 订阅「云端下发」主题，接收控制命令（呼应 M3 给 PLC 写设定值）
await client.SubscribeAsync("factory/line1/cmd");
client.ApplicationMessageReceivedAsync += async (s, e) =>   // async void 类,必须 try-catch
{
    try
    {
        var cmd = JsonSerializer.Deserialize<Command>(e.ApplicationMessage.ConvertPayloadToString());
        // 把命令转成给 PLC/设备的写操作（接 M3 的 Write）
    }
    catch (Exception ex) { /* 必须吞,否则崩进程 */ }
};
```
> 记住：**Publish = 上报数据，Subscribe = 接收指令**，上位机通常两者都要。完整代码见 Day 1 的"🔴 知识点：MQTT 双向订阅"。

**② 订阅（主动推送）**（🟧）
```csharp
var sub = new Subscription(session.DefaultSubscription) { PublishingInterval = 500 };
sub.AddItem(new MonitoredItem { StartNodeId = "ns=2;s=Line1.Temp" });
sub.ItemChanged += (s, e) => Console.WriteLine($"值={e.NotificationValue}");
session.AddSubscription(sub); sub.Create();
```

### ⚠️ 重点 & 易踩坑
| 项 | 说明 |
|---|---|
| ⭐ 节点路径 `ns=2;s=...` | 命名空间+标识符，需服务器方给，别瞎猜 |
| ⭐ 用订阅而非轮询 | 服务端推送更高效，少打扰设备 |
| 🔥 证书 | OPC UA 默认要证书，测试可关安全策略(生产不可) |
| 🔥 这是加分项 | 不要求精通，能讲清"OPC UA 解决跨厂商互通"即可 |

### 🟢 基础题
（若有本地 OPC UA 模拟服务器）连上并读一个节点值。

### 🟡 进阶题
把 ① 的 `.Result` 改成全程 `await` 的异步客户端（消除死锁风险），并读出 `ns=2;s=Line1.Temp` 的值。

### 🔴 挑战题
用 OPC UA 订阅（而非轮询）`Line1.Temp`，值变化时 `ItemChanged` 事件里把数据喂进 DAQ Monitor 的 `PointStore` —— 让 OPC UA 服务器也成为一个 `IDevice` 数据源。

**✅ 答案（进阶题核心）**
```csharp
var session = await Session.Create(cfg, ep, false, "DAQMonitor", 60000, null, null);
var val = await session.ReadValueAsync("ns=2;s=Line1.Temp");
```

**🏗️ 项目任务**：DAQ Monitor 加 `Cloud/OpcUaClient.cs`（按需），能作为客户端订阅 SCADA 节点。M7 整体达标。

**✅ 打卡[ ]**

## 📌 温故知新（跨模块联动）
- **M0 Day7 异步 / M9 容错 → 这里全用得上**：OPC UA 用 `await`（别 `.Result`），MQTT 断线用 M9 的 `Retry` 退避重连。
- **M3 PLC 写设定值 → 这里云端下发接上**：MQTT 订阅收到的命令，转成给 PLC/设备的写操作，构成「云端→上位机→设备」闭环。
- **M9 统一管道 → 这里批量发布**：MQTT 发布从 `BatchReady` 取批，别在 `DataReceived` 逐点 `PublishAsync`。

## 📚 延伸阅读（卡点时点开）
- MQTTnet 仓库：https://github.com/dotnet/MQTTnet · MQTT 协议概念：https://mqtt.org/
- OPC 基金会：https://opcfoundation.org/ · OPC UA .NET 栈：https://github.com/OPCFoundation/UA-.NETStandard
- 全部模块外链汇总见 `外部链接索引.md`
- 📎 **没有硬件？看 `硬件替代方案与讲解_深度版.md`**：本地 OPC UA 模拟服务器(Prosys) / MQTT Broker(Mosquitto) 零成本练手

## 🧩 完整代码组装（MqttPublisher 批量发布 + 双向订阅，对齐工程）
```csharp
// DaqMonitor.Core/Cloud/MqttPublisher.cs
using System.Threading.Channels;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using Microsoft.Extensions.Logging;

public class MqttPublisher
{
    private readonly IMqttClient _client;
    private readonly AcquisitionPipeline _pipeline;
    private readonly IPlcDevice _plcDevice;
    private readonly ILogger<MqttPublisher>? _log;

    // 有界 Channel:防 OOM + 串行消费(解决 async void 反模式)
    private readonly Channel<IReadOnlyList<SensorPoint>> _publishQ =
        Channel.CreateBounded<IReadOnlyList<SensorPoint>>(100);

    private CancellationTokenSource? _pipelineRunCts;   // 控制 pipeline 启停(参考 AcquisitionPipeline.RunAsync)

    public MqttPublisher(IMqttClient client, AcquisitionPipeline pipeline,
        IPlcDevice plcDevice, ILogger<MqttPublisher>? log = null)
    {
        _client = client;
        _pipeline = pipeline;
        _plcDevice = plcDevice;
        _log = log;
        _pipeline.BatchReady += OnBatchReady;   // 同步方法,绝不 async void
    }

    private void OnBatchReady(object? sender, List<SensorPoint> batch)
    {
        // 仅入队,瞬时返回。满了就丢弃本批(报警但不停采集)
        if (!_publishQ.Writer.TryWrite(batch))
            _log?.LogWarning("MQTT 发布队列满,丢弃一批 {Count} 条", batch.Count);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        // 用 M9 的 Retry 做断线重连(指数退避 + 抖动)
        await ConnectWithRetryAsync(ct);

        // 启动后台 consumer:串行消费,保证顺序 + 异常不崩
        _ = Task.Run(() => ConsumeLoopAsync(ct), ct);

        // 订阅云端下发命令(双向)
        await SubscribeCommandsAsync(ct);
    }

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        // M9 的 Retry.ExecuteAsync(...) 包装,失败自动重试
        await Retry.ExecuteAsync(() => _client.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer("broker.emqx.io", 1883).Build(), ct), maxRetries: 5);
    }

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        await foreach (var batch in _publishQ.Reader.ReadAllAsync(ct))
        {
            foreach (var p in batch)
            {
                try
                {
                    await _client.PublishAsync(new MqttApplicationMessageBuilder()
                        .WithTopic($"factory/line1/point{p.Id}")
                        .WithPayload($"{{\"v\":{p.Value},\"t\":\"{p.Timestamp:o}\"}}")
                        .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                        .Build());
                }
                catch (Exception ex)
                {
                    _log?.LogError(ex, "MQTT 发布失败 PointId={Id}", p.Id);
                    // 不抛,继续下一条
                }
            }
        }
    }

    public async Task SubscribeCommandsAsync(CancellationToken ct)
    {
        await _client.SubscribeAsync(new MqttTopicFilterBuilder()
            .WithTopic("factory/line1/cmd")
            .WithAtLeastOnceQoS()
            .Build());

        // ApplicationMessageReceivedAsync 是 async void 类,必须 try-catch
        _client.ApplicationMessageReceivedAsync += async e =>
        {
            try
            {
                var json = e.ApplicationMessage.ConvertPayloadToString();
                var cmd = JsonSerializer.Deserialize<CloudCommand>(json);

                switch (cmd?.Action)
                {
                    case "setpoint":
                        _plcDevice.Write(cmd.PointId, cmd.Value);   // 下发 PLC
                        _log?.LogInformation("云端下发设定值: PointId={Id} Value={V}",
                            cmd.PointId, cmd.Value);
                        break;
                    case "start":
                        _pipelineRunCts = new CancellationTokenSource();
                        _ = _pipeline.RunAsync(_pipelineRunCts.Token);   // 后台启动采集
                        break;
                    case "stop":
                        _pipelineRunCts?.Cancel();                      // 取消即停
                        break;
                }
            }
            catch (Exception ex)
            {
                _log?.LogError(ex, "处理云端命令失败");
            }
        };
    }

    public record CloudCommand(string Action, int PointId, double Value);
}
```

## 🔗 明日预告
**M8 工程化收尾**：系统能跑了，但怎么"交付"？——MVVM 重构、配置文件、安装包、README。把玩具变成能装到客户机器上的产品。

## 模块交付清单（M7）
- [ ] MQTTnet 连接 Broker + 发布 JSON
- [ ] 断线重连 + QoS
- [ ] 数据上云接入 DataReceived
- [ ] OPC UA 客户端（连接/读/订阅，了解即可）
