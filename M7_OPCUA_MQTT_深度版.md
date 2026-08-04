# M7 — OPC UA / MQTT 上云（15K 加分项）

> **优先级定位**：🔴 必学（加分项）· 上云 OPC UA/MQTT（15K 加分，主流岗位常要）
> **技术来源**：🟧 第三方 NuGet `MQTTnet`（MQTT）、`OPCFoundation.NetStandard.Opc.Ua.Client`（OPC UA）。
> **给简历加的能力**：把数据推到云端 / 对接 SCADA —— 这是 13K→15K 的分水岭，体现"会联网"。
> **前置**：M0–M6（有完整采集/存储/报警链路）。

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

**② 批量发布（事件→入队→定时批量，呼应 M9）**（🟧）
> 🔥 **修正说明**：早期版本在 `DataReceived` 里 `await client.PublishAsync(...)` 逐点发布 —— 高频下会阻塞采集线程、MQTT 抖动。正确做法：从 **M9 的统一采集管道 `BatchReady`** 批量发布。

```csharp
// 订阅统一管道的“批量就绪”事件（M9 的 AcquisitionPipeline 已把多设备数据聚合成批）
_pipeline.BatchReady += async (s, batch) =>
{
    foreach (var p in batch)
    {
        var m = new MqttApplicationMessageBuilder()
            .WithTopic($"factory/line1/point{p.Id}")
            .WithPayload($"{{\"v\":{p.Value},\"t\":\"{p.Timestamp:o}\"}}")   // JSON 载荷，云端好解析
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        await client.PublishAsync(m);   // 后台线程 await，不卡 UI
    }
};
```
> 📌 断线重连用 **M9 的 `Retry`**（指数退避 + 抖动）：连接失败自动重试，而不是裸 `try/catch` 抛给用户。

### ⚠️ 重点 & 易踩坑
| 项 | 说明 |
|---|---|
| ⭐ 用 JSON 载荷 | 云端好解析，字段对齐你的 `SensorPoint`（含 `Timestamp`） |
| ⭐ QoS | AtLeastOnce 至少一次，关键数据别用 AtMostOnce |
| 🔥 别逐点发布 | 必须从 `BatchReady` 批量发布（见上），别在 `DataReceived` 里逐点 `PublishAsync` |
| 🔥 断线重连 | Broker 会掉，用 M9 的 `Retry` 自动重连，别裸 `try/catch` |

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

**🏗️ 项目任务**：DAQ Monitor 加 `Cloud/MqttPublisher.cs`，订阅 `AcquisitionPipeline.BatchReady` 批量发布到配置的主题；用 M9 的 `Retry` 做断线重连。上云能力达标（15K 亮点）。

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

var cfg = new ApplicationConfiguration { ApplicationName = "DAQMonitor", ... };
var ep = new ConfiguredEndpoint(null, new EndpointDescription("opc.tcp://localhost:4840"));
using var session = Session.Create(cfg, ep, false, "DAQMonitor", 60000, null, null).Result;
var node = session.ReadNode("ns=2;s=Line1.Temp");   // 按节点名读
var val = session.ReadValue("ns=2;s=Line1.Temp").Value;
```

### 🔬 掰开揉碎：OPC UA 别用 `.Result` 同步阻塞（本项目已改为异步）
上面 ① 的 `Session.Create(...).Result` 是**反模式**，真项目要改：
- **为什么危险**：`.Result` 会「阻塞当前线程等结果」。如果在 UI 线程调，UI 卡死；更隐蔽的是**死锁**——当异步方法内部要 `await` 回 UI 线程（WPF 的 `SynchronizationContext`），而 UI 线程正被 `.Result` 卡着等它，两边互等 = 永久死锁。
- **正确写法（异步）**：
  ```csharp
  var session = await Session.Create(cfg, ep, false, "DAQMonitor", 60000, null, null);
  var val = await session.ReadValueAsync("ns=2;s=Line1.Temp");
  ```
  全程 `async/await`，不阻塞任何线程。M9 讲过 `async Task` 测试，这里同理。

### 🔬 掰开揉碎：MQTT 是「双向」的（上位机常要接收云端下发）
讲义只发了（Publish），真实上云是**双向**——云端/大屏给上位机下发「设定值」「启停指令」：
```csharp
// 订阅「云端下发」主题，接收控制命令（呼应 M3 给 PLC 写设定值）
await client.SubscribeAsync("factory/line1/cmd");
client.ApplicationMessageReceivedAsync += (s, e) =>
{
    var cmd = JsonSerializer.Deserialize<Command>(e.ApplicationMessage.ConvertPayloadToString());
    // 把命令转成给 PLC/设备的写操作（接 M3 的 Write）
};
```
> 记住：**Publish = 上报数据，Subscribe = 接收指令**，上位机通常两者都要。

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

## 🧩 完整代码组装（MqttPublisher 批量发布，对齐工程）
```csharp
// DaqMonitor.Core/Cloud/MqttPublisher.cs
public class MqttPublisher
{
    private readonly IMqttClient _client;
    private readonly AcquisitionPipeline _pipeline;
    public MqttPublisher(IMqttClient client, AcquisitionPipeline pipeline)
    { _client = client; _pipeline = pipeline; }

    public void Start()
    {
        _pipeline.BatchReady += async (s, batch) =>   // 从 M9 统一管道批量取
        {
            foreach (var p in batch)
                await _client.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic($"factory/line1/point{p.Id}")
                    .WithPayload($"{{\"v\":{p.Value},\"t\":\"{p.Timestamp:o}\"}}")
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build());
        };
    }
}
// 断线重连用 M9 的 Retry.ExecuteAsync(() => client.ConnectAsync(...), maxRetries: 5)
```

## 🔗 明日预告
**M8 工程化收尾**：系统能跑了，但怎么"交付"？——MVVM 重构、配置文件、安装包、README。把玩具变成能装到客户机器上的产品。

## 模块交付清单（M7）
- [ ] MQTTnet 连接 Broker + 发布 JSON
- [ ] 断线重连 + QoS
- [ ] 数据上云接入 DataReceived
- [ ] OPC UA 客户端（连接/读/订阅，了解即可）
