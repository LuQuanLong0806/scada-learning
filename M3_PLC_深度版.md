# M3 — PLC 通信（西门子 S7）⭐ 13K 硬通货

> **优先级定位**：🔴 必学 · PLC(S7) 直连（JD 主干必会）
> **技术来源**：🟧 第三方 NuGet `S7.Net Plus`（`dotnet add package S7.Net`）。
> **给简历加的能力**：直连 PLC 读写 DB 块 —— 工业现场**最核心**、面试最加分的对接。
> **前置**：M1（串口/字节）/ M2（寄存器/轮询/重试）。
> **前端类比总纲**：PLC 像"一台永远在跑的后端服务"，DB 块是它的数据库表，上位机是"前端客户端"，S7 协议就是你们的"私有 API"。

---

> ⏱️ **阅读路径**(按时间预算选入口)
> - **3 分钟**:看「模块目标」+ 「Day 1 一句话讲清楚」— 知道 PLC 是工业控制大脑
> - **30 分钟**:加看 Day 1 前端类比表 + S7.Net 建立连接
> - **3 小时**:全文精读 + Day 2 读 DB1.DBX0.0 / DB1.DBD4 + Day 3 错误码排查
> - 🎯 **面试高频**:**IsConnected 不可信要靠 Read 验证** / DB 块地址(DBX 位/DBB 字节/DBD 双字)/ 优化块访问 / S7.Net vs Modbus 选型
> - 🔁 **配套复习**:[速记卡 Q12-Q13](面试高频知识点_速记卡.md) · [间隔重复表](记忆与复习机制_间隔重复版.md)

> 📚 **前置语法**(M3 用到的,陌生请查 [C# 语法速查 — 前端视角](CSharp语法速查_前端视角.md))
> - `class Device : IDevice` — 类继承接口,速查 §12
> - `async Task<bool>` / `await` — 异步,速查 §8
> - `event EventHandler<T>?` — 事件发布订阅,速查 §7
> - `using var plc = new Plc(...)` — 资源释放,速查 §13
> - `Interlocked.Exchange(ref _isConnected, value)` — 原子操作,速查 §14
> - `CancellationToken` — 取消令牌,速查 §8

> 📦 **前置类型**(本模块示例代码用到的核心自定义类型)
> M3 示例引用 `DeviceBase` / `IDevice` / `SensorPoint` 等类型 — 这些在 [📦 前置类型定义 · 学员粘贴版](前置类型定义_学员粘贴版.md) **集中定义**。**遇到"找不到类型 XXX"报错,先去那份文档复制对应类型**,在项目里建 `_PredefinedTypes.cs` 粘进去就能跑。本模块会**新建** `PlcDevice : DeviceBase`(用 S7.Net 库),跟着 Day 1-3 敲。

## 模块目标
用 S7.Net 连接一台西门子 S7-1200 / 1500 PLC，读取 DB 块里的温度 / 压力点位，写入 DAQ Monitor。学完你能讲清：**为什么 `IsConnected` 不可信、错误码怎么判、连不上怎么一步步排查**——这三点是现场和面试的生死线。

---

## Day 1 — 连接 PLC + 读 DB 块 🟡

### 一句话讲清楚
PLC 把工艺数据存在"DB 数据块"里，上位机用 S7 协议直接读 / 写指定 DB 的偏移地址，像直接读一个结构体字段。它比 Modbus 高级的地方在于：PLC 帮你按**类型**（Real/Bool/Int）解码，不用自己拼字节。

### 前端类比秒懂
| S7 概念 | 前端类比 |
|---|---|
| PLC IP + Rack/Slot | 数据库 host + schema |
| DB 块号 + 偏移 | 表名 + 字段偏移 |
| `ReadBytes` | 按字节读一行 |
| `Read(VarType.Real)` | ORM 把列映射成强类型字段 |
| `LastErrorCode` | HTTP 状态码（≠ 连接状态） |

### 分点精讲
**① 连接 + 读字节**（🟧）

> 🔧 **必装 NuGet**(在 `src/DaqMonitor.Core/` 目录执行):
> ```bash
> cd src/DaqMonitor.Core
> dotnet add package S7.Net
> ```
> 💡 **为什么用 Async**：S7.Net 同步 API 会阻塞线程，真实项目里 1 个 PLC 连接可能挂多个读请求，异步才能并发。**前端类比**：像 `Promise.all` 比多个 `await` 快——并发请求才能压榨网络吞吐。

> 📂 **代码放哪**:这段是语法演示,可临时放在 `Program.cs` 跑。**真实工程**放进 `DaqMonitor.Core/Devices/PlcDevice.cs`(继承 `DeviceBase`)。

```csharp
using S7.Net;
using System.Threading.Tasks;

// ⚠️ 外层方法必须标 async Task,不能是 void / 同步方法
public async Task ReadTempAsync()
{
    var plc = new Plc(CpuType.S71200, "192.168.0.1", 0, 1); // Rack=0, Slot=1
    await plc.OpenAsync();
    if (plc.IsConnected)
    {
        byte[] buf = await plc.ReadBytesAsync(DataType.DataBlock, 1, 0, 4); // 读 DB1 偏移0 共4字节
        float temp = S7.GetRealAt(buf, 0);                                  // 一个 float
        Console.WriteLine($"温度={temp}");
    }
}
```
> 注：外层方法签名要改成 `async Task`，调用方 `await` 它；别用 `.Result` / `.Wait()`，会死锁（M0 Day7 讲过）。

**② 直接读强类型变量**（🟧）
```csharp
var temp = (float)(await plc.ReadAsync(DataType.DataBlock, 1, 0, VarType.Real, 1));
var run  = (bool) (await plc.ReadAsync(DataType.DataBlock, 1, 4, VarType.Bit, 1));
```

### 🔬 掰开揉碎：S7 错误模型（现场高频坑 + 面试高频）
和串口/Modbus 不同，**S7.Net 很多失败「不抛异常」，而是返回错误状态**——这是新手最容易栽的地方：
- **`plc.IsConnected` 不可全信**：`Open()` 成功只代表「握手成功」，线中途掉了 `IsConnected` 可能还是 `true`。正确做法：每次读后**检查 `plc.LastErrorCode`**（0 = OK，非 0 = 出错），或读之前 `ReadStatus()` 探活。
- **读失败要判错误码再决定重试**：
  ```csharp
  var buf = await plc.ReadBytesAsync(DataType.DataBlock, 1, 0, 4);
  if (plc.LastErrorCode != 0)            // 非 0 即出错
  {
      Log.Warning("读 DB1 失败 错误码{Code}", plc.LastErrorCode);
      // 用 M9 的 Retry 退避重连，别裸 throw
  }
  ```
- **常见错误码**：`0x0005` 地址/长度非法（DB 号或偏移写错）、`0x000A` 对象不存在（DB 没下载/被删除）、`0x7000+` 通信层错误（断线/PLC 停止）。
- **类比修正**：之前说「DB块号+偏移 = 表名+字段偏移」**不够准**——DB 块是「结构化变量容器」，更像「一个 `class` 实例」，`DB1.DBW2` 是「实例的字段」，不是裸字节表。读字节后用 `S7.GetRealAt(buf,0)` 这种**带类型的解码方法**才是正道（类比 ORM 把行映射成强类型对象）。

### ⚠️ 重点 & 易踩坑
| 项 | 说明 |
|---|---|
| ⭐ DB 号 / 偏移必须与 TIA 一致 | PLC 侧定义 DB1.0=温度(Real)，代码才能对上 |
| ⭐ 优化块访问 | 西门子"优化访问"DB 不能用绝对偏移，要勾掉或用符号访问 |
| 🔥 连不上 | 检查 IP / 子网 / 防火墙 / PLC 是否允许 PUT/GET 通信 |
| 🔥 读错类型 | Real 读成 Int = 全错值；用 `S7.GetRealAt` 等辅助方法 |
| 🔥 频繁 Open | 保持长连接，断线再重连，别每次读都 Open |

### 📋 S7.Net 错误码表（查码 + 处理策略）
S7.Net 不抛异常而是塞 `LastErrorCode`，现场/面试常被追问"0x0005 是啥、怎么办"。对照表（呼应 M2 的 Modbus 异常码表风格）：

| LastErrorCode | 含义 | 典型原因 | 处理策略 |
|---|---|---|---|
| `0x0000` | 成功 | —— | 正常处理数据 |
| `0x0005` | 地址 / 长度非法 | DB 号或偏移写错、越界、点位类型不匹配 | 对 TIA Portal 的偏移表逐字节核对，别凭记忆 |
| `0x000A` | 对象不存在 | DB 块没下载 / 被删除 / 没勾"允许 PUT/GET" | 检查 PLC 侧 DB 是否在线、权限是否开 |
| `0x7000+` | 通信层错误 | TCP 断线 / PLC 停止 / 心跳超时 | 触发 M9 `Retry` 退避重连，别裸 throw |
| `0xD005` / `0xD201` | 套接字/连接错误 | 握手失败、IP 不通 | 排查链路（见下方导师说 6 步），重连 |

> 处理铁律：**别用异常**，按"判码 → 退避重试 → 仍失败则上报离线"的三段式。错误码会随 S7.Net 版本微调，遇到没见过的码去 [S7.Net Plus 源码](https://github.com/killnine/s7netplus) 的 `ErrorCode.cs` 直接查表。

### 🟢 基础题
连接模拟 PLC（PLCSIM + NetToPLCSim），读 DB1 偏移 0 的 Real 并打印。

### 🟡 进阶题
读 DB1 偏移 0 的 Real（温度）和偏移 4 的 Bit（运行信号），拼成一个 `Model` 对象打印。

### 🔴 挑战题
写一个 `ReadWithCheck(Plc plc)` 方法：读前 `ReadStatus()` 探活，读后判 `LastErrorCode`，非 0 时返回 `null` 并打日志（不抛异常），模拟"生产中读失败但系统不崩"。

**✅ 答案（挑战题骨架）**
```csharp
async Task<float?> ReadTempSafeAsync(Plc plc)
{
    if (plc.ReadStatus() != 0) { Log.Warn("PLC 离线"); return null; }
    var buf = await plc.ReadBytesAsync(DataType.DataBlock, 1, 0, 4);
    if (plc.LastErrorCode != 0) { Log.Warn("读失败 {Code}", plc.LastErrorCode); return null; }
    return S7.GetRealAt(buf, 0);
}
```

**🏗️ 项目任务**：建 `PlcDevice : DeviceBase`（参考工程里 `CanDevice`/`UsbHidDevice` 的同套路），`Connect()` 里 `Open()`，`Read(addr)` 读 DB 变量并映射成点；后台轮询。DAQ Monitor 现在能直连 PLC。（代码见文末「完整代码组装」，接进工程待下一轮落地。）

**🎓 工控导师说**：我带过一学员，现场连 PLC 死活连不上，折腾一下午。最后发现是 PLC 的"允许 PUT/GET 通信"没勾——**TIA Portal 里这个选项默认是关的**。连不上的排查顺序永远是：①网线/PN口灯亮？②IP 同网段？③防火墙？④PUT/GET 勾了？⑤Rack/Slot 对（1200 是 0/1，1500 常是 0/1）？⑥DB 块下载了？一步步来，别瞎猜。

**💼 职业建议**：PLC 对接是 13K 硬通货。面试被问"你连过 PLC 吗"，不仅要答"用 S7.Net"，更要答出"**IsConnected 不可信，靠 LastErrorCode 判错 + 重试重连**"——这句话直接证明你踩过坑、有现场经验。

**✅ 打卡[ ]**

---

## Day 2 — 写 PLC + 结构体映射 + 多设备统一 🟡

### 一句话讲清楚
上位机下发设定值到 PLC（写 DB），并用"结构体映射"把一批点位一次读出来，再用 M0 Day 5 的 `IDevice` 统一调度多设备——这是 DAQ Monitor "多设备接入"达标的临门一脚。

### 分点精讲
**① 写 DB**（🟧）
```csharp
plc.Write(DataType.DataBlock, 1, 4, 1.0f);   // 写 Real 设定值
plc.Write("DB1.DBW2", 100);                  // 也可用符号 / 绝对地址
```

**② 一次读多个点位 + 映射**（🟧🟦）
```csharp
byte[] buf = plc.ReadBytes(DataType.DataBlock, 1, 0, 12);
var model = new
{
    Temp  = S7.GetRealAt(buf, 0),
    Press = S7.GetRealAt(buf, 4),
    Run   = S7.GetBitAt(buf, 8, 0)
};
```

**③ 接入统一接口**（呼应 M0 Day 5）
```csharp
IDevice dev = new PlcDevice(1, "PLC-01", new[] { new PlcDevice.PlcMap(1, "DB1.DBW0") });
dev.DataReceived += (s, e) => Console.WriteLine($"点{e.PointId}={e.Value}");
dev.Connect();   // 同步门面:内部起后台轮询循环(与 SimulatedDevice 同套路)
```

### ⚠️ 重点 & 易踩坑
| 项 | 说明 |
|---|---|
| ⭐ 写生产 PLC 要授权 | 现场写 PLC 可能直接动设备，必须走审批 / 测试环境 |
| ⭐ 统一接口价值 | UI 不关心串口 / Modbus / PLC，全用 `IDevice` —— 简历亮点 |
| 🔥 读取频率 | PLC 扫描周期有限，轮询别太猛（<50ms 易压力设备） |
| 🔥 event 退订 | `event +=` 后必须 `-=`，否则订阅方生命周期结束也无法被 GC（详见上文「退订模式」） |

### 🟢 基础题
写 DB1 偏移 4 一个 Real 设定值 1.0。

### 🟡 进阶题
读 DB1 前 12 字节映射成 Temp/Press/Run 三个字段并打印。

### 🔴 挑战题
在 DAQ Monitor 里同时挂 `SerialDevice` + `PlcDevice`，用 `List<IDevice>` 统一轮询（参考 `AcquisitionPipeline`），验证"多设备接入"达标——写出轮询调度骨架。

**✅ 答案（挑战题骨架）**
```csharp
var devices = new List<IDevice> { new SerialDevice(1, "COM3", ...), new PlcDevice("192.168.0.1") };
foreach (var d in devices) { d.Connect(); d.DataReceived += OnData; }
// AcquisitionPipeline 按设备统一轮询，UI 零感知背后是串口还是 PLC
```

### 🧯 退订模式（不补就是内存泄漏）
> 💡 **为什么必须退订**：`event +=` 后若忘了 `-=`，订阅方（比如某个临时窗口 / ViewModel）即便被关闭，发布方（设备）仍持有一条指向它的委托引用 → **GC 永远回收不掉** → 越跑内存越大。**前端类比**：像 React 在 `useEffect` 里加了 `event listener` 但 `return` 里忘了 `removeEventListener` —— 组件卸了还在偷偷听、偷偷 setState，最后崩。

**正确姿势：用 `IDisposable` 把"订阅 + 退订 + 释放"打包**
```csharp
public sealed class DeviceHost : IDisposable
{
    private readonly List<IDevice> _devices = new();
    private bool _disposed;

    public void Add(IDevice d)
    {
        _devices.Add(d);
        d.DataReceived += OnData;        // 订阅
    }

    private void OnData(object? s, SensorPoint e) => /* 转发到 UI / 管道 */;

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var d in _devices) d.DataReceived -= OnData;          // 关键: 退订
        foreach (var d in _devices.OfType<IDisposable>()) d.Dispose(); // 关设备
        _devices.Clear();
        _disposed = true;
    }
}
```
> 用法：`using var host = new DeviceHost(); host.Add(plcDev); host.Add(serialDev);` —— `host` 出作用域自动退订 + 关设备，绝无遗漏。

| ⚠️ 坑点 | 后果 / 处理 |
|---|---|
| 🔥 `event +=` 后必须 `-=` | 否则订阅方生命周期结束也无法被 GC，内存泄漏 |
| 🔥 忘记把 `_disposed = true` | 重复 `Dispose` 会重复 `-=`（虽然不报错，但脏） |
| 🔥 退订顺序错误 | 先关设备再退订，可能在多线程下错过最后一次事件 → 先 `-=`，再 `Dispose()` |

**🏗️ 项目任务**：`PlcDevice` 完成 `Write`；配合 `AcquisitionPipeline` 把 `PlcDevice` 和已有 `SerialDevice`/`ModbusDevice` 统一调度 —— 至此"多设备接入"达标（M0–M3 串联）。

**🎓 工控导师说**：现场最怕"实习生手一抖把设定值 1.0 写成 100.0 写进 PLC，产线直接超压"。**写 PLC 前必须 double check 地址和量纲**，最好加一层"写前确认 + 写后回读校验"。这是保命习惯。

**💼 职业建议**：能讲"多设备统一 IDevice 调度 + 写 PLC 的回读校验"的候选人，在工控岗极其稀缺——因为这需要既懂架构又懂现场风险。

**✅ 打卡[ ]**

---

## 📌 温故知新（跨模块联动）
- **M0 Day5 `IDevice` 接口 → 这里最经典的落地**：`PlcDevice` 实现统一接口，UI 不关心背后是串口/Modbus/PLC —— **这就是「面向接口」的价值，也是 15K 面试必讲的点**。
- **M1 字节解码 → 这里 `S7.GetRealAt`**：本质一样是「字节 → 强类型」，只是 PLC 帮你按 DB 结构解。
- **M2 轮询/重试 → 这里同样适用**：PLC 扫描周期有限，轮询别 <50ms；通信错用 M9 的 `Retry` 退避。
- **前瞻 M9**：多设备统一调度最终由 `AcquisitionPipeline` 收口，别各自写轮询循环；心跳重连见 M9 Day4 + M15。

## 🧩 完整代码组装（PlcDevice,与参考工程一字不差）

> 📂 `DaqMonitor.Core/Devices/PlcDevice.cs` · namespace `DaqMonitor.Core.Devices`
> 🔧 模拟模式**零 NuGet**;真实模式需在 `src/DaqMonitor.Core/` 执行 `dotnet add package S7NetPlus`(工程为保持零依赖可编译,真实路径以注释保留)
> 💡 继承 [`DeviceBase`](前置类型定义_学员粘贴版.md),`RaiseData(pointId, value)` 触发事件。**接口是同步的** `Connect()`,轮询在内部 `Task.Run` 后台循环跑 — 这是工程"同步门面 + 内部异步"的标准套路(参 SimulatedDevice)

```csharp
// DaqMonitor.Core/Devices/PlcDevice.cs(工程真实文件,模拟模式默认开,零硬件可跑)
using DaqMonitor.Core.Models;

namespace DaqMonitor.Core.Devices;

public sealed class PlcDevice : DeviceBase
{
    // 点位 → PLC 地址映射,如 "DB1.DBW0"(数据块1、字0)
    public sealed record PlcMap(int PointId, string DbAddress);

    private readonly bool _simulate;
    private readonly List<PlcMap> _maps;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly Random _rnd = new();
    private readonly Dictionary<int, double> _last = new();

    public PlcDevice(int id, string name, IEnumerable<PlcMap> maps, bool simulate = true)
        : base(id, name)
    {
        _simulate = simulate;
        _maps = maps.ToList();
    }

    public override void Connect()
    {
        State = DeviceState.Connecting;
        Start();                              // 起后台轮询循环
        State = DeviceState.Online;
    }

    public override void Disconnect()
    {
        Stop();
        State = DeviceState.Offline;
    }

    private void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    Tick();
                    await Task.Delay(500, token);
                }
            }
            catch (OperationCanceledException) { /* 正常退出 */ }
        }, token);
    }

    private void Stop()
    {
        _cts?.Cancel();
        try { _loop?.Wait(500); } catch { /* 忽略 */ }
        _cts?.Dispose(); _cts = null; _loop = null;
    }

    private void Tick()
    {
        if (_simulate)   // 模拟模式:随机值,零硬件跑通链路
        {
            foreach (var m in _maps)
            {
                double v = Math.Round(20 + _rnd.NextDouble() * 70, 2);
                _last[m.PointId] = v;
                RaiseData(m.PointId, v);
            }
            return;
        }

        // —— 真实 S7.Net 写法(需 dotnet add package S7NetPlus,填 PLC 的 IP)——
        // using S7;
        // var plc = new Plc(CpuType.S71200, "192.168.0.1", 0, 1);
        // plc.Open();
        // try
        // {
        //     foreach (var m in _maps)
        //     {
        //         var raw = (short)plc.Read(m.DbAddress);
        //         if (plc.LastErrorCode != 0) continue;   // IsConnected 不可全信,看错误码
        //         _last[m.PointId] = raw;
        //         RaiseData(m.PointId, raw);
        //     }
        // }
        // finally { plc.Close(); }
    }

    public override double Read(int addr)
        => _last.TryGetValue(addr, out var v) ? v : double.NaN;

    public override void Write(int addr, double value)
    {
        if (_simulate) return;
        // 真实模式:plc.Write(map.DbAddress, (short)value);
    }
}
```
> 接进工程：在 `Bootstrapper` 里 `services.AddSingleton<IDevice>(_ => new PlcDevice(2, "PLC-01", "192.168.0.1"));`，UI 与采集层一行不用改。参考 `CanDevice.cs` 的注册方式。

## 📎 附录 · 西门子通讯全景(15K 必考扩展)

> 30 天路线 M3 主力用 S7.Net,本附录补"西门子通讯全景",覆盖 6 种访问方式 + 1200 vs 1500 差异 + 5 个实战陷阱。**13K 不要求背全部,但 15K 半导体/汽车/新能源岗至少追到第二层**。

### A. 4 层架构(从物理层到上位机)

```
物理层:    RS485(PROFIBUS / MPI / USS)        以太网(PROFINET / 普通 TCP)
              ↓                                   ↓
链路层:    PROFIBUS DP(老,逐步淘汰)        PROFINET RT(普通,延迟<10ms)/ IRT(硬实时,<1ms,伺服场景)
              ↓                                   ↓
应用层:    S7 协议(私有,Snap7/S7.Net 反向实现)  Modbus TCP(1200/1500 可作 Server)
              ↓                                   ↓
上位机访问: S7.Net / Snap7 / Sharp7 / OPC UA / 自定义 TCP / TIA Portal SDK
```

**记忆口诀**:**"物理 RS485 或以太网 → 链路 PB 或 PN → 应用 S7 或 Modbus → 上位 S7.Net/Snap7/OPC UA"**

### B. 上位机访问西门子的 6 种方式(.NET 视角)

| 方式 | 库 / 类型 | 适用 PLC | 性能 | 学习成本 | 何时用 |
|---|---|---|---|---|---|
| **S7.Net** | 开源 NuGet,纯 C# 托管 | 200smart / 1200 / 1500 | 中 | 低 | **学习首选**,30 天主力 |
| **Snap7** | C++ 原生 + .NET wrapper | 全系 200/300/400/1200/1500 | 高 | 中 | 15K 半导体/汽车岗常考 |
| **Sharp7** | Snap7 的纯 C# 移植版 | 同 Snap7 | 中 | 低 | 不想用 C++ 绑定时 |
| **OPC UA** | OpcUaNet / OPCFoundation | S7-1200/1500(内置 Server) | 中 | 中 | 跨厂商标准化访问,M7 选做 |
| **Modbus TCP** | NModbus4 / MQTTnet | 1200/1500(配置为 Server) | 低 | 低 | 简单跨平台场景 |
| **TIA Portal SDK** | Siemens 官方 | TIA 项目集成 | - | 高 | 上位机少用,SCADA 用 |

**核心结论**:
- **13K** = 会 S7.Net 就够(M3 主力)
- **15K** = S7.Net + 知道 Snap7 / Sharp7 / OPC UA 何时用(本附录)
- **绝不用** = TIA Portal SDK(上位机不需要)

### C. S7-1200 vs S7-1500 通讯差异表(15K 必考)

| 维度 | S7-1200 | S7-1500 |
|---|---|---|
| **PUT/GET 跨 PLC 读写** | 默认**关闭**,需 TIA 配置开启 | 默认**开启** |
| **优化块访问** | 早期版本绝对寻址,新版默认优化 | **默认优化访问** |
| **PROFINET IRT** | 不支持(只有 RT) | **支持**(伺服/高精度场景) |
| **OPC UA Server** | 内置基础版 | 内置完整版 |
| **S7 连接数上限** | 默认 8-10 个(Web Server/HMI 都占) | 32+ 个 |
| **机架/槽号(rack/slot)** | 0/0 | 0/0 |
| **TSAP 寻址** | 兼容但通常用 rack/slot | 兼容但通常用 rack/slot |
| **典型价位** | 5K-15K | 15K-50K+ |

**记忆口诀**:**"1200 什么都默认关(PUT/GET 关、IRT 不支持),1500 什么都默认开"**

### D. 5 个实战陷阱(15K 面试官最爱挖)

#### 陷阱 1:优化访问 vs 非优化访问

- **优化访问**(默认):TIA 按符号哈希存储,**不能用 `DB1.DBX0.0` 绝对地址读**,S7.Net 要用 `plc.ReadClass<T>()` 按结构体读
- **非优化访问**(取消勾选):按绝对偏移存储,可用 `plc.Read("DB1.DBX0.0")`
- **现场常见坑**:从客户那拿到的程序是优化访问,你的 S7.Net 直接读 `DB1.DBX0.0` 报错"无法访问块" → 在 TIA 里取消"优化块访问"重新下载,或改用 `ReadClass<T>`

#### 陷阱 2:PUT/GET 必须先开启(S7-1200)

- 路径:TIA Portal → PLC 属性 → "防护与安全" → 勾选"允许 PUT/GET 通信访问"
- 否则 S7.Net 报"无法访问块" → 新手会以为代码错了找半天,实际是 PLC 配置

#### 陷阱 3:连接数限制(S7-1200 默认 8-10 个)

- HMI 触摸屏 + Web Server + OPC UA Server + 上位机 = 很快顶满
- 多客户端采集要在 PLC 配置里加连接数,或用 OPC UA 做中间层(多个客户端共享一个连接)
- **现象**:多开几个上位机就连不上 PLC,关一个就好了

#### 陷阱 4:机架/槽号(rack/slot)

| PLC 型号 | rack | slot | 来源 |
|---|---|---|---|
| S7-1200 | 0 | 0 | 以太网寻址,不真用机架 |
| S7-1500 | 0 | 0 | 同上 |
| S7-300 | 0 | 2 | CPU 默认在槽 2 |
| S7-400 | 0 | 取决于型号 | 通常 2-4 |
| S7-200 smart | 0 | 1 | - |

S7.Net 构造:`new Plc(CpuType.S71500, "192.168.0.1", rack: 0, slot: 0)`

#### 陷阱 5:TSAP 寻址(老协议,被追问就老实说)

- TSAP = Transport Service Access Point,传输服务访问点
- S7-300/400 老协议用 TSAP 寻址(如 `01.00` / `02.00`)而非 rack/slot
- 1200/1500 兼容但通常用 rack/slot
- **被追问就答**:"老协议 S7-300/400 用 TSAP,新机型 1200/1500 用 rack/slot,我项目只用过后者"

### E. 学习建议(13K vs 15K)

- **13K(数据采集/监控/MES/传统制造)**:M3 主线 + 本附录 A/B 两节看完即可,不要求背 1200 vs 1500 表
- **15K(半导体设备/汽车零部件/新能源锂电)**:本附录全部看完,Week 4 投简历前看 Snap7 官方 wiki 30 分钟(http://snap7.sourceforge.net/)
- **简历写法**(诚实版):
  > 西门子 PLC 通信:基于 S7.Net 实现 S7-1200/1500 数据采集,熟悉 DB 块读写(DBX 位/DBW 字/DBD 双字)、PUT/GET 配置、优化访问 vs 非优化访问差异、rack/slot 寻址;了解 Snap7 / Sharp7 / OPC UA / Modbus TCP 等替代方案,可按项目切换

### F. 1 分钟面试讲法模板

> "我们项目用 S7.Net 读西门子 1200/1500,核心是 `IPlcClient` 接口抽象 — 上位机不直接依赖 S7.Net 的 `Plc` 类(它是 sealed 不能 mock),换 Snap7 或 OPC UA 只要写一个新适配器,业务代码零改动。
>
> 遇到最大的坑是 PUT/GET 默认关、优化块访问读不了绝对地址,后来在 IPlcClient 加了一个 `IsOptimizedBlock` 标志,根据 PLC 配置自动切换 `Read(address)` 或 `ReadClass<T>()`。
>
> 性能上从 100 个变量 500ms 优化到 8ms,核心是**批量读整个 DB 块在内存里解析**,而不是逐个点 Read。"

---

## 🔗 明日预告
**M4 数据持久化（EF Core + SQLite）**：把今天采到的 PLC 点位**存进历史库**，支持按时间/点位查询、导出 CSV——让系统从"只看实时"升级成"能查历史"。

## 📚 延伸阅读（卡点时点开）
- S7.Net Plus 仓库 + Wiki：https://github.com/killnine/s7netplus
- PLCSIM + NetToPLCSim（无真机练手）：https://github.com/mesta1/NetToPLCSim
- 全部模块外链汇总见 `外部链接索引.md`
- 📎 **没有硬件？看 `硬件替代方案与讲解_深度版.md`**：PLCSIM+NetToPLCSim 仿真练手 + PLC/DB块科普

## 模块交付清单（M3）
- [ ] S7.Net 连接 PLC（Rack/Slot）
- [ ] 读 DB 字节 / 变量 + 类型辅助方法
- [ ] 写 DB 下发设定值（含回读校验习惯）
- [ ] 结构体批量映射
- [ ] 多设备统一 `IDevice` 调度（M0–M3 串联）
- [ ] `LastErrorCode` 判错 + 不抛异常 （接 M9 重试）
