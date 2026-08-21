# R4 · 真实设备接入(串口 / Modbus / TCP / PLC / CAN / USB-HID)

> **定位**:R2 立了设备契约,这一篇造 6 种"真设备"——全部实现 [IDevice](kp:idevice),上层零改动。没硬件?每种设备都带**回环/模拟通道**,测试照样全绿。
> **前置**:R3 全绿。**预计敲码**:120 分钟。
> **产出**:9 个设备类 + 12 个测试,`dotnet test` 全绿(累计 27)。

---

## 🎯 本篇交付物

```
src/DaqMonitor.Core/Devices/
├─ ISerialChannel.cs          # 串口通道抽象:链路与协议解耦的关键
├─ RealSerialChannel.cs       # 真串口(包 SerialPort,唯一要装 NuGet 的地方)
├─ LoopbackSerialChannel.cs   # 内存回环:零硬件测试串口链路
├─ SerialDevice.cs            # AA55 帧串口设备(R3 FrameParser 的第一个消费者)
├─ ModbusDevice.cs            # Modbus RTU:手搓帧,不依赖 NModbus
├─ TcpDevice.cs               # TCP 长连接+心跳+指数退避重连
├─ PlcDevice.cs               # S7 PLC(模拟模式,真实路径注释保留)
├─ ICanChannel.cs             # CAN 通道抽象
├─ SimulatedCanChannel.cs     # CAN 模拟通道
├─ CanDevice.cs               # CAN 广播帧解码
├─ IHidChannel.cs             # USB-HID 报告通道抽象
├─ SimulatedHidChannel.cs     # HID 模拟通道
└─ UsbHidDevice.cs            # HID 报告解码
src/DaqMonitor.Tests/
├─ SerialDeviceTests.cs       # 5 测试(穿管道测试 R5 再加)
├─ ModbusDeviceTests.cs       # 2 测试
├─ CanDeviceTests.cs          # 2 测试
├─ UsbHidDeviceTests.cs       # 2 测试
└─ TcpFrameParserTests.cs     # +1 测试(TcpDevice 模拟模式)
```

## 📋 需求单(先自己设计,再对照)

| # | 需求 | 验收 |
|---|---|---|
| FR4-1 | 串口通道抽象 `ISerialChannel`(Open/Write/Close + BytesReceived 事件);两个实现:真串口 / 内存回环 | SerialDevice 只认接口,换链路零改动 |
| FR4-2 | SerialDevice:字节流 → R3 的 FrameParser(verifyCrc) → `DataEventArgs`;Read 返回最近值;Write 下发 AA55 命令帧;`RawLog` 联调开关输出 TX/RX 十六进制 | 回环单帧/粘包/半包/坏 CRC 4 个场景全对 |
| FR4-3 | ModbusDevice 双模式:simulate=true 后台轮询;真实模式手搓 RTU 请求帧 → SerialPort → 解析+CRC;RegisterMap 支持 float(跨 2 寄存器+字节序)/word | 模拟模式 700ms 内收到映射点位数据 |
| FR4-4 | TcpDevice:长连接 + ReadAsync 滚动缓冲拼帧;心跳 10s;30s 静默判掉线;断线按 1/2/4/8/16s 指数退避重连;另有 simulate 模式 | 模拟模式 ≥2 个事件 |
| FR4-5 | PlcDevice:S7 模拟模式(真实 S7NetPlus 路径注释保留);记住 M3 铁律:IsConnected 不可信,要看读回的错误码 | 模拟模式收到点位数据 |
| FR4-6 | CanDevice:只认 ID=0x100 温度帧,2 字节大端 ÷10 标定,其它 ID 忽略;UsbHidDevice:report[0]=类型(0x01 温度/0x02 压力)解码 | 25.0℃ / 30.0kPa 解码正确;异 ID 零事件 |
| FR4-7 | 全部测试零硬件可跑(CI 绿) | 12 个新测试全绿 |

**自己先想 10 分钟**:
1. 为什么 SerialDevice 不直接 new SerialPort,而要隔一层 ISerialChannel?(链路可替换 → 回环测试;协议与 IO 解耦)
2. Modbus 真实模式"手搓帧"vs 直接用 NModbus 库,取舍是什么?(懂协议 vs 省事,面试要能讲)
3. TCP 心跳 30s 判掉线,为什么不用 Socket 的 Connected 属性?(Connected 只反映上次收发,不可信——和 PLC IsConnected 同一个坑)

## 📚 本篇知识点

- [AA55 串口帧](kp:serial-frame) · [Modbus 协议](kp:modbus) · [PLC S7](kp:plc-s7) · [指数退避重试](kp:retry-backoff) · [IDevice 统一抽象](kp:idevice) · [xUnit 单元测试](kp:unit-test)

## 🛠️ 参考实现

### ⓪ 装包(整个项目唯一一次动 Core 的 csproj)

```bash
dotnet add src/DaqMonitor.Core package System.IO.Ports --version 8.0.0
```
> 💡 BCL 官方串口库。只有 RealSerialChannel / ModbusDevice 真实模式用到;测试全走回环,不碰真 COM 口。

### ① 串口通道三件套(抽象 + 回环 + 真串口)

> 📂 `src/DaqMonitor.Core/Devices/ISerialChannel.cs` · namespace `DaqMonitor.Core.Devices`
> 🔧 无 NuGet
> 💡 "链路"与"协议"解耦:SerialDevice 只认这个接口——生产换真串口、测试换内存回环,各是一个实现类
> 🗺️ **新手读码地图**(三个类一起看):`ISerialChannel` 只回答一个问题——"字节从哪来、到哪去"(Open/Write/Close + 一个 BytesReceived 事件)。`RealSerialChannel` 包一层 .NET 官方 SerialPort,把硬件收到的字节转成事件;`LoopbackSerialChannel` 是测试替身——Write 进去的字节原样"当成线上收到的"弹回来,零硬件跑通全链路。**前端类比**:把 axios 抽成 `IHttp` 接口——组件只认接口,测试换 msw mock、生产换真 axios;这里的 SerialDevice 就是业务层,对链路具体实现无感。

#### 🏗️ 为什么这样设计:设备为什么不直接用 SerialPort,中间还要隔一层 ISerialChannel?

**当时面临的选择**:

| 方案 | 优点 | 代价 |
|---|---|---|
| SerialDevice 直接 new SerialPort | 少一个接口两个类 | 测试必须插真串口(或虚拟串口对);SerialPort 是 Win32 东西,CI/Linux 上没有 |
| 抽 ISerialChannel,真串口/回环各一实现(选定) | 多写约 60 行 | 多一层间接 |

**为什么选它**:R2 的 IDevice 抽的是"设备长什么样",ISerialChannel 抽的是"**字节从哪来**"——SerialDevice 的职责是协议(组帧/拆帧/超时),它不该关心字节是串口线来的还是内存里造的。**LoopbackSerialChannel 是关键收益**:Write 进去的帧原样弹回来,设备"发出请求→收到响应"的完整时序在无硬件环境下就能穷举测试(响应延迟、坏帧、超时都能模拟)。

**不这样会怎样**:ModbusDevice 直接持有 SerialPort,单测要装 com0com 虚拟串口对,CI 上跑不了;换个 USB 转串口芯片的怪癖行为,设备协议代码跟着遭殃。

**🎤 面试一句话**:"设备协议和传输链路我分开抽:SerialDevice 只认 ISerialChannel,生产包真 SerialPort、测试用内存回环——串口设备'请求-响应'的全时序,包括坏帧和超时,不插硬件就能穷举测试。"

```csharp
namespace DaqMonitor.Core.Devices;

/// <summary>
/// 串口传输通道抽象:把"用什么物理链路收发字节"和"怎么解析协议"解耦。
/// 生产环境用 RealSerialChannel(包 SerialPort,接真实硬件);
/// 没硬件时用 LoopbackSerialChannel(内存回环)也能跑通整条链路。
/// 切换链路 = 换一个实现,协议解析与 UI 一行都不用改。
/// </summary>
public interface ISerialChannel : IDisposable
{
    /// <summary>从"线上"收到一段字节时触发(异步到达,模拟串口 DataReceived)。</summary>
    event Action<byte[]>? BytesReceived;

    /// <summary>打开链路(真实串口即 Open,回环通道为空操作)。</summary>
    void Open();

    /// <summary>向"线下"写出一段字节(命令/置数帧)。</summary>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>关闭链路。</summary>
    void Close();
}
```

📚 **知识点**
- **接口只有 4 个成员,回答一个问题:"字节从哪来、到哪去"**——事件(字节来)+ Open/Write/Close(字节走)。故意不含"解析协议"的任何成员:**链路层不该懂协议语义**,这是 ② SerialDevice 能换链路零改动的前提。
- **`event Action<byte[]>?` 而不是自定义 EventArgs**:载荷就是裸字节,链路层对内容零立场——协议解释权完全交给上层。和前端把 `onMessage(Event)` 里的 `event.data` 直接给业务层一个道理,传输层不猜格式。
- **继承 `IDisposable`**:串口/socket 是操作系统资源,不用必须释放——接口声明周期契约,编译器强制 using/Dispose 纪律。

> 📂 `src/DaqMonitor.Core/Devices/LoopbackSerialChannel.cs`

```csharp
namespace DaqMonitor.Core.Devices;

/// <summary>
/// 内存回环通道(零硬件):Write 进来的字节,直接作为"从线上收到的数据"异步回调出去。
/// 用途:演示 / 单元测试——不需要任何真实串口,也不需要 com0com 虚拟串口,就能验证
/// SerialDevice 的协议解析与"换设备 UI 零改动"是否真的成立。
/// 生产环境别用它,它不接任何硬件。
/// </summary>
public sealed class LoopbackSerialChannel : ISerialChannel
{
    public event Action<byte[]>? BytesReceived;

    public void Open() { /* 回环通道无需打开 */ }

    public void Write(ReadOnlySpan<byte> data)
    {
        var copy = data.ToArray();
        // 用后台线程回调,模拟串口"异步到达",更贴近真实行为
        Task.Run(() => BytesReceived?.Invoke(copy));
    }

    public void Close() { /* 无操作 */ }
    public void Dispose() { BytesReceived = null; }
}
```

📚 **知识点**
- **`Task.Run(() => ...)` 让回环"像真串口一样异步到达"**:如果同步回调,测试里的粘包/半包场景就测不出时序问题——**测试替身的"仿真度"很重要**,差太远的 mock 会给你假信心(前端类比:msw 延迟响应比同步 mock 更接近真网络)。
- **`data.ToArray()` 防御性拷贝**:ReadOnlySpan 是别人的内存视图,回调到别的线程时原缓冲可能已被复用——**跨线程前先拷贝成独立数组**,这是 Span 使用的安全边界。
- **`Dispose` 里 `BytesReceived = null`**:事件是对外部对象的引用,不置空会让订阅方被意外"勾住"——通道死了就彻底断开所有订阅。

> 📂 `src/DaqMonitor.Core/Devices/RealSerialChannel.cs`

```csharp
using System.IO.Ports;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// 真实串口通道:用 SerialPort(.NET 官方串口类,需装 System.IO.Ports 包)收发字节。
/// 这是"直接用现成库"的部分——你不用自己写串口驱动。
/// 自己要写的,是把收到的字节流按协议解析(见 SerialDevice / FrameParser)。
/// </summary>
public sealed class RealSerialChannel : ISerialChannel
{
    private readonly SerialPort _sp;
    public event Action<byte[]>? BytesReceived;

    public RealSerialChannel(string portName, int baud = 9600)
    {
        _sp = new SerialPort(portName, baud) { ReadTimeout = 500, WriteTimeout = 500 };
        _sp.DataReceived += (_, _1) =>
        {
            int n = _sp.BytesToRead;
            if (n <= 0) return;
            var buf = new byte[n];
            _sp.Read(buf, 0, n);
            BytesReceived?.Invoke(buf);
        };
    }

    public void Open() { if (!_sp.IsOpen) _sp.Open(); }
    public void Write(ReadOnlySpan<byte> data) => _sp.Write(data.ToArray(), 0, data.Length);
    public void Close() { if (_sp.IsOpen) _sp.Close(); }
    public void Dispose() { Close(); _sp.Dispose(); }
}
```

📚 **知识点**
- **`DataReceived` 里"有多少读多少"**:`BytesToRead` 拿到当前堆着的字节数,一把读走弹事件——注意 **n 可能是半条帧**!串口天生流式,这里只负责"把字节送出门",拼帧是 FrameParser 的事。分层的意义就在这:每层只干一件事。
- **`ReadTimeout/WriteTimeout = 500`**:卡死保护——设备不回最多等 500ms 抛超时,绝不永久挂起(工业软件"任何 IO 都要有时限"的纪律)。
- **NuGet 依赖被关在这一个文件里**:`System.IO.Ports` 的 using 只出现在这——将来真要换 USB 转串口方案,改动不出这个类。**依赖隔离是接口抽象的隐形收益**。
- **`(_, _1)` 丢弃参数**:SerialPort 的 DataReceived 签名带 sender/args,这里用不上——下划线丢弃,避免警告。

### ② SerialDevice —— AA55 帧串口设备

> 📂 `src/DaqMonitor.Core/Devices/SerialDevice.cs`
> 💡 R3 FrameParser 的第一个真实消费者:字节流进、SensorPoint 语义的事件出
> 🗺️ **新手读码地图**(3 步看懂):1. `Connect()` 只做两件事:订阅通道的 BytesReceived + Open 链路——从此字节是"推"过来的,不用轮询 2. 灵魂在 `OnBytes` 的 10 行:收到一坨字节 → `RawLog` 先留痕(联调时看清线上到底来了什么)→ 喂 `_parser.Feed`,半包/粘包它内部搞定,吐出 N 条完整载荷 → 每条按"1 字节 pointId + 8 字节 double"解码,`RaiseData` 发事件(DeviceBase 继承来的,自动盖时间戳) 3. `Read(addr)` 返回 `_last` 缓存的新值——不是真去问设备;串口设备没有"随叫随到"的读法,值全靠事件推。**前端类比**:`OnBytes` ≈ WebSocket onmessage 的 handler:先 log → 切包 → emit 给上层。整类 = 链路 + 协议解析 + 事件发射的三合一。

**第 1 步 · 骨架:三件家当 + 联调开关**(整个文件先建出来)

```csharp
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Protocol;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// 串口设备:继承 DeviceBase,把"串口字节流"转成系统统一的 DataEventArgs 事件,
/// 从而无缝接入采集管道——UI 与采集层一行都不用改,只是组合根里把 SimulatedDevice 换成了它。
///
/// 职责划分(这就是"现成 vs 自研"的边界):
///   - 🟩 现成:字节收发靠 ISerialChannel(真实串口用 SerialPort,回环用内存);
///   - 🛠️ 自研:协议解析靠 FrameParser(AA55|Len|Payload|CRC)+ CRC16 校验 + 载荷解码。
/// </summary>
public sealed class SerialDevice : DeviceBase
{
    private readonly ISerialChannel _channel;
    private readonly FrameParser _parser = new(verifyCrc: true);
    private readonly Dictionary<int, double> _last = new();

    /// <summary>联调"调试开关":非 null 时,收发字节会回调出去(接日志即可落盘)。联调定位必备。</summary>
    public Action<string>? RawLog { get; set; }

    public SerialDevice(int id, string name, ISerialChannel channel) : base(id, name)
        => _channel = channel;
}
```

📚 **知识点**
- **三个字段就是三个职责**:`_channel`(链路,注入)→ `_parser`(协议,verifyCrc:true 生产必开)→ `_last`(数据缓存,Read 用)。类的依赖图 = 一句话架构图。
- **`RawLog` 是可空属性不是接口方法**:`Action<string>?` 默认 null、谁需要谁挂上——**联调能力做成"可选插槽"而不是必经之路**,平时零开销。前端类比:可选的 onDebug 回调,不传就不执行。
- **`sealed` 明确"到我为终点"**:串口设备不做继承扩展,要新协议就写新类——防止继承滥用导致的隐式耦合。

**第 2 步 · 连接生命周期 + 字节解码 `OnBytes`**(贴进类里,最后一个 `}` 之前)

```csharp
    public override void Connect()
    {
        _channel.BytesReceived += OnBytes;
        _channel.Open();
        State = DeviceState.Online;
    }

    public override void Disconnect()
    {
        _channel.BytesReceived -= OnBytes;
        _channel.Close();
        State = DeviceState.Offline;
    }

    private void OnBytes(byte[] bytes)
    {
        RawLog?.Invoke($"RX {Convert.ToHexString(bytes)}");   // 联调:看清收到了啥
        // 把收到的字节流喂给解析器;它自动处理"半包/粘包",逐帧回调载荷
        foreach (var payload in _parser.Feed(bytes))
        {
            if (payload.Length < 9) continue;                 // 载荷格式:pointId(1) + double(8)
            int pointId = payload[0];
            double value = BitConverter.ToDouble(payload, 1);  // 🛠️ 自研解码:字节 → 工程量点位
            _last[pointId] = value;
            RaiseData(pointId, value);                        // 推给采集管道 → 最终到 UI
        }
    }
```

📚 **知识点**
- **Connect 的顺序:先订阅再 Open**:反过来会丢数据——Open 之后字节可能立刻到,而你还没订阅。**"先挂号再看诊"是事件订阅的铁律**;Disconnect 对称地先退订再关链路,防关闭瞬间的事件打到已停用的对象。
- **`Convert.ToHexString` 是十六进制留痕的标准姿势**:输出 `RX AA5509...` 直接和抓包工具/设备手册比对——联调日志的价值取决于**能不能和现场证据对上号**。
- **`payload.Length < 9 continue`**:载荷约定 1 字节 id + 8 字节 double,不足 9 字节的帧是残废帧,**跳过而不是崩**——协议代码对畸形数据的默认态度是"丢弃并继续"。
- **`BitConverter.ToDouble(payload, 1)`**:从偏移 1 读 8 字节拼 double(小端,和 Build 的 GetBytes 互逆)——自研协议的解码就这一行,难的不是 API 是**字节布局约定**。

**第 3 步 · 读缓存 + 写命令帧**(贴进类里,收尾)

```csharp
    public override double Read(int addr)
        => _last.TryGetValue(addr, out var v) ? v : double.NaN;

    public override void Write(int addr, double value)
    {
        // 下发查询/置数命令帧(写方向也走同一套自研协议)
        var frame = FrameParser.Build(addr, value);
        RawLog?.Invoke($"TX {Convert.ToHexString(frame)}");    // 联调:看清发出了啥
        _channel.Write(frame);
    }
```

📚 **知识点**
- **`Read` 读的是缓存,不问设备**:串口是"设备推、我记"的模型,`_last` 就是账本;没收到过就 NaN——**NaN 是"不知道"的诚实表达**,比返回 0 好(0 是合法温度!)。
- **`Write` 走同一套 Build 组帧**:命令帧和数据帧同一格式,发送端免费获得 CRC 保护——协议复用的红利。

<details markdown="1">
<summary>📄 完整文件 SerialDevice.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Protocol;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// 串口设备:继承 DeviceBase,把"串口字节流"转成系统统一的 DataEventArgs 事件,
/// 从而无缝接入采集管道——UI 与采集层一行都不用改,只是组合根里把 SimulatedDevice 换成了它。
///
/// 职责划分(这就是"现成 vs 自研"的边界):
///   - 🟩 现成:字节收发靠 ISerialChannel(真实串口用 SerialPort,回环用内存);
///   - 🛠️ 自研:协议解析靠 FrameParser(AA55|Len|Payload|CRC)+ CRC16 校验 + 载荷解码。
/// </summary>
public sealed class SerialDevice : DeviceBase
{
    private readonly ISerialChannel _channel;
    private readonly FrameParser _parser = new(verifyCrc: true);
    private readonly Dictionary<int, double> _last = new();

    /// <summary>联调"调试开关":非 null 时,收发字节会回调出去(接日志即可落盘)。联调定位必备。</summary>
    public Action<string>? RawLog { get; set; }

    public SerialDevice(int id, string name, ISerialChannel channel) : base(id, name)
        => _channel = channel;

    public override void Connect()
    {
        _channel.BytesReceived += OnBytes;
        _channel.Open();
        State = DeviceState.Online;
    }

    public override void Disconnect()
    {
        _channel.BytesReceived -= OnBytes;
        _channel.Close();
        State = DeviceState.Offline;
    }

    private void OnBytes(byte[] bytes)
    {
        RawLog?.Invoke($"RX {Convert.ToHexString(bytes)}");   // 联调:看清收到了啥
        // 把收到的字节流喂给解析器;它自动处理"半包/粘包",逐帧回调载荷
        foreach (var payload in _parser.Feed(bytes))
        {
            if (payload.Length < 9) continue;                 // 载荷格式:pointId(1) + double(8)
            int pointId = payload[0];
            double value = BitConverter.ToDouble(payload, 1);  // 🛠️ 自研解码:字节 → 工程量点位
            _last[pointId] = value;
            RaiseData(pointId, value);                        // 推给采集管道 → 最终到 UI
        }
    }

    public override double Read(int addr)
        => _last.TryGetValue(addr, out var v) ? v : double.NaN;

    public override void Write(int addr, double value)
    {
        // 下发查询/置数命令帧(写方向也走同一套自研协议)
        var frame = FrameParser.Build(addr, value);
        RawLog?.Invoke($"TX {Convert.ToHexString(frame)}");    // 联调:看清发出了啥
        _channel.Write(frame);
    }
}
```

</details>

### ③ ModbusDevice —— 手搓 RTU 帧(不依赖 NModbus)

> 📂 `src/DaqMonitor.Core/Devices/ModbusDevice.cs`
> 💡 双模式套路:**simulate=true 零硬件跑链路;真实模式手搓请求帧**。TCP 模式只需把 SerialPort 换 TcpClient + MBAP 头,解析逻辑复用
> 🗺️ **新手读码地图**(4 步看懂):1. `RegisterMap` 是一张"翻译对照表":哪个点位对应哪个寄存器地址、什么类型(float 跨 2 寄存器/word 1 个)、什么字节序——现场调试改的是这张表,不是代码 2. 双模式在 `Start()` 的后台循环里分叉:每 500ms 一次 `SimulateTick()`(发随机值,零硬件)或 `RealTick()`(手搓"读保持寄存器"请求帧 → 写串口 → 收响应 → Crc16 验 → ModbusFrameParser 拆 → 按字节序拼回浮点) 3. 真实模式的"手搓帧"就是 R3 `BuildReadHoldingRequest` + `ParseReadRegisters` + `ToFloatModbus` 三件套串起来——R3 的纯函数在这落地成真设备 4. 对外仍然只暴露 IDevice:管道/UI 完全不知道底下是 Modbus。**前端类比**:`RegisterMap` ≈ 后端字段映射表(接口返回 snake_case 映射到组件的 camelCase),双模式 ≈ dev 环境 mock/生产环境真接口同一开关切换。

#### 🏗️ 为什么这样设计:Modbus 为什么手搓 RTU 帧,而不是直接用 NModbus 库?

**当时面临的选择**:

| 方案 | 优点 | 代价 |
|---|---|---|
| NuGet 引 NModbus | 协议细节不用管 | 黑盒:半包粘包、字节序、异常码出问题时只能翻库源码;面试讲不出原理;版本坑(NModbus4/5 API 大改) |
| 用 R3 的解析层手搓请求/响应(选定) | 多写约 100 行 | 要自己管超时、重试 |

**为什么选它**:本项目只用 Modbus 的**一小角**(03 读保持寄存器 + 06/16 写),手搓的成本是百行级,收益是**全链路白盒**——寄存器地址、字节序、CRC、异常码每一环都自己走过,现场抓包能对着字节逐个解释。这不是"永远别用库":如果明天要支持 Modbus 全部功能码 + 从站仿真,我会立刻换 NModbus。**决策依据是"用的深度",不是"手搓瘾"**。真实工作里的判断也是这样:核心链路留控制力,边角功能用库。

**不这样会怎样**:现场设备返回异常码 0x02(非法地址),用库的只能看到"读取失败";手搓的能把响应帧十六进制打出来,对着手册查到"寄存器偏移没减 40001"——10 分钟定位 vs 半天猜。

**🎤 面试一句话**:"Modbus 我手搓没用库:只用 03/06/16 三个功能码,百行代码换来全链路白盒——字节序、CRC、异常码都能逐字节调试。什么时候该换库我也清楚:要用全功能码或从站仿真时,控制力的收益就不抵维护成本了。"

> 🧰 **另一条腿——调库路线**:生产里"直接用库怎么用"(FluentModbus/NModbus4/S7netplus/HSL 选型与代码,沙盒验证过)见[《速查 · 工业通讯调库指南》](速查_工业通讯调库指南.md)。**想直接走企业路线?本篇末尾有「调库版附录」——ModbusDevice 换 FluentModbus 实现(同名同接口,沙盒测试 3/3 绿,含断线自愈),①②⑤⑥跳过,一晚通关。**

**第 1 步 · 骨架:RegisterMap 映射表 + 字段 + 构造**(整个文件先建出来)

```csharp
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Protocol;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// Modbus 设备:实现 IDevice。
///
/// 两种运行模式:
/// - 模拟模式(simulate=true):后台轮询产生随机值,零硬件即可跑通整条链路;
/// - 真实 RTU 模式:手搓 Modbus RTU 请求帧 → 经 SerialPort 下发 → 收响应 → 用
///   ModbusFrameParser 解析 + Crc16 校验。不依赖第三方库
///   (生产要省事可直接换 NModbus,但手搓版让你真正"懂协议")。
/// </summary>
public sealed class ModbusDevice : DeviceBase
{
    /// <summary>点位 → 寄存器映射:地址 + 数据类型(float 跨 2 寄存器 / word 单寄存器) + 浮点字节序。</summary>
    public sealed record RegisterMap(int PointId, ushort Address, string Type, ModbusFrameParser.ByteOrder Order = ModbusFrameParser.ByteOrder.ABCD);

    private readonly bool _simulate;
    private readonly byte _slave;
    private readonly List<RegisterMap> _maps;
    private readonly string _portName;
    private readonly int _baud;
    private SerialPort? _port;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly Random _rnd = new();
    private readonly Dictionary<int, double> _last = new();

    public ModbusDevice(int id, string name, byte slave, IEnumerable<RegisterMap> maps,
        bool simulate = false, string portName = "COM3", int baud = 9600)
        : base(id, name)
    {
        _simulate = simulate;
        _slave = slave;
        _maps = maps.ToList();
        _portName = portName;
        _baud = baud;
        if (!_simulate) _port = new SerialPort(portName, baud, Parity.None, 8, StopBits.One);
    }
}
```

📚 **知识点**
- **`RegisterMap` 是"现场调试改的地方"**:哪个点位 ↔ 哪个寄存器地址、什么类型、什么字节序——**换一台设备改一张表,代码不动**。它用 `record` 一行定义(值语义 + 自动相等比较),映射条目天然适合 record。
- **`Order` 参数默认 ABCD**:约定优先(大多数手册默认 ABCD),特殊的现场显式传 CDAB——**默认值 = 最常见情况,别让调用方每次都写全**。
- **`if (!_simulate) _port = new SerialPort(...)`**:串口对象**只在真实模式才创建**——模拟模式连 COM 口对象都不碰,保证零硬件机器上 `new ModbusDevice(..., simulate: true)` 不炸。
- **构造参数 7 个全有默认值/可省**:slave、portName、baud 都给了典型值(COM3/9600 是现场最常见的配置)——API 的"顺手度"就是减少调用方的记忆负担。

**第 2 步 · 生命周期 + 轮询循环骨架**(贴进类里,最后一个 `}` 之前)

```csharp
    public override void Connect()
    {
        State = DeviceState.Connecting;
        if (!_simulate) _port!.Open();
        Start();
        State = DeviceState.Online;
    }

    public override void Disconnect()
    {
        Stop();
        if (_port?.IsOpen == true) _port.Close();
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
                    if (_simulate) SimulateTick();
                    else RealTick();
                    await Task.Delay(500, token);   // 500ms 轮询,不阻塞 UI
                }
            }
            catch (OperationCanceledException) { /* 正常退出 */ }
        }, token);
    }

    private void Stop()
    {
        _cts?.Cancel();
        try { _loop?.Wait(500); } catch { /* 忽略 */ }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }
```

📚 **知识点**
- **`if (_simulate) SimulateTick(); else RealTick();` 是双模式的全部机关**:循环骨架(启动/取消/防重/500ms 节拍)只有一份,**两条业务线在循环内部分叉**——加第三种模式也只加一个分支,骨架零改动。
- **Connect 三拍:Connecting → Open → Online**:先标 Connecting(握手期 UI 亮黄灯),开串口(可能几十 ms 到几百 ms),成功才 Online——**状态转换的粒度就是 UI 反馈的粒度**。
- **Start/Stop 与 R2 SimulatedDevice 的同款三步舞**:防重入(`_loop is not null`)→ 令牌取消 → 等收尾 500ms。**同一模式在项目里出现第三次时,你会闭眼写对**——这就是 R2 反复练的回报。

**第 3 步 · 模拟心跳 `SimulateTick`**(贴进类里)

```csharp
    private void SimulateTick()
    {
        foreach (var m in _maps)
        {
            double v = m.Type == "float"
                ? Math.Round(20 + _rnd.NextDouble() * 70, 2)   // 模拟工程量
                : Math.Round(_rnd.NextDouble() * 100, 2);
            _last[m.PointId] = v;
            RaiseData(m.PointId, v);                            // 推给采集管道 → 最终到 UI
        }
    }
```

📚 **知识点**
- **模拟数据按类型分流**:float 型给 20~90(像温度),word 型给 0~100(像百分比)——**模拟数据的形状要贴近真实物理量**,下游 UI 的量程刻度才不会闹笑话。
- **`RaiseData` 是从 DeviceBase 继承的单一出口**:不管模拟还是真实,数据最终都从这一个门出来——上层(管道/UI)对模式完全无感。

**第 4 步 · 真实轮询 `RealTick`(R3 三件套的落地现场)**(贴进类里)

```csharp
    private void RealTick()
    {
        foreach (var m in _maps)
        {
            bool isFloat = m.Type == "float";
            ushort count = isFloat ? (ushort)2 : (ushort)1;    // float 跨 2 个寄存器
            var req = ModbusFrameParser.BuildReadHoldingRequest(_slave, m.Address, count);
            _port!.Write(req, 0, req.Length);

            // 等响应(简化:轮询 BytesToRead;真实工程要按 3.5 字符静默判断帧边界)
            int waited = 0;
            int minLen = isFloat ? 9 : 7;                      // 读1寄存器响应=7B;读2=9B
            while (_port.BytesToRead < minLen && waited < 1000) { Thread.Sleep(10); waited += 10; }
            if (_port.BytesToRead == 0) continue;

            var resp = new byte[_port.BytesToRead];
            _port.Read(resp, 0, resp.Length);

            if (ModbusFrameParser.IsExceptionResponse(resp, out var code)) continue;  // 设备拒了
            if (!Crc16.Check(resp)) continue;                                       // CRC 坏帧丢弃
            var regs = ModbusFrameParser.ParseReadRegisters(resp);
            if (regs.Length == 0) continue;

            double value = isFloat
                ? ModbusFrameParser.ToFloatModbus(regs[0], regs[1], m.Order)        // 32 位浮点按字节序拼
                : regs[0];
            _last[m.PointId] = value;
            RaiseData(m.PointId, value);
        }
    }
```

📚 **知识点**
- **这一个方法 = R3 协议层的"应用现场"**:`BuildReadHoldingRequest`(组帧)→ 写串口 → 等响应 → `IsExceptionResponse`(设备拒绝?)→ `Crc16.Check`(帧完好?)→ `ParseReadRegisters`(拆寄存器)→ `ToFloatModbus`(按字节序拼浮点)。**R3 每个纯函数都在这排好了队**——这就是"协议层零 IO、设备层串起来"的架构红利。
- **等待响应用"轮询 BytesToRead + 上限 1000ms"**:10ms 一看、最多等 1 秒——简化实现,真实工程要按 Modbus 规范的"3.5 字符静默"判帧边界(帧间静默 = 帧结束),注释里已标明。**简化版先跑通,升级点留注释**,是学习项目的正确姿势。
- **四道防线一路 `continue`**:设备拒了、CRC 坏了、寄存器拆空了——**任何一个点位失败只跳过该点位,不拖垮整轮轮询**:工业采集的"局部故障局部处理"。
- **`regs[0], regs[1]` 直接下标**:因为 minLen 已保证读到至少 9 字节(2 寄存器),ParseReadRegisters 也校验过长度——**前面防线立住了,后面才敢裸下标**。

**第 5 步 · 读缓存 + 写单寄存器**(贴进类里,收尾)

```csharp
    public override double Read(int addr)
        => _last.TryGetValue(addr, out var v) ? v : double.NaN;

    public override void Write(int addr, double value)
    {
        if (_simulate) return;   // 模拟设备只读
        // 写单寄存器(功能码 0x06):[从站][0x06][地址2B][值2B][CRC低前]
        var payload = new List<byte> { _slave, 0x06, (byte)(addr >> 8), (byte)addr,
                                       (byte)((int)value >> 8), (byte)(int)value };
        ushort crc = Crc16.Modbus(payload.ToArray());
        payload.Add((byte)(crc & 0xFF));
        payload.Add((byte)(crc >> 8));
        _port!.Write(payload.ToArray(), 0, payload.Count);
    }
```

📚 **知识点**
- **写单寄存器 = 功能码 0x06,和读请求同一个拼法**:从站 + 功能码 + 地址 2B + 值 2B + CRC 低前——**会拼读请求就会拼写命令**,Modbus 的对称美。
- **`(byte)((int)value >> 8)` 把 double 硬转 int 再拆字节**:写寄存器只能写整数(65535 封顶),带小数的值要先乘倍率(如温度×10)再写——真实工程由 RegisterMap 层处理标定,这里演示最小路径。

<details markdown="1">
<summary>📄 完整文件 ModbusDevice.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Protocol;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// Modbus 设备:实现 IDevice。
///
/// 两种运行模式:
/// - 模拟模式(simulate=true):后台轮询产生随机值,零硬件即可跑通整条链路;
/// - 真实 RTU 模式:手搓 Modbus RTU 请求帧 → 经 SerialPort 下发 → 收响应 → 用
///   ModbusFrameParser 解析 + Crc16 校验。不依赖第三方库
///   (生产要省事可直接换 NModbus,但手搓版让你真正"懂协议")。
/// </summary>
public sealed class ModbusDevice : DeviceBase
{
    /// <summary>点位 → 寄存器映射:地址 + 数据类型(float 跨 2 寄存器 / word 单寄存器) + 浮点字节序。</summary>
    public sealed record RegisterMap(int PointId, ushort Address, string Type, ModbusFrameParser.ByteOrder Order = ModbusFrameParser.ByteOrder.ABCD);

    private readonly bool _simulate;
    private readonly byte _slave;
    private readonly List<RegisterMap> _maps;
    private readonly string _portName;
    private readonly int _baud;
    private SerialPort? _port;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly Random _rnd = new();
    private readonly Dictionary<int, double> _last = new();

    public ModbusDevice(int id, string name, byte slave, IEnumerable<RegisterMap> maps,
        bool simulate = false, string portName = "COM3", int baud = 9600)
        : base(id, name)
    {
        _simulate = simulate;
        _slave = slave;
        _maps = maps.ToList();
        _portName = portName;
        _baud = baud;
        if (!_simulate) _port = new SerialPort(portName, baud, Parity.None, 8, StopBits.One);
    }

    public override void Connect()
    {
        State = DeviceState.Connecting;
        if (!_simulate) _port!.Open();
        Start();
        State = DeviceState.Online;
    }

    public override void Disconnect()
    {
        Stop();
        if (_port?.IsOpen == true) _port.Close();
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
                    if (_simulate) SimulateTick();
                    else RealTick();
                    await Task.Delay(500, token);   // 500ms 轮询,不阻塞 UI
                }
            }
            catch (OperationCanceledException) { /* 正常退出 */ }
        }, token);
    }

    private void Stop()
    {
        _cts?.Cancel();
        try { _loop?.Wait(500); } catch { /* 忽略 */ }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    private void SimulateTick()
    {
        foreach (var m in _maps)
        {
            double v = m.Type == "float"
                ? Math.Round(20 + _rnd.NextDouble() * 70, 2)   // 模拟工程量
                : Math.Round(_rnd.NextDouble() * 100, 2);
            _last[m.PointId] = v;
            RaiseData(m.PointId, v);                            // 推给采集管道 → 最终到 UI
        }
    }

    private void RealTick()
    {
        foreach (var m in _maps)
        {
            bool isFloat = m.Type == "float";
            ushort count = isFloat ? (ushort)2 : (ushort)1;    // float 跨 2 个寄存器
            var req = ModbusFrameParser.BuildReadHoldingRequest(_slave, m.Address, count);
            _port!.Write(req, 0, req.Length);

            // 等响应(简化:轮询 BytesToRead;真实工程要按 3.5 字符静默判断帧边界)
            int waited = 0;
            int minLen = isFloat ? 9 : 7;                      // 读1寄存器响应=7B;读2=9B
            while (_port.BytesToRead < minLen && waited < 1000) { Thread.Sleep(10); waited += 10; }
            if (_port.BytesToRead == 0) continue;

            var resp = new byte[_port.BytesToRead];
            _port.Read(resp, 0, resp.Length);

            if (ModbusFrameParser.IsExceptionResponse(resp, out var code)) continue;  // 设备拒了
            if (!Crc16.Check(resp)) continue;                                       // CRC 坏帧丢弃
            var regs = ModbusFrameParser.ParseReadRegisters(resp);
            if (regs.Length == 0) continue;

            double value = isFloat
                ? ModbusFrameParser.ToFloatModbus(regs[0], regs[1], m.Order)        // 32 位浮点按字节序拼
                : regs[0];
            _last[m.PointId] = value;
            RaiseData(m.PointId, value);
        }
    }

    public override double Read(int addr)
        => _last.TryGetValue(addr, out var v) ? v : double.NaN;

    public override void Write(int addr, double value)
    {
        if (_simulate) return;   // 模拟设备只读
        // 写单寄存器(功能码 0x06):[从站][0x06][地址2B][值2B][CRC低前]
        var payload = new List<byte> { _slave, 0x06, (byte)(addr >> 8), (byte)addr,
                                       (byte)((int)value >> 8), (byte)(int)value };
        ushort crc = Crc16.Modbus(payload.ToArray());
        payload.Add((byte)(crc & 0xFF));
        payload.Add((byte)(crc >> 8));
        _port!.Write(payload.ToArray(), 0, payload.Count);
    }
}
```

</details>

### ④ TcpDevice —— 长连接 + 心跳 + 指数退避重连

> 📂 `src/DaqMonitor.Core/Devices/TcpDevice.cs`
> 💡 本篇最重的类:把 R3 的 TcpFrameParser 塞进真实 socket 循环;[指数退避](kp:retry-backoff)在这里先见第一面(R7 会抽成通用 Retry)
> 🗺️ **新手读码地图**(按"一条命"的周期看):1. 外层 `MaintainConnectionLoop` 是一条永动的命:连上 → 干活 → 断了 → 睡一会儿再连,直到 Dispose。睡多久不是固定值,是 `BackoffMs = {1s,2s,4s,8s,16s}` 一级级往上爬——网络刚抖完马上重连只会雪上加霜,这就是**指数退避** 2. 连上后兵分两路:`HeartbeatLoop` 每 10s 发一帧 `[0x02]` 心跳保活,30s 收不到对端消息判离线;`ReceiveLoop` 是主收线——socket 只管把字节堆进滚动缓冲,切帧全交给 `TcpFrameParser.TryParse`(R3 的无状态设计在这兑现:缓冲归调用方管) 3. 切出的帧按载荷解码成点位 → `RaiseData` 上报,和串口设备殊途同归 4. `OfflineTimeout` = 心跳超时兜底:TCP 半开连接(对端拔网线)不会自动报错,必须自己掐表。**前端类比**:`MaintainConnectionLoop` ≈ socket.io 内置的重连机制(它默认也是指数退避),心跳 ≈ ping/pong 帧——你写前端长连接时框架替你干的事,这里全部手写一遍。

#### 🏗️ 为什么这样设计:断线重连为什么是指数退避(1s→2s→4s→…→16s),而不是固定 3 秒一次?心跳为什么要自己发?

**当时面临的选择(重连节奏)**:

| 方案 | 优点 | 代价 |
|---|---|---|
| 固定间隔重连(每 3s 试一次) | 实现最简单 | 对端刚恢复就被匀速敲门;多客户端断网恢复瞬间同时冲击(惊群) |
| 指数退避 1→2→4→8→16s 封顶(选定) | 多一张退避表 | 网络恢复后最长多等十几秒 |

**为什么选它**:断线的常见原因是**对端过载或网络风暴**,固定间隔 = 故障期间匀速敲门,恢复窗口一开,所有客户端按同一节拍涌入,把刚喘过气的服务再打崩。指数退避让重试密度随失败次数**递减**,给对端留恢复时间;16s 封顶保证最坏延迟有界。前端类比:socket.io 默认重连就是指数退避,同一套理由。

**心跳为什么必须自己发**:TCP 是惰性协议——对端拔网线/断电,本端 socket **不会收到任何通知**,连接"看起来还在"(半开连接)。心跳 10s 一发 + 30s 收不到就判离线,是应用层用自己的节奏探测死活;没有它,设备断电后上位机可能要等 TCP keepalive 默认的 2 小时才发现。WebSocket 的 ping/pong 帧是同一个问题的同一个解。

**🎤 面试一句话**:"重连我用指数退避:故障期匀速重试会在恢复瞬间形成惊群,退避让重试密度递减、给对端喘息时间。心跳必须应用层自发——对端断电 TCP 不通知你,半开连接只有靠 10s 心跳 + 30s 超时才能及时判死。"

> 这个文件是**本篇最重的类(约 280 行),且方法互相调用成网**(RealLoop 调 ConnectOnce/ReceiveLoop/HeartbeatLoop,Stop 调 CloseSocket……),拆开贴中间态编译不过——所以玩法是:**先展开文末折叠块把完整文件贴进去,然后按下面 6 步逐块读懂**。每一步都标了它在文件里的位置。

**第 1 步 · 骨架与常数:心跳/超时/退避三个魔法数**(文件开头到构造函数)

```csharp
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Protocol;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// TCP 设备:实现 IDevice,面向长连接 + 长度前缀帧。
///
/// 设计要点(与 ModbusDevice / SerialDevice 一致的接口语义):
///   - 上层只认 IDevice,换成它零改动。
///   - 帧格式由 TcpFrameParser 处理(AA55 + 小端长度 + payload + CRC16)。
///   - 后台 ReadAsync 循环 + 滚动缓冲做粘包/半包拼帧。
///   - 心跳:每 10 秒发心跳包;30 秒没收到任何对端数据判定掉线 → 自动重连。
///   - 自动重连:SocketException 后按 1s/2s/4s/8s/16s 指数退避重试,直到成功或 Dispose。
///
/// 模拟模式(simulate=true):不建 socket,后台周期产生随机值,零硬件即可跑通链路。
/// </summary>
public sealed class TcpDevice : DeviceBase, IDisposable
{
    /// <summary>点位映射:地址 → PointId(解析后用 PointId 抬事件)。</summary>
    public sealed class TcpMap(int addr, int pointId) { public int Addr { get; } = addr; public int PointId { get; } = pointId; }

    private const int HeartbeatIntervalMs = 10_000;
    private const int OfflineTimeoutMs = 30_000;
    private static readonly int[] BackoffMs = { 1000, 2000, 4000, 8000, 16000 };

    private readonly bool _simulate;
    private readonly string _host;
    private readonly int _port;
    private readonly List<TcpMap> _maps;
    private readonly ConcurrentDictionary<int, double> _last = new();

    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly byte[] _rx = new byte[4096];

    public TcpDevice(int id, string name, string host, int port, IEnumerable<TcpMap>? maps = null, bool simulate = false)
        : base(id, name)
    {
        _simulate = simulate;
        _host = host;
        _port = port;
        _maps = maps?.ToList() ?? new();
    }
```

📚 **知识点**
- **三个常数定下整条命的健康指标**:10s 心跳(保活)、30s 静默判死(超时)、1→16s 退避(重连节奏)——**TCP 可用性的全部参数就这三个数**,调优现场网络就是调它们。
- **`TcpMap` 用了主构造函数(C# 12)**:参数直接进类体,`{ get; } = addr; }` 一行收尾——和 record 的适用场景辨析:这里是**可变行为的小映射对象**,record 更适合纯数据。
- **`_last` 是 `ConcurrentDictionary` 而不是普通 Dictionary**:收线线程写、UI 线程 Read 读——**两个线程同时摸的字典必须线程安全**(前面的 ModbusDevice 用普通 Dictionary 是因为只有一个轮询线程摸它)。
- **`_rx` 字段声明了但 RealLoop 里用的是局部 `seg`**:字段版本是早期实现的遗留,保留无害;读代码时遇到"没被用到的字段"别慌,**先确认没有隐藏的反射/序列化引用再下结论**。

**第 2 步 · 生命周期 + 模拟模式**(构造函数之后,文件的"上半层")

```csharp
    public override void Connect()
    {
        if (State == DeviceState.Online) return;
        State = DeviceState.Connecting;
        Start();
        if (_simulate) State = DeviceState.Online; // 真实模式由 RealLoop 在 ConnectOnce 成功后标 Online
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
        _loop = Task.Run(() => _simulate ? SimulateLoop(token) : RealLoop(token), token);
    }

    private void Stop()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _loop?.Wait(1000); } catch { /* ignore */ }
        CloseSocket();
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    // ===== 模拟模式:与 ModbusDevice.SimulateTick 同套路,零硬件跑通链路 =====
    private async Task SimulateLoop(CancellationToken ct)
    {
        var rnd = new Random();
        while (!ct.IsCancellationRequested)
        {
            foreach (var m in _maps)
            {
                double v = Math.Round(20 + rnd.NextDouble() * 70, 2);
                _last[m.PointId] = v;
                RaiseData(m.PointId, v);
            }
            try { await Task.Delay(500, ct); } catch (OperationCanceledException) { break; }
        }
    }
```

📚 **知识点**
- **`Connect()` 的注释点破一个不对称**:模拟模式 Connect 完立即 Online;真实模式 Start 只点火,**Online 由后台 RealLoop 在真正连上后才标**——"立即表示成功"和"稍后表示成功"的差别,UI 状态灯的黄灯时长就是证据。
- **Stop 的 try/catch 密度是全类最高的**:停机路径上 Cancel/Wait/Dispose 每一步都可能抛,**吞掉一切往上冒的异常**——"关闭代码的容错标准高一档"(R2 讲过的纪律,这里第三次出现)。
- **`Task.Run(() => _simulate ? SimulateLoop(token) : RealLoop(token), token)`**:三元选一个 async 方法,lambda 里自动包成 Task——双模式在"起线程"这一层就分流,后面两条线互不相见。

**第 3 步 · 重连外环:RealLoop + ConnectOnce + DelayBackoff**(文件中部,真实模式的心脏)

```csharp
    // ===== 真实模式:长连接 + ReadAsync 拼帧 + 心跳 + 自动重连 =====
    private async Task RealLoop(CancellationToken ct)
    {
        int backoffIdx = 0;
        // 外层循环:断了重连,直到成功连上或被取消
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!ConnectOnce())
                {
                    await DelayBackoff(ct, backoffIdx);
                    backoffIdx = Math.Min(backoffIdx + 1, BackoffMs.Length - 1);
                    continue;
                }
                backoffIdx = 0;
                State = DeviceState.Online;

                // 心跳用 Task.Delay 并行触发;read 循环内同步判断静默时长
                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _ = HeartbeatLoop(heartbeatCts.Token);

                await ReceiveLoop(ct);   // 正常退出=对端关闭或掉线
                heartbeatCts.Cancel();
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { /* 走重连 */ }
            catch (IOException) { /* 走重连 */ }
            finally
            {
                CloseSocket();
                if (State != DeviceState.Offline) State = DeviceState.Connecting;
            }

            if (ct.IsCancellationRequested) break;
            await DelayBackoff(ct, backoffIdx);
            backoffIdx = Math.Min(backoffIdx + 1, BackoffMs.Length - 1);
        }
    }

    private bool ConnectOnce()
    {
        try
        {
            CloseSocket();
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                ReceiveTimeout = OfflineTimeoutMs,
                SendTimeout = 5000
            };
            _socket.Connect(_host, _port);
            return true;
        }
        catch (SocketException) { return false; }
    }

    private static async Task DelayBackoff(CancellationToken ct, int idx)
    {
        int ms = BackoffMs[Math.Min(idx, BackoffMs.Length - 1)];
        try { await Task.Delay(ms, ct); } catch (OperationCanceledException) { }
    }
```

📚 **知识点**
- **外层 while = "一条命的周期"**:连上(ConnectOnce)→ 活着(心跳+收线)→ 死了(异常/超时)→ 睡退避 → 再来。**读长连接代码先找这个环**,它就是 socket.io 的 reconnect 引擎裸奔版。
- **`backoffIdx` 只升不降,连上清零**:`Math.Min(idx+1, Length-1)` 封顶在 16s——失败越多次睡越久(给对端喘息),一旦成功立刻归零。**指数退避的全部实现就这两行加减法**,R7 会把它抽成通用 Retry。
- **`catch (SocketException) / catch (IOException)` 空 catch 故意"走重连"**:网络类异常是**环境状态不是程序错误**,吃了回到环顶重试;而 `OperationCanceledException` break——用户主动断开不叫故障。
- **`CreateLinkedTokenSource` 造"联动令牌"**:外层 ct 取消会自动联动到心跳令牌——**子任务的生命周期挂在父任务上**,Dispose 用 `using` 保证不泄漏。这是"取消树"模式,一cancel全倒。
- **`ConnectOnce` 里先 `CloseSocket()` 再 new**:残留的旧 socket 先埋了再建新的——**重连前清理现场**,否则句柄泄漏攒着攒着就 IOException。

**第 4 步 · 收线拼帧:ReceiveLoop + DrainBuffer**(RealLoop 之后)

```csharp
    private async Task ReceiveLoop(CancellationToken ct)
    {
        // 滚动缓冲区:堆字节 + TryParse 滑窗。生产可用 ArrayPool 进一步优化分配。
        var buffer = new List<byte>(4096);
        var seg = new byte[4096];
        DateTime lastRecv = DateTime.UtcNow;

        while (!ct.IsCancellationRequested && _socket?.Connected == true)
        {
            int n;
            try { n = await _socket.ReceiveAsync(new ArraySegment<byte>(seg), SocketFlags.None, ct); }
            catch (SocketException) { return; }
            if (n == 0) return; // 对端关闭
            lastRecv = DateTime.UtcNow;

            buffer.AddRange(seg.AsSpan(0, n));
            DrainBuffer(buffer);

            if ((DateTime.UtcNow - lastRecv).TotalMilliseconds > OfflineTimeoutMs) return; // 心跳超时
        }
    }

    private void DrainBuffer(List<byte> buffer)
    {
        while (buffer.Count > 0)
        {
            var arr = buffer.ToArray();
            if (!TcpFrameParser.TryParse(arr, out var payload, out int frameLen, out bool needResync))
            {
                if (needResync) buffer.RemoveAt(0);   // 头不对齐:丢 1 字节重同步
                break;                                // 数据不够:等下次 Receive
            }
            HandlePayload(payload);
            buffer.RemoveRange(0, frameLen);
        }
    }
```

📚 **知识点**
- **`n == 0` 是 TCP 的"礼貌告别"**:ReceiveAsync 返回 0 字节 = 对端正常关闭——**和异常不是一回事**,单独判断;而不抛错的"半开连接"(拔网线)要靠下面的静默超时兜底。
- **ReceiveLoop 自己不管拼帧**,只"堆字节 + 叫 DrainBuffer"——**IO 和协议继续解耦**,TryParse 失败的两种含义(等/弃)在 DrainBuffer 里精准处理:needResync 丢 1 字节,否则 break 等下一车。R3 无状态设计的兑现现场。
- **`lastRecv` 判超时看似死代码?** 不——`DateTime.UtcNow - lastRecv` 在循环里每圈都查,只是**只有 ReceiveAsync 长时间阻塞不返回时才会触发**(30s 没收到任何字节,ReceiveTimeout 把 ReceiveAsync 打醒抛异常,或返回后判超时 return)。**死连接靠"时间"发现,不靠"报错"发现**——这是心跳机制的存在理由。
- **`buffer.ToArray()` 每圈复制一份**:TryParse 要 ReadOnlySpan,List 不能直接给——**牺牲一点分配换代码简洁**,注释标了生产可用 ArrayPool 优化。学习版先直白,优化有方向。

**第 5 步 · 业务解码 + 心跳:HandlePayload + ParseMultiPoints + HeartbeatLoop**(DrainBuffer 之后)

```csharp
    /// <summary>
    /// 业务侧 payload 解析:约定第一字节是功能码。
    /// 0x01:多点上报,[0x01][N][N×(addr:2, val:4 float LE)];0x02:心跳响应,忽略。
    /// 真实工程可把 codec 抽成单独策略类注入,这里只演示一例。
    /// </summary>
    private void HandlePayload(byte[] payload)
    {
        if (payload.Length == 0) return;
        switch (payload[0])
        {
            case 0x01: ParseMultiPoints(payload); break;
            case 0x02: /* 心跳响应,无操作 */ break;
        }
    }

    private void ParseMultiPoints(byte[] payload)
    {
        if (payload.Length < 2) return;
        int n = payload[1];
        for (int i = 0; i < n; i++)
        {
            int off = 2 + i * 6;
            if (off + 6 > payload.Length) break;
            int pointId = payload[off] | (payload[off + 1] << 8);
            double value = BitConverter.ToSingle(payload, off + 2);
            _last[pointId] = value;
            RaiseData(pointId, Math.Round(value, 3));
        }
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        // 心跳 payload = [0x02],由 BuildFrame 现算 CRC
        var frame = TcpFrameParser.BuildFrame(new byte[] { 0x02 });
        while (!ct.IsCancellationRequested && _socket?.Connected == true)
        {
            try
            {
                await _socket!.SendAsync(new ArraySegment<byte>(frame), SocketFlags.None, ct);
            }
            catch (SocketException) { return; }
            catch (OperationCanceledException) { return; }
            try { await Task.Delay(HeartbeatIntervalMs, ct); } catch (OperationCanceledException) { return; }
        }
    }
```

📚 **知识点**
- **载荷第一字节是功能码——TCP 版的"路由"**:`0x01` 数据上报、`0x02` 心跳、`0x03` 写命令——**同一条连接上跑多种消息,靠功能码分流**(前端类比:WebSocket 消息里的 `type` 字段,switch 分发到不同 handler)。
- **`ParseMultiPoints` 的 `off = 2 + i * 6` 定长数组布局**:每点 6 字节(addr 2 + float 4),**定长结构才能用乘法寻址**——和 R3 线圈的 `i/8, i%8` 一个思路,变的是"定长"还是"按位"。
- **心跳是"只发不问"的保活**:每 10s 发一帧 [0x02],对端回不回都行——**我们的超时判据是"收到过任何字节"**(ReceiveLoop 的 lastRecv),对端回的心跳响应恰好贡献活跃度。比"请求-响应配对"简单且有弹性。
- **心跳失败 `return` 而不重试**:发送失败说明连接已坏,**把死连接的发现交给重连环**——各组件只做自己最擅长的事,不越位抢活。

**第 6 步 · 对外读写与清理:Read + Write + Dispose + CloseSocket**(文件末尾)

```csharp
    private void CloseSocket()
    {
        try { _socket?.Shutdown(SocketShutdown.Both); } catch { /* ignore */ }
        try { _socket?.Close(); } catch { /* ignore */ }
        _socket = null;
    }

    /// <summary>读最近一次缓存值(与 ModbusDevice.Read 同语义)。</summary>
    public override double Read(int addr)
    {
        var map = _maps.FirstOrDefault(m => m.Addr == addr);
        int pid = map?.PointId ?? addr;
        return _last.TryGetValue(pid, out var v) ? v : double.NaN;
    }

    /// <summary>
    /// 写值:把 [0x03][addr:2][value:4] 打成一帧下发。
    /// </summary>
    public override void Write(int addr, double value)
    {
        if (_simulate) return;
        if (_socket?.Connected != true) return;
        var payload = new byte[7];
        payload[0] = 0x03;
        payload[1] = (byte)(addr & 0xFF);
        payload[2] = (byte)((addr >> 8) & 0xFF);
        BitConverter.TryWriteBytes(payload.AsSpan(3), (float)value);
        var frame = TcpFrameParser.BuildFrame(payload);
        try { _socket.Send(frame); } catch (SocketException) { /* 重连循环会接手 */ }
    }

    public void Dispose()
    {
        Stop();
        _last.Clear();
    }
```

📚 **知识点**
- **`Write` 里 `if (_socket?.Connected != true) return;` 静默失败**:断线时写命令直接丢——**不抛异常不重试**,因为重连环会恢复连接,上层下个周期再写就通了。**"可用性靠自愈不靠报错"**是长连接系统的写侧哲学。
- **`BitConverter.TryWriteBytes(payload.AsSpan(3), (float)value)`**:Try 版本写进指定切片、不产生临时数组——组帧的零分配写法,和 GetBytes 返回新数组相对。
- **`Shutdown(Both)` 先于 `Close()`**:礼貌地告诉对端"我要走了"(发 FIN),再关句柄——直接 Close 对端要等超时才发现,**好聚好散也是网络礼仪**。
- **`Dispose = Stop + 清缓存`**:IDisposable 的标准收尾,调用方 `using var dev = new TcpDevice(...)` 出作用域自动挂断。

<details markdown="1">
<summary>📄 完整文件 TcpDevice.cs(先把这个贴进工程,再回头读上面 6 步)</summary>

```csharp
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Protocol;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// TCP 设备:实现 IDevice,面向长连接 + 长度前缀帧。
///
/// 设计要点(与 ModbusDevice / SerialDevice 一致的接口语义):
///   - 上层只认 IDevice,换成它零改动。
///   - 帧格式由 TcpFrameParser 处理(AA55 + 小端长度 + payload + CRC16)。
///   - 后台 ReadAsync 循环 + 滚动缓冲做粘包/半包拼帧。
///   - 心跳:每 10 秒发心跳包;30 秒没收到任何对端数据判定掉线 → 自动重连。
///   - 自动重连:SocketException 后按 1s/2s/4s/8s/16s 指数退避重试,直到成功或 Dispose。
///
/// 模拟模式(simulate=true):不建 socket,后台周期产生随机值,零硬件即可跑通链路。
/// </summary>
public sealed class TcpDevice : DeviceBase, IDisposable
{
    /// <summary>点位映射:地址 → PointId(解析后用 PointId 抬事件)。</summary>
    public sealed class TcpMap(int addr, int pointId) { public int Addr { get; } = addr; public int PointId { get; } = pointId; }

    private const int HeartbeatIntervalMs = 10_000;
    private const int OfflineTimeoutMs = 30_000;
    private static readonly int[] BackoffMs = { 1000, 2000, 4000, 8000, 16000 };

    private readonly bool _simulate;
    private readonly string _host;
    private readonly int _port;
    private readonly List<TcpMap> _maps;
    private readonly ConcurrentDictionary<int, double> _last = new();

    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly byte[] _rx = new byte[4096];

    public TcpDevice(int id, string name, string host, int port, IEnumerable<TcpMap>? maps = null, bool simulate = false)
        : base(id, name)
    {
        _simulate = simulate;
        _host = host;
        _port = port;
        _maps = maps?.ToList() ?? new();
    }

    public override void Connect()
    {
        if (State == DeviceState.Online) return;
        State = DeviceState.Connecting;
        Start();
        if (_simulate) State = DeviceState.Online; // 真实模式由 RealLoop 在 ConnectOnce 成功后标 Online
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
        _loop = Task.Run(() => _simulate ? SimulateLoop(token) : RealLoop(token), token);
    }

    private void Stop()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _loop?.Wait(1000); } catch { /* ignore */ }
        CloseSocket();
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    // ===== 模拟模式:与 ModbusDevice.SimulateTick 同套路,零硬件跑通链路 =====
    private async Task SimulateLoop(CancellationToken ct)
    {
        var rnd = new Random();
        while (!ct.IsCancellationRequested)
        {
            foreach (var m in _maps)
            {
                double v = Math.Round(20 + rnd.NextDouble() * 70, 2);
                _last[m.PointId] = v;
                RaiseData(m.PointId, v);
            }
            try { await Task.Delay(500, ct); } catch (OperationCanceledException) { break; }
        }
    }

    // ===== 真实模式:长连接 + ReadAsync 拼帧 + 心跳 + 自动重连 =====
    private async Task RealLoop(CancellationToken ct)
    {
        int backoffIdx = 0;
        // 外层循环:断了重连,直到成功连上或被取消
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!ConnectOnce())
                {
                    await DelayBackoff(ct, backoffIdx);
                    backoffIdx = Math.Min(backoffIdx + 1, BackoffMs.Length - 1);
                    continue;
                }
                backoffIdx = 0;
                State = DeviceState.Online;

                // 心跳用 Task.Delay 并行触发;read 循环内同步判断静默时长
                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _ = HeartbeatLoop(heartbeatCts.Token);

                await ReceiveLoop(ct);   // 正常退出=对端关闭或掉线
                heartbeatCts.Cancel();
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { /* 走重连 */ }
            catch (IOException) { /* 走重连 */ }
            finally
            {
                CloseSocket();
                if (State != DeviceState.Offline) State = DeviceState.Connecting;
            }

            if (ct.IsCancellationRequested) break;
            await DelayBackoff(ct, backoffIdx);
            backoffIdx = Math.Min(backoffIdx + 1, BackoffMs.Length - 1);
        }
    }

    private bool ConnectOnce()
    {
        try
        {
            CloseSocket();
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                ReceiveTimeout = OfflineTimeoutMs,
                SendTimeout = 5000
            };
            _socket.Connect(_host, _port);
            return true;
        }
        catch (SocketException) { return false; }
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        // 滚动缓冲区:堆字节 + TryParse 滑窗。生产可用 ArrayPool 进一步优化分配。
        var buffer = new List<byte>(4096);
        var seg = new byte[4096];
        DateTime lastRecv = DateTime.UtcNow;

        while (!ct.IsCancellationRequested && _socket?.Connected == true)
        {
            int n;
            try { n = await _socket.ReceiveAsync(new ArraySegment<byte>(seg), SocketFlags.None, ct); }
            catch (SocketException) { return; }
            if (n == 0) return; // 对端关闭
            lastRecv = DateTime.UtcNow;

            buffer.AddRange(seg.AsSpan(0, n));
            DrainBuffer(buffer);

            if ((DateTime.UtcNow - lastRecv).TotalMilliseconds > OfflineTimeoutMs) return; // 心跳超时
        }
    }

    private void DrainBuffer(List<byte> buffer)
    {
        while (buffer.Count > 0)
        {
            var arr = buffer.ToArray();
            if (!TcpFrameParser.TryParse(arr, out var payload, out int frameLen, out bool needResync))
            {
                if (needResync) buffer.RemoveAt(0);   // 头不对齐:丢 1 字节重同步
                break;                                // 数据不够:等下次 Receive
            }
            HandlePayload(payload);
            buffer.RemoveRange(0, frameLen);
        }
    }

    /// <summary>
    /// 业务侧 payload 解析:约定第一字节是功能码。
    /// 0x01:多点上报,[0x01][N][N×(addr:2, val:4 float LE)];0x02:心跳响应,忽略。
    /// 真实工程可把 codec 抽成单独策略类注入,这里只演示一例。
    /// </summary>
    private void HandlePayload(byte[] payload)
    {
        if (payload.Length == 0) return;
        switch (payload[0])
        {
            case 0x01: ParseMultiPoints(payload); break;
            case 0x02: /* 心跳响应,无操作 */ break;
        }
    }

    private void ParseMultiPoints(byte[] payload)
    {
        if (payload.Length < 2) return;
        int n = payload[1];
        for (int i = 0; i < n; i++)
        {
            int off = 2 + i * 6;
            if (off + 6 > payload.Length) break;
            int pointId = payload[off] | (payload[off + 1] << 8);
            double value = BitConverter.ToSingle(payload, off + 2);
            _last[pointId] = value;
            RaiseData(pointId, Math.Round(value, 3));
        }
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        // 心跳 payload = [0x02],由 BuildFrame 现算 CRC
        var frame = TcpFrameParser.BuildFrame(new byte[] { 0x02 });
        while (!ct.IsCancellationRequested && _socket?.Connected == true)
        {
            try
            {
                await _socket!.SendAsync(new ArraySegment<byte>(frame), SocketFlags.None, ct);
            }
            catch (SocketException) { return; }
            catch (OperationCanceledException) { return; }
            try { await Task.Delay(HeartbeatIntervalMs, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private static async Task DelayBackoff(CancellationToken ct, int idx)
    {
        int ms = BackoffMs[Math.Min(idx, BackoffMs.Length - 1)];
        try { await Task.Delay(ms, ct); } catch (OperationCanceledException) { }
    }

    private void CloseSocket()
    {
        try { _socket?.Shutdown(SocketShutdown.Both); } catch { /* ignore */ }
        try { _socket?.Close(); } catch { /* ignore */ }
        _socket = null;
    }

    /// <summary>读最近一次缓存值(与 ModbusDevice.Read 同语义)。</summary>
    public override double Read(int addr)
    {
        var map = _maps.FirstOrDefault(m => m.Addr == addr);
        int pid = map?.PointId ?? addr;
        return _last.TryGetValue(pid, out var v) ? v : double.NaN;
    }

    /// <summary>
    /// 写值:把 [0x03][addr:2][value:4] 打成一帧下发。
    /// </summary>
    public override void Write(int addr, double value)
    {
        if (_simulate) return;
        if (_socket?.Connected != true) return;
        var payload = new byte[7];
        payload[0] = 0x03;
        payload[1] = (byte)(addr & 0xFF);
        payload[2] = (byte)((addr >> 8) & 0xFF);
        BitConverter.TryWriteBytes(payload.AsSpan(3), (float)value);
        var frame = TcpFrameParser.BuildFrame(payload);
        try { _socket.Send(frame); } catch (SocketException) { /* 重连循环会接手 */ }
    }

    public void Dispose()
    {
        Stop();
        _last.Clear();
    }
}
```

</details>

### ⑤ PlcDevice —— S7 模拟模式(真实路径注释保留)

> 📂 `src/DaqMonitor.Core/Devices/PlcDevice.cs` · 🔧 无 NuGet(S7NetPlus 到真接 PLC 再装)

**第 1 步 · 骨架:PlcMap 地址映射 + 字段 + 构造**(整个文件先建出来)

```csharp
using DaqMonitor.Core.Models;
using System.Collections.Generic;
using System.Threading;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// PLC 设备:实现 IDevice,直连西门子 S7 读 DB 块。
/// 模拟模式(默认 simulate=true)零硬件跑通;真实模式用 S7NetPlus(dotnet add package S7NetPlus),
/// 因本机无 PLC 且为保持工程零依赖可编译,真实路径以注释保留。
///
/// 重点:IsConnected 不可全信,真正"通不通"要看读回来的 LastErrorCode / 读值是否合理。
/// </summary>
public sealed class PlcDevice : DeviceBase
{
    /// <summary>点位 → PLC 地址映射,如 "DB1.DBW0"(数据块1、字0)。</summary>
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
}
```

📚 **知识点**
- **`simulate = true` 是 PLC 版的"默认模拟"**:和 ModbusDevice(默认 false)相反——因为 S7 真实路径需要装 S7NetPlus 包 + 真机,**默认零依赖可跑,要真机再显式关掉**。参数默认值反映"这个设备最常见的使用姿势"。
- **`PlcMap.DbAddress` 是字符串地址 "DB1.DBW0"**:西门子的寻址语法(数据块 1、字 0)直接做成数据——**协议的寻址体系映射到类型系统**,手册上抄下来的地址就能用。

**第 2 步 · 生命周期 + 轮询骨架**(贴进类里,最后一个 `}` 之前)

```csharp
    public override void Connect()
    {
        State = DeviceState.Connecting;
        Start();
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
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }
```

📚 **知识点**
- **这套 Start/Stop 你已经是第三次写了**(SimulatedDevice → ModbusDevice → PlcDevice)——**重复三遍的模式就该抽基类**,这是 R7 组装篇的伏笔;学习期手写三遍,肌肉记忆比抽象更重要。
- **轮询周期 500ms 与 ModbusDevice 一致**:约定的节拍,UI 刷新率(≈2Hz)和设备压力的平衡点。

**第 3 步 · Tick:模拟分支 + 真实 S7 注释路径**(贴进类里)

```csharp
    private void Tick()
    {
        if (_simulate)
        {
            foreach (var m in _maps)
            {
                double v = Math.Round(20 + _rnd.NextDouble() * 70, 2);
                _last[m.PointId] = v;
                RaiseData(m.PointId, v);
            }
            return;
        }

        // —— 真实 S7.Net 写法(需 dotnet add package S7NetPlus,并填 PLC 的 IP)——
        // using S7;
        // var plc = new Plc(CpuType.S71200, "192.168.0.1", 0, 1);
        // plc.Open();
        // try
        // {
        //     foreach (var m in _maps)
        //     {
        //         var raw = (short)plc.Read(m.DbAddress);
        //         if (plc.LastErrorCode != 0) continue;     // IsConnected 不可全信,看错误码
        //         double v = raw;
        //         _last[m.PointId] = v;
        //         RaiseData(m.PointId, v);
        //     }
        // }
        // finally { plc.Close(); }
    }
```

📚 **知识点**
- **真实路径整段注释保留,而不是删掉**:面试官问"你真接过 PLC 吗",诚实答案 + **能逐行讲出 S7NetPlus 的用法**——注释是"我没硬件,但我懂路径"的证据。工作后真机到位,放开注释装包即跑。
- **`if (plc.LastErrorCode != 0) continue;` 是 M3 铁律的落地**:`IsConnected` 属性只反映"上次动作时的状态",**真正通不通要看这次读回来的错误码**——和 TcpDevice 不信 `Connected` 只信超时,同一个世界观:**状态属性会撒谎,IO 结果不会**。
- **`(short)plc.Read(...)`**:S7 的 DBW(Word)读到的是 16 位有符号数——PLC 侧数值范围规划(温度×10 存 word)要在两端一致,又见标定问题。

**第 4 步 · 读缓存 + 写占位**(贴进类里,收尾)

```csharp
    public override double Read(int addr)
        => _last.TryGetValue(addr, out var v) ? v : double.NaN;

    public override void Write(int addr, double value)
    {
        if (_simulate) return;
        // 真实模式:plc.Write(m.DbAddress, (short)value);
    }
```

📚 **知识点**
- **Write 的真实路径注释一行即可**:S7 写和读对称(`plc.Write(addr, value)`),不像 Modbus 要手工组帧——**用库的好处是写方向免费**,代价是你没练过组帧(所以 Modbus 我们手搓、PLC 用库,两头都学到)。

<details markdown="1">
<summary>📄 完整文件 PlcDevice.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Models;
using System.Collections.Generic;
using System.Threading;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// PLC 设备:实现 IDevice,直连西门子 S7 读 DB 块。
/// 模拟模式(默认 simulate=true)零硬件跑通;真实模式用 S7NetPlus(dotnet add package S7NetPlus),
/// 因本机无 PLC 且为保持工程零依赖可编译,真实路径以注释保留。
///
/// 重点:IsConnected 不可全信,真正"通不通"要看读回来的 LastErrorCode / 读值是否合理。
/// </summary>
public sealed class PlcDevice : DeviceBase
{
    /// <summary>点位 → PLC 地址映射,如 "DB1.DBW0"(数据块1、字0)。</summary>
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
        Start();
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
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    private void Tick()
    {
        if (_simulate)
        {
            foreach (var m in _maps)
            {
                double v = Math.Round(20 + _rnd.NextDouble() * 70, 2);
                _last[m.PointId] = v;
                RaiseData(m.PointId, v);
            }
            return;
        }

        // —— 真实 S7.Net 写法(需 dotnet add package S7NetPlus,并填 PLC 的 IP)——
        // using S7;
        // var plc = new Plc(CpuType.S71200, "192.168.0.1", 0, 1);
        // plc.Open();
        // try
        // {
        //     foreach (var m in _maps)
        //     {
        //         var raw = (short)plc.Read(m.DbAddress);
        //         if (plc.LastErrorCode != 0) continue;     // IsConnected 不可全信,看错误码
        //         double v = raw;
        //         _last[m.PointId] = v;
        //         RaiseData(m.PointId, v);
        //     }
        // }
        // finally { plc.Close(); }
    }

    public override double Read(int addr)
        => _last.TryGetValue(addr, out var v) ? v : double.NaN;

    public override void Write(int addr, double value)
    {
        if (_simulate) return;
        // 真实模式:plc.Write(m.DbAddress, (short)value);
    }
}
```

</details>

### ⑥ CAN 三件套 + USB-HID 三件套

> 📂 `src/DaqMonitor.Core/Devices/ICanChannel.cs`

```csharp
namespace DaqMonitor.Core.Devices;

/// <summary>
/// CAN 传输通道抽象:把"用什么物理链路收发 CAN 帧"和"怎么解析协议"解耦。
/// 真实硬件用厂商 DLL(PCAN / Vector / 周立功)实现本接口;没硬件时用 SimulatedCanChannel 内存模拟。
/// </summary>
public interface ICanChannel
{
    /// <summary>从总线上收到一帧(ID + 数据)时触发。</summary>
    event Action<ulong, byte[]>? FrameReceived;

    bool IsOpen { get; }

    void Open();

    /// <summary>向总线广播一帧:ID 标识信号含义,data 为 0~8 字节负载。</summary>
    void Send(ulong id, byte[] data);

    void Close();
}
```

📚 **知识点**
- **CAN 帧和串口字节流是两种世界观**:串口是"无边界字节流"(要 FrameParser 拼帧),CAN 是"帧固有边界"(硬件保证每帧 ID+0~8 字节完整到达)——所以接口直接给 `FrameReceived(ulong id, byte[] data)`,**没有半包粘包问题,解析器都省了**。协议性质决定接口形状。
- **`ulong id`**:CAN 扩展帧 ID 是 29 位,ulong 一步到位——和 Modbus 的"从站地址"不同,**CAN 的 ID 是"信号的名字"不是"设备的名字"**(一条总线多设备广播,ID 区分的是报文类型)。

> 📂 `src/DaqMonitor.Core/Devices/SimulatedCanChannel.cs`

```csharp
namespace DaqMonitor.Core.Devices;

/// <summary>
/// CAN 内存模拟通道(零硬件):Open/Send 时直接回调一帧"温度 = 25.0℃"的假数据(ID=0x100,数据 [0x00,0xFA]=250)。
/// 用于单元测试 / 没有真实 CAN 卡时验证 CanDevice 解析与整条链路。生产别用它。
/// </summary>
public sealed class SimulatedCanChannel : ICanChannel
{
    public event Action<ulong, byte[]>? FrameReceived;
    public bool IsOpen { get; private set; }

    public void Open()
    {
        IsOpen = true;
        // 模拟"设备上线即广播当前温度"
        FrameReceived?.Invoke(0x100, new byte[] { 0x00, 0xFA });
    }

    public void Send(ulong id, byte[] data)
        => FrameReceived?.Invoke(0x100, new byte[] { 0x00, 0xFA }); // 模拟设备回温度

    public void Close() => IsOpen = false;
}
```

📚 **知识点**
- **假数据也有讲究**:`[0x00, 0xFA]` = 250,÷10 = 25.0℃——**测试断言的期望值就藏在这**,CanDeviceTests 里 `Assert 25.0` 和这里遥相呼应。改一处忘另一处,测试立刻提醒你。
- **Open 即广播一帧**:模拟"设备上电就把当前温度广播出来"——CAN 设备的真实行为(状态即广播,不等你问),替身连行为节拍都要像。

> 📂 `src/DaqMonitor.Core/Devices/CanDevice.cs`
> 💡 CAN vs Modbus(面试常考):Modbus 主从问答;CAN 多主广播,靠 **ID** 区分信号,没有地址/功能码

**第 1 步 · 骨架:通道注入 + 缓存**(整个文件先建出来)

```csharp
using DaqMonitor.Core.Models;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// CAN 设备:继承 DeviceBase,把 CAN 总线上"按 ID 广播的帧"解码成统一 DataEventArgs。
/// 例如约定 ID=0x100 = 温度,2 字节大端 raw,÷10 得 ℃(工程量标定)。
/// 真实硬件用 PCANChannel 等实现 ICanChannel,换链路不换本类。
/// </summary>
public sealed class CanDevice : DeviceBase
{
    private readonly ICanChannel _ch;
    private readonly Dictionary<int, double> _last = new();

    public CanDevice(int id, string name, ICanChannel channel) : base(id, name)
        => _ch = channel;
}
```

📚 **知识点**
- **和 SerialDevice 骨架几乎一样,只换通道类型**:注入 `ICanChannel` 而不是 `ISerialChannel`——**设备类 = 生命周期 + 解码逻辑 + 通道**,前两者每个设备都相似,通道决定它"说什么方言"。

**第 2 步 · 生命周期 + 帧解码 `OnFrame`**(贴进类里,最后一个 `}` 之前)

```csharp
    public override void Connect()
    {
        _ch.FrameReceived += OnFrame;
        _ch.Open();
        State = DeviceState.Online;
    }

    public override void Disconnect()
    {
        _ch.FrameReceived -= OnFrame;
        _ch.Close();
        State = DeviceState.Offline;
    }

    private void OnFrame(ulong id, byte[] data)
    {
        if (id != 0x100 || data.Length < 2) return;   // 只认"温度帧",其它 ID 忽略
        int raw = (data[0] << 8) | data[1];           // 大端:高字节在前
        double value = raw / 10.0;                     // 工程量标定
        _last[1] = value;
        RaiseData(1, value);                           // 推给采集管道 → 最终到 UI
    }
```

📚 **知识点**
- **`if (id != 0x100 ...) return` 是 CAN 的"订阅过滤"**:总线上所有设备广播的帧都会到这,**只认自己关心的 ID**——CAN 没有"主从问答",过滤器就是你的订阅器(前端类比:mqtt 的 topic 过滤、事件的 type 判断)。
- **`raw / 10.0` 是工程量标定**:报文里装的是整数 250(0x00FA),物理量是 25.0℃——**协议里的数 ≠ 物理世界的数**,标定系数(÷10、×0.1、+偏移)永远来自设备手册的"工程量换算"一节。这行代码是"读手册"的落地。
- **大端拼 raw 与 Modbus 寄存器同款**:`(data[0] << 8) | data[1]`——CAN 数据场习惯大端,又一次"手册说了算"。

**第 3 步 · 读缓存 + 写占位**(贴进类里,收尾)

```csharp
    public override double Read(int addr)
        => _last.TryGetValue(addr, out var v) ? v : double.NaN;

    public override void Write(int addr, double value)
    {
        // CAN 是广播总线,"写单个寄存器"语义不存在;具体写入请由网关/子类覆盖。
    }
```

📚 **知识点**
- **Write 空实现不是偷懒**:CAN 是多主广播,没有"写某设备的某个寄存器"这回事——**接口强迫你实现一个语义不存在的操作,空方法 + 注释是最诚实的答案**(比抛异常好:调用方遍历设备统一 Write 时不会炸)。

<details markdown="1">
<summary>📄 完整文件 CanDevice.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Models;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// CAN 设备:继承 DeviceBase,把 CAN 总线上"按 ID 广播的帧"解码成统一 DataEventArgs。
/// 例如约定 ID=0x100 = 温度,2 字节大端 raw,÷10 得 ℃(工程量标定)。
/// 真实硬件用 PCANChannel 等实现 ICanChannel,换链路不换本类。
/// </summary>
public sealed class CanDevice : DeviceBase
{
    private readonly ICanChannel _ch;
    private readonly Dictionary<int, double> _last = new();

    public CanDevice(int id, string name, ICanChannel channel) : base(id, name)
        => _ch = channel;

    public override void Connect()
    {
        _ch.FrameReceived += OnFrame;
        _ch.Open();
        State = DeviceState.Online;
    }

    public override void Disconnect()
    {
        _ch.FrameReceived -= OnFrame;
        _ch.Close();
        State = DeviceState.Offline;
    }

    private void OnFrame(ulong id, byte[] data)
    {
        if (id != 0x100 || data.Length < 2) return;   // 只认"温度帧",其它 ID 忽略
        int raw = (data[0] << 8) | data[1];           // 大端:高字节在前
        double value = raw / 10.0;                     // 工程量标定
        _last[1] = value;
        RaiseData(1, value);                           // 推给采集管道 → 最终到 UI
    }

    public override double Read(int addr)
        => _last.TryGetValue(addr, out var v) ? v : double.NaN;

    public override void Write(int addr, double value)
    {
        // CAN 是广播总线,"写单个寄存器"语义不存在;具体写入请由网关/子类覆盖。
    }
}
```

</details>

> 📂 `src/DaqMonitor.Core/Devices/IHidChannel.cs`

```csharp
namespace DaqMonitor.Core.Devices;

/// <summary>
/// USB-HID 传输通道抽象:HID 是"固定长度报告(Report)"模型,不像串口能发任意长度字节流。
/// 真实仪器用 HidLibrary 实现本接口;没硬件时用 SimulatedHidChannel 内存模拟。
/// 关键点:HID 用 VID/PID 找到设备(操作系统原生免驱),不像串口靠 COM 号——换 USB 口也不变。
/// </summary>
public interface IHidChannel
{
    event Action<byte[]>? ReportReceived;
    bool IsOpen { get; }
    /// <summary>HID 报告固定长度(如 64 字节/包),收发都按这个长度。</summary>
    int ReportLength { get; }
    void Open();
    /// <summary>给设备发一包控制指令(如"开始采样")。</summary>
    void Write(byte[] report);
    void Close();
}
```

📚 **知识点**
- **HID 是"定长报告"模型**:`ReportLength`(如 64 字节)收发都按这个长度——**没有流、没有帧边界问题,每包就是一包**。鼠标键盘就是 HID,操作系统原生免驱,所以很多仪器走 HID 而不是虚拟串口。
- **VID/PID 找设备 vs COM 号找设备**:VID/PID 烧在固件里,**换 USB 口不变**;COM 号是操作系统分配的,换个口可能从 COM3 变 COM5——这就是 HID 设备"免配置"的底气,注释里专门强调。

> 📂 `src/DaqMonitor.Core/Devices/SimulatedHidChannel.cs`

```csharp
namespace DaqMonitor.Core.Devices;

/// <summary>
/// USB-HID 内存模拟通道(零硬件):Open 时回调一包温度报告 [0x01,0,0xFA],Write 时回一包压力报告 [0x02,1,0x2C]。
/// 用于单元测试 UsbHidDevice,无需真实 HID 仪器。生产别用它。
/// </summary>
public sealed class SimulatedHidChannel : IHidChannel
{
    public event Action<byte[]>? ReportReceived;
    public bool IsOpen { get; private set; }
    public int ReportLength => 64;

    public void Open()
    {
        IsOpen = true;
        ReportReceived?.Invoke(new byte[] { 0x01, 0x00, 0xFA }); // 温度 25.0℃
    }

    public void Write(byte[] report)
        => ReportReceived?.Invoke(new byte[] { 0x02, 0x01, 0x2C }); // 压力 30.0kPa

    public void Close() => IsOpen = false;
}
```

📚 **知识点**
- **Open 回温度报告、Write 回压力报告**:模拟"设备主动上报温度 + 收到命令回压力"两种交互——替身的行为剧本对齐真实仪器,测试才能覆盖两条解码路径。

> 📂 `src/DaqMonitor.Core/Devices/UsbHidDevice.cs`

**第 1 步 · 骨架:通道注入 + 缓存**(整个文件先建出来)

```csharp
using DaqMonitor.Core.Models;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// USB-HID 仪器设备:继承 DeviceBase,把 HID"报告(Report)"解码成统一 DataEventArgs。
/// 约定(和仪器厂家的协议文档对齐):report[0] = 报告类型(0x01 温度 / 0x02 压力);
/// report[1..2] = 2 字节大端原始值,÷10 得工程量。
/// 真实仪器用 HidLibrary 实现 IHidChannel,本类只认接口,换仪器/换厂商库不改动业务。
/// </summary>
public sealed class UsbHidDevice : DeviceBase
{
    private readonly IHidChannel _ch;
    private readonly Dictionary<int, double> _last = new();

    public UsbHidDevice(int id, string name, IHidChannel channel) : base(id, name)
        => _ch = channel;
}
```

📚 **知识点**
- **第五个设备类,骨架已经"模板化"**:通道字段 + 缓存字典 + 注入构造——写到这里你应该能默写这个骨架了,**设备类的变化点只剩"解码方法"一处**,好的抽象就是让变化点收窄。

**第 2 步 · 生命周期 + 报告解码 `OnReport`**(贴进类里,最后一个 `}` 之前)

```csharp
    public override void Connect()
    {
        _ch.ReportReceived += OnReport;
        _ch.Open();
        State = DeviceState.Online;
    }

    public override void Disconnect()
    {
        _ch.ReportReceived -= OnReport;
        _ch.Close();
        State = DeviceState.Offline;
    }

    private void OnReport(byte[] report)
    {
        if (report.Length < 3) return;
        if (report[0] == 0x01)                              // 温度
        {
            double v = ((report[1] << 8) | report[2]) / 10.0;
            _last[1] = v; RaiseData(1, v);
        }
        else if (report[0] == 0x02)                         // 压力
        {
            double v = ((report[1] << 8) | report[2]) / 10.0;
            _last[2] = v; RaiseData(2, v);
        }
    }
```

📚 **知识点**
- **`report[0]` 报告类型 = HID 版功能码**:和 TcpDevice 载荷第一字节分流同构——**"消息第一字节说是谁"是二进制协议的通用模式**,认出它你就能读任何仪器手册的"报文格式"表。
- **`_last[1]` / `_last[2]` 硬编码点位号**:温度固定抬点位 1、压力固定抬点位 2——简化版映射(真实工程会做成 ctor 传映射表,像 RegisterMap 那样)。**先硬编码跑通,再参数化**,学习曲线的正确顺序。

**第 3 步 · 读缓存 + 写报告**(贴进类里,收尾)

```csharp
    public override double Read(int addr)
        => _last.TryGetValue(addr, out var v) ? v : double.NaN;

    public override void Write(int addr, double value)
    {
        var outBuf = new byte[Math.Max(3, _ch.ReportLength)];
        outBuf[0] = 0x02;
        outBuf[1] = (byte)value;
        _ch.Write(outBuf);                                  // 发控制命令(如置数/启动)
    }
```

📚 **知识点**
- **`Math.Max(3, _ch.ReportLength)` 尊重定长协议**:HID 报告必须按 ReportLength 整包发(哪怕只用 3 字节,后面补零)——**协议要求的"浪费"不能省**,短包设备直接不认。

<details markdown="1">
<summary>📄 完整文件 UsbHidDevice.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Models;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// USB-HID 仪器设备:继承 DeviceBase,把 HID"报告(Report)"解码成统一 DataEventArgs。
/// 约定(和仪器厂家的协议文档对齐):report[0] = 报告类型(0x01 温度 / 0x02 压力);
/// report[1..2] = 2 字节大端原始值,÷10 得工程量。
/// 真实仪器用 HidLibrary 实现 IHidChannel,本类只认接口,换仪器/换厂商库不改动业务。
/// </summary>
public sealed class UsbHidDevice : DeviceBase
{
    private readonly IHidChannel _ch;
    private readonly Dictionary<int, double> _last = new();

    public UsbHidDevice(int id, string name, IHidChannel channel) : base(id, name)
        => _ch = channel;

    public override void Connect()
    {
        _ch.ReportReceived += OnReport;
        _ch.Open();
        State = DeviceState.Online;
    }

    public override void Disconnect()
    {
        _ch.ReportReceived -= OnReport;
        _ch.Close();
        State = DeviceState.Offline;
    }

    private void OnReport(byte[] report)
    {
        if (report.Length < 3) return;
        if (report[0] == 0x01)                              // 温度
        {
            double v = ((report[1] << 8) | report[2]) / 10.0;
            _last[1] = v; RaiseData(1, v);
        }
        else if (report[0] == 0x02)                         // 压力
        {
            double v = ((report[1] << 8) | report[2]) / 10.0;
            _last[2] = v; RaiseData(2, v);
        }
    }

    public override double Read(int addr)
        => _last.TryGetValue(addr, out var v) ? v : double.NaN;

    public override void Write(int addr, double value)
    {
        var outBuf = new byte[Math.Max(3, _ch.ReportLength)];
        outBuf[0] = 0x02;
        outBuf[1] = (byte)value;
        _ch.Write(outBuf);                                  // 发控制命令(如置数/启动)
    }
}
```

</details>

### ⑦ 测试(12 个新增)

> 📂 `src/DaqMonitor.Tests/SerialDeviceTests.cs`(R4 版 5 个;穿管道测试 R5 有了管道再补)
> 💡 用 LoopbackSerialChannel 喂字节——**不碰任何真实串口,CI / 没硬件的机器也能跑绿**

搭积木:第 1 步建骨架顺带写单帧测试,之后粘包/半包/坏 CRC/RawLog 四个场景分两批贴入。

**第 1 步 · 骨架 + 单帧直通测试**(整个文件先建出来)

```csharp
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Protocol;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 验证"加一种设备 = 只写一个小类、UI/采集层零改动":
/// 用 LoopbackSerialChannel(内存回环,零硬件)喂字节,断言 SerialDevice 的协议解析成立。
/// </summary>
public class SerialDeviceTests
{
    [Fact]
    public void Parses_SingleFrame_AndRaisesData()
    {
        var ch = new LoopbackSerialChannel();
        var dev = new SerialDevice(1, "SER", ch);
        var got = new List<(int, double)>();
        dev.DataReceived += (_, e) => got.Add((e.PointId, e.Value));

        dev.Connect();
        ch.Write(FrameParser.Build(1, 123.5));
        Thread.Sleep(200);                      // 等回环异步回调
        dev.Disconnect();

        Assert.Contains(got, x => x.Item1 == 1 && Math.Abs(x.Item2 - 123.5) < 1e-6);
    }
}
```

📚 **知识点**
- **测试的"组装三件套"**:造通道(回环)→ 造设备(注入通道)→ 订阅事件收结果——**和生产代码组装一模一样,只是通道换成替身**。这就是 ISerialChannel 抽象的测试红利。
- **`Thread.Sleep(200)` 等"回环异步回调"**:LoopbackSerialChannel 用 Task.Run 模拟异步到达,Sleep 给它时间跑——比 R2 的 ManualResetEventSlim 粗糙但够用(精确等待留给关键断言,普通场景 Sleep 简单直接)。
- **`Assert.Contains(got, x => ...)` 谓词断言**:收到的事件里"存在一条"满足条件即可——**不断言"恰好一条"**(期间可能有别的),给异步时序留容差,测试才不闪断。

**第 2 步 · 粘包 + 半包:流式的两种残缺**(贴进类里,最后一个 `}` 之前)

```csharp
    [Fact]
    public void Handles_粘包_TwoFramesInOneChunk()
    {
        var ch = new LoopbackSerialChannel();
        var dev = new SerialDevice(1, "SER", ch);
        var got = new List<int>();
        dev.DataReceived += (_, e) => got.Add(e.PointId);

        dev.Connect();
        var both = FrameParser.Build(1, 10).Concat(FrameParser.Build(2, 20)).ToArray();
        ch.Write(both);                         // 两帧粘在一起一次到达
        Thread.Sleep(200);
        dev.Disconnect();

        Assert.Contains(1, got);
        Assert.Contains(2, got);
    }

    [Fact]
    public void Handles_半包_SplitAcrossChunks()
    {
        var ch = new LoopbackSerialChannel();
        var dev = new SerialDevice(1, "SER", ch);
        var got = new List<int>();
        dev.DataReceived += (_, e) => got.Add(e.PointId);

        dev.Connect();
        var frame = FrameParser.Build(3, 30);
        ch.Write(frame.AsSpan(0, frame.Length / 2).ToArray());   // 先来半包
        Thread.Sleep(50);
        ch.Write(frame.AsSpan(frame.Length / 2).ToArray());      // 补齐剩余
        Thread.Sleep(200);
        dev.Disconnect();

        Assert.Contains(3, got);                  // 半包必须能拼回完整帧
    }
```

📚 **知识点**
- **粘包测试 = `Concat` 两帧一次喂**:模拟"TCP/串口缓冲把两条消息一起送来"——设备层必须拆出两条,**断言 Contains(1) 和 Contains(2) 双双在场**。
- **半包测试掐中分**:整帧从中间劈成两半、分两次 Write——中间 `Sleep(50)` 保证"第一半先到"的时序。**R3 FrameParser 的蓄水池在这被端到端验证**(单元测试测它自己,这测它在设备里的集成)。

**第 3 步 · 坏 CRC 丢弃 + RawLog 联调开关**(贴进类里,收尾)

```csharp
    [Fact]
    public void Drops_Frame_WithBadCrc()
    {
        var ch = new LoopbackSerialChannel();
        var dev = new SerialDevice(1, "SER", ch);
        var got = new List<int>();
        dev.DataReceived += (_, e) => got.Add(e.PointId);

        dev.Connect();
        var frame = FrameParser.Build(5, 50);
        frame[^1] ^= 0xFF;                        // 故意破坏 CRC
        ch.Write(frame);
        Thread.Sleep(200);
        dev.Disconnect();

        Assert.DoesNotContain(5, got);            // 坏帧被丢弃,不污染业务
    }

    [Fact]
    public void RawLog_Fires_OnSendAndReceive()   // 验证联调"调试开关"
    {
        var ch = new LoopbackSerialChannel();
        var dev = new SerialDevice(1, "SER", ch);
        var logs = new List<string>();
        dev.RawLog = m => logs.Add(m);

        dev.Connect();
        dev.Write(9, 1.0);                     // 触发 TX 日志(下发命令帧)
        ch.Write(FrameParser.Build(1, 1.0));   // 触发 RX 日志(设备回数据)
        Thread.Sleep(200);
        dev.Disconnect();

        Assert.Contains(logs, l => l.StartsWith("TX "));
        Assert.Contains(logs, l => l.StartsWith("RX "));
    }
}
```

📚 **知识点**
- **`Assert.DoesNotContain` 断言"什么都没发生"**:坏帧喂进去、事件一个不响——**"负向断言"是防污染测试的核心**,数据落库/报警误触发类 bug 全靠它兜底。
- **RawLog 测试连"联调功能"都不放过**:TX/RX 日志各触发一次断言在场——**调试功能也是功能,坏了现场就抓瞎**,值得一个测试守护。

<details markdown="1">
<summary>📄 完整文件 SerialDeviceTests.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Protocol;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 验证"加一种设备 = 只写一个小类、UI/采集层零改动":
/// 用 LoopbackSerialChannel(内存回环,零硬件)喂字节,断言 SerialDevice 的协议解析成立。
/// </summary>
public class SerialDeviceTests
{
    [Fact]
    public void Parses_SingleFrame_AndRaisesData()
    {
        var ch = new LoopbackSerialChannel();
        var dev = new SerialDevice(1, "SER", ch);
        var got = new List<(int, double)>();
        dev.DataReceived += (_, e) => got.Add((e.PointId, e.Value));

        dev.Connect();
        ch.Write(FrameParser.Build(1, 123.5));
        Thread.Sleep(200);                      // 等回环异步回调
        dev.Disconnect();

        Assert.Contains(got, x => x.Item1 == 1 && Math.Abs(x.Item2 - 123.5) < 1e-6);
    }

    [Fact]
    public void Handles_粘包_TwoFramesInOneChunk()
    {
        var ch = new LoopbackSerialChannel();
        var dev = new SerialDevice(1, "SER", ch);
        var got = new List<int>();
        dev.DataReceived += (_, e) => got.Add(e.PointId);

        dev.Connect();
        var both = FrameParser.Build(1, 10).Concat(FrameParser.Build(2, 20)).ToArray();
        ch.Write(both);                         // 两帧粘在一起一次到达
        Thread.Sleep(200);
        dev.Disconnect();

        Assert.Contains(1, got);
        Assert.Contains(2, got);
    }

    [Fact]
    public void Handles_半包_SplitAcrossChunks()
    {
        var ch = new LoopbackSerialChannel();
        var dev = new SerialDevice(1, "SER", ch);
        var got = new List<int>();
        dev.DataReceived += (_, e) => got.Add(e.PointId);

        dev.Connect();
        var frame = FrameParser.Build(3, 30);
        ch.Write(frame.AsSpan(0, frame.Length / 2).ToArray());   // 先来半包
        Thread.Sleep(50);
        ch.Write(frame.AsSpan(frame.Length / 2).ToArray());      // 补齐剩余
        Thread.Sleep(200);
        dev.Disconnect();

        Assert.Contains(3, got);                  // 半包必须能拼回完整帧
    }

    [Fact]
    public void Drops_Frame_WithBadCrc()
    {
        var ch = new LoopbackSerialChannel();
        var dev = new SerialDevice(1, "SER", ch);
        var got = new List<int>();
        dev.DataReceived += (_, e) => got.Add(e.PointId);

        dev.Connect();
        var frame = FrameParser.Build(5, 50);
        frame[^1] ^= 0xFF;                        // 故意破坏 CRC
        ch.Write(frame);
        Thread.Sleep(200);
        dev.Disconnect();

        Assert.DoesNotContain(5, got);            // 坏帧被丢弃,不污染业务
    }

    [Fact]
    public void RawLog_Fires_OnSendAndReceive()   // 验证联调"调试开关"
    {
        var ch = new LoopbackSerialChannel();
        var dev = new SerialDevice(1, "SER", ch);
        var logs = new List<string>();
        dev.RawLog = m => logs.Add(m);

        dev.Connect();
        dev.Write(9, 1.0);                     // 触发 TX 日志(下发命令帧)
        ch.Write(FrameParser.Build(1, 1.0));   // 触发 RX 日志(设备回数据)
        Thread.Sleep(200);
        dev.Disconnect();

        Assert.Contains(logs, l => l.StartsWith("TX "));
        Assert.Contains(logs, l => l.StartsWith("RX "));
    }
}
```

</details>

> 📂 `src/DaqMonitor.Tests/ModbusDeviceTests.cs`(R4 版 2 个;穿管道测试 R5 再补)

搭积木:第 1 步骨架 + Modbus 模拟测试,第 2 步贴入 PLC 模拟测试。

**第 1 步 · 骨架 + Modbus 模拟模式测试**(整个文件先建出来)

```csharp
using DaqMonitor.Core.Devices;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 验证 ModbusDevice/PlcDevice 的模拟模式(零硬件)真实落工程。
/// </summary>
public class ModbusDeviceTests
{
    [Fact]
    public void SimulateMode_RaisesData_ForMappedPoints()
    {
        var dev = new ModbusDevice(1, "MB", slave: 1,
            new[] { new ModbusDevice.RegisterMap(1, 0, "float"), new ModbusDevice.RegisterMap(2, 1, "word") },
            simulate: true);
        var got = new List<int>();
        dev.DataReceived += (_, e) => got.Add(e.PointId);

        dev.Connect();
        Thread.Sleep(700);     // 等至少一个轮询周期(500ms)
        dev.Disconnect();

        Assert.Contains(1, got);
        Assert.Contains(2, got);
    }
}
```

📚 **知识点**
- **RegisterMap 在测试里当"配置"用**:点位 1 = 地址 0 的 float、点位 2 = 地址 1 的 word——映射表即测试数据,**不用 mock 任何东西,模拟模式本身就是最好的替身**(比 Moq 模拟一个 IDevice 更真实:走的是完整的 Connect→轮询→事件链路)。
- **Sleep 700 = 轮询 500 + 余量**:至少跑完一个 tick 才有数据——**等待时长永远 ≥ 周期 + 缓冲**,这是异步轮询测试的时间数学。

**第 2 步 · PLC 模拟模式测试**(贴进类里,最后一个 `}` 之前)

```csharp
    [Fact]
    public void PlcDevice_SimulateMode_RaisesData()
    {
        var dev = new PlcDevice(2, "PLC", new[] { new PlcDevice.PlcMap(3, "DB1.DBW0") }, simulate: true);
        var got = new List<int>();
        dev.DataReceived += (_, e) => got.Add(e.PointId);

        dev.Connect();
        Thread.Sleep(700);
        dev.Disconnect();

        Assert.Contains(3, got);
    }
}
```

📚 **知识点**
- **PlcMap 的 DbAddress "DB1.DBW0" 在模拟模式只是个字符串钥匙**:真实模式它才是 S7 寻址——**同一个配置对象,两种模式两种重量**,模拟测试验证的是"链路活不活",不是"地址对不对"(地址对错要真机)。

<details markdown="1">
<summary>📄 完整文件 ModbusDeviceTests.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Devices;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 验证 ModbusDevice/PlcDevice 的模拟模式(零硬件)真实落工程。
/// </summary>
public class ModbusDeviceTests
{
    [Fact]
    public void SimulateMode_RaisesData_ForMappedPoints()
    {
        var dev = new ModbusDevice(1, "MB", slave: 1,
            new[] { new ModbusDevice.RegisterMap(1, 0, "float"), new ModbusDevice.RegisterMap(2, 1, "word") },
            simulate: true);
        var got = new List<int>();
        dev.DataReceived += (_, e) => got.Add(e.PointId);

        dev.Connect();
        Thread.Sleep(700);     // 等至少一个轮询周期(500ms)
        dev.Disconnect();

        Assert.Contains(1, got);
        Assert.Contains(2, got);
    }

    [Fact]
    public void PlcDevice_SimulateMode_RaisesData()
    {
        var dev = new PlcDevice(2, "PLC", new[] { new PlcDevice.PlcMap(3, "DB1.DBW0") }, simulate: true);
        var got = new List<int>();
        dev.DataReceived += (_, e) => got.Add(e.PointId);

        dev.Connect();
        Thread.Sleep(700);
        dev.Disconnect();

        Assert.Contains(3, got);
    }
}
```

</details>

> 📂 `src/DaqMonitor.Tests/CanDeviceTests.cs`

搭积木:第 1 步骨架 + 温度解码测试(含内嵌的"异 ID 通道"),第 2 步贴入忽略测试。

**第 1 步 · 骨架 + 温度帧解码测试**(整个文件先建出来)

```csharp
using DaqMonitor.Core.Devices;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 验证 CAN 设备:用 SimulatedCanChannel(内存模拟,零硬件)喂温度帧,
/// 断言 CanDevice 把 ID=0x100 的帧解码成 point1 = 25.0℃,且不认其它 ID 的帧。
/// </summary>
public class CanDeviceTests
{
    [Fact]
    public void Decodes_TempFrame_To_25C()
    {
        var ch = new SimulatedCanChannel();
        var dev = new CanDevice(1, "CAN", ch);
        var got = new List<(int, double)>();
        dev.DataReceived += (_, e) => got.Add((e.PointId, e.Value));

        dev.Connect();
        Thread.Sleep(150);
        dev.Disconnect();

        Assert.Contains(got, x => x.Item1 == 1 && Math.Abs(x.Item2 - 25.0) < 1e-6);
    }

    /// <summary>非 0x100 的帧(如 0x999)应被忽略,不污染业务。</summary>
    private sealed class OtherIdChannel : ICanChannel
    {
        public event Action<ulong, byte[]>? FrameReceived;
        public bool IsOpen { get; private set; }
        public void Open() { IsOpen = true; FrameReceived?.Invoke(0x999, new byte[] { 0x00, 0xFA }); }
        public void Send(ulong id, byte[] d) { }
        public void Close() { IsOpen = false; }
    }
}
```

📚 **知识点**
- **测试里内嵌私有替身类 `OtherIdChannel`**:SimulatedCanChannel 只会发 0x100,想测"异 ID 被忽略"就得现造一个发 0x999 的通道——**替身不一定抽成公共文件,内嵌私有类够用就好**(前端类比:测试文件里写个局部 mock 组件)。
- **`sealed` + `private` 双保险**:替身类不许被继承不许被外部引用——它只服务于这一个测试。

**第 2 步 · 异 ID 忽略测试**(贴进类里,最后一个 `}` 之前)

```csharp
    [Fact]
    public void Ignores_NonTempId_Frames()
    {
        var dev = new CanDevice(1, "CAN", new OtherIdChannel());
        int count = 0;
        dev.DataReceived += (_, e) => count++;
        dev.Connect();
        Thread.Sleep(100);
        dev.Disconnect();
        Assert.Equal(0, count);
    }
}
```

📚 **知识点**
- **0x999 帧的数据 `00 FA` 和温度帧一模一样**:故意的——如果解码器忘了过滤 ID、只按数据算,这条"伪温度 25.0"就会漏进来。**替身的数据也要设计成陷阱**,专骗没写过滤的实现。

<details markdown="1">
<summary>📄 完整文件 CanDeviceTests.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Devices;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 验证 CAN 设备:用 SimulatedCanChannel(内存模拟,零硬件)喂温度帧,
/// 断言 CanDevice 把 ID=0x100 的帧解码成 point1 = 25.0℃,且不认其它 ID 的帧。
/// </summary>
public class CanDeviceTests
{
    [Fact]
    public void Decodes_TempFrame_To_25C()
    {
        var ch = new SimulatedCanChannel();
        var dev = new CanDevice(1, "CAN", ch);
        var got = new List<(int, double)>();
        dev.DataReceived += (_, e) => got.Add((e.PointId, e.Value));

        dev.Connect();
        Thread.Sleep(150);
        dev.Disconnect();

        Assert.Contains(got, x => x.Item1 == 1 && Math.Abs(x.Item2 - 25.0) < 1e-6);
    }

    /// <summary>非 0x100 的帧(如 0x999)应被忽略,不污染业务。</summary>
    private sealed class OtherIdChannel : ICanChannel
    {
        public event Action<ulong, byte[]>? FrameReceived;
        public bool IsOpen { get; private set; }
        public void Open() { IsOpen = true; FrameReceived?.Invoke(0x999, new byte[] { 0x00, 0xFA }); }
        public void Send(ulong id, byte[] d) { }
        public void Close() { IsOpen = false; }
    }

    [Fact]
    public void Ignores_NonTempId_Frames()
    {
        var dev = new CanDevice(1, "CAN", new OtherIdChannel());
        int count = 0;
        dev.DataReceived += (_, e) => count++;
        dev.Connect();
        Thread.Sleep(100);
        dev.Disconnect();
        Assert.Equal(0, count);
    }
}
```

</details>

> 📂 `src/DaqMonitor.Tests/UsbHidDeviceTests.cs`

搭积木:第 1 步骨架 + 温度报告测试,第 2 步贴入压力报告测试。

**第 1 步 · 骨架 + 温度报告解码测试**(整个文件先建出来)

```csharp
using DaqMonitor.Core.Devices;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 验证 USB-HID 设备:用 SimulatedHidChannel(内存模拟,零硬件)喂报告,
/// 断言温度报告 0x01→25.0℃、压力报告 0x02→30.0kPa 都能正确解码。
/// </summary>
public class UsbHidDeviceTests
{
    [Fact]
    public void Decodes_TempReport_To_25C()
    {
        var ch = new SimulatedHidChannel();
        var dev = new UsbHidDevice(1, "HID", ch);
        var got = new List<(int, double)>();
        dev.DataReceived += (_, e) => got.Add((e.PointId, e.Value));

        dev.Connect();
        Thread.Sleep(150);
        dev.Disconnect();

        Assert.Contains(got, x => x.Item1 == 1 && Math.Abs(x.Item2 - 25.0) < 1e-6);
    }
}
```

📚 **知识点**
- **温度报告是 Open 自推的**:Connect 后 Sleep 等它到——**HID 设备"上电即报"的行为在替身里复现**,测试节奏和真实仪器一致。

**第 2 步 · 压力报告解码测试(主动触发型)**(贴进类里,最后一个 `}` 之前)

```csharp
    [Fact]
    public void Decodes_PressureReport_To_30kPa()
    {
        var ch = new SimulatedHidChannel();
        var dev = new UsbHidDevice(1, "HID", ch);
        var got = new List<(int, double)>();
        dev.DataReceived += (_, e) => got.Add((e.PointId, e.Value));

        dev.Connect();
        dev.Write(2, 5);            // 触发模拟设备回压力包
        Thread.Sleep(150);
        dev.Disconnect();

        Assert.Contains(got, x => x.Item1 == 2 && Math.Abs(x.Item2 - 30.0) < 1e-6);
    }
}
```

📚 **知识点**
- **这个测试走的是"命令 → 响应"往返**:Write 触发替身回压力包——**和温度测试的"自推"互补**,一个测被动接收、一个测主动交互,两条解码路径各有一个测试把守。

<details markdown="1">
<summary>📄 完整文件 UsbHidDeviceTests.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Devices;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 验证 USB-HID 设备:用 SimulatedHidChannel(内存模拟,零硬件)喂报告,
/// 断言温度报告 0x01→25.0℃、压力报告 0x02→30.0kPa 都能正确解码。
/// </summary>
public class UsbHidDeviceTests
{
    [Fact]
    public void Decodes_TempReport_To_25C()
    {
        var ch = new SimulatedHidChannel();
        var dev = new UsbHidDevice(1, "HID", ch);
        var got = new List<(int, double)>();
        dev.DataReceived += (_, e) => got.Add((e.PointId, e.Value));

        dev.Connect();
        Thread.Sleep(150);
        dev.Disconnect();

        Assert.Contains(got, x => x.Item1 == 1 && Math.Abs(x.Item2 - 25.0) < 1e-6);
    }

    [Fact]
    public void Decodes_PressureReport_To_30kPa()
    {
        var ch = new SimulatedHidChannel();
        var dev = new UsbHidDevice(1, "HID", ch);
        var got = new List<(int, double)>();
        dev.DataReceived += (_, e) => got.Add((e.PointId, e.Value));

        dev.Connect();
        dev.Write(2, 5);            // 触发模拟设备回压力包
        Thread.Sleep(150);
        dev.Disconnect();

        Assert.Contains(got, x => x.Item1 == 2 && Math.Abs(x.Item2 - 30.0) < 1e-6);
    }
}
```

</details>

> 📂 `src/DaqMonitor.Tests/TcpFrameParserTests.cs` — **贴进 TcpFrameParserTests 类里**(最后一个 `}` 之前)追加 1 个测试;文件头同步补 `using DaqMonitor.Core.Devices;`

```csharp
    [Fact]
    public void TcpDevice_Simulate_ProducesValues()
    {
        var maps = new[] { new TcpDevice.TcpMap(1, 1001), new TcpDevice.TcpMap(2, 1002) };
        using var dev = new TcpDevice(1, "TCP-Sim", "127.0.0.1", 9999, maps, simulate: true);

        int events = 0;
        dev.DataReceived += (_, _) => Interlocked.Increment(ref events);

        dev.Connect();
        Thread.Sleep(700);   // 等一轮 500ms tick
        dev.Disconnect();

        Assert.True(events >= 2, $"expected ≥2 events, got {events}");
        Assert.False(double.IsNaN(dev.Read(1)));
    }
```

## ✅ 验证(必做)

```bash
dotnet build
dotnet test
```
**期望输出(关键行)**:
```
已成功生成。 → 0 个警告 0 个错误
已通过! - 失败: 0,通过: 27 ... DaqMonitor.Tests.dll
```
(27 = R2 的 2 + R3 的 13 + 本篇 12)

## ✅ 验收清单

- [ ] build 0 错 0 警,test 27/27 绿
- [ ] 能回答:为什么 ISerialChannel 是"链路抽象"而 IDevice 是"设备抽象"?各自解耦什么?
- [ ] 能回答:Modbus 手搓帧 vs NModbus,什么场景选手搓?(学习/特殊设备/零依赖;生产赶工选库)
- [ ] 能回答:TcpDevice 的 needResync 为什么要"丢 1 字节"而不是清空缓冲?(可能丢掉下一帧的开头)
- [ ] 亲手试:把 SerialDeviceTests 里的 LoopbackSerialChannel 换成 RealSerialChannel("COM3", ...)——代码不用改,只有没插硬件会连不上,体会接口的边界
- [ ] git commit -m "R4: 真实设备接入 串口/Modbus/TCP/PLC/CAN/HID+12测试"

## 🎤 面试怎么讲这一篇

> "设备接入层我做了六种:串口 AA55、Modbus RTU、TCP 长连接、S7 PLC、CAN、USB-HID,全部实现同一个 IDevice 接口,采集管道和 UI 零改动。关键设计是把'链路'和'协议'再解耦一层:SerialDevice 不直接摸 SerialPort,而是依赖 ISerialChannel,所以单测用内存回环通道就能覆盖单帧、粘包、半包、坏 CRC 四种场景,CI 不需要任何硬件。Modbus 我手搓 RTU 帧不依赖 NModbus——组帧、CRC、异常码、浮点字节序都自己写过一遍,现场抓包能直接看懂。TCP 设备带心跳保活、30 秒静默判掉线和指数退避重连。这套通道抽象在 CAN 和 HID 上复用了同样的套路:模拟通道喂标准帧,设备类的解码逻辑全部可单测。"

**✅ 打卡[ ]**

---

## 🧩 附录:调库版 R4 —— ModbusDevice(FluentModbus,一晚通关)

> **定位**:大部分企业做 R4 这层**直接调库**——本附录就是那条路线:把手搓 ModbusDevice 换成 FluentModbus 实现,**同名、同接口(IDevice)、同测试思路,上层零改动**。走这条路线时,③ 手搓帧降为"通读",①②⑤⑥跳过(见文末"怎么处理本篇其余部分")。
> **验证声明**:以下代码+测试在 .NET 8 沙盒真跑通(3/3 绿,含"设备晚上电自愈"),不是伪代码。
> 选型依据见[《速查 · 工业通讯调库指南》](速查_工业通讯调库指南.md):Modbus 首选 FluentModbus(net8 原生,MIT)。

### ⓪ 装包

```bash
cd src/DaqMonitor.Core
dotnet add package FluentModbus
```

- 只装 Core,Tests 项目**经项目引用自动可见**(PackageReference 默认传递),不用再装
- TCP 版不需要 System.IO.Ports(那是 RTU 串口才要的)

### 先想清楚:库替你干了什么、没替你干什么

| 手搓版(③ 的几十行) | 调库版 |
|---|---|
| BuildReadHoldingRequest 组 8 字节请求帧 | `client.ReadHoldingRegisters(unitId, addr, count)` 一行 |
| Crc16.Modbus 移位异或算校验 | 库内部算,不可见 |
| 解析响应/异常码分流 | 直接给你 `short[]` |
| **连接生命周期(Connect/状态机三态)** | **还是你写** |
| **轮询节奏(Start/Stop/Task.Run 循环)** | **还是你写** |
| **断线重连(catch → 新客户端 → 重试)** | **还是你写** |
| **点表映射(寄存器→PointId)+ RaiseData 广播** | **还是你写** |

**一句话:库外包的只有字节层;设备层四件套(连接/轮询/重连/广播)一寸不少**——这张表就是面试答"调了库你自己写了什么"的清单。

### 完整代码(贴入即用)

> 📂 `src/DaqMonitor.Core/Devices/ModbusDevice.cs`(新文件;你工程里没有手搓版,直接用这个名字)· namespace `DaqMonitor.Core.Devices`
> 🔧 FluentModbus(⓪ 已装) · 💡 骨架完全照抄 R2 的 SimulatedDevice——同步门面 + Task.Run 后台循环 + 停机三步舞,一个模式不换

```csharp
using DaqMonitor.Core.Models;
using System.Net;
using System.Threading;
using FluentModbus;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// Modbus TCP 设备(调库版:FluentModbus)。
/// 与手搓版同接口——上层(管道/UI/报警)零改动;区别只在内部:
/// 组帧/CRC/解析由库代劳,自己只管连接生命周期、轮询、断线重连、事件广播。
/// </summary>
public class ModbusDevice : DeviceBase
{
    private readonly IPEndPoint _endPoint;
    private readonly byte _unitId;
    private readonly ushort _startAddress;
    private readonly int[] _pointIds;          // 点表:寄存器 startAddress+i → PointId
    private ModbusTcpClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public ModbusDevice(int id, string name, string ip, int port, byte unitId,
                        ushort startAddress, params int[] pointIds)
        : base(id, name)
    {
        _endPoint = new IPEndPoint(IPAddress.Parse(ip), port);
        _unitId = unitId;
        _startAddress = startAddress;
        _pointIds = pointIds.Length > 0 ? pointIds : new[] { 1 };
    }

    /// <summary>当前客户端(未连接先 Connect)</summary>
    private ModbusTcpClient Client
        => _client ?? throw new InvalidOperationException("先调用 Connect()");

    public override void Connect()
    {
        State = DeviceState.Connecting;
        try
        {
            _client = new ModbusTcpClient();
            _client.Connect(_endPoint);
            State = DeviceState.Online;
        }
        catch
        {
            // 现场常态:设备还没上电。连接失败不向上抛,状态回 Offline,
            // 由 Start() 的重连循环继续试——软件不能因为一台设备没开就崩。
            State = DeviceState.Offline;
        }
    }

    public override void Disconnect()
    {
        Stop();
        _client?.Dispose();
        _client = null;
        State = DeviceState.Offline;
    }

    public override double Read(int addr)
        => Client.ReadHoldingRegisters<short>(_unitId, (ushort)addr, 1)[0];

    public override void Write(int addr, double value)
        // (ushort) 显式转型:WriteSingleRegister 的 short/ushort 重载会让整数字面量二义性
        => Client.WriteSingleRegister(_unitId, (ushort)addr, (ushort)value);

    /// <summary>
    /// 开始轮询:每 interval 读一批保持寄存器(startAddress 起,共 pointIds.Length 个),
    /// 按点表逐个 RaiseData 广播;读失败(断线/设备掉电)自动重连。
    /// </summary>
    public void Start(TimeSpan interval)
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
                    try
                    {
                        var regs = ReadBlock();
                        for (int i = 0; i < _pointIds.Length; i++)
                            RaiseData(_pointIds[i], regs[i]);
                        State = DeviceState.Online;
                    }
                    catch
                    {
                        // 断线重连:换新客户端重试。指数退避版见 ④ TcpDevice,
                        // 这里用轮询间隔本身节流(50ms~1s 一试,足够温和)。
                        State = DeviceState.Connecting;
                        try
                        {
                            _client?.Dispose();
                            _client = new ModbusTcpClient();
                            _client.Connect(_endPoint);   // 失败会抛,下一轮再试
                            State = DeviceState.Online;
                        }
                        catch { /* 这轮连不上,留着 Connecting,下一轮再试 */ }
                    }
                    await Task.Delay(interval, token);
                }
            }
            catch (OperationCanceledException) { /* 正常退出 */ }
        }, token);
    }

    /// <summary>停止轮询并释放后台任务</summary>
    public void Stop()
    {
        _cts?.Cancel();
        try { _loop?.Wait(500); } catch { /* 忽略 */ }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    /// <summary>
    /// Span 不能跨 await(编译器禁止 ref struct 进异步方法局部),
    /// 所以用一个小的同步方法把寄存器块读出来、就地 .ToArray() 留存。
    /// </summary>
    private short[] ReadBlock()
        => Client.ReadHoldingRegisters<short>(_unitId, _startAddress, (ushort)_pointIds.Length).ToArray();
}
```

📚 **知识点**(4 个,全是调库实踩)

- **Connect 不抛异常**:设备没上电是现场常态——状态回 Offline、交给重连循环,比抛异常崩给用户看更"工业"。前端类比:请求失败进 error 态而不是白屏。
- **ReadBlock 为什么是个小同步方法**:FluentModbus 返回 `Span<short>`(ref struct),**不能当 async 方法的局部变量**——包一层同步方法、就地 `.ToArray()`,编译器就闭嘴了。库的现代化 API 和"同步门面"架构在这里碰了一下,这样接住。
- **`(ushort)value` 显式转型**:`WriteSingleRegister` 有 short/ushort 两个重载,整数字面量直接传会二义性编译错。
- **点表**:`params int[] pointIds`,寄存器 `startAddress+i` → `pointIds[i]`,= 前端接 API 时写的字段映射表。真项目点表来自配置文件/数据库,不在代码里写死。

### 测试(3 个,进程内主从,零硬件)

> 📂 `src/DaqMonitor.Tests/ModbusDeviceTests.cs` · 🔧 无需装包(经 Core 传递可见)
> 💡 妙点:测试里起一个 **ModbusTcpServer 当"真设备"**——比 ⑦ 的 Loopback 假字节更真实:你是真的在跑 Modbus 主从协议,断言的是协议往返

```csharp
using System.Net;
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Models;
using FluentModbus;
using Xunit;

namespace DaqMonitor.Tests;

// 调库版 ModbusDevice 的验收测试:进程内起 ModbusTcpServer 当"真设备",零硬件
public class ModbusDeviceTests
{
    /// <summary>起一个进程内 Modbus TCP 从站,预置地址 0/1 两个寄存器</summary>
    private static ModbusTcpServer StartServer(int port, short v0, short v1)
    {
        var server = new ModbusTcpServer();
        server.AddUnit(1);                                   // 先注册从站号,再取缓冲(顺序反了抛 KeyNotFound)
        server.Start(new IPEndPoint(IPAddress.Loopback, port));
        server.GetHoldingRegisterBuffer<short>(1)[0] = v0;
        server.GetHoldingRegisterBuffer<short>(1)[1] = v1;
        return server;
    }

    private static bool WaitUntil(Func<bool> cond, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (cond()) return true;
            Thread.Sleep(50);
        }
        return cond();
    }

    [Fact]
    public void ModbusDevice_Poll_ReadsPresetRegisters()
    {
        using var server = StartServer(15030, 0x1234, 0x5678);
        var dev = new ModbusDevice(1, "Mod-01", "127.0.0.1", 15030, 1, 0, 101, 102);

        var arrived = new AutoResetEvent(false);
        DataEventArgs? first = null;
        dev.DataReceived += (_, e) => { if (first is null) { first = e; arrived.Set(); } };

        dev.Connect();
        Assert.Equal(DeviceState.Online, dev.State);

        dev.Start(TimeSpan.FromMilliseconds(50));
        Assert.True(arrived.WaitOne(3000), "3 秒内应收到至少一次数据事件");
        Assert.Equal(101, first!.PointId);
        Assert.Equal(0x1234, (int)first.Value);
        dev.Stop();
    }

    [Fact]
    public void ModbusDevice_Write_ReadsBack()
    {
        using var server = StartServer(15031, 0, 0);
        var dev = new ModbusDevice(2, "Mod-02", "127.0.0.1", 15031, 1, 0, 201);

        dev.Connect();
        dev.Write(5, 1234);
        Assert.Equal(1234, (int)dev.Read(5));
        dev.Disconnect();
    }

    [Fact]
    public void ModbusDevice_ServerLateOnline_Recovers()
    {
        // 场景:软件先启动,设备(服务器)还没上电——设备应进入重连态不崩;设备上电后自愈
        var dev = new ModbusDevice(3, "Mod-03", "127.0.0.1", 15032, 1, 0, 301);

        dev.Connect();                                       // 端口没人听 → 容忍,状态回 Offline
        Assert.Equal(DeviceState.Offline, dev.State);

        dev.Start(TimeSpan.FromMilliseconds(50));            // 轮询循环开始重试
        Assert.True(WaitUntil(() => dev.State == DeviceState.Connecting, 3000),
            "连不上时应处于 Connecting(重连中)而不是崩掉");

        using var server = StartServer(15032, 7, 8);         // "设备上电"
        var arrived = new AutoResetEvent(false);
        DataEventArgs? first = null;
        dev.DataReceived += (_, e) => { if (first is null) { first = e; arrived.Set(); } };

        Assert.True(arrived.WaitOne(5000), "设备上线后 5 秒内应恢复出数");
        Assert.Equal(301, first!.PointId);
        Assert.Equal(7, (int)first.Value);
        Assert.Equal(DeviceState.Online, dev.State);
        dev.Stop();
    }
}
```

### ✅ 验证(附录路线)

```bash
dotnet build
dotnet test
```

**期望输出(关键行)**——沙盒实测(.NET 8 + FluentModbus 5.3.2,0 错 0 警):

```
已通过! - 失败:     0，通过:     3，已跳过:     0，总计:     3，持续时间: 1 s - DaqMonitor.Tests.dll (net8.0)
```

调库路线下总测试数 = R2 的 2 + 本附录 3 = **5 个绿**(③⑦ 的手搓帧测试不敲)。

### 走调库路线时,本篇其余部分怎么处理

| 部分 | 处理 |
|---|---|
| ①② 串口通道三件套 / SerialDevice | **跳过不敲**——那是手搓字节层;读懂"链路抽象是为了可测试"这个动机即可 |
| ③ 手搓 ModbusDevice | 降为**通读一遍**——面试要讲得出组帧/CRC 在干嘛(E04 弹药) |
| ④ TcpDevice | **精读**——心跳/30 秒静默判掉线/指数退避,调库版没细做,面试常问 |
| ⑤⑥ PlcDevice / CAN / USB-HID | 跳过,知道存在即可 |

### 🎤 调库路线面试一句话

> "生产里我用 FluentModbus:设备类实现同一个 IDevice,连接生命周期、轮询、断线重连是我写的,字节层(组帧/CRC/解析)交给库。手搓实现我也逐行拆过——CRC、帧格式、粘包半包,所以库出问题时我知道往哪查。"

