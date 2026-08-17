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

### ② SerialDevice —— AA55 帧串口设备

> 📂 `src/DaqMonitor.Core/Devices/SerialDevice.cs`
> 💡 R3 FrameParser 的第一个真实消费者:字节流进、SensorPoint 语义的事件出
> 🗺️ **新手读码地图**(3 步看懂):1. `Connect()` 只做两件事:订阅通道的 BytesReceived + Open 链路——从此字节是"推"过来的,不用轮询 2. 灵魂在 `OnBytes` 的 10 行:收到一坨字节 → `RawLog` 先留痕(联调时看清线上到底来了什么)→ 喂 `_parser.Feed`,半包/粘包它内部搞定,吐出 N 条完整载荷 → 每条按"1 字节 pointId + 8 字节 double"解码,`RaiseData` 发事件(DeviceBase 继承来的,自动盖时间戳) 3. `Read(addr)` 返回 `_last` 缓存的新值——不是真去问设备;串口设备没有"随叫随到"的读法,值全靠事件推。**前端类比**:`OnBytes` ≈ WebSocket onmessage 的 handler:先 log → 切包 → emit 给上层。整类 = 链路 + 协议解析 + 事件发射的三合一。

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

### ③ ModbusDevice —— 手搓 RTU 帧(不依赖 NModbus)

> 📂 `src/DaqMonitor.Core/Devices/ModbusDevice.cs`
> 💡 双模式套路:**simulate=true 零硬件跑链路;真实模式手搓请求帧**。TCP 模式只需把 SerialPort 换 TcpClient + MBAP 头,解析逻辑复用
> 🗺️ **新手读码地图**(4 步看懂):1. `RegisterMap` 是一张"翻译对照表":哪个点位对应哪个寄存器地址、什么类型(float 跨 2 寄存器/word 1 个)、什么字节序——现场调试改的是这张表,不是代码 2. 双模式在 `Start()` 的后台循环里分叉:每 500ms 一次 `SimulateTick()`(发随机值,零硬件)或 `RealTick()`(手搓"读保持寄存器"请求帧 → 写串口 → 收响应 → Crc16 验 → ModbusFrameParser 拆 → 按字节序拼回浮点) 3. 真实模式的"手搓帧"就是 R3 `BuildReadHoldingRequest` + `ParseReadRegisters` + `ToFloatModbus` 三件套串起来——R3 的纯函数在这落地成真设备 4. 对外仍然只暴露 IDevice:管道/UI 完全不知道底下是 Modbus。**前端类比**:`RegisterMap` ≈ 后端字段映射表(接口返回 snake_case 映射到组件的 camelCase),双模式 ≈ dev 环境 mock/生产环境真接口同一开关切换。

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

### ④ TcpDevice —— 长连接 + 心跳 + 指数退避重连

> 📂 `src/DaqMonitor.Core/Devices/TcpDevice.cs`
> 💡 本篇最重的类:把 R3 的 TcpFrameParser 塞进真实 socket 循环;[指数退避](kp:retry-backoff)在这里先见第一面(R7 会抽成通用 Retry)
> 🗺️ **新手读码地图**(按"一条命"的周期看):1. 外层 `MaintainConnectionLoop` 是一条永动的命:连上 → 干活 → 断了 → 睡一会儿再连,直到 Dispose。睡多久不是固定值,是 `BackoffMs = {1s,2s,4s,8s,16s}` 一级级往上爬——网络刚抖完马上重连只会雪上加霜,这就是**指数退避** 2. 连上后兵分两路:`HeartbeatLoop` 每 10s 发一帧 `[0x02]` 心跳保活,30s 收不到对端消息判离线;`ReceiveLoop` 是主收线——socket 只管把字节堆进滚动缓冲,切帧全交给 `TcpFrameParser.TryParse`(R3 的无状态设计在这兑现:缓冲归调用方管) 3. 切出的帧按载荷解码成点位 → `RaiseData` 上报,和串口设备殊途同归 4. `OfflineTimeout` = 心跳超时兜底:TCP 半开连接(对端拔网线)不会自动报错,必须自己掐表。**前端类比**:`MaintainConnectionLoop` ≈ socket.io 内置的重连机制(它默认也是指数退避),心跳 ≈ ping/pong 帧——你写前端长连接时框架替你干的事,这里全部手写一遍。

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

### ⑤ PlcDevice —— S7 模拟模式(真实路径注释保留)

> 📂 `src/DaqMonitor.Core/Devices/PlcDevice.cs` · 🔧 无 NuGet(S7NetPlus 到真接 PLC 再装)

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

> 📂 `src/DaqMonitor.Core/Devices/CanDevice.cs`
> 💡 CAN vs Modbus(面试常考):Modbus 主从问答;CAN 多主广播,靠 **ID** 区分信号,没有地址/功能码

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

> 📂 `src/DaqMonitor.Core/Devices/UsbHidDevice.cs`

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

### ⑦ 测试(12 个新增)

> 📂 `src/DaqMonitor.Tests/SerialDeviceTests.cs`(R4 版 5 个;穿管道测试 R5 有了管道再补)
> 💡 用 LoopbackSerialChannel 喂字节——**不碰任何真实串口,CI / 没硬件的机器也能跑绿**

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

> 📂 `src/DaqMonitor.Tests/ModbusDeviceTests.cs`(R4 版 2 个;穿管道测试 R5 再补)

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

> 📂 `src/DaqMonitor.Tests/CanDeviceTests.cs`

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

> 📂 `src/DaqMonitor.Tests/UsbHidDeviceTests.cs`

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
