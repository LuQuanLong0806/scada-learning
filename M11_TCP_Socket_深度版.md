# M11 — TCP Socket 自定义协议通信

> **优先级定位**：🟡 缓学 · TCP/Socket 私有协议（JD 也提 Socket/TCP-IP，但串口/Modbus/PLC 已覆盖主场景）
> **技术来源**：🟩 .NET 类库 `System.Net.Sockets`（BCL，装好 .NET 就有，**不装包**）；复杂私有协议框架可用 🟧 SuperSocket。
> **给简历加的能力**：通过 TCP 对接"非 Modbus 的私有协议设备"——大量仪表、网关、PLC 网关、自研下位机都用私有 TCP，这是 JD 里「Socket / TCP-IP 必会」的真本事。
> **前置**：M0（并发/事件）、M1（串口字节流解析）。M11 把 M1 的"字节流 → 帧解析 → 事件"整套思维原样搬到 TCP。
> **前端类比总纲**：TCP 像"裸 WebSocket 的底层 socket"——有连接、有字节流、但没有"一条消息"边界，要自己切帧；这正是 Node `net.Socket` 的体验。

---

> ⏱️ **阅读路径**(按时间预算选入口)
> - **3 分钟**:看「模块目标」+ 「Day 1 一句话讲清楚」— 知道 TCP 像"裸 WebSocket 底层 socket"
> - **30 分钟**:加看 Day 1 帧格式设计(AA55+长度+payload+CRC)
> - **3 小时**:全文精读 + Day 2 **粘包/拆包状态机** + Day 3 心跳+重连
> - 🎯 **面试高频**:**粘包/拆包(状态机拼帧)** / 帧头为什么 AA55 / 长度前缀 vs 分隔符 / 心跳保活 + 静默超时
> - 🔁 **配套复习**:[速记卡 Q11 粘包半包](面试高频知识点_速记卡.md) · [代码肌肉 B16 TcpDevice 粘包状态机 15min 白板](代码肌肉训练手册_30天刷题版.md) · [间隔重复表](记忆与复习机制_间隔重复版.md)

> 📚 **前置语法**(M11 用到的,陌生请查 [C# 语法速查 — 前端视角](CSharp语法速查_前端视角.md))
> - `byte[] buf = new byte[1024]` / `ArraySegment<byte>` — 字节缓冲,速查 §2
> - `Memory<byte>` / `ReadOnlySpan<byte>` — 高性能零拷贝
> - `async Task<int> ReceiveAsync(byte[] buf, int offset, int count, ct)` — 异步 IO,速查 §8
> - `event EventHandler<byte[]>? FrameReceived` — 帧到达事件,速查 §7
> - `enum ParseState { WaitHeader, ReadLength, ReadBody, CheckCrc }` — 状态机枚举,速查 §12
> - `class FrameParser` 内部 `private int _state;` 字段 + 方法 — 完整 class 设计

> 📦 **前置类型**(本模块示例代码用到的核心自定义类型)
> M11 示例引用 `DeviceBase` / `IDevice` / `SensorPoint` 等类型 — 这些在 [📦 前置类型定义 · 学员粘贴版](前置类型定义_学员粘贴版.md) **集中定义**。**遇到"找不到类型 XXX"报错,先去那份文档复制对应类型**,在项目里建 `_PredefinedTypes.cs` 粘进去就能跑。本模块会**新建** `TcpDevice : DeviceBase` 和 `TcpFrameParser`(状态机),跟着 Day 1-3 敲。

## 模块目标
写出一个 `TcpDevice` 实现 `IDevice`：连接 TCP → 收字节流 → 按自定义帧解析（处理**粘包/拆包**）→ 通过 `DataReceived` 事件抛业务数据 → 进 DAQ Monitor。证明"换通信介质不改采集层"。

---

## Day 1 — TCP 基础 + 第一个回显 + 粘包拆包 🟡

### 一句话讲清楚
TCP 是一条"字节河流"，**只保证顺序、不保证边界**。你发 100 字节，对方可能一次收 100、也可能先收 30 再收 70；你发两条 50 字节，对方可能一次收 100（**粘包**）。所以必须自己定义"一帧从哪到哪"。

### 🎭 拟人秒懂:水龙头流水 + 快递员批量送货(画面感记忆锚点)

> 把 TCP 字节流想象成"水龙头往水桶里放水,你来舀"。

- **TCP 像水龙头**:上游(发送方)随时开水龙头倒水,下游(接收方)拿着水桶舀
- **舀水的量不确定**:你发 100 滴水,对面**可能一勺舀到 100 滴,也可能第一勺 30 滴 + 第二勺 70 滴**(拆包)
- **多次发水可能混一勺**:你发了 50 滴 + 又发 50 滴,对面**可能一勺舀到 100 滴**(粘包)— 因为 TCP 不管你"发了多少次",只看到一条河
- **TCP 的承诺**:水滴顺序不会乱(顺序保证),水滴不会丢(可靠传输);**但它不承诺"一勺正好一瓢"**(无消息边界)

**唯一正确做法**:**把所有水舀进自己的大水缸(累积缓冲区)**,然后按"标记"从水缸里**精确分装**:
- **长度前缀法**(推荐):每瓢水开头贴"本瓢 30 滴"标签,你按标签舀
- **分隔符法**:每瓢水结尾掺红色染料,你看到红色就停 — 简单但水里不能本来就有红色

**新人 100% 翻车场景**:
```csharp
// ❌ 错误:Read 一次当一帧
int n = await ns.ReadAsync(buf, 0, buf.Length);
ProcessFrame(buf[..n]);   // 设备一次发 2 帧 → 这里只处理一半,后一半丢了
```
正确做法:**累积 + 循环切帧**(`TryParseFrames`),刻进骨头里。

### 前端类比秒懂
| TCP 概念 | 前端类比 | 说明 |
|---|---|---|
| `TcpClient` / `NetworkStream` | Node `net.Socket` / 浏览器 `WebSocket` | 建立连接通道 |
| IP:Port | `http://host:port` | 地址 + 端口 |
| 字节流（无消息边界） | TCP 原始 `socket` vs `ws`（ws 有帧） | **TCP 没有"一条消息"概念** |
| 粘包/拆包 | 自己用 `\n` 分隔的 `socket.on('data')` | 必须自己切帧 |
| 长度前缀帧 | WebSocket 的 payload-length | 先读长度再读体 |
| 回 UI 线程 | `requestAnimationFrame` 回主线程 | 后台线程不能直接改 UI |

### 分点精讲
**① 建立连接 + 异步读写**（🟩）
```csharp
using System.Net.Sockets;
using System.Text;

var client = new TcpClient();
await client.ConnectAsync("127.0.0.1", 502);     // 连设备
NetworkStream ns = client.GetStream();

// 发：把业务数据按"帧"封好再发
byte[] frame = BuildFrame(0x03, Encoding.ASCII.GetBytes("READ"));
await ns.WriteAsync(frame, 0, frame.Length);

// 收：先读长度，再读体（见 ④）
```

**② 后台线程持续收 + 抛事件**（🟩 + 🟦，复用 M0 的事件模式）
> 💡 **为什么不能只靠 `n==0`**：TCP 重置 / 网线拔掉 / 路由器重启，`ReadAsync` 会抛 `SocketException`，你不 catch 进程就崩。**前端类比**：`fetch` 不 catch 网络错误应用就挂。

```csharp
// 与 M0/M1 同款：收到解析好的业务点，就触发 DataReceived
public event EventHandler<SensorPoint>? DataReceived;

async Task RecvLoop(CancellationToken ct)
{
    var buf = new byte[4096];
    while (!ct.IsCancellationRequested)
    {
        int n;
        try { n = await ns.ReadAsync(buf, 0, buf.Length, ct); }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
        { /* 客户端硬断开, 优雅退出 */ break; }
        catch (IOException ex) when (ex.InnerException is SocketException)
        { /* 网络中断 */ break; }
        if (n == 0) break;                        // 对端正常关闭 (FIN)
        _accumulator.Write(buf, 0, n);            // 累积到缓冲区
        foreach (var pt in TryParseFrames(_accumulator))   // 切帧
            DataReceived?.Invoke(this, pt);
    }
    // 退出循环 = 掉线, 触发重连 (见文末「心跳实现完整版」)
}
```

**③ 为什么必须"累积 + 切帧"**（🟦 思维）
TCP 把你的 `Write` 当成"往河里倒水"，对面 `Read` 舀上来的水量不确定。所以：
- 不能"Read 一次 = 一帧"；
- 必须有个**累积缓冲区**（`MemoryStream` 或环形缓冲），每次 Read 后尝试从缓冲区里**尽可能多地**切出完整帧。

**④ 两种主流切帧法**（🟧 私有协议设计）
- **长度前缀法（推荐）**：帧头 `AA 55` + `长度(2字节)` + `命令` + `数据` + `校验`。先读固定头，拿到长度，再精确读"长度"个体。
- **分隔符法**：用 `\r\n` 或 `|` 分隔（如文本协议），按分隔符 Split。简单但数据里不能出现分隔符。

### 🎭 拟人秒懂:挂号信的信封(帧格式记忆锚点)

> 把私有协议帧想象成"中国邮政的挂号信"。

```
[AA 55]  [长度 2B]  [命令 1B]  [数据 N B]  [校验 1B]  [尾 0D]
 信封封面  重量标签   信件类型   信纸内容    防拆印     封口蜡
```

- **`AA 55` 帧头(信封封面)**:告诉接收方"新信开始啦"。为什么选 `AA 55`?**二进制 `10101010 01010101`** — 0 和 1 交替最容易和随机噪声区分,**电磁干扰很难正好生成 `AA 55` 序列**
- **长度 2B(重量标签)**:告诉接收方"信纸多重"。先读 2 字节拿到 N,再精确读 N 字节 — 这样**不管粘包还是拆包,都能精准切**
- **命令 1B(信件类型)**:01=读、02=写、03=心跳、04=报警...像 HTTP 的 method
- **数据 N B(信纸内容)**:实际业务数据(温度值、设定值等)
- **校验 1B(防拆印)**:和校验或 CRC8 — 邮递员篡改过内容,防拆印对不上
- **尾 `0D`(封口蜡)**:可选,部分协议用来标识"信件完"(冗余设计,有长度其实不需要)

**为什么长度前缀 > 分隔符**:
- 长度前缀:**数据里可以出现任何字节**(包括 `0D`),只要按长度读就行
- 分隔符法:数据里**绝不能出现分隔符**(否则误切帧),工业二进制数据几乎肯定会有这些字节 → **分隔符只适合文本协议**

**⑤ 心跳 + 断线重连**（🟦，呼应 M9 容错）
- 每 5s 发 `0x00` 心跳；超时未收到响应 → 标记断线 → 指数退避重连（M9 的 `Retry` 直接复用）。

### 🎭 拟人秒懂:夫妻吵架冷战 + 重拨电话(心跳 + 重连记忆锚点)

> 把 TCP 连接的"假在线"想象成"夫妻吵架冷战,电话还显示通话中,但对方早睡着了"。

- **TCP 的"已连接"是软状态**:OS 只知道"连接握手过",**网线被拔、路由器重启、对端断电,OS 可能几十秒都不知道**链路已经断了
- **心跳 = 周期性"喂"一声**:
  - 每 5s 你发"喂"(0x00 心跳包)
  - 对端正常 5s 内回"嗯"(任何数据都算,或者协议规定的心跳应答帧)
  - 你只要"听到对方说话"就刷新判活时间戳
  - **连续 3 次没回应(15s 静默)→ 判死,触发重连**
- **断线重连 = 重拨电话指数退避**:
  - 第 1 次重连:等 1s(可能只是抖动)
  - 第 2 次:等 2s(可能路由器重启中)
  - 第 3 次:等 4s
  - 第 4 次:等 8s...
  - **为什么指数退避而不是每秒重试**:服务端可能已经崩了在重启,你每秒一次重连 = DOS 攻击自己的服务端;指数退避给对方喘息时间

**前端类比**:WebSocket 不发 ping/pong 就会"假在线" — `ws.readyState === OPEN` 但实际对端早死了。WebSocket 协议层自带 ping/pong,但 TCP 是裸的,**心跳必须应用层自己实现**。

**新人坑**:`SocketException` 不 catch → 进程崩。TCP 网络异常是常态不是异常,**必须有 try-catch + 重连机制**。

### 🔬 掰开揉碎：粘包到底怎么回事
假设你连发两帧 `[AA55 03 ..][AA55 04 ..]`：
- 理想：对面两次 Read 各收一帧。
- 现实：可能一次 Read 收到 `[AA55 03 .. AA55 04 ..]`（**粘包**），也可能 `[AA55 03 ..AA]` + `[55 04 ..]`（**拆包**）。
- **唯一正确做法**：把所有收到的字节塞进累积缓冲，按"头 + 长度"算法循环切帧；切不完整的就留在缓冲等下次。缓冲区不会被"一次 Read"骗到。

### ⭐ 重点 / 🔥 坑
| | 内容 |
|---|---|
| ⭐ 重点 | TCP 无消息边界 → 必须自管累积缓冲 + 切帧 |
| ⭐ 重点 | 长度前缀法 > 分隔符法（工业首选） |
| 🔥 坑 | "Read 一次当一帧"——90% 新手 bug 来源 |
| 🔥 坑 | 对端正常断开 `Read` 返回 **0**；硬断开（RST / 网线）抛 `SocketException`，两者都要处理 |
| 🔥 坑 | `ReadAsync` 不 catch 网络异常 → 进程崩（详见 ②） |
| 🔥 坑 | 中文/多字节用 `Encoding.UTF8` 一致；切帧按**字节**不是按字符 |
| 🔥 坑 | 后台线程直接改 UI 会抛（见 Day2 / M0） |

### 🟢 基础题
用 `TcpListener` 写一个回显服务端，`TcpClient` 连上后发 "HELLO" 收到原样返回。

### 🟡 进阶题
在回显服务端基础上，给客户端加一个"后台线程每秒发一条 `AA 55 <len> <cmd> <data> <sum>`"，服务端按长度前缀切帧后回显"收到帧数"。

### 🔴 挑战题
写一个 `TryParseFrames(MemoryStream buf)` 静态方法：支持**粘包**（一次塞两帧）和**拆包**（一帧被拆成两次 Read 才到齐）；用 `LoopbackTcpChannel` 模拟这两种情况并写测试断言两帧都被正确切出。

**✅ 答案（挑战题骨架）**
> 💡 **为什么不用 `MemoryStream.CopyTo`**：它内部循环 byte-by-byte，高频小包时性能炸。`Array.Copy` 走 SIMD，差 10 倍。**前端类比**：在循环里 `arr.push(...bigArr)` vs 用 `TypedArray.set` 批量拷贝，后者快得多。

> 📂 `DaqMonitor.Core/Protocol/FrameParser.cs` · namespace `DaqMonitor.Core.Protocol`
> 🔧 无 NuGet（纯 .NET 类库，`System.IO` 是 BCL）
> 💡 静态工具类，被 `TcpDevice.RecvLoop` 调用（见 Day2「完整代码组装」）

```csharp
// DaqMonitor.Core/Protocol/FrameParser.cs
using System.Collections.Generic;
using System.IO;

namespace DaqMonitor.Core.Protocol;

/// <summary>长度前缀帧解析器：帧格式 [AA 55][长度 2B 大端][命令 1B][数据 N B][校验 1B]</summary>
public static class FrameParser
{
    /// <summary>从累积缓冲区里循环切出所有完整帧；半包留到下次拼接。</summary>
    public static IEnumerable<byte[]> TryParseFrames(MemoryStream buf)
    {
        byte[] header = new byte[4];
        buf.Position = 0;
        while (buf.Length - buf.Position >= 4)            // 至少够读头+长度
        {
            buf.Read(header, 0, 4);                        // AA 55 lenHi lenLo
            int bodyLen = (header[2] << 8) | header[3];
            if (buf.Length - buf.Position < bodyLen)
            {
                // 头已读但体不全: 回退 Position, 让半包(含头)留到下次
                buf.Position -= 4;
                break;
            }
            var body = new byte[bodyLen]; buf.Read(body, 0, bodyLen);
            yield return body;
        }
        // 半包留到下次拼接: 用 Array.Copy 比 MemoryStream 高效 10 倍
        int remaining = (int)(buf.Length - buf.Position);
        if (remaining > 0 && buf.Position > 0)
        {
            Array.Copy(buf.GetBuffer(), buf.Position, buf.GetBuffer(), 0, remaining);
            buf.SetLength(remaining);
            buf.Position = 0;
        }
        else { buf.SetLength(0); buf.Position = 0; }
    }
}
```
> 改进点：① 半包回退 Position，避免丢帧头；② 用 `Array.Copy` 直接在底层 buffer 上原地搬移，零额外分配；③ `remaining == 0` 时直接清空，状态干净。

**🏗️ 项目任务**：实现 `TcpDevice : IDevice`，用长度前缀帧解析，触发 `DataReceived`，在 `Bootstrapper` 注册，DAQ Monitor 点"启动采集"能收到 TCP 模拟器发的点。

**🎓 工控导师说**：调试 TCP 设备，第一招永远是"先用网络调试助手（如 SSCOM 的 TCP 模式 / Hercules）手动连、手动发，确认设备回了啥"，**再写代码**。我见过太多人代码里 `Read` 一次当一帧，设备明明回了，他那边死活解析不出——就是栽在粘包上。累积缓冲 + 切帧，刻进骨头里。

**💼 职业建议**："TCP 粘包怎么处理？"是上位机面试必考题。答"TCP 是字节流无边界，必须自管累积缓冲 + 长度前缀（或分隔符）切帧，绝不 Read 一次当一帧"——这一句直接证明你真写过 Socket 通信，不是只会 `HttpClient`。

### 💓 心跳实现完整版（4 件套）
前面只提了"每 5s 发 0x00 心跳"，没给完整代码——这块在面试里被追问的频率超高，这里把**定时器 + 心跳响应校验 + 超时判活 + 重连触发**完整补上（呼应 M9 容错）。

> 💡 **为什么必须心跳**：TCP 的"已连接"状态是软的，网线被拔 / 路由器重启后 OS 可能几十秒都不知道链路断了。心跳是**应用层探活**，让上位机能秒级感知掉线。**前端类比**：WebSocket 不发 ping 就会"假在线"，明明对端早死了，本地 `readyState` 还是 1。

> 📂 `DaqMonitor.Core/Devices/TcpHeartbeatHost.cs` · namespace `DaqMonitor.Core.Devices`
> 🔧 无 NuGet（`System.Net.Sockets` 是 BCL）
> 💡 被 `TcpDevice` 持有为字段；`ConnectionLost` 事件触发后由 `TcpDevice` 调用 M9 的 `Retry` 重连

```csharp
// DaqMonitor.Core/Devices/TcpHeartbeatHost.cs
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace DaqMonitor.Core.Devices;

public class TcpHeartbeatHost
{
    private readonly TcpClient _client;
    private readonly NetworkStream _ns;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _timeout  = TimeSpan.FromSeconds(15);   // 3 次心跳没回 = 判死
    private DateTime _lastHeartbeatAck = DateTime.UtcNow;
    private Timer? _ticker;

    public event EventHandler? ConnectionLost;   // 触发 M9 的 Retry 重连

    // ① 心跳定时器: 每 interval 同时做两件事 —— 发探活、判超时
    public void Start()
    {
        _ticker = new Timer(_ => Tick(), null, _interval, _interval);
    }

    private async void Tick()
    {
        // A. 超时判活: 长时间没收到对端任何字节, 判死
        if (DateTime.UtcNow - _lastHeartbeatAck > _timeout)
        {
            ConnectionLost?.Invoke(this, EventArgs.Empty);
            Stop();   // 避免重复触发
            return;
        }
        // B. 发心跳 (0x00 单字节, 按你协议可换成 AA 55 00 0X)
        try
        {
            byte[] ping = new byte[] { 0x00 };
            await _ns.WriteAsync(ping, 0, ping.Length);
        }
        catch (SocketException) { ConnectionLost?.Invoke(this, EventArgs.Empty); }
        catch (IOException)     { ConnectionLost?.Invoke(this, EventArgs.Empty); }
    }

    // ② RecvLoop 每收到任何字节就刷新判活时间戳 (心跳响应也算)
    //    把这行加进你的 RecvLoop 收到 n 字节的分支里:
    //    _host.NotifyAlive();
    public void NotifyAlive() => _lastHeartbeatAck = DateTime.UtcNow;

    // ③ 心跳响应校验 (可选, 某些协议对 0x00 回 0x01)
    //    在 TryParseFrames 解出"心跳应答帧"时调用:
    public void OnHeartbeatAck()
    {
        _lastHeartbeatAck = DateTime.UtcNow;   // 校验通过 = 对端活着
    }

    // ④ 重连触发 (交给 M9 的 Retry)
    public void Stop()
    {
        _ticker?.Dispose();
        _ticker = null;
    }
}
```

| 件 | 作用 | 不做的后果 |
|---|---|---|
| ① 定时器 | 周期发探活 + 判超时 | 假在线，掉线半天不知道 |
| ② 判活时间戳 | RecvLoop 收到字节就刷新 | 对端半死（发了数据但心跳不回）感知不到 |
| ③ 应答校验 | 协议层确认心跳被回 | 链路单向通（发得出收不到）漏判 |
| ④ 重连触发 | 掉线后交给 M9 `Retry` 退避 | 死了不复活，必须人工重启服务 |

**✅ 打卡[ ]**

---

## Day 2 — 私有协议帧设计 + 接入 DAQMonitor 🟡

### 一句话讲清楚
真实设备手册会给"通信协议"：帧格式、命令字、数据域含义、校验算法。你的 job 是把手册翻成"封帧函数 + 解析函数"。

### 前端类比秒懂
| 协议设计 | 前端类比 |
|---|---|
| 封帧函数 `BuildFrame` | 构造一个带类型字段的 JSON 请求体 |
| 解帧函数 | 解析后端返回的带约定结构的数据 |
| 校验和 | 请求签名 / 数据完整性校验 |

### 分点精讲
**① 典型私有帧**
```
[头 AA55][长度 2B][命令 1B][数据 N B][校验 1B][尾 0D]
```
- 校验：和校验（所有字节相加取低 8 位）或 CRC8（比 M2 的 CRC16 轻）。
- 数据域：按手册拆成"通道号 + 原始值"，再交给 M12 的工程量转换。

**② 封帧 / 解帧对称**

> 📂 把 `BuildFrame` / `Checksum` 加到 `DaqMonitor.Core/Protocol/FrameParser.cs`(就是 Day1 那个静态类)
> 🔧 无 NuGet
> 💡 `BuildFrame` 是封帧(发送方用),`TryParseFrames` 是解帧(接收方用),两者对称

```csharp
// 加在 FrameParser 静态类里(同 Day1 那个文件)
public static byte[] BuildFrame(byte cmd, byte[] payload)
{
    var ms = new MemoryStream();
    ms.Write(new byte[] { 0xAA, 0x55 });
    ms.Write(BitConverter.GetBytes((short)(payload.Length + 1)));
    ms.Write(new[] { cmd });
    ms.Write(payload);
    ms.Write(new[] { Checksum(ms.ToArray()) });
    return ms.ToArray();
}

public static byte Checksum(byte[] d)
{
    int s = 0; foreach (var b in d) s += b; return (byte)(s & 0xFF);
}
```

**③ 接进 DAQMonitor（零改采集层）**
- `TcpDevice` 实现 `IDevice`；`Bootstrapper` 里 `services.AddSingleton<IDevice, TcpDevice>()` 一行切换，UI/管道/报警**完全不用动**——这就是 M0 面向接口的价值。

### 🔬 掰开揉碎：校验到底防什么
串口/网线传输偶尔会"翻转一位"（电磁干扰）。校验和/CRC 就是"收完一帧算一遍，对不上就丢"——**宁可丢一帧，也不让错误数据进报警/报表**。M2 的 Modbus CRC16、M1 的串口 CRC、本模块的和校验，本质都是同一件事：用一点算力换数据可信。

### ⭐ 重点 / 🔥 坑
| | 内容 |
|---|---|
| ⭐ 重点 | 封帧/解帧对称：发的格式 = 收的格式 |
| 🔥 坑 | 校验覆盖的范围要和对方一致（含头还是不含头） |
| 🔥 坑 | 字节序：多字节长度/数值用 `BitConverter` 时要确认大小端 |
| 🔥 坑 | 心跳和取数分清：探活归探活，别和正常数据帧混在一起 |

### 🟢 基础题
写一个 `Checksum(byte[] data)` 和校验（所有字节相加取低 8 位），并用它给一帧 `[AA 55 02 01 0A]` 算出校验字节。

### 🟡 进阶题
把 `BuildFrame` 和 `TryParseFrames` 串起来：构造一帧 → 塞进缓冲 → 切出来 → 断言命令字和数据一致（端到端自洽测试）。

### 🔴 挑战题
给 `TcpDevice` 加"断线自动重连"：捕获 `IOException`/`SocketException` 后用 M9 的 `Retry.ExecuteAsync` 指数退避重连，并重订阅 `RecvLoop`；写测试模拟"连接断开后恢复"断言能继续收数。

**✅ 答案（基础题）**
```csharp
byte Checksum(byte[] d) { int s = 0; foreach (var b in d) s += b; return (byte)(s & 0xFF); }
// [AA 55 02 01 0A] → 0xAA+0x55+0x02+0x01+0x0A = 0x112 → 低 8 位 = 0x12
```

**🏗️ 项目任务**：把 `TcpDevice` 接进 DAQ Monitor：在 `Bootstrapper` 注册，UI 点启动后能收到 TCP 模拟器（你 Day1 写的回显/发帧端）发来的点。

**🎓 工控导师说**：私有协议最坑的不是"怎么写"，而是"两边对不上"——你算 CRC 含头、对方不含头，调三天调不通。我的习惯是：**先拿一个已知样本帧（设备手册给的示例），把你的解帧函数跑一遍，逐字节核对**，确认解析对了再写业务逻辑。别凭空猜协议。

**💼 职业建议**：能独立"读懂一份私有协议文档 → 写出封帧/解帧 → 接进采集系统"的人，在上位机岗非常稀缺。这是 M11 给你的"可写进简历"的硬实力，面试时带一台模拟器现场 demo 最炸。

**✅ 打卡[ ]**

---

## 🎤 M11 整体面试 3 分钟讲法(模块综合)

> "TCP 是字节流,**没有消息边界**,这是新人 100% 翻车的地方。`Read` 一次 ≠ 一帧,必须自管累积缓冲 + 切帧状态机。
>
> **切帧我用长度前缀法**:帧格式 `[AA 55][长度 2B][命令 1B][数据 N B][校验 1B]`。先读 4 字节头拿到长度 N,再精确读 N 字节体。**累积缓冲 + 循环切帧**,粘包(一次收多帧)和拆包(一帧分多次收)都能处理。
>
> **粘包状态机的关键**:半包时回退 Position 让帧头留在缓冲等下次拼接;切完的帧从缓冲前部移走,用 `Array.Copy` 原地搬移比 MemoryStream 高效 10 倍。
>
> **心跳保活**:TCP 的'已连接'是软状态,网线拔了 OS 几十秒不知道。我每 5s 发心跳包,RecvLoop 收到任何字节就刷新判活时间戳,15s 静默判死。**指数退避重连**(1s/2s/4s/8s)防止 DOS 自己的服务端。
>
> **异常处理**:`ReadAsync` 必须 catch `SocketException`(硬断开 RST)和 `IOException`(网线拔),`n==0` 是对端正常关闭 FIN,三种情况都要处理否则进程崩。
>
> 我项目里 `TcpDevice : DeviceBase`,实现了 IDevice 接口,在 Bootstrapper 一行注册切换,UI 和采集管道零改动 — 这就是面向接口的价值。"

**面试官可能连环追问**:
- "粘包和拆包具体怎么处理?" → 累积到 MemoryStream,循环读头+长度+体,半包时回退 Position 留到下次,完整帧 yield return。**核心:不假设一次 Read 等于一帧**
- "为什么用长度前缀不用分隔符?" → 长度前缀允许数据域出现任意字节(包括分隔符本身),分隔符法数据里不能出现分隔符。工业二进制协议数据域几乎肯定会有这些字节,所以首选长度前缀
- "心跳为什么用应用层而不是 TCP keepalive?" → TCP keepalive 默认 2 小时才探一次,太慢;且修改系统参数需要 root 权限。应用层心跳可以 5-15s 探一次,且能携带业务信息
- "指数退避为什么必须有上限?" → 不设上限会无限重试耗资源;一般上限 30s 或 1min,达到上限后保持等间隔重试。同时要有总重试次数限制(避免日志爆炸)
- "怎么调试 TCP 协议?" → 网络调试助手(SSCOM/Hercules)手动连手动发,确认设备回了什么;Wireshark 抓包逐字节看;先确认协议对了再写代码
- "怎么处理 TCP 帧头找不到的情况?" → 状态机有"找帧头"状态,逐字节扫描直到匹配 `AA 55`,丢弃前面的"垃圾字节"。这是流式协议的标准做法

## 📌 温故知新 / 跨模块联动
- **M0 并发**：`RecvLoop` 跑在 `Task`/后台线程，UI 不卡（同 M0 Day7）。
- **M1 串口**：串口也是"字节流 + 事件"，M1 的解析函数**直接复用**到 TCP——区别只是"水从串口来还是从网口来"。
- **M9 容错**：心跳超时 → 用 M9 的 `Retry` 重连；切帧缓冲思路与 M1 的 `FrameParser` 同源。
- **M12**：解出的原始值交给 M12 工程量转换才变成真实物理量。

## 🧩 完整代码组装（TcpDevice 可直接抄进工程）

> 📂 `DaqMonitor.Core/Devices/TcpDevice.cs` · namespace `DaqMonitor.Core.Devices`
> 🔧 无 NuGet（`System.Net.Sockets` 是 BCL）
> 💡 依赖 `DeviceBase` / `SensorPoint`(前置类型定义) + `FrameParser.TryParseFrames`(Day1 静态类) + `TcpHeartbeatHost`(本模块「心跳实现完整版」)

```csharp
// DaqMonitor.Core/Devices/TcpDevice.cs
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Protocol;   // 用 FrameParser.TryParseFrames

namespace DaqMonitor.Core.Devices;

public class TcpDevice : DeviceBase
{
    private readonly string _host; private readonly int _port;
    private TcpClient? _client; private NetworkStream? _ns;
    private readonly MemoryStream _acc = new();
    private TcpHeartbeatHost? _heartbeat;   // 心跳探活, 见 Day1 「心跳实现完整版」

    public TcpDevice(int id, string name, string host, int port) : base(id, name)
        => (_host, _port) = (host, port);

    public override void Connect()
    {
        _client = new TcpClient(); _client.Connect(_host, _port);
        _ns = _client.GetStream(); State = DeviceState.Online;
        _heartbeat = new TcpHeartbeatHost(_client, _ns);
        _heartbeat.ConnectionLost += (s, e) => _ = ReconnectAsync(CancellationToken.None);
        _heartbeat.Start();
        _ = RecvLoop(CancellationToken.None);
    }
    public override void Disconnect() { _heartbeat?.Stop(); _ns?.Close(); _client?.Close(); State = DeviceState.Offline; }

    private async Task RecvLoop(CancellationToken ct)
    {
        var buf = new byte[4096];
        while (_ns is { } ns && !ct.IsCancellationRequested)
        {
            int n;
            try { n = await ns.ReadAsync(buf, 0, buf.Length, ct); }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            { /* 客户端硬断开, 优雅退出 */ break; }
            catch (IOException) { break; }   // 网络中断: 网线拔掉 / 路由器重启
            catch (OperationCanceledException) { break; }   // 正常关闭
            if (n == 0) break;   // 对端正常关闭 (FIN)
            _heartbeat?.NotifyAlive();    // 收到任何字节都算对端活着
            _acc.Write(buf, 0, n);
            foreach (var body in FrameParser.TryParseFrames(_acc))
                RaiseData(Id, body[0]);   // 简化: 首字节当值, 真实按协议拆
        }
        // 退出循环 = 掉线, 触发重连 (TcpHeartbeatHost 也会判活触发, 双保险)
        _ = ReconnectAsync(ct);
    }
    private async Task ReconnectAsync(CancellationToken ct)
    {
        State = DeviceState.Offline;
        // 简化版重连: 真实场景用 M9 的 Retry.ExecuteAsync 指数退避
        await Task.Delay(1000, ct);
        try { Connect(); } catch { /* 重连失败, 等 Retry 再试 */ }
    }
}
```
> 接进工程：在 `Bootstrapper` 里 `services.AddSingleton<IDevice>(_ => new TcpDevice(3, "TCP-01", "127.0.0.1", 502));`，UI 与采集层一行不用改。

## 🔗 明日预告
**M12 工程量转换 与 企业数据库（SQL Server / MySQL）**：今天解出的"原始字节值"还不能直接显示给人看——要标定成真实温度/压力，还要能存进企业最常见的 SQL Server/MySQL。这就是 M12 要解决的。

## 📚 延伸阅读
- Microsoft Learn · [TcpClient 类](https://learn.microsoft.com/zh-cn/dotnet/api/system.net.sockets.tcpclient)
- Microsoft Learn · [NetworkStream](https://learn.microsoft.com/zh-cn/dotnet/api/system.net.sockets.networkstream)
- SuperSocket（国产 TCP 框架）· [GitHub](https://github.com/kerryjiang/SuperSocket)

## 📎 关联附录
- 工程量转换见 **M12**；多设备接入见 **M1/M2/M3/M13**；断线重连见 **M9**。
