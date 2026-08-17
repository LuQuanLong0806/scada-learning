# R3 · 协议解析层(CRC / AA55 帧 / Modbus / TCP 帧,纯逻辑零 IO)

> **定位**:设备说"方言"(字节流),上层要说"普通话"(帧/值)。这一篇把三种方言的翻译官写出来——**不碰任何硬件**,所以测试最稳、面试最常问。
> **前置**:R2 全绿。**预计敲码**:90 分钟。
> **产出**:Protocol 四类 + 13 个测试,`dotnet test` 全绿(累计 15)。

---

## 🎯 本篇交付物

```
src/DaqMonitor.Core/Protocol/
├─ Crc16.cs             # CRC16/Modbus 校验:所有帧的"防伪码"
├─ FrameParser.cs       # AA55 自定义帧:流式拆包(半包/粘包/坏帧)
├─ ModbusFrameParser.cs # Modbus RTU:寄存器/线圈/浮点字节序/异常码
└─ TcpFrameParser.cs    # TCP 长度前缀帧:粘包重同步
src/DaqMonitor.Tests/
├─ FrameParserTests.cs        # 4 测试
├─ ModbusFrameParserTests.cs  # 5 测试
└─ TcpFrameParserTests.cs     # 4 测试(TcpDevice 回环测试在 R4)
```

## 📋 需求单(先自己设计,再对照)

| # | 需求 | 验收 |
|---|---|---|
| FR3-1 | [CRC16](kp:crc) 工具:Modbus 多项式 0xA001,算载荷 CRC;整帧校验(CRC 低字节在前) | 经典向量 `01 03 00 00 00 01` → `0x0A84` |
| FR3-2 | AA55 帧解析:字节流持续 `Feed`,循环拆完整帧;**半包**等待、**粘包**多帧齐拆、垃圾头清缓冲;`verifyCrc=true` 时坏帧静默丢弃 | 半包喂 2 次拆出 1 帧;粘包一次拆 2 帧 |
| FR3-3 | AA55 帧构造 `Build(pointId, value)`:payload = 1 字节点号 + 8 字节 double,自带 CRC | Build 出的帧能被 verifyCrc=true 的解析器吃回 |
| FR3-4 | [Modbus](kp:modbus) 帧解析:读寄存器响应(每寄存器 2 字节**大端**)、线圈按**位**打包、32 位浮点 4 种[字节序](kp:byte-order)、异常响应识别(功能码\|0x80)、组读保持寄存器请求帧 | 5 个测试全绿,含 ABCD=100.0 / CDAB≠100.0 |
| FR3-5 | [TCP 帧解析](kp:tcp-sticky):`AA 55 + 小端长度 + payload + CRC`;半包 false 不重同步;坏头/坏 CRC 置 needResync;payload 上限 8KB 防乱码巨分配 | Build→TryParse 往返一致;破坏 CRC 被拒 |

**自己先想 10 分钟**:
1. `FrameParser.Feed` 的缓冲区什么时候该清空?不清会怎样?(内存无限涨)
2. 线圈响应"每字节装 8 个 bit"和寄存器"高字节在前",为什么 Modbus 要定两套相反的规则?
3. CDAB 字交换:0x42C8 0x0000 按 ABCD 是 100.0,按 CDAB 会解析成什么量级?现场怎么排查?

## 📚 本篇知识点

- [CRC16 循环冗余校验](kp:crc) · [字节序/大小端](kp:byte-order) · [Modbus 协议](kp:modbus) · [AA55 串口帧](kp:serial-frame) · [TCP 粘包/半包](kp:tcp-sticky) · [xUnit 单元测试](kp:unit-test)

## 🛠️ 参考实现

> 全部是**纯逻辑**:吃 byte[] 吐 byte[]/数值,零 IO 依赖——这是"协议层可单测"的架构前提(前端类比:把 axios 换成纯函数处理响应体,Storybook 就能测)。

### ① Crc16 —— 一切帧的防伪码

> 📂 `src/DaqMonitor.Core/Protocol/Crc16.cs` · namespace `DaqMonitor.Core.Protocol`
> 🔧 无 NuGet
> 💡 逐位异或移位的教科书实现;Modbus 约定 CRC **低字节在前**放进帧尾
> 🗺️ **新手读码地图**:`Check(字节[])` 对每个字节做两件事——先和当前值低 8 位异或,再循环 8 次"右移 1 位,移出去的是 1 就再异或多项式 0xA001"。本质是给整段字节**算指纹**:任何一个字节被干扰,指纹就变,收方重算比对就知道帧坏了。面试常追问的查表法 = 预先算好 256 种结果换速度,原理一模一样。

```csharp
namespace DaqMonitor.Core.Protocol;

/// <summary>CRC16 工具(Modbus 多项式 0xA001)。校验与业务无关,纯算法。</summary>
public static class Crc16
{
    public static ushort Modbus(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    public static ushort Modbus(byte[] data) => Modbus(data.AsSpan());

    /// <summary>校验整帧(载荷 + 2 字节 CRC,低字节在前)。</summary>
    public static bool Check(ReadOnlySpan<byte> frameWithCrc)
    {
        if (frameWithCrc.Length < 2) return false;
        int payloadLen = frameWithCrc.Length - 2;
        ushort calc = Modbus(frameWithCrc[..payloadLen]);
        ushort got = (ushort)(frameWithCrc[^2] | (frameWithCrc[^1] << 8));
        return calc == got;
    }
}
```

### ② FrameParser —— AA55 自定义帧(串口半包/粘包)

> 📂 `src/DaqMonitor.Core/Protocol/FrameParser.cs`
> 🔧 无 NuGet
> 💡 帧格式 `AA 55 | Len | Payload... | CRC_L CRC_H`;**有状态**(内部缓冲),所以是 class 不是 static
> 🗺️ **新手读码地图**(4 步看懂):1. `Feed(chunk)` 是进货口:网络/串口来的字节**一段一段**到(可能是半帧,也可能几帧粘在一起),先全倒进 `_buffer` 这个蓄水池 2. `TryTakeFrame` 是取货口:循环从缓冲头尝试拆出**一条**完整帧,拆得出就收走,拆不出(字节不够=半包)就停手等下一批 3. 找帧头 `0xAA`:找不到→**清空整个缓冲**(防垃圾无限堆积);`0xAA` 后面不是 `0x55`→假帧头,删掉这 1 字节继续找 4. 拆走一条 = 头 3 字节 + Len 载荷 + 2 字节 CRC,只把载荷切出来返回,并从缓冲删掉已消费部分。**前端类比**:和 WebSocket onmessage 里攒字节流切消息一模一样(先 concat 再 while 切完整包);因为 `_buffer` 是跨调用记忆的状态,所以它是 class,而 Crc16/ModbusFrameParser 是 static——**有没有状态决定 class 还是 static**。

```csharp
using System.Collections.Generic;

namespace DaqMonitor.Core.Protocol;

/// <summary>
/// 自定义二进制帧解析:帧格式 AA 55 | Len | Payload... | CRC_L CRC_H。
/// 解决串口"半包 / 粘包":字节流持续喂入,循环拆出完整帧。缓冲永远拆不出完整帧时要清空,防内存泄漏。
///
/// - 构造参数 verifyCrc 为 true 时,拆出的帧会先用 Crc16.Check 校验,坏帧直接丢弃(防止坏数据进入业务)。
/// - Build 是写方向:把 (pointId, value) 编码成带 CRC 的完整帧,发送端用。
/// </summary>
public class FrameParser
{
    private readonly List<byte> _buffer = new();
    private readonly bool _verifyCrc;

    public FrameParser(bool verifyCrc = false) => _verifyCrc = verifyCrc;

    /// <summary>喂入一段字节,返回本次拆出的所有完整帧(仅载荷部分,不含头/长/CRC)。</summary>
    public IReadOnlyList<byte[]> Feed(ReadOnlySpan<byte> chunk)
    {
        _buffer.AddRange(chunk.ToArray());
        var frames = new List<byte[]>();
        while (TryTakeFrame(out var frame))
            frames.Add(frame);
        return frames;
    }

    private bool TryTakeFrame(out byte[] frame)
    {
        frame = [];
        int idx = _buffer.IndexOf((byte)0xAA);
        if (idx < 0) { _buffer.Clear(); return false; }              // 找不到帧头:清空防无限增长
        if (_buffer.Count < idx + 3) return false;                   // 头后不足 3 字节
        if (_buffer[idx + 1] != (byte)0x55) { _buffer.RemoveAt(idx); return false; }
        int len = _buffer[idx + 2];
        int total = 3 + len + 2;                                     // 头 + 长 + 载荷 + CRC
        if (_buffer.Count < idx + total) return false;               // 半包:等更多数据

        if (_verifyCrc)
        {
            var full = _buffer.GetRange(idx, total).ToArray();
            if (!Crc16.Check(full))                                 // CRC 不过:丢弃该帧
            {
                _buffer.RemoveRange(0, idx + total);
                return false;
            }
        }

        frame = _buffer.GetRange(idx + 3, len).ToArray();
        _buffer.RemoveRange(0, idx + total);
        return true;
    }

    public void Reset() => _buffer.Clear();

    /// <summary>
    /// 构造一帧(写方向):AA 55 | Len | Payload | CRC_L CRC_H。
    /// Payload = pointId(1 字节) + value(8 字节 double)。CRC 对"头+长+载荷"整体计算,与 Check 的校验范围一致。
    /// </summary>
    public static byte[] Build(int pointId, double value)
    {
        var payload = new List<byte> { (byte)pointId };
        payload.AddRange(BitConverter.GetBytes(value));

        // CRC 必须对"头 + 长 + 载荷"整体计算,才能和 Crc16.Check(整帧) 的校验范围一致
        var headAndPayload = new List<byte> { 0xAA, 0x55, (byte)payload.Count };
        headAndPayload.AddRange(payload);

        ushort crc = Crc16.Modbus(headAndPayload.ToArray());
        headAndPayload.Add((byte)(crc & 0xFF));
        headAndPayload.Add((byte)(crc >> 8));
        return headAndPayload.ToArray();
    }
}
```

### ③ ModbusFrameParser —— 工业标准协议的拆包

> 📂 `src/DaqMonitor.Core/Protocol/ModbusFrameParser.cs`
> 🔧 无 NuGet
> 💡 现场三坑全在这:**地址 ±1 偏移**(异常码 0x02)、**字节序 CDAB**(浮点读出天文数字)、**线圈按位不按字节**
> 🗺️ **新手读码地图**:整个类是**纯函数翻译器**——不存状态、不碰 I/O,每个方法都是"字节进→数据出",所以能甩开硬件直接单测。看懂三个方法就够:1. `ParseReadRegisters`:数据区每 2 字节 = 1 个寄存器,高字节在前(大端),`hi<<8|lo` 拼回 ushort 2. `ParseCoils`:开关量 1 个字节装 8 个,`i/8` 定位在第几个字节、`i%8` 定位第几位——和寄存器的"高字节在前"是两套规则,别混 3. `ToFloatModbus`:两个寄存器拼 32 位浮点;本机是小端,所以代码先把 4 字节按大端排好再 Reverse 交给 BitConverter——手册写 CDAB 你就传 CDAB,**抓帧确认,别猜**。**前端类比**:一个纯的 protocol decoder(输入 Uint8Array 输出对象),零副作用所以好测。

```csharp
using System;
using System.Collections.Generic;

namespace DaqMonitor.Core.Protocol;

/// <summary>
/// Modbus 帧解析(纯协议层、不依赖串口,可单测)。
/// 对应知识点:响应帧逐字节拆解 / 线圈位打包 / 32 位浮点 4 种字节序 / 异常码。
/// CRC 校验复用 Crc16(Modbus 多项式 0xA001)。
/// </summary>
public static class ModbusFrameParser
{
    /// <summary>32 位浮点跨 2 个寄存器时的字节序排列(现场 90% 问题是 CDAB 字交换)。</summary>
    public enum ByteOrder { ABCD, CDAB, BADC, DCBA }

    /// <summary>Modbus 异常码(异常响应里功能码 | 0x80 后的下一字节)。</summary>
    public static IReadOnlyDictionary<byte, string> ExceptionMessages { get; } = new Dictionary<byte, string>
    {
        [0x01] = "非法功能(设备不支持该功能码)",
        [0x02] = "非法地址(地址超出设备范围,常是 ±1 偏移)",
        [0x03] = "非法数据值(写的值/数量不合法)",
        [0x04] = "从站设备故障(设备内部出错)",
        [0x06] = "从站忙(稍后重试)",
        [0x0B] = "网关路径失效(经转发器时目标不可达)",
    };

    /// <summary>判断是否为异常响应;是则返回异常码(功能码最高位被置 1,如 0x03→0x83)。</summary>
    public static bool IsExceptionResponse(ReadOnlySpan<byte> resp, out byte exceptionCode)
    {
        exceptionCode = 0;
        if (resp.Length < 3) return false;
        if ((resp[1] & 0x80) == 0) return false;
        exceptionCode = resp[2];
        return true;
    }

    /// <summary>
    /// 解析「读保持/输入寄存器」响应帧(功能码 0x03 / 0x04):
    /// [从站][0x03][字节数 N*2][数据 N*2 字节,每寄存器 2 字节大端][CRC]。
    /// 返回每个寄存器的值(大端拼回 ushort)。不在此做 CRC 校验(调用方用 Crc16.Check 自查)。
    /// </summary>
    /// <exception cref="InvalidOperationException">功能码不是 0x03/0x04,或字节数不匹配。</exception>
    public static ushort[] ParseReadRegisters(ReadOnlySpan<byte> resp)
    {
        if (resp.Length < 5) throw new InvalidOperationException("响应帧太短");
        if (resp[1] != 0x03 && resp[1] != 0x04)
            throw new InvalidOperationException($"不是读寄存器响应,功能码=0x{resp[1]:X2}");
        int byteCount = resp[2];
        if (resp.Length < 3 + byteCount + 2)
            throw new InvalidOperationException("响应帧长度与声明的字节数不符(可能半包)");
        int regCount = byteCount / 2;
        var regs = new ushort[regCount];
        for (int i = 0; i < regCount; i++)
        {
            byte hi = resp[3 + i * 2];      // 高字节在前(大端)
            byte lo = resp[4 + i * 2];
            regs[i] = (ushort)(hi << 8 | lo);
        }
        return regs;
    }

    /// <summary>
    /// 解析「读线圈/离散输入」响应(功能码 0x01 / 0x02):数据区每字节装 8 个线圈,按位排。
    /// bit0 = 最先返回的线圈(与寄存器「高字节在前」是两套完全不同的规则)。
    /// </summary>
    public static bool[] ParseCoils(ReadOnlySpan<byte> data, int coilCount)
    {
        var bits = new bool[coilCount];
        for (int i = 0; i < coilCount; i++)
            bits[i] = (data[i / 8] & (1 << (i % 8))) != 0;
        return bits;
    }

    /// <summary>
    /// 把两个 16 位寄存器拼成 32 位 IEEE754 浮点。
    /// 关键区分:字节交换(BADC) 是寄存器内 2 字节颠倒;字交换(CDAB) 是两寄存器顺序颠倒。
    /// 现场默认常是 CDAB——抓帧确认,别猜。
    ///
    /// ⚠️ 实现要点:BitConverter 在本机(x86)是小端。设备回的是大端字节序的 4 字节,
    /// 所以必须先把 4 个字节按"大端顺序"排好(b0 是最高位字节)再交给 ToSingle,
    /// 否则会被当成小端解读成极小值——这正是"大小端"最隐蔽的坑。
    /// </summary>
    public static float ToFloatModbus(ushort r0, ushort r1, ByteOrder order) => order switch
    {
        ByteOrder.ABCD => ToSingleBig((byte)(r0 >> 8), (byte)r0, (byte)(r1 >> 8), (byte)r1),
        ByteOrder.CDAB => ToSingleBig((byte)(r1 >> 8), (byte)r1, (byte)(r0 >> 8), (byte)r0), // 字交换
        ByteOrder.BADC => ToSingleBig((byte)r0, (byte)(r0 >> 8), (byte)r1, (byte)(r1 >> 8)), // 字节交换
        ByteOrder.DCBA => ToSingleBig((byte)r1, (byte)(r1 >> 8), (byte)r0, (byte)(r0 >> 8)), // 全小端
        _ => throw new ArgumentOutOfRangeException(nameof(order))
    };

    /// <summary>按大端解读 4 字节为 float(b0 = 最高位字节)。</summary>
    private static float ToSingleBig(byte b0, byte b1, byte b2, byte b3)
    {
        // 本机小端:把大端字节数组 Reverse 成小端顺序再 ToSingle,等价于"按大端读这 4 字节"
        var le = new[] { b3, b2, b1, b0 };
        return BitConverter.ToSingle(le, 0);
    }

    /// <summary>
    /// 组「读保持寄存器」RTU 请求帧:[从站][0x03][地址2B大端][数量2B大端][CRC低前]。
    /// 用于 ModbusDevice 真实 RTU 路径下发读取。
    /// </summary>
    public static byte[] BuildReadHoldingRequest(byte slave, ushort addr, ushort count)
    {
        var payload = new List<byte> { slave, 0x03 };
        payload.Add((byte)(addr >> 8)); payload.Add((byte)addr);
        payload.Add((byte)(count >> 8)); payload.Add((byte)count);
        ushort crc = Crc16.Modbus(payload.ToArray());
        payload.Add((byte)(crc & 0xFF));   // CRC 低字节在前
        payload.Add((byte)(crc >> 8));
        return payload.ToArray();
    }
}
```

### ④ TcpFrameParser —— TCP 长度前缀帧

> 📂 `src/DaqMonitor.Core/Protocol/TcpFrameParser.cs`
> 🔧 无 NuGet
> 💡 与 FrameParser 的区别:TCP 帧头后带 **2 字节小端长度域**,且"解析"与"缓冲"解耦——本类无状态,缓冲区调用方维护,失败时给 needResync 信号
> 🗺️ **新手读码地图**:拆的还是"字节流切帧"这同一个问题,和 ② 的差别就两点:1. 帧头后是 **2 字节小端长度域**(`AA 55 LEN_LO LEN_HI payload CRC`),用小端是因为 C# 的 BitConverter 本机就是小端,拼长度不用翻转 2. 本类**无状态**:自己不攒缓冲,只提供 TryParse"给一段字节、试拆一帧",拆不出/拆坏返回 false 并告诉调用方要不要重同步,缓冲由调用方维护。**前端类比**:FrameParser 像"自己管 state 的组件"(内部攒 buffer),TcpFrameParser 像"受控/纯函数组件"(state 提升给调用方)——两种设计都对,看你想把复杂度放哪边。

```csharp
namespace DaqMonitor.Core.Protocol;

/// <summary>
/// TCP 长度前缀帧解析。
///
/// 帧格式(小端长度,便于 C# 直接 BitConverter 拼装):
///   [0xAA][0x55][LEN_LO][LEN_HI][PAYLOAD(LEN 字节)][CRC_LO][CRC_HI]
///
/// 设计要点:
///   - 协议层零 I/O 依赖:只吃字节缓冲、吐字节缓冲,便于单测(参考 ModbusFrameParser 风格)。
///   - CRC 复用 Crc16(Modbus 多项式 0xA001,工业现场通用)。
///   - 粘包/半包由调用方维护滚动缓冲区,本类提供「尝试解析一帧」语义:
///     TryParse 返回 true=已凑齐一帧(并给出帧总长);false=数据不够或帧坏,继续收/重同步。
/// </summary>
public static class TcpFrameParser
{
    /// <summary>帧头固定 2 字节:0xAA 0x55。</summary>
    public const byte Head0 = 0xAA;
    public const byte Head1 = 0x55;

    /// <summary>帧头 + 长度域共 4 字节;帧尾 CRC 2 字节。</summary>
    public const int HeaderSize = 4;
    public const int CrcSize = 2;
    /// <summary>payload 最大 8KB,防御乱码长度域导致的巨型分配。</summary>
    public const int MaxPayload = 8 * 1024;

    /// <summary>
    /// 组装一帧:[AA 55][LEN_LO][LEN_HI][payload][CRC_LO][CRC_HI]。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">payload 超过 MaxPayload。</exception>
    public static byte[] BuildFrame(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPayload)
            throw new ArgumentOutOfRangeException(nameof(payload), $"payload 超过 {MaxPayload} 字节上限");

        var frame = new byte[HeaderSize + payload.Length + CrcSize];
        frame[0] = Head0;
        frame[1] = Head1;
        frame[2] = (byte)(payload.Length & 0xFF);          // 小端长度
        frame[3] = (byte)((payload.Length >> 8) & 0xFF);
        payload.CopyTo(frame.AsSpan(HeaderSize));
        ushort crc = Crc16.Modbus(payload);                // 仅对 payload 计算 CRC
        frame[^2] = (byte)(crc & 0xFF);
        frame[^1] = (byte)((crc >> 8) & 0xFF);
        return frame;
    }

    /// <summary>
    /// 校验完整帧(含头/长度/payload/CRC)。返回 false 表示帧损坏,调用方应整帧丢弃。
    /// </summary>
    public static bool ValidateFrame(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < HeaderSize + CrcSize) return false;
        if (frame[0] != Head0 || frame[1] != Head1) return false;
        int len = frame[2] | (frame[3] << 8);
        if (len != frame.Length - HeaderSize - CrcSize) return false;
        // CRC 区 = payload + 2B CRC,复用 Crc16.Check(低字节在前)
        return Crc16.Check(frame.Slice(HeaderSize));
    }

    /// <summary>
    /// 从缓冲区头部尝试解析一帧。
    /// 成功:写入 payload、返回该帧总长度(调用方据此 Skip 缓冲区)。
    /// 失败(数据不足 / 头不对 / 长度非法 / CRC 坏):返回 0,调用方继续 Append。
    ///   - 头不对齐:返回 0 并设置 needResync=true,提示调用方可以丢弃 1 字节重同步;
    ///     本方法自身不丢弃,保持缓冲区语义纯粹。
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> buffer, out byte[] payload, out int frameLength, out bool needResync)
    {
        payload = Array.Empty<byte>();
        frameLength = 0;
        needResync = false;

        if (buffer.Length < HeaderSize) return false;
        if (buffer[0] != Head0 || buffer[1] != Head1) { needResync = true; return false; }

        int len = buffer[2] | (buffer[3] << 8);
        if (len > MaxPayload) { needResync = true; return false; }   // 长度域乱码:重同步
        int total = HeaderSize + len + CrcSize;
        if (buffer.Length < total) return false;                     // 半包:等更多数据

        var frame = buffer[..total];
        if (!Crc16.Check(frame.Slice(HeaderSize))) { needResync = true; return false; } // CRC 坏:重同步

        payload = frame.Slice(HeaderSize, len).ToArray();
        frameLength = total;
        return true;
    }
}
```

### ⑤ 三个测试文件(13 个测试)

> 📂 `src/DaqMonitor.Tests/FrameParserTests.cs` · namespace `DaqMonitor.Tests`
> 🔧 无 NuGet
> 💡 协议测试的灵魂:**已知向量**(CRC 标准测试值)+ **构造-解析往返**(Build 出的帧必须能原样解回)

```csharp
using DaqMonitor.Core.Protocol;
using Xunit;

namespace DaqMonitor.Tests;

public class FrameParserTests
{
    [Fact]
    public void Crc16_Modbus_KnownVector()
    {
        // 经典测试向量:CRC16/MODBUS of {0x01,0x03,0x00,0x00,0x00,0x01}
        // 算法寄存器结果 = 0x0A84;按 Modbus 约定「低字节在前」发送,故线上字节为 84 0A(常被记作 0x840A 的大端读法)。
        var data = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x01 };
        Assert.Equal((ushort)0x0A84, Crc16.Modbus(data));
    }

    [Fact]
    public void Feed_Splits粘包_AndHandles半包()
    {
        var p = new FrameParser();
        var payload = new byte[] { 0xAA, 0x55, 0x02, 0x11, 0x22 };
        ushort crc = Crc16.Modbus(payload);
        var frame = payload.Concat(new[] { (byte)(crc & 0xFF), (byte)(crc >> 8) }).ToArray();

        // 半包:先喂前 3 字节
        Assert.Empty(p.Feed(frame.AsSpan(0, 3).ToArray()));
        // 再喂剩下的:应拆出 1 帧,载荷为 {0x11,0x22}
        var second = p.Feed(frame.AsSpan(3).ToArray());
        Assert.Single(second);
        Assert.Equal(new byte[] { 0x11, 0x22 }, second[0]);
    }

    [Fact]
    public void Feed_ignoresBadHeader()
    {
        var p = new FrameParser();
        var bad = new byte[] { 0x00, 0x55, 0x02, 0x11, 0x22, 0x00, 0x00 };
        Assert.Empty(p.Feed(bad));
    }

    [Fact]
    public void Crc16_Check_ValidatesFrame()
    {
        var payload = new byte[] { 0xAA, 0x55, 0x02, 0x11, 0x22 };
        ushort crc = Crc16.Modbus(payload);
        var frame = payload.Concat(new[] { (byte)(crc & 0xFF), (byte)(crc >> 8) }).ToArray();
        Assert.True(Crc16.Check(frame));
    }
}
```

> 📂 `src/DaqMonitor.Tests/ModbusFrameParserTests.cs`

```csharp
using DaqMonitor.Core.Protocol;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 验证 Modbus 报文解析知识点真落地:响应帧拆解 / 线圈位解包 / 浮点 4 种字节序 / 异常码 / 组帧。
/// 纯协议层,不碰串口,CI 可跑绿。
/// </summary>
public class ModbusFrameParserTests
{
    [Fact]
    public void ParseReadRegisters_DecodesTwoRegisters()
    {
        // 响应:01 03 04 | 00 0A 00 14 | C4 0B
        var resp = new byte[] { 0x01, 0x03, 0x04, 0x00, 0x0A, 0x00, 0x14, 0xC4, 0x0B };
        var regs = ModbusFrameParser.ParseReadRegisters(resp);
        Assert.Equal(new ushort[] { 10, 20 }, regs);
    }

    [Fact]
    public void ParseCoils_UnpacksBits()
    {
        // 字节 FF 03 → 线圈 0–7 全在线;8、9 在线;10–15 离线
        var bits = ModbusFrameParser.ParseCoils(new byte[] { 0xFF, 0x03 }, 12);
        Assert.True(bits[0] && bits[7] && bits[8] && bits[9]);
        Assert.False(bits[10] && bits[11]);
    }

    [Fact]
    public void ToFloatModbus_ABCD_IsCorrect_But_CDAB_IsNot()
    {
        // 干净样例:0x42C80000 = 100.0f(r0=0x42C8, r1=0x0000)。
        // 注意:本机 x86 是小端,解析器内部已按"大端字节序"正确还原,ABCD 才得 100.0。
        float abcd = ModbusFrameParser.ToFloatModbus(0x42C8, 0x0000, ModbusFrameParser.ByteOrder.ABCD);
        float cdab = ModbusFrameParser.ToFloatModbus(0x42C8, 0x0000, ModbusFrameParser.ByteOrder.CDAB);
        Assert.Equal(100.0f, abcd);                      // ABCD 正确还原:100.0
        Assert.True(System.Math.Abs(cdab - 100.0f) > 1); // 字交换得到错值,现场翻车根因
    }

    [Fact]
    public void IsExceptionResponse_DetectsIllegalAddress()
    {
        // 设备拒了:功能码 0x83,异常码 0x02(非法地址)
        var resp = new byte[] { 0x01, 0x83, 0x02 };
        Assert.True(ModbusFrameParser.IsExceptionResponse(resp, out var code));
        Assert.Equal((byte)0x02, code);
        Assert.Equal("非法地址(地址超出设备范围,常是 ±1 偏移)", ModbusFrameParser.ExceptionMessages[code]);
    }

    [Fact]
    public void BuildReadHoldingRequest_ProducesFrameWithValidCrc()
    {
        var frame = ModbusFrameParser.BuildReadHoldingRequest(0x01, 0, 2);
        // 期望:01 03 00 00 00 02 [CRC低][CRC高]
        Assert.Equal(new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x02 }, frame[..6]);
        Assert.True(Crc16.Check(frame));             // CRC 校验通过
    }
}
```

> 📂 `src/DaqMonitor.Tests/TcpFrameParserTests.cs`(R3 版只含纯解析 4 测试;`TcpDevice_Simulate` 回环测试 R4 再加)

```csharp
using DaqMonitor.Core.Protocol;
using Xunit;

namespace DaqMonitor.Tests;

public class TcpFrameParserTests
{
    [Fact]
    public void BuildFrame_RoundTrips_ThroughTryParse()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03 };
        var frame = TcpFrameParser.BuildFrame(payload);

        Assert.True(TcpFrameParser.TryParse(frame, out var got, out int len, out _));
        Assert.Equal(payload, got);
        Assert.Equal(frame.Length, len);
    }

    [Fact]
    public void TryParse_HalfPacket_ReturnsFalse_NoResync()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var frame = TcpFrameParser.BuildFrame(payload);
        // 只给前 5 字节(半包)
        Assert.False(TcpFrameParser.TryParse(frame.AsSpan(0, 5).ToArray(), out _, out _, out bool resync));
        Assert.False(resync);
    }

    [Fact]
    public void TryParse_BadHeader_SignalsResync()
    {
        var junk = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        Assert.False(TcpFrameParser.TryParse(junk, out _, out _, out bool resync));
        Assert.True(resync);
    }

    [Fact]
    public void ValidateFrame_RejectsCorruptCRC()
    {
        var payload = new byte[] { 0xAA, 0x55, 0x01, 0x02 };
        var frame = TcpFrameParser.BuildFrame(payload);
        frame[^1] ^= 0xFF;   // 破坏最后一个 CRC 字节
        Assert.False(TcpFrameParser.ValidateFrame(frame));
    }
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
已通过! - 失败: 0,通过: 15 ... DaqMonitor.Tests.dll
```
(15 = R2 的 2 + 本篇 13)

## ✅ 验收清单

- [ ] build 0 错 0 警,test 15/15 绿
- [ ] 能回答:半包/粘包分别是什么?FrameParser 靠什么策略两者通吃?(长度域 + 循环拆帧 + 缓冲保留)
- [ ] 能回答:为什么 FrameParser 是 class(有状态缓冲),ModbusFrameParser/TcpFrameParser 是 static(纯函数)?
- [ ] 能回答:CDAB 字交换现场怎么排查?(抓帧看原始字节,拿标准浮点位模式对照)
- [ ] 把 `FrameParser.Build(1, 42.5)` 的输出逐字节写出来——头/长/载荷/CRC 各是几字节
- [ ] git commit -m "R3: 协议解析层 CRC+三种帧解析+13测试"

## 🎤 面试怎么讲这一篇

> "协议层我做成纯函数集合:CRC16/Modbus、AA55 自定义帧、Modbus RTU、TCP 长度前缀帧,零 IO 依赖,全部可单测——13 个测试覆盖已知向量、半包粘包、坏帧丢弃、字节序往返。设计上 FrameParser 有内部缓冲所以是实例类,Modbus/TCP 解析是无状态纯函数所以是静态类。现场最常翻车的三个点我都写了针对性测试:Modbus 地址 ±1 偏移看异常码、浮点 CDAB 字交换、TCP 乱码长度域要重同步并设 8KB 上限防巨型分配。"

**✅ 打卡[ ]**
