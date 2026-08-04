# M16 — 更多工业总线（CAN + USB-HID）🟡

> **优先级定位**：🟢 再学 · 工业总线扩展 CAN/USB-HID（汽车/锂电等特定行业才用）
> **技术来源**：🟧 `HidLibrary`（USB-HID）；🟦 自研 `ICanChannel`/`IHidChannel` 抽象（复用 M9 面向接口）；真实 CAN 用厂商 DLL（PCAN/Vector/周立功）。
> **给简历加的能力**：CAN 和 USB-HID 是汽车/半导体/仪器极常见的通信方式（天津/苏州 JD 点名）。把它们也接进 `IDevice`，DAQ Monitor 从"串口/Modbus/PLC"扩展到"CAN/USB 也能插"。
> **前置**：M1（串口字节流+模拟通道）、M2（字节拼值）、M9（面向接口/DI）、M12（工程量）。
> **前端类比总纲**：CAN 像"群聊广播（按话题 ID 订阅）"、USB-HID 像"填表交表（固定长度 Report）"——比起串口"自由写信"更结构化。

---

> ⏱️ **阅读路径**(按时间预算选入口)
> - **3 分钟**:看「模块目标」— 知道 CAN 是"群聊广播" / USB-HID 是"填表交表"
> - **30 分钟**:加看 Day 1 CAN 报文 ID + 标准帧/扩展帧
> - **3 小时**:全文精读 + Day 2 USB-HID Report 协议
> - 🎯 **面试高频**:CAN 报文 ID 优先级仲裁 / 标准帧 11bit vs 扩展帧 29bit / **HID Report 长度固定 64B**
> - 🔁 **配套复习**:[间隔重复表](记忆与复习机制_间隔重复版.md)

## 模块目标
1. 搞懂 CAN 总线：它和 Modbus 有什么不同、帧结构、为什么汽车/设备爱用。
2. 用 `IDevice` 接口写一个 `CanDevice`（先给可测的 `SimulatedCanChannel`，再给真实 API 形态）。
3. 搞懂 USB-HID：为什么仪器走 HID 而不是虚拟串口、怎么用 `HidLibrary` 收发。
4. 写一个 `UsbHidDevice` 同样实现 `IDevice`，接进 DAQ Monitor。

---

## Day 1 — CAN 总线（Controller Area Network）🟡

### 一句话讲清楚
Modbus 是"你问一个寄存器、我回一个值"的**主从问答**。CAN 是"总线上所有节点都能随时广播一帧**带 ID 的消息**，谁关心这个 ID 谁就收"的**多主广播**。就像公司群聊（CAN，谁都能发、按话题 ID 订阅）vs 私聊点名（Modbus，主叫从答）。

### 前端类比秒懂
| CAN 概念 | 前端类比 |
|---|---|
| 广播一帧带 ID | 向一个 topic/事件名 emit |
| 按 ID 订阅 | `emitter.on('topicId', handler)` |
| 多主 | 任何节点都能发，无需中央服务器 |
| 差分双绞线 | WebSocket over 可靠传输层 |

### 分点精讲
**① 为什么工业/汽车爱用 CAN**
| 特性 | 好处（企业为什么选它） |
|---|---|
| **多主** | 刹车系统和仪表盘都能主动发，不用等"主机"轮询 |
| **ID 优先级** | 两个节点同时发，ID 小的自动赢（非破坏性仲裁），关键报文不丢 |
| **强校验** | 每帧有 15 位 CRC + 应答位，错帧自动重发，抗干扰 |
| **双绞线差分** | 和 RS485 一样抗干扰，厂里电机乱转也不易误码 |

**② CAN 帧结构（重点：和数据怎么对应）**
标准帧（11 位 ID）简化版：
```
[ SOF ][ ID(11bit) ][ RTR ][ IDE ][ DLC(4bit,数据长度) ][ 数据(0~8字节) ][ CRC(15bit) ][ ACK ][ EOF ]
```
> 和 M2 的 Modbus 帧对比记忆：Modbus 有"从站地址+功能码+寄存器地址"；CAN 只有"**ID + 数据**"，**没有地址**——靠 ID 区分"这是哪路信号"。比如 `ID=0x100` 约定是"电机温度"，数据 `0x00 0xFA`=250 就是 25.0℃（÷10，工程量，见 M12）。

**③ 最小可跑代码（先模拟，不用硬件）**
我们仿照 M1 的 `LoopbackSerialChannel`，给 CAN 做一个**可单元测试的模拟通道**：
```csharp
// CanChannel.cs —— 抽象 CAN 收发，真实/模拟都实现它
public interface ICanChannel
{
    void Open();
    void Send(ulong id, byte[] data);                 // 广播一帧
    event Action<ulong, byte[]>? FrameReceived;        // 收到一帧（按 ID 订阅）
}

// 模拟通道：Send 立刻回一个假的温度帧，方便测试
public class SimulatedCanChannel : ICanChannel
{
    public event Action<ulong, byte[]>? FrameReceived;
    public void Open() { }
    public void Send(ulong id, byte[] data)
        => FrameReceived?.Invoke(0x100, new byte[] { 0x00, 0xFA }); // 假装设备回 250
}
```
**运行结果（测试里）**：调用 `Send(0x200, ...)` 后，`FrameReceived` 触发，`id=0x100`、`data=[0x00,0xFA]`。这就是"设备回了温度"，无需真实 CAN 卡。

**④ 接进你的项目（DeviceBase 那套）**
```csharp
// CanDevice.cs —— 实现你项目已有的 IDevice
public class CanDevice : DeviceBase
{
    private readonly ICanChannel _ch;
    public CanDevice(int id, string name, ICanChannel ch) : base(id, name) => _ch = ch;

    public override void Connect() { _ch.Open(); _ch.FrameReceived += OnFrame; State = DeviceState.Online; }
    public override void Disconnect() { _ch.FrameReceived -= OnFrame; State = DeviceState.Offline; }

    private void OnFrame(ulong id, byte[] data)
    {
        if (id == 0x100)                              // ID=0x100 → 点位1
        {
            int raw = (data[0] << 8) | data[1];      // 大端：高字节在前
            RaiseData(1, raw / 10.0);                // 触发 UI 刷新（M0 Day6 事件）
        }
    }
    public override double Read(int addr) => 0;      // CAN 是广播，通常靠事件收
    public override void Write(int addr, double value) { }
}
```
> 🟦 注意 `(data[0] << 8) | data[1]` 就是 M2 Day1 讲过的"两个字节拼一个大于 255 的数"，**跨模块复用**，不是新知识点。

**⑤ 真实硬件长什么样（知道形态即可，真跑要买卡）**
```csharp
// 真实通道形态（以 PCAN 为例，需装驱动/DLL）
public class PcanChannel : ICanChannel
{
    public void Open() => PcanBasic.Initialize(TPCANHandle.PCAN_USBBUS1, TPCANBaudrate.PCAN_BAUD_500K);
    public void Send(ulong id, byte[] data) { /* 调厂商 API 发帧 */ }
    public event Action<ulong, byte[]>? FrameReceived; // 在厂商回调里 Invoke
}
```
> 企业里接 CAN **几乎都用厂商 DLL**（周立功/PCAN/Vector），不同卡 API 不同，但都包成我们上面的 `ICanChannel`——这就是 M9"面向接口"的价值：**换卡不换业务代码**。

### 🔬 掰开揉碎：CAN 的"ID 即语义"思维
Modbus 你脑子里要想"寄存器 40001 是温度"；CAN 你要想"ID 0x100 是温度"。**ID 就是信号的名字**，和数据内容解耦。好处：加一路新信号只要约定一个新 ID，不用和设备协商"下一个空闲寄存器地址"——这对多节点系统极友好。

### ⭐ 重点 / 🔥 坑
| | 内容 |
|---|---|
| ⭐ 重点 | CAN = 多主广播 + ID 区分信号，无地址 |
| 🔥 坑 | 字节序：(data[0]<<8)|data[1] 是大端，别搞反 |
| 🔥 坑 | ID 冲突：两个节点用同一 ID 广播会互相干扰 |
| 🔥 坑 | 真实 CAN 要终端电阻/波特率一致，否则通信失败 |

### 🟢 基础题
写一个 xUnit 测试：用 `SimulatedCanChannel` 构造 `CanDevice`，触发 `Send`，断言"点位1 收到 25.0"。

### 🟡 进阶题
改 `SimulatedCanChannel` 让它回 `0x01 0x2C`（=300），算算点位1应该是多少℃？（答案：30.0℃）

### 🔴 挑战题
给 `CanDevice` 支持"多 ID 映射"：用配置 `Dictionary<ulong, int>` 把不同 CAN ID 映射到不同点位号（而不是硬编码 0x100→点位1），写测试验证两个 ID 分别进不同点位。

**✅ 答案（进阶题）**
300/10.0 = 30.0。

**🏗️ 项目任务**：把 `CanDevice` 接进 DAQ Monitor（真实工程已有文件，见文末），UI 实时表能显示 CAN 来的温度点。

**🎓 工控导师说**：CAN 总线最经典的新手坑是"忘了终端电阻"——总线两端各要一个 120Ω 电阻，没它信号反射、通信时好时坏，示波器上看波形全是毛刺。还有波特率，所有节点必须一致（250k/500k/1M），差一点都不通。**现场查 CAN 不通，先量终端电阻、再对波特率**，比改代码快。

**💼 职业建议**：CAN 在汽车/半导体/锂电设备里是标配。简历写"熟悉 CAN 总线通信、能用 IDevice 抽象接入 DAQ 系统"，对投这类行业的岗位是明确加分——它们 JD 常点和 Modbus/TCP 并列点名 CAN。

**✅ 打卡[ ]**

---

## Day 2 — USB-HID 通信（仪器最爱）🟡

### 一句话讲清楚
大量实验室/医疗仪器（流量计、注射泵、扫码枪、电子天平）走 USB-HID：**免驱**（系统原生支持）、即插即用、用 VID/PID 固定识别。JD 点名的"USB"通常指它。

### 前端类比秒懂
| 方式 | 前端类比 |
|---|---|
| 虚拟串口 CDC | 需要装驱动、端口号会变 |
| USB-HID | 系统原生支持，按 VID/PID 认设备 |
| Report 固定长度 | 填一张固定格式的表交上去 |

### 分点精讲
**① 为什么仪器走 USB-HID 而不是虚拟串口**
| 方式 | 痛点 | HID 的优势 |
|---|---|---|
| 虚拟串口(CDC) | 每次换 USB 口可能变 COM 号；要装驱动 | **免驱**（操作系统原生支持 HID） |
| 网口 | 仪器通常不会给你配 IP | HID 即插即用，VID/PID 固定识别 |

**② HID 通信模型（和串口的区别）**
- 串口：你 `Write(bytes)` 一串任意长度，对面 `Read` 一串。
- HID：**固定报文大小**（如 64 字节/包），你填一个 `Report`（报告），设备回一个 `Report`。像"填表交表"而非"自由写信"。

**③ 最小可跑代码（用 HidLibrary，真实库）**
```bash
dotnet add package HidLibrary
```
```csharp
// UsbHidDevice.cs —— 同样实现你项目的 IDevice
public class UsbHidDevice : DeviceBase
{
    private readonly HidDevice _hid;
    public UsbHidDevice(int id, string name, int vid, int pid) : base(id, name)
        => _hid = HidDevices.Enumerate(vid, pid).FirstOrDefault()
           ?? throw new InvalidOperationException("未找到 HID 设备");

    public override void Connect()
    {
        _hid.Open();
        _hid.Inserted += (s, e) => State = DeviceState.Online;
        _hid.Removed += (s, e) => State = DeviceState.Offline;
        _hid.ReadReport(OnReport);          // 异步等报告
        State = DeviceState.Online;
    }
    public override void Disconnect() => _hid.Close();

    private void OnReport(HidReport report)
    {
        var d = report.Data;                // 固定长度字节数组
        if (d[0] == 0x01)                   // 约定：报告ID=1 是温度
            RaiseData(1, (d[1] << 8) | d[2] / 10.0);
        _hid.ReadReport(OnReport);          // 继续等下一包（循环）
    }
    public override double Read(int addr) => 0;
    public override void Write(int addr, double value)
    {
        var outBuf = new byte[_hid.Capabilities.FeatureReportLength];
        outBuf[0] = 0x02; outBuf[1] = (byte)value;  // 发控制命令
        _hid.Write(outBuf);
    }
}
```
**逐行白话**：
- `HidDevices.Enumerate(vid,pid)`：按"厂商ID+产品ID"找到那台仪器（不用管它插哪个 USB 口）。
- `ReadReport(OnReport)`：异步等仪器"交表"，收到就进 `OnReport`。
- `report.Data`：那一包固定长度的字节，`d[0]` 是报告类型，`d[1]d[2]` 是数值。
- `Write(outBuf)`：给仪器发一包控制指令（如"开始采样"）。

**④ 没有真实仪器怎么练（沿用 T2 替代方案）**
- 用 **HID 调试助手**（如"USB HID 调试工具"）当"假仪器"，手动发 `01 00 FA` 看上位机收到 25.0。
- 或写 `SimulatedHidChannel`（同 Day1 的 `SimulatedCanChannel` 套路），测试里回包。

### 🔬 掰开揉碎：VID/PID 是 HID 的"身份证"
串口靠"COM3 这个端口号"找设备——换了个 USB 口变 COM4 就找不到了。HID 靠 **VID（厂商ID）+ PID（产品ID）** 在全球唯一标识一台设备，插哪个口都一样能认。这就是为什么仪器爱用 HID：**稳定识别、免驱、即插即用**。

### ⭐ 重点 / 🔥 坑
| | 内容 |
|---|---|
| ⭐ 重点 | HID 免驱、用 VID/PID 找设备、固定长度 Report |
| 🔥 坑 | Report 长度必须按设备 Capabilities 填，错了写不进 |
| 🔥 坑 | 异步 ReadReport 要"收到后继续 ReadReport"维持循环 |
| 🔥 坑 | d[0] 报告类型约定别和设备文档冲突 |

### 🟢 基础题
列出 USB-HID 和串口在"你怎么找到设备"上的区别（提示：HID 用 VID/PID，串口用 COM 号）。

### 🟡 进阶题
在 `OnReport` 里，`d[0]==0x01` 表示温度。若仪器改约定 `d[0]==0x02` 是压力(单位 0.1kPa)，写一行把压力转成 kPa 的 `RaiseData` 调用。

### 🔴 挑战题
写 `SimulatedHidChannel` 模拟仪器：构造时存一组"假报告"，`ReadReport` 回调里回包；用 xUnit 验证 `UsbHidDevice` 能正确解出温度和压力两个点——零硬件测通整条链路。

**✅ 答案（进阶题）**
`if (d[0] == 0x02) RaiseData(2, ((d[1]<<8)|d[2]) / 10.0);`

**🏗️ 项目任务**：把 `UsbHidDevice` 接进 DAQ Monitor（真实工程已有文件，见文末），用 `SimulatedHidChannel` 跑通测试，证明"USB 仪器也能进采集系统"。

**🎓 工控导师说**：USB-HID 最爽的是"免驱即插即用"——但坑在 **Report 长度**。设备规定 64 字节一包，你发 65 字节直接失败，发 63 字节可能被补零或拒收。永远先读 `Capabilities.FeatureReportLength` 再填缓冲，别凭感觉写长度。

**💼 职业建议**：医疗/实验室/精密仪器行业，USB-HID 是主流。会 HID 通信 + 能用 IDevice 抽象接入，是这类细分行业岗位的明确加分项，JD 常和"串口/Modbus"并列点名"USB"。

**✅ 打卡[ ]**

---

## 📌 温故知新 / 跨模块联动
- **M1**（串口）：CAN/USB 的"模拟通道"套路直接复用 M1 的 `LoopbackSerialChannel` 思想——**真实硬件不可达时，先模拟打通逻辑**。
- **M2**（Modbus）：CAN 没有"地址+功能码"，只有 ID+数据；理解差异才能不混用。
- **M9**（面向接口/DI）：`ICanChannel`/`IHidChannel` 抽象让"换硬件不换业务"，是 M9 分层架构的真实落地。
- **M12**（工程量）：`raw/10.0` 就是 M12 的线性标定，跨模块复用。
- **M15**（调试工具）：CAN 用 CANalyst 协议分析仪、USB 用 HID 调试助手，正是 M15 Day3 的工具。

## 🧩 完整代码组装（CAN/USB 设备，已真写进 DAQMonitor 工程）
```csharp
// src/DaqMonitor.Core/Devices/CanDevice.cs + ICanChannel.cs + SimulatedCanChannel.cs
// src/DaqMonitor.Core/Devices/UsbHidDevice.cs + IHidChannel.cs + SimulatedHidChannel.cs
// src/DaqMonitor.Core/Health/DeviceHealthMonitor.cs（M9 容错 + M15 心跳重连落地）
```
> **本模块的设备代码已真写进 DAQMonitor 工程**（2026-07-23 工程代码落地轮次），不是只讲不练：
> - `CanDevice`/`ICanChannel`/`SimulatedCanChannel`（CAN 设备实现 IDevice，零硬件可测）
> - `UsbHidDevice`/`IHidChannel`/`SimulatedHidChannel`（USB-HID 设备实现 IDevice）
> - `DeviceHealthMonitor`（心跳探活 + 退避重连，本模块设备也可被它守护）
> - 配套单测：`CanDeviceTests` / `UsbHidDeviceTests` / `DeviceHealthMonitorTests`（共 6 个，全绿）
> - 组合根 `Bootstrapper.cs` 已加 CAN/USB/心跳的注册示例注释。
>
> 真实硬件形态：把 `SimulatedXxxChannel` 换成 PCANChannel（CAN）或 HidLibrary 实现的 `IHidChannel`（USB），业务类一行不动——和 SerialDevice/LoopbackSerialChannel 同一套路。

## 🔗 明日预告（路线收尾）
**M10 报表（历史聚合 + Excel/PDF 导出）**：把 M4 存的历史库、M5 曲线、M6 报警收口成"能交付客户的报表"。至此 M0→M16 主线 + 广度全部串完，你的 DAQ Monitor 成为**能演示、能交付、覆盖 13K→15K 主流 JD** 的简历项目。下一步：写简历初稿 + 投递。

## 📚 延伸阅读
- CAN 总线 · [维基百科](https://zh.wikipedia.org/wiki/控制器區域網絡)
- HidLibrary · [GitHub](https://github.com/mikeobrien/HidLibrary)
- PCAN-Basic · [PEAK-System](https://www.peak-system.com/)

## 模块交付清单（M16）
- [ ] 能口述 CAN 与 Modbus 的核心区别（广播ID vs 主从问答）
- [ ] 写/读懂 `CanDevice` + `SimulatedCanChannel` 并跑通测试
- [ ] 能说清 USB-HID 为什么免驱、怎么用 VID/PID 找设备
- [ ] 写/读懂 `UsbHidDevice`（HidLibrary）并理解 ReadReport 循环
- [ ] 完成 Day1/Day2 练习
