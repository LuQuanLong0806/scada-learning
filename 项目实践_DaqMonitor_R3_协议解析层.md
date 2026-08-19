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

**第 1 步 · 算法核心 `Modbus()`:给字节算指纹**(整个文件先建出来)

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
}
```

📚 **知识点**
- **两个常数是规范定的,不是设计选择**:初值 `0xFFFF`、多项式 `0xA001` 都是 CRC16/Modbus 标准写死的——改一个数,和别家设备就"对不上暗号"。这就像 HTTP 的 `GET/POST`,没得商量。
- **双层循环在干什么**:外层每个字节先 `crc ^= b`(把这个字节的影响搅进去),内层 8 次"右移 1 位,移出去的如果是 1 就再异或多项式"——把 8 个 bit 逐个摊开参与运算。结果:任何一个 bit 翻转,指纹面目全非。
- **`ReadOnlySpan<byte>` 参数**:零拷贝的"只读视图"——调用方传数组、切片都不复制内存。协议层高频调用,这种微优化是 C# 的常规操作(前端类比:传 TypedArray 的 subarray 视图而不是 slice 出新数组)。

**第 2 步 · `Check()`:整帧校验**(贴进类里,最后一个 `}` 之前)

```csharp
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
```

📚 **知识点**
- **重载是给调用方递便利**:`Modbus(byte[])` 一行转调 Span 版——纯糖,让上层写 `Crc16.Modbus(myArray)` 不用自己 `.AsSpan()`。和 `axios.get(url)` / `axios.get(url, config)` 的重载思路一样:核心只有一个,外壳按习惯加。
- **重算比对,不是解密**:`Check` 把"帧尾那 2 字节"拆出来当对方算的指纹,自己把前面的载荷**重算一遍**,比对——防伪码的本质是哈希校验,防的是**线路误码**(电磁干扰翻了个 bit),不是防篡改。和 HTTP 的 ETag/文件校验和同一个思想。
- **`[^2] | [^1] << 8` 就是在读"低字节在前"**:倒数第二个字节是低位、倒数第一个是高位——Modbus 约定 CRC **低字节在前**放进帧尾。这个约定和帧里其他部分经常相反,是新手抓包时最容易懵的点。
- **范围运算符是 C# 8 的糖**:`frame[..payloadLen]`(从头到 n)、`[^2]`(倒数第二)——等价于 `Substring`/`arr[arr.Length-2]`,但协议代码里这种"掐头去尾"特别多,糖让边界条件一目了然。

<details markdown="1">
<summary>📄 完整文件 Crc16.cs(对答案 / 整体粘贴用)</summary>

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

</details>

### ② FrameParser —— AA55 自定义帧(串口半包/粘包)

> 📂 `src/DaqMonitor.Core/Protocol/FrameParser.cs`
> 🔧 无 NuGet
> 💡 帧格式 `AA 55 | Len | Payload... | CRC_L CRC_H`;**有状态**(内部缓冲),所以是 class 不是 static
> 🗺️ **新手读码地图**(4 步看懂):1. `Feed(chunk)` 是进货口:网络/串口来的字节**一段一段**到(可能是半帧,也可能几帧粘在一起),先全倒进 `_buffer` 这个蓄水池 2. `TryTakeFrame` 是取货口:循环从缓冲头尝试拆出**一条**完整帧,拆得出就收走,拆不出(字节不够=半包)就停手等下一批 3. 找帧头 `0xAA`:找不到→**清空整个缓冲**(防垃圾无限堆积);`0xAA` 后面不是 `0x55`→假帧头,删掉这 1 字节继续找 4. 拆走一条 = 头 3 字节 + Len 载荷 + 2 字节 CRC,只把载荷切出来返回,并从缓冲删掉已消费部分。**前端类比**:和 WebSocket onmessage 里攒字节流切消息一模一样(先 concat 再 while 切完整包);因为 `_buffer` 是跨调用记忆的状态,所以它是 class,而 Crc16/ModbusFrameParser 是 static——**有没有状态决定 class 还是 static**。

**第 1 步 · 骨架:蓄水池 + 开关**(整个文件先建出来,一个有状态类最小可编译的形态)

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
}
```

📚 **知识点**
- **`_buffer` 就是蓄水池,也是这个类存在的理由**:串口/TCP 一次给你的字节和"帧边界"毫无对齐关系——半条帧、一条半、三条粘一起都正常。**跨调用记住没消费完的字节**,这半点状态决定了它是 class 而 Crc16 是 static:有没有状态决定实例还是静态(前端类比:有内部 state 就得是组件实例,纯 props→结果的才是工具函数)。
- **`verifyCrc` 构造时定死、之后不可变**(`readonly`):"要不要校验"是**部署期决策**不是运行期决策——演示环境可以关掉方便构造坏帧做测试,生产必开。用构造参数而不是方法参数,就是把它焊在实例的生命周期上,调用方想临时关都关不了。
- **`new()` 目标类型推断**:`List<byte> _buffer = new()` 省去右边重复类型名,C# 9 起的糖,等于 `new List<byte>()`。

**第 2 步 · 拆帧状态机:`Feed` + `TryTakeFrame`**(贴进类里,最后一个 `}` 之前)

```csharp
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
```

📚 **知识点**
- **`Feed` 只做两件事:进货 + 循环取货**。把 chunk 全倒进 `_buffer`,然后 `while (TryTakeFrame(...))` 能拆几条拆几条——粘包的"一次 3 条"就是这么拆出来的;一条都拆不动(半包)while 直接不进,字节安静留在池子里等下一批。**取货逻辑全部下沉到 `TryTakeFrame`**,Feed 自己 6 行,这种"壳薄芯厚"的拆法和前端"handler 只管取数,解析放纯函数"一致。
- **`TryTakeFrame` 的 return 有四种含义,注释已标**:找不到帧头(清池)、字节不够(等)、假帧头(删 1 字节重找)、半包(等)。**读协议代码先数 return 路径**——每条 return 都是"流式解析的一种现场情况",面试让你讲半包粘包,把这 4 条讲全就是满分。
- **`out byte[] frame` + `frame = []` 的 C# 惯用法**:out 参数必须在方法返回前赋值,开头先给空数组兜底——失败路径也能安全编译。等于 JS 里 `let result = []` 再填,但编译器强制你兜底。
- **坏帧静默丢弃是产品决策不是偷懒**:CRC 不过的帧 `RemoveRange` 掉、`return false`——不抛异常不通知,因为工业链路上坏帧就是常态(干扰),抛异常会把"环境事实"变成"程序错误"。
- **注意消费起点是 `RemoveRange(0, idx + total)`**:删的是**从缓冲头到帧尾**——帧头之前若有垃圾字节,这一刀顺手带走了。

**第 3 步 · 复位 + 写方向 `Build`**(同样贴进类里,最后一个 `}` 之前)

```csharp
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
```

📚 **知识点**
- **读、写放同一个类是协议的"单一事实来源"**:帧格式只定义一次,Build(写)和 TryTakeFrame(读)都遵守——改格式两边编译器逼你一起改。前端类比:schema 和它的 serializer/deserializer 放一个模块,而不是两头手写。
- **`BitConverter.GetBytes(value)` 是 8 字节小端**:double 在 C# 内存里就是 IEEE754 小端 8 字节,直接倒出来——这就是"Payload = 1 字节点号 + 8 字节 double"的来源。同一份字节 Java 端(大端)解出来就是乱码,跨语言协议必须白纸黑字写清字节序。
- **CRC 计算范围必须和校验范围完全一致**:`头+长+载荷` 算的 CRC,就得用 `Crc16.Check(整帧)` 校验——范围差一个字节,永远校验失败。注释里专门写了一句,这是自定义协议最常见的"自己骗自己"事故。

<details markdown="1">
<summary>📄 完整文件 FrameParser.cs(对答案 / 整体粘贴用)</summary>

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

</details>

### ③ ModbusFrameParser —— 工业标准协议的拆包

> 📂 `src/DaqMonitor.Core/Protocol/ModbusFrameParser.cs`
> 🔧 无 NuGet
> 💡 现场三坑全在这:**地址 ±1 偏移**(异常码 0x02)、**字节序 CDAB**(浮点读出天文数字)、**线圈按位不按字节**
> 🗺️ **新手读码地图**:整个类是**纯函数翻译器**——不存状态、不碰 I/O,每个方法都是"字节进→数据出",所以能甩开硬件直接单测。看懂三个方法就够:1. `ParseReadRegisters`:数据区每 2 字节 = 1 个寄存器,高字节在前(大端),`hi<<8|lo` 拼回 ushort 2. `ParseCoils`:开关量 1 个字节装 8 个,`i/8` 定位在第几个字节、`i%8` 定位第几位——和寄存器的"高字节在前"是两套规则,别混 3. `ToFloatModbus`:两个寄存器拼 32 位浮点;本机是小端,所以代码先把 4 字节按大端排好再 Reverse 交给 BitConverter——手册写 CDAB 你就传 CDAB,**抓帧确认,别猜**。**前端类比**:一个纯的 protocol decoder(输入 Uint8Array 输出对象),零副作用所以好测。

**第 1 步 · 骨架:两张表(枚举 + 异常码字典)**(整个文件先建出来)

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
}
```

📚 **知识点**
- **异常码字典把"魔数"变"可读现场证据"**:设备拒绝你时只回一个字节(如 0x02),字典查一下立刻知道"非法地址,常是 ±1 偏移"——现场排障第一现场就用它。用**静态属性 + 只读接口**暴露(`IReadOnlyDictionary`),等于一个内置的、编译期就定型的枚举转文案表。
- **`[0x02] = ...` 是字典初始化器的索引写法**,等价 `new Dictionary<byte,string>{ { 0x02, "..." } }`——键是字节值时更直白。
- **枚举放类里(嵌套类型)划定归属**:`ModbusFrameParser.ByteOrder` 一看就知道是"Modbus 浮点的字节序",不会和别的 ByteOrder(比如将来的 CAN 解析)撞名——和前端把 `Status` 枚举挂在 `Order` 类下而不是全局一个道理。
- **ABCD/CDAB/BADC/DCBA 四个名字是行业黑话**:A=第 1 寄存器高字节、B=第 1 寄存器低字节、C=第 2 寄存器高字节、D=第 2 寄存器低字节——设备手册就用这套字母,代码同名等于把手册直接搬进类型系统。

**第 2 步 · `IsExceptionResponse`:设备说"不"的识别**(贴进类里,最后一个 `}` 之前)

```csharp
    /// <summary>判断是否为异常响应;是则返回异常码(功能码最高位被置 1,如 0x03→0x83)。</summary>
    public static bool IsExceptionResponse(ReadOnlySpan<byte> resp, out byte exceptionCode)
    {
        exceptionCode = 0;
        if (resp.Length < 3) return false;
        if ((resp[1] & 0x80) == 0) return false;
        exceptionCode = resp[2];
        return true;
    }
```

📚 **知识点**
- **Modbus 报错的暗号:功能码最高位置 1**:正常读寄存器回 `0x03`,出错回 `0x83`(0x03 | 0x80)——**响应是"数据"还是"错误",看一个 bit**。协议设计成这样是为了让收方一个 `& 0x80` 就能分流,不用先解析完才知道坏了。
- **`(resp[1] & 0x80) == 0` 是位掩码判断**:`& 0x80` 取出最高位,非零即真——位运算读协议帧的基本功,和 CSS 里 `flags & FLAG_A` 或 JS 的位掩码权限一个玩法。
- **防御式长度检查在最前**:`resp.Length < 3` 直接 false——异常响应至少"从站+功能码+异常码"3 字节。协议代码里**每个下标访问前都要先想"够长吗"**,否则一个坏帧就是 IndexOutOfRange 崩溃。

**第 3 步 · `ParseReadRegisters`:寄存器拆包(大端)**(贴进类里)

```csharp
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
```

📚 **知识点**
- **响应帧布局口诀:从站、功能码、字节数、数据、CRC**——`resp[0]` 从站号、`resp[1]` 功能码、`resp[2]` 声明后面有几个数据字节、`resp[3..]` 每寄存器 2 字节。**第三字节是"自带说明书"**:长度不按它核对就是给半包开门(半包帧数组越界或读到垃圾)。
- **`hi << 8 | lo` = 大端拼数**:高字节左移 8 位再或上低字节——Modbus 寄存器**规定大端**(网络字节序),拼错方向 0x000A 变 0x0A00(10 变 2560),数值离谱但"看起来还有效",是最阴的错。
- **抛异常 vs 返回 false 的分界**:这里帧已经通过了"长度够+功能码对"的门槛,再错就是**调用方接错了响应**——编程错误该抛异常炸出来调试;而 FrameParser 里 CRC 不过是**环境常态**,静默丢。什么时候炸、什么时候吞,是协议层设计的一半功力。
- **`$"...功能码=0x{resp[1]:X2}"` 格式化插值**:`X2` = 两位大写十六进制——报错信息直接给可抓包比对的字节值,排障时救命。

**第 4 步 · `ParseCoils`:线圈按位拆包**(贴进类里)

```csharp
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
```

📚 **知识点**
- **一个字节装 8 个开关,`i/8` 定字节、`i%8` 定位**:线圈 0~7 在第 0 字节、8~15 在第 1 字节——除法和取模就是把"线性编号"翻译成"行列坐标"的万能公式(前端类比:一维数组转 m 列网格,`row = i / m, col = i % m`)。
- **线圈低位在前,和寄存器大端相反**——同一个协议两套字节规则,这是 Modbus 最容易记混的点。为什么?历史包袱:线圈打包按"第 n 位 = 第 n 个线圈"自然序,寄存器沿用大端网络序。**记不住没关系,手册 + 抓帧说了算**。
- **`1 << (i % 8)` 造掩码**:第 3 位就是 `0000_1000`,`&` 一下非零即通——和权限位掩码(`1 << PERM_WRITE`)完全同构,一套位运算走天下。

**第 5 步 · `ToFloatModbus` + `ToSingleBig`:两寄存器拼浮点**(贴进类里,两个方法一起贴——它们互相引用)

```csharp
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
```

📚 **知识点**
- **32 位值跨 2 个寄存器 = 字节序问题的平方**:一个 float 4 字节,Modbus 每寄存器只装 2 字节,所以"拆成两半再拼"出现 4 种拼法(ABCD/CDAB/BADC/DCBA)。**现场 90% 的"温度读出天文数字"就是 CDAB 字交换**——设备把两个寄存器的顺序和你的假设反了。
- **switch 表达式按模式分发**:`order switch { A => ..., B => ..., _ => throw }`——比 if-else 链可读得多,`_` 兜底抛参数异常,枚举加了新值忘了处理会立刻炸(和 TS 的 exhaustive switch + never 检查同款保险)。
- **`(byte)(r0 >> 8)` / `(byte)r0` 拆寄存器高低字节**:ushort 右移 8 位取高字节、直接转 byte 取低字节——每个 ByteOrder 分支都在"重新排列 4 个字节",排列完统一交给 `ToSingleBig` 按大端解读。**归一化设计**:不管哪种序,先排成大端,后面只剩一种解读逻辑。
- **`ToSingleBig` 的 Reverse 套路**:本机 x86 是小端,把大端 4 字节倒序后 `BitConverter.ToSingle` 就等于"按大端读"——借本机 API 干相反端序的活,一行 `new[] { b3, b2, b1, b0 }` 是整个大小端问题的缩影。

**第 6 步 · `BuildReadHoldingRequest`:组读请求帧**(贴进类里,收尾)

```csharp
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
```

📚 **知识点**
- **请求帧 8 字节,手工可背**:`从站 0x03 地址高 地址低 数量高 数量低 CRC低 CRC高`——读保持寄存器(功能码 0x03)是 Modbus 用得最多的一招,抓包工具里看到 `01 03 00 00 00 02` 开头,就是"1 号从站,从地址 0 读 2 个"。
- **`addr >> 8` 在前、`addr` 在后 = 请求里的地址/数量也是大端**:和响应数据区一致;但帧尾 CRC 又是低在前——**同一条帧两种端序**,这是 Modbus 的既成事实,代码注释里专门标出来防自己写反。
- **组帧三板斧:拼载荷 → 算 CRC → 追加 CRC**——和 FrameParser.Build 结构完全一样。写多几个协议你就会发现:**组帧代码长得都一样,难的永远是拆帧和字节序**。

<details markdown="1">
<summary>📄 完整文件 ModbusFrameParser.cs(对答案 / 整体粘贴用)</summary>

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

</details>

### ④ TcpFrameParser —— TCP 长度前缀帧

> 📂 `src/DaqMonitor.Core/Protocol/TcpFrameParser.cs`
> 🔧 无 NuGet
> 💡 与 FrameParser 的区别:TCP 帧头后带 **2 字节小端长度域**,且"解析"与"缓冲"解耦——本类无状态,缓冲区调用方维护,失败时给 needResync 信号
> 🗺️ **新手读码地图**:拆的还是"字节流切帧"这同一个问题,和 ② 的差别就两点:1. 帧头后是 **2 字节小端长度域**(`AA 55 LEN_LO LEN_HI payload CRC`),用小端是因为 C# 的 BitConverter 本机就是小端,拼长度不用翻转 2. 本类**无状态**:自己不攒缓冲,只提供 TryParse"给一段字节、试拆一帧",拆不出/拆坏返回 false 并告诉调用方要不要重同步,缓冲由调用方维护。**前端类比**:FrameParser 像"自己管 state 的组件"(内部攒 buffer),TcpFrameParser 像"受控/纯函数组件"(state 提升给调用方)——两种设计都对,看你想把复杂度放哪边。

**第 1 步 · 骨架:协议常数先立规矩**(整个文件先建出来)

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
}
```

📚 **知识点**
- **协议常数 `public const` 暴露给全世界**:R4 的 TcpDevice 要用 `HeaderSize` 来 Skip 缓冲区——协议的"尺寸事实"只定义一次,上下游都引用它。和前端把 API 路径、分页大小收进 constants 文件一个道理,但协议里这是**硬约束**:数字对不上,帧就永远拆不出。
- **`MaxPayload = 8KB` 是防御性上限,不是业务上限**:长度域是 2 字节,理论最大 64KB——但如果字节流错位,把垃圾当长度读,你会按 60000 字节去分配/等待,**8KB 上限让乱码最多骗你 8K**。"给不可信输入设上限"是协议代码的第一反应(前端类比:上传文件大小限制防内存炸)。
- **一个空类 + 一堆常数也有价值**:先把协议的"地面规矩"(头多长、CRC 几字节、载荷多大)钉死,后面的方法都在这些常数上做算术——**协议实现 = 常数 + 对常数做算术**。

**第 2 步 · `BuildFrame`:组帧**(贴进类里,最后一个 `}` 之前)

```csharp
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
```

📚 **知识点**
- **先算总长一次分配**:`new byte[HeaderSize + len + CrcSize]` 然后逐格填——组帧不走 List 逐个 Add,因为长度在开头就知道,一次分配零扩容(高频发送路径的常规优化)。
- **长度域写小端:`& 0xFF` 取低、`>> 8` 取高**——和 Modbus 的"地址大端"相反!这里选小端是**自家协议自由裁量**:C# 本机小端,`frame[2] | (frame[3] << 8)` 拼回来不用动脑。自定义协议的第一课:**端序你可以自己定,但定了就全线统一,并且写进文档**。
- **`payload.CopyTo(frame.AsSpan(HeaderSize))`**:把载荷复制到偏移 4 之后——Span 版 CopyTo 是数组块复制的零糖衣写法。

**第 3 步 · `ValidateFrame`:整帧体检**(贴进类里)

```csharp
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
```

📚 **知识点**
- **体检四连,由浅入深**:长度够吗 → 头对吗 → 长度域和实际长度一致吗 → CRC 过吗。**顺序是便宜检查在前**:前两个是 O(1) 比对,CRC 要遍历整帧——把贵的放最后,失败早退(和前端表单校验"先 required 后 async 唯一性"同一逻辑)。
- **`len != frame.Length - HeaderSize - CrcSize` 这条最容易被漏**:长度域声称 100 字节、实际帧只有 80 字节——多半是半包/错位。**"声明的长度"和"实际的长度"必须互证**,信任何一方都可能被骗。

**第 4 步 · `TryParse`:流式试拆一帧**(贴进类里,收尾)

```csharp
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
```

📚 **知识点**
- **`needResync` 是本方法的灵魂输出**:`false` 失败 = "字节还不够,继续等"(半包);`true` 失败 = "这份数据有毛病,**等不来**了,丢 1 字节重新对齐"。**两种 false 催调用方做完全不同的事**——协议解析最难的就是区分"耐心等"和"果断弃",一个 bool 说清楚。
- **为什么自己不丢字节、只发信号?** 类是无状态纯函数,"动缓冲区"会破坏纯度;**丢 1 字节的决策权交给管缓冲的调用方**(R4 的 TcpDevice)。和受控组件把 setState 留给父组件完全同构。
- **`buffer[..total]` 只切本帧**:万一缓冲里粘着下一帧的前几个字节,`[..total]` 精确按本帧长度切——**绝不越界读下一帧的地盘**,粘包安全靠这一行。
- **`frame.Slice(HeaderSize)` 从长度域之后开始验 CRC**:注意 CRC 的计算范围是 payload,不含头和长度域(和 BuildFrame 里"仅对 payload 计算"严格对偶)——**组帧和拆帧的 CRC 范围必须互为镜像**,错一个字节就是永远校验失败。

<details markdown="1">
<summary>📄 完整文件 TcpFrameParser.cs(对答案 / 整体粘贴用)</summary>

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

</details>

### ⑤ 三个测试文件(13 个测试)

> 📂 `src/DaqMonitor.Tests/FrameParserTests.cs` · namespace `DaqMonitor.Tests`
> 🔧 无 NuGet
> 💡 协议测试的灵魂:**已知向量**(CRC 标准测试值)+ **构造-解析往返**(Build 出的帧必须能原样解回)
>
> 搭积木:第 1 步建骨架时顺带把 CRC 向量测试写上(它不依赖 FrameParser),后面两个测试再逐个贴进类里。

**第 1 步 · 骨架 + CRC 已知向量测试**(整个文件先建出来)

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
}
```

📚 **知识点**
- **"已知向量"测试 = 协议实现的出生证明**:这 6 个字节的 CRC 是**规范里公认的结果**(拿任何 CRC16/Modbus 计算器算都是 0x0A84)——你的实现跑出这个数,才有资格说"我实现了 Modbus CRC"。没有它,自研算法的正确性只是自我感觉(前端类比:时区库测试里钉死 `2026-01-01T00:00Z → 具体星期`)。
- **注释里解释了 0x0A84 vs 84 0A 的千年误会**:算法返回值是 0x0A84(寄存器读法),线上发送是低字节在前(84 0A)。抓包看到 `84 0A` 别怀疑算法反了——**两种读法都对,只是视角不同**。

**第 2 步 · 半包/粘包测试**(贴进类里,最后一个 `}` 之前)

```csharp
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
```

📚 **知识点**
- **测试名直接用中文写场景**:`Feed_Splits粘包_AndHandles半包`——xUnit 完全支持,测试报告里一眼看懂测的是什么,比 `Feed_Test2` 强十倍。
- **手工造帧三件套**:算 CRC → 拼帧尾(低在前)→ Concat 成整帧。测试不调 `Build()` 而是手工拼——**故意的**:Build 和解析都出自被测类,用它造数等于"用嫌疑人做证人";手工造帧让测试独立于实现。
- **两次 Feed 模拟半包**:第一次只给 3 字节断言空,第二次给余下断言拆出 1 帧——**"分批到达"是流式解析的常态**,这个测试锁的就是"跨调用的记忆能力"(蓄水池语义)。

**第 3 步 · 坏头丢弃 + 整帧校验测试**(同样贴进类里)

```csharp
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
```

📚 **知识点**
- **坏头测试的数据故意藏了一个 `0x55`**:`00 55 02 ...` 里第 2 字节是 0x55 但第 1 字节不是 0xAA——如果解析器只找 0x55 或找错位置,就会误拆。**测试数据里的每个字节都是陷阱**,专门骗写错的实现。
- **`Assert.Empty` 断言"什么都不拆出"**:垃圾进 → 空 list 出、不炸不崩——"对垃圾输入的静默容错"本身就是要锁死的行为契约。

<details markdown="1">
<summary>📄 完整文件 FrameParserTests.cs(对答案 / 整体粘贴用)</summary>

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

</details>

> 📂 `src/DaqMonitor.Tests/ModbusFrameParserTests.cs`
>
> 搭积木:第 1 步骨架,之后按"寄存器/线圈 → 浮点 → 异常码/组帧"三批贴入。

**第 1 步 · 空骨架**(整个文件先建出来)

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
}
```

📚 **知识点**
- **类注释写的是"测什么 + 为什么能 CI 跑"**:`纯协议层,不碰串口`——这行字是这个测试文件存在的全部前提,零 IO 所以任何机器上都能绿。

**第 2 步 · 寄存器 + 线圈测试**(贴进类里,最后一个 `}` 之前)

```csharp
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
```

📚 **知识点**
- **测试数据就是活的协议手册**:`01 03 04 | 00 0A 00 14 | C4 0B` 拆开念——1 号从站、功能码 03、4 个数据字节、寄存器值 0x000A(10)和 0x0014(20)、CRC C4 0B。**读得懂这 9 个字节 = Modbus 响应帧入门**。
- **线圈测试用 `0xFF` 打头**:第一字节全 1 → 线圈 0~7 全通,一梭子覆盖 8 个下标;再配 0x03 覆盖部分通/部分断——**一份数据同时测全真、全假、混合三种形态**。

**第 3 步 · 浮点字节序测试(现场翻车根因)**(贴进类里)

```csharp
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
```

📚 **知识点**
- **一个测试同时锁正反两面**:ABCD 必须对(100.0)、CDAB 必须错(差得远)——**"错也错得符合预期"**才是完整断言。只测"对的能对",字交换 bug 藏在"错的能蒙对"里。
- **`0x42C80000 = 100.0f` 是 IEEE754 的手算样本**:符号 0、指数 0x85、尾数…这个十六进制浮点位模式值得记住——现场抓包看到 `42 C8 00 00`,立刻知道"设备在说 100.0"。
- **浮点断言不写 Equal(100.0f, cdab)**:CDAB 会解析成极小值(接近 0 的 1e-41 量级),断言"差值 > 1"比断言具体值稳——浮点比较永远用误差容忍,和前端 JS 的 `Math.abs(a-b) < EPS` 同款。

**第 4 步 · 异常码 + 组帧测试**(贴进类里,收尾)

```csharp
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
```

📚 **知识点**
- **`0x83` = 0x03 | 0x80,一个测试锁三件事**:识别出是异常、码是 0x02、文案查得出来——**协议层的"用户友好"也是要测的**,不然报警弹窗里给操作员看的就是个裸字节。
- **组帧测试断言前 6 字节 + 整帧 CRC**:前 6 字节是确定的(从站/功能码/地址/数量),CRC 随算法走不写死——**确定的钉死,计算过的用性质断言**(能过 Check),测试才不脆。

<details markdown="1">
<summary>📄 完整文件 ModbusFrameParserTests.cs(对答案 / 整体粘贴用)</summary>

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

</details>

> 📂 `src/DaqMonitor.Tests/TcpFrameParserTests.cs`(R3 版只含纯解析 4 测试;`TcpDevice_Simulate` 回环测试 R4 再加)
>
> 搭积木:第 1 步骨架 + 往返测试,之后两批贴入。

**第 1 步 · 骨架 + Build→TryParse 往返测试**(整个文件先建出来)

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
}
```

📚 **知识点**
- **"往返测试"(round-trip)是序列化的黄金测试**:Build 出的帧必须能被 TryParse 原样解回——组帧和拆帧只要有一个写反,往返立刻断。和前端 `JSON.parse(JSON.stringify(x))` 深拷贝后 deepEqual 一个思想。
- **`out _` 丢弃不关心的输出**:这次不用 needResync,下划线扔掉——C# 的"解构忽略"语法,等于 JS 的 `const [_head, ...rest]`。

**第 2 步 · 半包不重同步 + 坏头要重同步**(贴进类里,最后一个 `}` 之前)

```csharp
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
```

📚 **知识点**
- **两个测试合起来锁死 needResync 的语义**:同样返回 false,半包 resync=false(继续等)、坏头 resync=true(丢字节)——**一个 bool 的两面各配一个测试**,这种"对称测试"是行为契约的最严写法。
- **`frame.AsSpan(0, 5)` 硬造半包**:好帧掐掉尾巴喂进去——真实 TCP 就这么干,内核不管你的帧边界,每次 read 给多少算多少。

**第 3 步 · 破坏 CRC 被拒**(贴进类里,收尾)

```csharp
    [Fact]
    public void ValidateFrame_RejectsCorruptCRC()
    {
        var payload = new byte[] { 0xAA, 0x55, 0x01, 0x02 };
        var frame = TcpFrameParser.BuildFrame(payload);
        frame[^1] ^= 0xFF;   // 破坏最后一个 CRC 字节
        Assert.False(TcpFrameParser.ValidateFrame(frame));
    }
```

📚 **知识点**
- **`^=` 异或翻转一个字节 = 模拟线路误码**:好帧只动最后 1 个 bit,帧头长度全对、只有 CRC 对不上——**最小破坏定位最大嫌疑**,比造一整帧垃圾数据精准得多。这招在 R4 的回环测试里还会反复用。

<details markdown="1">
<summary>📄 完整文件 TcpFrameParserTests.cs(对答案 / 整体粘贴用)</summary>

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

</details>

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
