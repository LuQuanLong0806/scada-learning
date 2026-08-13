# M2 — Modbus RTU/TCP（工业标准协议）

> **优先级定位**：🔴 必学 · 串口 + Modbus RTU/TCP（JD 主干必会）
> **技术来源**：🟧 第三方 NuGet `NModbus4`（`dotnet add package NModbus4`）；CRC 算法 🟦 自己写（面试常考）。
> **给简历加的能力**：用工业**事实标准**协议读写寄存器 —— 面试 / 现场最高频的对接方式，没有之一。
> **前置**：M1（串口收发、字节帧、CRC 原理）。

> ⏱️ **阅读路径**(按时间预算选入口)
> - **3 分钟**:看「模块目标」+ 「Day 1 一句话讲清楚」— 知道 Modbus 是工业事实标准协议
> - **30 分钟**:加看 Day 1 前端类比表 + **手算 1 帧 CRC16(必过关)**
> - **3 小时**:全文精读 + NModbus4 跑通虚拟从站 + 异常码 0x01-0x04 排查
> - 🎯 **面试高频**:4 种寄存器(线圈/离散/输入/保持)/ 功能码 03-06-10 / **手算 CRC16(5min 白板必过)** / 地址偏移 40001→0
> - 🔁 **配套复习**:[速记卡 Q9-Q11](面试高频知识点_速记卡.md) · [代码肌肉 B1 CRC16 手写](代码肌肉训练手册_30天刷题版.md) · [间隔重复表](记忆与复习机制_间隔重复版.md)

> 📚 **前置语法**(M2 用到的,陌生请查 [C# 语法速查 — 前端视角](CSharp语法速查_前端视角.md))
> - `byte[]` / `ushort` / `0x1A` — 字节、双字节、十六进制字面量
> - `<<` / `>>` / `&` / `|` / `^` — 位运算(CRC 算法必备)
> - `0x8005 & 0xFFFF` — 位掩码
> - `BitConverter.ToString(bytes)` — 字节转 hex 字符串
> - `for (int i = 0; i < buf.Length; i++)` — 经典 for 循环(位移运算用)
> - `event EventHandler<byte[]>?` — 事件,速查 §7

> 📦 **前置类型**(本模块示例代码用到的核心自定义类型)
> M2 示例引用 `DeviceBase` / `IDevice` / `SensorPoint` / `ByteOrder` 等类型 — 这些在 [📦 前置类型定义 · 学员粘贴版](前置类型定义_学员粘贴版.md) **集中定义**。**遇到"找不到类型 XXX"报错,先去那份文档复制对应类型**,在项目里建 `_PredefinedTypes.cs` 粘进去就能跑。本模块还会**新建** `ModbusDevice : DeviceBase`,跟着 Day 1-3 敲。

## 模块目标
用 NModbus 连接一个 Modbus 从站（RTU 走串口 / TCP 走网口），循环读取保持寄存器，把值写入 DAQ Monitor 的 `PointStore`。并且——**你能从零手搓一帧 Modbus RTU 请求、手算它的 CRC、再解析响应**，因为现场排错和面试都靠这个。

---

## Day 1 — Modbus RTU 帧结构 + CRC 校验（从零手搓）

### 一句话讲清楚
Modbus 把设备数据排成「4 种表格」（线圈 / 离散输入 / 输入寄存器 / 保持寄存器），上位机用「功能码」当指令去读 / 写指定地址，像调 REST API 的 GET/POST 到固定端点；**CRC 是这趟通信的「防伪签名」**，确保串口里那串字节没被工厂电磁干扰篡改。

### 前端类比秒懂（每个概念都对上）
| Modbus 概念 | 前端类比 | 说明 |
|---|---|---|
| 4 种寄存器表 | 4 张数据库表 | 线圈=开关表，保持寄存器=设定值表 |
| 功能码 0x03 读保持寄存器 | `GET /hold-registers/:addr` | 只读不写 |
| 功能码 0x06 / 0x10 写 | `POST / PUT` | 下发设定值 |
| 从站地址 | 路由 / 微服务名 | 同一总线上哪台设备应答 |
| 轮询 | 定时 `fetch` | 上位机主动问，设备答 |
| **CRC 校验** | **请求 `sign` / 防篡改哈希** | 接收方用同样算法验算，不一致就丢 |

---

### 🟢 知识点 1：Modbus RTU 帧格式（最小认知版）

**前端类比**：就像 HTTP 请求有固定格式（请求行 + 头 + body），Modbus RTU 也有固定帧格式。

| 从站地址 | 功能码 | 数据 | CRC 校验 |
|---|---|---|---|
| 1 字节 | 1 字节 | N 字节 | 2 字节（**低字节在前**） |

常用功能码（记这 5 个就够应付 90% 现场）：

| 功能码 | 含义 | 前端类比 |
|---|---|---|
| 0x01 / 0x02 | 读线圈 / 读离散输入 | `GET` 布尔量 |
| **0x03** | **读保持寄存器** | `GET` 模拟量主用 |
| 0x04 | 读输入寄存器 | `GET` 传感器只读量 |
| 0x05 / 0x06 | 写单线圈 / 写单寄存器 | `POST` 单条 |
| 0x0F / 0x10 | 写多线圈 / 写多寄存器 | `POST` 批量 |

### 🎭 拟人秒懂:餐厅 4 张办公桌(4 种寄存器记忆锚点)

> 把 Modbus 设备想象成"一家餐厅,有 4 张办公桌"。

| 寄存器类型 | 拟人 | 读/写 | 位数 |
|---|---|---|---|
| **线圈(Coil)** | 大堂灯开关 — 只有 ON/OFF,**你能按** | 读写 | 1 bit |
| **离散输入(Discrete Input)** | "营业中"招牌 — 只有 ON/OFF,**你不能改**(经理控制) | 只读 | 1 bit |
| **输入寄存器(Input Register)** | 今日客流量计数器 — 数字,**自动增加,你不能改** | 只读 | 16 bit |
| **保持寄存器(Holding Register)** | 空调温度设定值 — 数字,**你能调** | 读写 | 16 bit |

**记口诀**:
- "线圈"= 开关,你能控 → 可读可写 1 位
- "输入" = 别人控,你只能看 → 只读(不管是离散还是寄存器)
- "保持" = 设定值,你能调 → 可读可写 16 位

**为什么工业 90% 用 0x03(读保持寄存器)**:现场最高频的就是读"设定值/当前值"(温度、压力、流量),全是 16 位数,全是保持寄存器。其他 3 种只是配角。

**Step 2：手动构造一个读取帧（先手搓，再写代码）**
> 目标：读 1 号从站、寄存器地址 40001（协议地址 `0x0000`）开始的 2 个寄存器。

```
从站地址：0x01
功能码：  0x03
起始地址：0x0000  （高字节在前，大端）
寄存器数：0x0002  （读 2 个）
CRC校验：稍后算（低字节在前！）
完整请求帧（十六进制）：01 03 00 00 00 02 [CRC低] [CRC高]
```
逐字节讲解：`01`=找 1 号设备；`03`=我要读保持寄存器；`00 00`=从地址 0 开始（对应 PLC 的 40001）；`00 02`=读 2 个；后面两字节是 CRC。

---

### 🟡 知识点 2：CRC16 校验算法（从零手搓，面试必考）

### 🎭 拟人秒懂:快递包裹的指纹章 + 魔刷 0xA001(画面感记忆锚点)

> 把 CRC 想象成"快递员给包裹盖的指纹章"。

- **场景**:工厂车间电磁干扰很强,你发"01 03 00 00 00 02",接收方可能收到"01 03 00 00 00 03"(一个比特被干扰翻)
- **CRC 解决方案**:发送方在包裹尾部贴一个 2 字节"指纹",接收方用同样方法验指纹 — 不一致 = 路上有人动过
- **魔刷 0xA001**:这把刷子的"形状"由国际标准定死(`0x8005` 反向),全世界 Modbus 设备都用同一把 — 你用错刷子,指纹永远对不上
- **初值 0xFFFF**:为什么不是 0?因为如果数据全 0,CRC 也算出 0,你分不清"没发数据"和"发了全 0 数据"。用 0xFFFF 当起点,全 0 数据也能算出非零指纹

**3 步指纹盖章流程**(每个字节都重复):
1. **揉进去**:当前字节异或进 CRC 低字节(`crc ^= b`)— 把新数据"揉"进指纹泥
2. **挤 8 次**:循环右移 8 次,每次检查挤出去的最低位
3. **看挤出的是 1 还是 0**:挤出 1 → 右移后再异或 `0xA001`(用魔刷刷一下);挤出 0 → 只右移不刷

**最大的坑**:**数据域用大端(高位在前),CRC 反而用小端(低位在前)** — 同一个协议两套字节序,这是新人翻车 No.1。
口诀:"**数据跟着网络走(大端),CRC 跟着 Intel 走(小端)**"。

**Step 1：为什么需要 CRC？**
串口通信就像在嘈杂的工厂车间打电话，数据在传输中可能被电磁干扰「篡改」：
```
发送：01 03 00 00 00 02 C4 0B
接收：01 03 00 00 00 03 C4 0B   ← 一个比特被干扰了！
```
解决：**发送方在包尾加 2 字节 CRC，接收方用同样算法重算，对比一致才认账。**
前端类比：`const sign = md5(params + secretKey)` —— Modbus 的 CRC 是同一思路，只是算法换成多项式除法。

**Step 2：最小可用实现（CrcCalculator.cs）**
```csharp
// CrcCalculator.cs —— Modbus RTU CRC16
namespace SCADA.Modbus
{
    public static class CrcCalculator
    {
        /// <summary>算 Modbus CRC16，返回 [低字节, 高字节]</summary>
        public static byte[] Calculate(byte[] data)
        {
            ushort crc = 0xFFFF;                 // ① 初始值，Modbus 规定 0xFFFF
            foreach (byte b in data)
            {
                crc ^= b;                        // ② 每个字节与 CRC 异或
                for (int i = 0; i < 8; i++)      // ③ 逐位处理
                {
                    if ((crc & 0x0001) != 0)     // 最低位是 1？
                    {
                        crc >>= 1;               //   右移 1 位（相当于 ÷2）
                        crc ^= 0xA001;           //   再异或多项式 0xA001
                    }
                    else
                    {
                        crc >>= 1;               // 最低位是 0，只右移
                    }
                }
            }
            return new byte[] { (byte)(crc & 0xFF), (byte)(crc >> 8) }; // ④ 低字节在前！
        }
    }
}
```
**逐行白话**：
- `ushort crc = 0xFFFF`：CRC 初值。M1 讲过为什么是 0xFFFF（让「全 0 数据」也产生非零校验，否则分不清「没传」和「传错」）。
- `crc ^= b`：把当前字节「揉」进校验里，和 JS 的 `^` 按位异或一模一样。
- `crc >>= 1`：右移一位，把刚处理过的最低位「挤」出去。
- `crc ^= 0xA001`：最低位是 1 时才异或多项式 `0xA001`（它是标准多项式 0x8005 的位反转写法，Modbus 就认这个）。
- 返回时 `(crc & 0xFF)` 是低字节、`(crc >> 8)` 是高字节——**Modbus RTU 规定 CRC 低字节在前，这是和「数据域高字节在前」相反的坑**（见下方坑点）。

**🧪 验证（重点！自己算一遍才算会）**
```csharp
byte[] data = { 0x01, 0x03, 0x00, 0x00, 0x00, 0x02 };
byte[] crc  = CrcCalculator.Calculate(data);
Console.WriteLine($"{crc[0]:X2} {crc[1]:X2}");   // 运行结果：C4 0B
```
**运行结果：`C4 0B`** —— 和手搓请求帧补齐后的 `01 03 00 00 00 02 C4 0B` 完全一致 ✓。

**Step 3：封装成可复用工具（扩展方法版，像前端的 md5()）**
```csharp
public static class CrcCalculator
{
    public static byte[] Calculate(byte[] data) { /* 同上 */ }

    // 直接对 byte[] 调用 .ToModbusCrc()，像前端的 str.md5()
    public static byte[] ToModbusCrc(this byte[] data) => Calculate(data);

    // 验证一整帧的 CRC 是否正确（低字节在前）
    public static bool VerifyCrc(this byte[] frameWithCrc)
    {
        if (frameWithCrc.Length < 3) return false;
        byte[] data = frameWithCrc[..^2];          // 去掉末尾 2 字节 CRC
        byte[] recv = frameWithCrc[^2..];           // 收到的 CRC
        byte[] calc = Calculate(data);             // 自己重算
        return recv[0] == calc[0] && recv[1] == calc[1];
    }
}
```
使用效果：
```csharp
byte[] frame = { 0x01, 0x03, 0x00, 0x00, 0x00, 0x02 };
byte[] crc   = frame.ToModbusCrc();          // 扩展方法，像 str.md5()
byte[] resp  = { 0x01,0x03,0x04,0x00,0x0A,0x00,0x14,0xC4,0x0B };
bool ok = resp.VerifyCrc();                  // true = 数据没被篡改
```

---

### 🔴 知识点 3：用 NModbus 读保持寄存器（调库，别重复造轮子）

> 原理你刚手搓过了，实际项目直接用库，10 行搞定。

```csharp
using Modbus.Device;
using System.IO.Ports;

var port = new SerialPort("COM3", 9600, Parity.None, 8, StopBits.One);
port.Open();
var master = ModbusSerialMaster.CreateRtu(port);   // 创建 RTU 主站
byte slaveId = 1;
ushort[] regs = master.ReadHoldingRegisters(slaveId, 0, 10); // 功能码 0x03，从地址0读10个
foreach (var r in regs) Console.WriteLine(r);
```
**逐行白话**：
- `ModbusSerialMaster.CreateRtu(port)`：把串口包成「Modbus 主站」，之后你只管说「读哪个地址」，帧怎么组、CRC 怎么算库全包了。
- `ReadHoldingRegisters(slaveId, 0, 10)`：第 1 个参数是从站地址，第 2 个是起始寄存器地址（**从 0 开始，不是 40001**），第 3 个是数量。底层自动发出 `01 03 00 00 00 0A + CRC`。

**完整版：加超时 + 异常响应处理 + 日志（工厂现场必备）**
```csharp
public byte[] ReadHoldingRegisters(byte slave, ushort addr, ushort count)
{
    try
    {
        var frame = new List<byte> { slave, 0x03 }
            .Concat(BitConverter.GetBytes(addr).Reverse())   // 大端
            .Concat(BitConverter.GetBytes(count).Reverse());
        byte[] crc = CrcCalculator.Calculate(frame.ToArray());
        frame = frame.Concat(crc);
        _port.Write(frame.ToArray(), 0, frame.Count());

        // 超时等待（不能用 Thread.Sleep(100) 硬等，要轮询 BytesToRead）
        int waited = 0;
        while (_port.BytesToRead == 0 && waited < 1000) { Thread.Sleep(10); waited += 10; }
        if (_port.BytesToRead == 0) throw new TimeoutException($"从站 {slave} 读取超时！");

        byte[] resp = new byte[_port.BytesToRead];
        _port.Read(resp, 0, resp.Length);

        // Modbus 异常响应：功能码最高位被置 1（0x83 而不是 0x03）
        if ((resp[1] & 0x80) != 0)
            throw new Exception($"Modbus 异常！错误码 {resp[2]}（见下方坑点表）");
        if (!resp.VerifyCrc()) throw new Exception("CRC 校验失败，数据可能损坏");

        int len = resp[2];                       // 第 3 字节是数据长度
        return resp[3..(3 + len)];               // 取出数据域
    }
    catch (TimeoutException ex) { _log?.LogError(ex, "Modbus 超时"); throw; }
    catch (Exception ex)        { _log?.LogError(ex, "Modbus 通信异常"); throw; }
}
```
**关键改进 vs 玩具代码**：① `while` 轮询 + 1s 超时，不会卡死；② 识别异常响应（功能码 `|0x80`）；③ CRC 验证；④ 全程日志，现场排错靠它。

---

### 🔗 知识点串联
```
Modbus 帧格式（知识点1：地址+功能码+数据+CRC）
        ↓ 需要数据完整性保证
CRC16 算法（知识点2：手搓 + 验证）
        ↓ 封装成可复用通信方法
NModbus 读/写（知识点3：调库 + 超时/异常/日志）
        ↓ 最终用在
工业现场设备通信（真实项目 DAQ Monitor）
```
前端类比：`HTTP 格式 → 请求签名 → 封装 request() → 项目调 API`，一一对应。

### 🧪 今日三档练习
- 🟢 **基础题**：用今天的 `CrcCalculator.Calculate()` 算 `01 06 00 01 00 64`（写寄存器 40002 = 100）的 CRC。
  **✅ 答案**：`D9 E1`。⚠️ 你之前看到的那份样本写成 `19 98`，**那是错的**——自己算一遍才是 `D9 E1`，这就是为什么要会手算、不能抄答案。
- 🟡 **进阶题**：手搓 `WriteSingleRegister(slave, addr, value)` 的请求帧（功能码 0x06）：`[从站][0x06][地址高][地址低][值高][值低][CRC低][CRC高]`，并写出 C# 构造代码。
- 🔴 **挑战题**：用 `com0com` 虚拟串口对 + `Modbus Slave` 软件，设 1 号从站、40001=100 / 40002=200，用今天的代码读出来验证正确。

### 💡 工控导师说（真实战例）
> 我在某化工厂调试，PLC 上报的温度突然从 50℃ 跳到 65000℃——查了半天才发现是 **CRC 校验没开**，干扰信号被当成了有效数据。从那以后，我所有项目**强制开启 CRC 校验**，并在日志里打印每帧原始字节。
> 另一条：现场最坑的不是「不会写」，是「接错线 / 地址偏移」。先拿 `Modbus Poll` 这类工具确认设备真在回数据，再写代码，能省一天。

### ⚠️ 常见坑点预警
| 坑 | 说明 |
|---|---|
| **CRC 字节序** | CRC 是**低字节在前**（小端），但数据域是**高字节在前**（大端），别搞混！ |
| **寄存器地址偏移** | 手册写「40001」，协议地址是 `0x0000`，不是 `0x0001`！±1 差异是新手第一坑 |
| **功能码 0x03 数量限制** | 一次最多读 125 个寄存器，超了要分批 |
| **异常响应识别** | 功能码最高位变 1（0x83）表示设备拒了，要 catch 错误码 |
| **硬等 `Sleep`** | 别 `Thread.Sleep(100)` 硬等响应，要用超时轮询，否则偶发卡死 |

### 🎓 职业建议
Modbus 是工控面试**必考题**。面试官常让你「手写 Modbus CRC 算法」——今天这段代码（含 `0xA001`、`0xFFFF`、右移异或）**必须能背写出来**。会调库只能算「用过」，能手搓 + 讲清原理才是「懂」。

### 🎤 面试 1 分钟讲法(Day 1 综合)

> "Modbus 是工业事实标准协议,有 4 种寄存器表:**线圈(开关/可写)、离散输入(开关/只读)、输入寄存器(数值/只读)、保持寄存器(数值/可写)**。功能码 0x03 读保持寄存器最高频,0x06 写单个,0x10 写多个,对应 REST API 的 GET/POST。
>
> CRC16 我能手写:**初值 0xFFFF,每个字节先异或到 CRC 低字节,再循环右移 8 次 — 最低位是 1 就右移并异或 `0xA001`,否则只右移**。多项式 `0xA001` 是标准 `0x8005` 的位反转,因为算法用右移实现。
>
> 最容易翻车的坑:**CRC 是低字节在前(小端),数据域是高字节在前(大端),同一个协议两套字节序**。口诀'数据跟着网络走,CRC 跟着 Intel 走'。
>
> 我项目里 `VerifyCrc` 扩展方法验每一帧,生产环境从不裸读。"

**面试官可能追问**:
- "为什么 Modbus 用 0xA001?" → 它是 `0x8005` 的位反转,因为算法从低位往高位扫(右移实现),用反向多项式计算更高效
- "0xFFFF 起点为什么不用 0?" → 全 0 数据算 CRC 会得到 0,分不清"没传"和"传了全 0"。0xFFFF 让全 0 数据也产生非零校验
- "CRC16 能 100% 检错吗?" → 不能。CRC16 能检出 99.998% 错误(2 字节空间 65536 种),漏检概率约 1/65536。工业现场足够,关键场景用 CRC32

### 📅 明日预告
Day 2 讲 **Modbus 读写（RTU + TCP）**：用今天的 `ModbusRTU` 类扩展写入寄存器、读线圈；引入 `NModbus4` 标准库（实际项目直接用）；对比 RTU 与 TCP 两种传输（像前端对比 HTTP 与 WebSocket）；并写完整的 Modbus TCP 例子。

> 提示：今天我们是「重复造轮子」把原理吃透，明天教你「直接用轮子」——但**懂原理再用库**，才不会在现场抓瞎。

---

## Day 2 — 写寄存器 + 数据类型映射 + 轮询架构

### 一句话讲清楚
上位机不只是「看」，还要「控」：写保持寄存器下发设定值；32 位浮点 / 整数跨 2 寄存器，要按设备约定拼 / 拆；读要放后台轮询，别卡 UI。

### 🟢 知识点 1：写保持寄存器（功能码 0x06 / 0x10）

```csharp
master.WriteSingleRegister(slaveId, 10, 1234);                    // 0x06 写单个
master.WriteMultipleRegisters(slaveId, 10, new ushort[]{1,2,3}); // 0x10 写多个
```
**🧪 手搓写单个请求帧（验证你对帧的掌握）**
写单个（0x06）帧：`[从站][0x06][地址高][地址低][值高][值低][CRC低][CRC高]`
- `01 06 00 0A 04 D2 + CRC` = 对 1 号设备、地址 10、写 `0x04D2 = 1234`。
- 手搓时地址和值都按**大端**（高位在前），CRC 用昨天的 `CrcCalculator` 算且**低字节在前**。

### 🟡 知识点 2：浮点 / 整数跨寄存器拼拆（大小端是第一坑）

### 🎭 拟人秒懂:中文报纸寄到英文邮局(大小端记忆锚点)

> 把大小端问题想象成"中文报纸寄到了英文邮局"。

- **设备发的是大端字节序**(像中文报纸从左到右读,高位在前):发 `0x42C80000`(=100.0℃),字节顺序是 `42 C8 00 00`
- **x86 CPU 是小端**(像英文邮局从右到左读):`BitConverter.ToSingle` 看到 `42 C8 00 00` 会读成 `0x0000C842` ≈ 极小亚常态数
- **结果**:温度显示从 100℃ 飞到 `1.4e-39 ℃` — **不报错,但数值飞掉**,debug 一整晚

**3 种解决方案**:
1. **手动反转字节**(下面代码的方案):`byte[] le = { be[3], be[2], be[1], be[0] };`
2. **用 `BinaryPrimitives`**(推荐):`BinaryPrimitives.ReadSingleBigEndian(be)` — 一行搞定
3. **用 `ModbusFrameParser.ToFloatModbus`**(M2 Day3):自动处理 ABCD/CDAB/BADC/DCBA 四种顺序

**为什么 x86 选小端**:低位存在低地址,CPU 加法从低位开始算更快。但网络协议选大端,因为路由器要先看"目的地地址"(高位在前更直观)。两个世界打架,工业协议夹中间翻车。

**前端类比**:跟 Node.js Buffer 的 `readUInt16BE` vs `readUInt16LE` 一模一样,只是 JS 默认 Little Endian 你很少遇到 BE。

```csharp
float ToFloat(ushort hi, ushort lo)
{
    // ⚠️ 设备回的是「大端字节序」；本机 x86 是【小端】，BitConverter 会按小端读。
    // 直接把大端字节数组喂进去会读成极小值！必须先把 4 字节按大端排好，再反转为小端布局。
    byte[] be = { (byte)(hi >> 8), (byte)hi, (byte)(lo >> 8), (byte)lo }; // 大端字节序
    byte[] le = { be[3], be[2], be[1], be[0] };                            // 反转给 BitConverter
    return BitConverter.ToSingle(le, 0);
}
```
**🧪 运行结果（float 拼出来长啥样）**
```csharp
ushort r1 = 0x42C8, r2 = 0x0000;          // 设备回的两个保持寄存器 → 0x42C80000 = 100.0f
byte[] be = { (byte)(r1 >> 8), (byte)r1, (byte)(r2 >> 8), (byte)r2 };
byte[] le = { be[3], be[2], be[1], be[0] };
float temp = BitConverter.ToSingle(le, 0);
Console.WriteLine($"温度 = {temp:F2} ℃");   // 温度 = 100.00 ℃
```
**运行结果：`温度 = 100.00 ℃`**（拼成 `0x42C80000` 按 IEEE754 ≈ 100.0℃）。
> ⚠️ 大小端是现场最常踩的坑：x86 是小端，`BitConverter.ToSingle(大端字节数组)` 会读成极小值（这是最阴险的错法，不报错但数值飞掉）。**先反转再转**，或抓一帧用 `ModbusFrameParser.ToFloatModbus` 验证，别凭空猜。

### 🔴 知识点 3：后台轮询架构（呼应 M0 Day 7，不阻塞 UI）

```csharp
private async Task PollLoop(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        try
        {
            var regs = master.ReadHoldingRegisters(1, 0, 10);
            RaiseData(/* 映射成 SensorPoint */);
        }
        catch (Exception ex) { /* 重连 / 日志 */ }
        await Task.Delay(500, ct);   // 500ms 轮询，不阻塞 UI
    }
}
```
**逐行白话**：`while (!ct.IsCancellationRequested)` 让关闭窗口时能优雅停（呼应 M0 Day7 的 `CancellationToken`）；`await Task.Delay(500, ct)` 把轮询放后台，UI 线程不被卡。

### 🧪 今日三档练习
- 🟢 **基础题**：把两个寄存器 `0xABCD, 0x1234` 按大端拼成 float 并打印。
- 🟡 **进阶题**：给 `ModbusRTU` 加 `WriteSingleRegister()`（功能码 0x06），请求帧格式 `[地址][0x06][地址2B][值2B][CRC]`，响应原样回显。
- 🔴 **挑战题**：把 `ModbusDevice` 接进 DAQ Monitor（见下方项目任务），用虚拟从站跑通「读 10 个寄存器 → 点表实时刷新」。

### 💡 工控导师说
> 写保持寄存器可能**直接驱动执行器**（比如写个值就让电机转）。我见过新人把地址写错，现场设备猛地动一下把人吓一跳。**写之前务必先确认地址表，最好先在模拟器试。**

### ⚠️ 常见坑点预警
| 坑 | 说明 |
|---|---|
| `CancellationToken` 优雅退出 | 关窗口要 cancel 轮询，否则线程泄漏 |
| 寄存器 → 物理量换算 | 设备值常是原始码，要乘系数 / 偏移：`real = raw * 0.1` |
| 写错地址烧设备 | 写保持寄存器可能直接驱动执行器，先确认地址表 |

### 🎓 职业建议
「后台轮询 + 异常重试 + 优雅退出」是 13K 岗位的硬门槛。面试时能把 `PollLoop` + `CancellationToken` + 重试讲清楚，比单纯说「我会 Modbus」分量重得多。

### 📅 明日预告
Day 3 讲 **报文解析 + 大小端系统讲解**——把响应帧逐字节拆开（含线圈「位打包」解析）、把 32 位浮点的 ABCD/CDAB 字交换彻底讲透、附异常码全表。这俩是现场翻车重灾区，也是「懂协议」和「只会调库」的分界线。

---

## Day 3 — 报文解析（响应帧拆解 + 线圈位解包）+ 大小端系统讲解

### 一句话讲清楚
**报文解析** = 把设备回的一串字节，按 Modbus 规则「拆」成有意义的数据；**大小端** = 多字节数（16 位寄存器 / 32 位浮点）的字节排列顺序，拆错顺序数值就天差地别。Day 1/2 你只会「发」和「调库读」，今天学「收到后怎么自己拆」——现场抓到一帧乱码，全靠这手。

### 前端类比秒懂（解析 / 字节序 对上位机）
| Modbus 概念 | 前端类比 | 说明 |
|---|---|---|
| 报文解析 | 解析后端返回的 JSON / 二进制 protobuf | 按字段偏移量取值 |
| 寄存器大端 | 网络字节序（big-endian），像 IPv4 头 | 高位字节先传 |
| 线圈位打包 | 一个 byte 当 8 个 boolean（像权限位掩码） | 每 bit = 1 个线圈 |
| 3.5 字符静默间隔 | TCP 报文边界 / 帧分隔符 | 靠「沉默多久」切帧 |
| 浮点字交换(CDAB) | 数组元素顺序颠倒 `[lo,hi]` vs `[hi,lo]` | 两寄存器顺序反了 |

---

### 🟢 知识点 1：读保持寄存器响应帧——逐字节拆解

**请求帧**：`[从站][0x03][起始地址 2B 大端][数量 2B 大端][CRC 2B 低前]`
**响应帧**：`[从站][0x03][字节数 N×2][数据 N×2 字节，每寄存器 2 字节大端][CRC 2B 低前]`

用 Day 1 出现过的真实响应帧 `01 03 04 00 0A 00 14 C4 0B` 拆解：

```
01          ← 从站地址（1 号回的）
03          ← 功能码回显（读保持寄存器）
04          ← 后面数据区有 4 个字节（= 2 个寄存器）
00 0A       ← 第 1 个寄存器 = 0x000A = 10（大端：高字节在前）
00 14       ← 第 2 个寄存器 = 0x0014 = 20
C4 0B       ← CRC（低字节 C4 在前）
```

**解析代码（自己拆，不靠库）**：
```csharp
public static ushort[] ParseReadHoldingRegisters(byte[] resp)
{
    if (resp[1] != 0x03) throw new InvalidOperationException("不是 0x03 读响应");
    int byteCount = resp[2];                 // 第 3 字节 = 数据区字节数
    int regCount  = byteCount / 2;           // 每 2 字节 = 1 个寄存器
    var regs = new ushort[regCount];
    for (int i = 0; i < regCount; i++)
    {
        byte hi = resp[3 + i * 2];           // 高字节在前（大端）
        byte lo = resp[4 + i * 2];
        regs[i]  = (ushort)(hi << 8 | lo);  // 拼回 ushort
    }
    return regs;                             // → [10, 20]
}
```
**运行结果**：`ParseReadHoldingRegisters({0x01,0x03,0x04,0x00,0x0A,0x00,0x14,0xC4,0x0B})` → `[10, 20]` ✓

---

### 🟡 知识点 2：线圈 / 离散输入解析（位打包——最容易错！）

### 🎭 拟人秒懂:8 个开关挤 1 间宿舍(位打包记忆锚点)

> 把线圈数据想象成"8 个开关挤在 1 间宿舍里"。

- **寄存器**:1 个数 = 2 字节 = **1 张双人床**(2 字节,16 位,大端排)
- **线圈**:8 个开关 = 1 字节 = **8 人宿舍**(每位代表 1 个开关,bit0 = 第 1 床,bit7 = 第 8 床)

**最致命的混淆**:
- 寄存器解析:`hi << 8 | lo`(高字节在前,大端)
- 线圈解析:`(data[i/8] & (1 << (i%8))) != 0`(低 bit 在前)
- **两套规则互不兼容!** 用解析寄存器的方式去解析线圈,数据全错

**字段对位**(以 `01 01 02 FF 03 [CRC]` 为例):
```
01   ← 从站地址(1 号)
01   ← 功能码(读线圈)
02   ← 后面 2 字节数据(覆盖 16 个线圈)
FF   ← 第 1 间宿舍(线圈 0-7):11111111 → 全在线
03   ← 第 2 间宿舍(线圈 8-15):00000011 → 只有 8、9 在线
CRC
```

**为什么线圈要位打包**:工业现场开关量多(限位开关、报警灯、继电器状态),1 个字节塞 8 个开关省带宽 — 读 16 个开关只发 2 字节,不打包要发 16 字节,8 倍流量差距。

**记忆口诀**:"**寄存器=双人床大端排,线圈=8人宿舍低 bit 先**"。

**前端类比**:跟你写权限位掩码 `PERMISSION_READ = 1 << 0, PERMISSION_WRITE = 1 << 1` 完全一样,只是这里"权限"是物理开关状态。

**致命区别**：读**寄存器**（0x03/0x04）时，数据区是「每 2 字节 = 1 个数」；但读**线圈**（0x01/0x02）时，数据区是「每 1 字节 = 8 个线圈，按位排」！两套规则完全不同，混用必错。

帧示例：`01 01 02 FF 03 [CRC]` = 读 1 号从站线圈，回 2 字节数据（覆盖 16 个线圈）；`FF`=二进制`11111111`→线圈 0–7 全在线，`03`=二进制`00000011`→线圈 8、9 在线、10–15 离线。

**解析代码（位掩码）**：
```csharp
public static bool[] ParseCoils(byte[] data, int coilCount)
{
    var bits = new bool[coilCount];
    for (int i = 0; i < coilCount; i++)
        // 第 i 个线圈 = 第 (i/8) 字节的第 (i%8) 位；位序：bit0 = 最先返回的线圈
        bits[i] = (data[i / 8] & (1 << (i % 8))) != 0;
    return bits;
}
// ParseCoils({0xFF, 0x03}, 12) → [T,T,T,T,T,T,T,T, T,T,F,F, F,F]
```
> ⚠️ 线圈地址从 0 开始、位序「低 bit 在前」；寄存器是「高字节在前」。**这是两个独立的字节序世界**，永远别用解析寄存器的方式去解析线圈。

---

### 🔴 知识点 3：大小端系统讲解（32 位浮点 ABCD / CDAB 字交换）

### 🎭 拟人秒懂:双胞胎兄弟坐错位置(ABCD/CDAB 记忆锚点)

> 把 32 位浮点的字节顺序想象成"双胞胎兄弟坐 2 个位置,可能坐对也可能坐反"。

**背景**:32 位浮点(IEEE 754)= 4 字节 = **2 个 16 位寄存器**。问题来了:
- 寄存器内部字节序是固定的(大端,高字节在前)
- **但 2 个寄存器的顺序谁来当"高字"?** 各家设备自己定 → 4 种排法

**4 种坐法**(双胞胎叫 ABCD,4 个人是 4 个字节):
- **ABCD**(标准大端):A 在最高位,D 在最低位 — **哥哥坐主位,弟弟坐旁边**,正常顺序
- **CDAB**(字交换):前 2 字节是 CD,后 2 字节是 AB — **弟弟坐主位,哥哥坐旁边,但两人各自的手脚没乱**
- **BADC**(字节交换):前 2 字节是 BA,后 2 字节是 DC — **哥哥的手脚互换了**(单寄存器内字节颠倒)
- **DCBA**(全小端):完全反 — **弟弟坐主位 + 各自手脚互换**(罕见)

**为什么 CDAB(字交换)是国产设备默认**:
- 西门子早期 PLC 用大端(ABCD),但很多国产 PLC 厂商为了"省硬件成本"用小端 CPU,字寄存器值是大端但跨寄存器顺序就反了
- 后来大家发现"字交换比改寄存器实现简单" → 国产默认 CDAB
- 西门子 S7-1200/1500、施耐德 M580 等仍然是 ABCD

**判断口诀**:
- 拿到一帧 float,先按 ABCD 拼,如果数值合理(50℃ 左右)就是 ABCD
- 如果是极小亚常态数(1e-39)或极大数(1e+39) → 99% 是 CDAB,换 r0/r1 顺序再试
- 永远**抓帧定顺序**,别凭手册猜(手册经常写错)

**坑中坑**:**字节交换(byte swap) ≠ 字交换(word swap)** — 字节交换是寄存器内 2 字节颠倒,字交换是 2 个寄存器顺序颠倒。两者完全独立。

**前端类比**:跟 `Uint8Array` 和 `Uint16Array` 看 buffer 的方式一样,但更复杂(2 层字节序)。

**两个坑，别混**：
1. **寄存器内字节序**：16 位值永远是「高字节在前」（大端），即 `value = hi<<8 | lo`。
2. **跨寄存器字序**：32 位浮点占 2 个寄存器，但**哪一个是高字**各设备不一样，常见的 4 种排列：

| 名称 | 寄存器顺序 | 字节拼接（reg0, reg1） | 谁常用 |
|---|---|---|---|
| **ABCD** | 高字在前 | `hi(reg0) lo(reg0) hi(reg1) lo(reg1)` | 标准大端 |
| **CDAB** | **字交换(Word-swap)** | `hi(reg1) lo(reg1) hi(reg0) lo(reg0)` | **大量国产设备/PLC 默认！** |
| **BADC** | 字节交换(Byte-swap) | `lo(reg0) hi(reg0) lo(reg1) hi(reg1)` | 少数 |
| **DCBA** | 全小端 | `lo(reg1) hi(reg1) lo(reg0) hi(reg0)` | 少数 |

> **字节交换 ≠ 字交换**：byte swap 是「寄存器内的 2 字节颠倒」；word swap 是「两个寄存器的顺序颠倒」。现场 90% 的浮点问题其实是 **word swap（CDAB）**，不是单纯字节颠倒。

**可复用工具（一次写对 4 种，与工程 `ModbusFrameParser.ToFloatModbus` 完全一致）**：
```csharp
public enum ByteOrder { ABCD, CDAB, BADC, DCBA }

// ⚠️ 关键：x86 是小端，设备回的是"大端字节序"的 4 字节。
// 必须先把 4 字节按"大端"排好(b0 是最高位字节)再交给 ToSingle，否则 BitConverter 会把大端字节当小端读成极小值。
private static float ToSingleBig(byte b0, byte b1, byte b2, byte b3)
{
    var le = new[] { b3, b2, b1, b0 };   // 大端数组 Reverse 成小端顺序
    return BitConverter.ToSingle(le, 0); // 等价于"按大端解读这 4 字节"
}

public static float ToFloatModbus(ushort r0, ushort r1, ByteOrder order) => order switch
{
    ByteOrder.ABCD => ToSingleBig((byte)(r0>>8), (byte)r0, (byte)(r1>>8), (byte)r1),
    ByteOrder.CDAB => ToSingleBig((byte)(r1>>8), (byte)r1, (byte)(r0>>8), (byte)r0), // 字交换
    ByteOrder.BADC => ToSingleBig((byte)r0, (byte)(r0>>8), (byte)r1, (byte)(r1>>8)), // 字节交换
    ByteOrder.DCBA => ToSingleBig((byte)r1, (byte)(r1>>8), (byte)r0, (byte)(r0>>8)), // 全小端
    _ => throw new ArgumentOutOfRangeException()
};
```
**验证（抓帧定顺序）**：设备回 `r0=0x42C8, r1=0x0000`
- `ABCD` → `0x42C80000 = **100.00 ℃**` ✓ 合理
- `CDAB` → `0x000042C8` ≈ 极小亚常态数 ✗ 一眼就知道错

> ⚠️ **怎么判断设备是哪种？** 别猜。抓一帧「已知物理量」（比如触摸屏显示 35.1℃ 的那个点），把两个寄存器按四种顺序各拼一遍，哪个凑出 35.1 就是哪种。`ByteOrder` 默认值**绝不许瞎写**，必须抓帧确认。

---

### 🟢 知识点 4（补）：异常响应 + 异常码表 + RTU 帧边界

### 🎭 拟人秒懂:餐厅服务员的回执单 + 谈话停顿(异常码 + 帧边界记忆锚点)

> 把异常响应想象成"餐厅服务员的回执单",把 RTU 帧边界想象成"两个人谈话的停顿"。

**异常码 = 服务员的回执**:
| 异常码 | 服务员原话 | 现场最常见原因 |
|---|---|---|
| **0x01** 非法功能 | "我们这不卖这道菜" | 设备不支持该功能码(只读设备收到写) |
| **0x02** 非法地址 | "菜单上没这道菜" | **寄存器地址超范围(±1 偏移,新手第一坑)** — 90% 是这个 |
| **0x03** 非法数据值 | "您要 200 份?厨房做不出来" | 写的值不合法(数量超 125、值超范围) |
| **0x04** 从站故障 | "厨房着火了,改天再来" | 设备内部出错,要查设备日志 |
| **0x06** 从站忙 | "稍等,前面还有 5 桌" | 正在处理,稍后重试即可 |
| **0x0B** 网关失效 | "您要的菜在外卖窗口,但小哥送不到" | 经网关/转发器时目标不可达 |

**怎么识别异常响应**:**功能码最高位被置 1**(0x03 → 0x83,0x06 → 0x86,0x10 → 0x90)。
口诀:"**功能码 + 0x80 = 设备说 NO**"。

**RTU 帧边界(3.5 字符静默)= 谈话停顿**:
- **场景**:Modbus RTU 没有"包长度"字段(不像 TCP 头告诉你包多大),那怎么知道一帧结束?
- **答案**:靠"双方沉默多久"判断 — 一帧发送完后,**沉默超过 3.5 个字符时间**,接收方才知道"这句话讲完了"
- **3.5 字符多久**:9600 波特率 ≈ 4ms;115200 波特率 ≈ 0.3ms
- **谈话类比**:你和朋友聊天 — 你说完一句,**停顿超过 2 秒**,朋友才知道"该我接话了"。Modbus 设备也一样,只是停顿用 ms 计
- **手搓接收必须用定时器**:不计时就会把"两帧拼成一帧"(粘包)或"一帧拆成两帧"(半包)

**前端类比**:
- 异常码 = HTTP 状态码(404 = 地址不存在 = Modbus 0x02,403 = 不让访问 = Modbus 0x01)
- 3.5 字符静默 = HTTP 的 `\r\n\r\n`(空行分隔符),只是 Modbus 用"时间"分隔,HTTP 用"字符"分隔

**异常帧**：`[从站][功能码 | 0x80][异常码][CRC]`。功能码最高位被置 1（0x03→0x83）— 异常码含义详见上方"服务员回执"表(0x01 非法功能 / 0x02 非法地址 / 0x03 非法数据 / 0x04 故障 / 0x06 忙 / 0x0B 网关失效)。

**RTU 帧边界（3.5 字符静默间隔）**：Modbus RTU **没有长度头**，靠「帧与帧之间 ≥ 3.5 个字符时间」的静默来切帧。串口库一般帮你处理；但如果你**手搓接收循环**，必须用定时器判断「静默够了才算一帧收完」，否则半包/粘包——这正是 M1 讲的「半包原理」在 Modbus 上的具体表现。

---

### 🧪 今日三档练习
- 🟢 **基础题**：解析响应 `01 03 06 00 64 00 C8 01 2C 00 0A [CRC]`，写出 3 个寄存器的值。
  **✅ 答案**：`0x0064=100`，`0x00C8=200`，`0x012C=300`（温度/压力/流量原始码）。
- 🟡 **进阶题**：用今天的 `ParseCoils` 解析 `01 01 02 FF 03 [CRC]`（读线圈回 2 字节），列出前 12 个线圈状态。
  **✅ 答案**：`0xFF`→线圈 0–7 全在线；`0x03=0000 0011`→线圈 8、9 在线，10–15 离线。
- 🔴 **挑战题**：设备回 `r0=0x42C8, r1=0x0000`，分别按 `ABCD` / `CDAB` 用 `ToFloatModbus` 解析 float，说明哪个合理。再用 `Modbus Slave` 设一个 32 位 float（如 100.0），抓帧确认设备的 `ByteOrder`。
  **✅ 答案**：`ABCD → 0x42C80000 = 100.0` 合理；`CDAB → 0x000042C8` 是极小亚常态数，显然错 → 该设备是大端高字在前（ABCD）。

### 💡 工控导师说（真实战例）
> 某水处理项目，液位显示 1.2 米，半夜突然变 600 多万米——查了一整晚，最后发现是**设备把 32 位浮点存成字交换（CDAB），我却按 ABCD 解析**。改一行 `r1, r0` 顺序就正常了。从此我的 `ToFloatModbus` 第一个参数必是 `ByteOrder`，默认值绝不瞎写。
> 另一坑：**抓帧看原始字节永远比猜强**。我电脑常备 `Modbus Poll` / 串口助手，现场第一件事不是写代码，是先用工具抓一帧，确认设备到底回的啥、什么字节序。

### ⚠️ 常见坑点预警
| 坑 | 说明 |
|---|---|
| 线圈位序 | 线圈是「位打包」(bit0=第1个线圈)；寄存器是「2 字节大端」。两套规则别混 |
| 浮点字交换 | 32 位浮点常是 CDAB（字交换），不是 ABCD。抓帧验证，别猜 |
| 字节交换 vs 字交换 | byte swap = 寄存器内字节颠倒；word swap = 两寄存器顺序颠倒，别搞反 |
| 异常码 0x02 | 90% 是地址超范围 / ±1 偏移，先查地址表 |
| RTU 3.5 字符静默 | 手搓接收要计时判断帧边界，否则半包/粘包 |

### 🎓 职业建议
能讲清「响应帧怎么逐字节拆、线圈为什么是位打包、浮点为什么有字交换」的候选人，面试里直接甩开「只会 NModbus.Read()」的人一条街。这三点就是「懂协议」和「会用库」的分界线，13K+ 岗位必问。

### 📅 明日预告
M3 讲 **PLC 通信（S7 / 西门子）**——直连 PLC 读 DB 块，工业现场另一大主力；并用同一套 `IDevice` 抽象接进 DAQ Monitor，体验「换设备 UI 零改动」。

---

## 🎤 M2 整体面试 3 分钟讲法(模块综合)

> "Modbus 是工业事实标准协议,我有完整的从零实现经验。
>
> **协议层**:4 种寄存器表(线圈/离散输入/输入/保持),功能码 0x01-0x04 读,0x05-0x06 单写,0x0F-0x10 批量写。RTU 帧格式 = 从站 + 功能码 + 数据 + CRC(2 字节),没有长度头,靠 3.5 字符静默切帧。
>
> **CRC16 我能手写**:初值 0xFFFF,每字节先异或到 CRC 低字节,循环右移 8 次,最低位是 1 就异或 0xA001。返回时低字节在前(小端),数据域反而是高字节在前(大端) — 这是新人翻车 No.1。
>
> **报文解析**:响应帧 = 从站 + 功能码 + 字节数 + 数据 + CRC。数据区按 2 字节大端拼成寄存器(ushort);线圈是位打包(每字节 8 位,低 bit 在先)。
>
> **32 位浮点跨 2 寄存器**:有 ABCD/CDAB/BADC/DCBA 四种字节序,国产 PLC 多数默认 CDAB(字交换)。我的 `ToFloatModbus` 接受 `ByteOrder` 参数,默认值绝不瞎写,先抓帧定顺序。
>
> **异常响应**:功能码最高位置 1(0x83) 表示错误,异常码 0x01-0x0B 对应不同原因,0x02 非法地址占 90%。
>
> 我项目里 ModbusDevice 封装了 RTU/TCP 两种,后台 PollLoop + CancellationToken,异常自动重试,所有帧都 VerifyCrc。"

**面试官可能连环追问**:
- "RTU 和 TCP 主要区别?" → ① 传输介质:RTU 串口(RS485/RS232),TCP 网口 ② 帧格式:RTU 末尾 2 字节 CRC,TCP 头部 7 字节 MBAP 头(事务ID+协议ID+长度+单元ID) ③ 速度:TCP 更快(100Mbps vs 9600bps) ④ 完整性:RTU 靠 CRC,TCP 靠 TCP 自身的可靠性,所以 TCP 不需要 CRC
- "Modbus 一根总线能接多少设备?" → RS485 物理层理论 32 个,实际建议 16-24 个;地址范围 1-247,0 是广播
- "怎么调试 Modbus?" → Modbus Poll(主站模拟) + Modbus Slave(从站模拟),Wireshark 抓 Modbus TCP 包,串口助手 + 串口监控抓 RTU 字节
- "0x03 一次最多读多少寄存器?" → 标准 125 个(0x04 输入寄存器也是 125),0x10 写多最多 123 个,超了要分批

## 📌 温故知新（跨模块联动）
- **M0 Day7 后台轮询 → 这里 `PollLoop`**：读寄存器放后台 `Task`+`CancellationToken`，和 M0 一模一样。
- **M1 帧 / CRC → 这里底层复用**：RTU 帧的 CRC 就是你刚手搓的 `CrcCalculator`；NModbus 帮你算，但懂原理才排得了错。
- **前瞻 M4 / M9**：读出的点会进 `PointStore`(M4) 和统一管道(M9)；批量读性能见 M9 的限流。

## 📚 延伸阅读（卡点时点开）
- NModbus 仓库 + 示例：https://github.com/NModbus/NModbus
- Modbus 官方协议规范（帧结构 / 功能码 / 异常码权威）：https://modbus.org/specs
- 全部模块外链汇总见 `外部链接索引.md`
- 📎 没有硬件？看 `硬件替代方案与讲解_深度版.md`：Modbus 从站模拟器(diagslave/QModMaster) 零成本练手 + RS485/终端电阻科普

## 🏗️ 项目任务（边学边做，落到 DAQMonitor）
1. 在 `src/DaqMonitor.Core/Devices/` 建 `ModbusDevice : DeviceBase`，RTU/TCP 可选，后台 `Task` 循环 `ReadHoldingRegisters` → 映射成 `SensorPoint` → `RaiseData`。
2. 加 `Write` 实现（下发设定值）+ 一个 `Scale` 配置（系数 / 偏移）把原始码转物理量。
3. 接入 `CancellationToken` 管理生命周期，异常走 M9 的 `Retry` 重连。
4. 配套 xUnit 测试用 `ModbusSlave` 模拟从站或 Moq 验证「读 10 个寄存器 → 点表刷新」。

## 模块交付清单（M2）
- [ ] 能从零手搓 Modbus RTU 请求帧 + 手算 CRC（面试硬要求）
- [ ] NModbus RTU + TCP 两种连接
- [ ] 读 4 类寄存器（重点保持寄存器）
- [ ] **响应帧逐字节解析**（0x03 读响应 / 线圈位打包解析）
- [ ] **异常响应识别 + 异常码表（0x01~0x0B）**
- [ ] **32 位浮点 4 种字节序（ABCD / CDAB / BADC / DCBA）**
- [ ] **RTU 3.5 字符静默 / 帧边界（半包处理）**
- [ ] 浮点 / 整数跨寄存器映射（大小端已验证）
- [ ] 后台轮询 + 超时 + 异常重试 + 优雅退出
- [ ] 物理量换算配置

## 🧩 完整代码组装（Day 1 + Day 2 + Day 3 拼起来就是可用模块）
```
Modbus/
├── CrcCalculator.cs       （Day1：手搓 CRC + 扩展方法 VerifyCrc）
├── ModbusRTU.cs           （Day1/2：ReadHoldingRegisters 完整版 + WriteSingleRegister）
├── ModbusFrameParser.cs   （Day3：响应帧解析 + 线圈位解包 + 异常码 + ToFloatModbus 4 种字节序）
└── ModbusDevice.cs        （项目任务：接 DAQ Monitor 的 IDevice 实现）
```
