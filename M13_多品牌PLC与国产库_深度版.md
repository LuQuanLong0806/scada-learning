# M13 — 多品牌 PLC 与国产库（HslCommunication）

> **优先级定位**：🟡 缓学 · 多品牌 PLC + 国产库 HslCommunication（国内企业超常用，先把 S7 吃透）
> **技术来源**：🟧 `HslCommunication`（国产全能，国内工厂最常用）、`S7netplus`（西门子）、各品牌原生协议。
> **给简历加的能力**：不止西门子——三菱/欧姆龙也能对接；用一套 Hsl 库通吃多数设备，比分别学 NModbus+S7.Net 省事。
> **前置**：M2（Modbus）、M3（西门子 S7）、M0（IDevice 抽象）。
> **前端类比总纲**：Hsl 像"一个 axios 封装打所有后端"——不同品牌只是不同 baseURL，调用姿势统一。

---

> ⏱️ **阅读路径**(按时间预算选入口)
> - **3 分钟**:看「模块目标」— 知道 HslCommunication 是国产全能库
> - **30 分钟**:加看 Day 1 三菱/欧姆龙/西门子统一调用姿势
> - **3 小时**:全文精读 + Day 2 Hsl 试用期机制 + 选型对比
> - 🎯 **面试高频**:Hsl vs S7.Net + NModbus(一个全能 vs 多个专精)/ Hsl 试用期授权 / 国产库选型理由
> - 🔁 **配套复习**:[间隔重复表](记忆与复习机制_间隔重复版.md)

> 📚 **前置语法**(M13 用到的,陌生请查 [C# 语法速查 — 前端视角](CSharp语法速查_前端视角.md))
> - `class MitsubishiPlcDevice : IDevice` / `class OmronPlcDevice : IDevice` — 多实现,速查 §12
> - `abstract class PlcDeviceBase : IDevice` — 抽象基类(模板方法)
> - `OperateResult result = plc.Read("D100")` — Hsl 库返回类型(成功/失败合一)
> - `switch (brand) { case "Mitsubishi": ... case "Omron": ... }` — 多品牌分支
> - `event EventHandler<DataReceivedEventArgs>? DataReceived` — 统一事件契约

> 📦 **前置类型**(本模块示例代码用到的核心自定义类型)
> M13 示例引用 `IDevice` / `SensorPoint` / `PlcDeviceBase`(本模块新建抽象基类)等 — 其中 `IDevice` / `SensorPoint` 在 [📦 前置类型定义 · 学员粘贴版](前置类型定义_学员粘贴版.md) **集中定义**。**遇到"找不到类型 XXX"报错,先去那份文档复制对应类型**,在项目里建 `_PredefinedTypes.cs` 粘进去就能跑。本模块还会**新建** `PlcDeviceBase` 抽象类 + `MitsubishiPlcDevice` / `OmronPlcDevice` 子类。

## 模块目标
用 `HslCommunication` 封装一个 `PlcDevice : IDevice`，支持西门子/三菱/欧姆龙统一读点；理解"为什么国内爱 Hsl"，并能讲清多品牌地址差异这一最大坑。

---

## Day 1 — HslCommunication 一把梭 🟡

### 一句话讲清楚
`HslCommunication` 一个库覆盖：Modbus、西门子、三菱 MC、欧姆龙 FINS、扫码枪、仪表……国产工程师友好、中文文档、API 统一。上位机岗位"国产库"几乎必提。

### 前端类比秒懂
| Hsl | 前端类比 |
|---|---|
| 一个库通吃多协议 | 一个 `axios` 封装打所有后端 |
| 不同品牌 = 不同客户端实例 | 不同 baseURL 的 request 实例 |

### 分点精讲

> 🔧 **必装 NuGet**(在 `src/DaqMonitor.Core/` 目录执行):
> ```bash
> dotnet add package HslCommunication
> ```
> 💡 HslCommunication 是国产全能工业通信库,**一个 NuGet 包覆盖西门子/三菱/欧姆龙/Modbus** 等,中文文档完善。

**① 读西门子**（🟧，替代 M3 的 S7.Net）
```csharp
using HslCommunication.Profinet.Siemens;
var plc = new SiemensS7Net(SiemensPLCS.S1200, "192.168.0.1");
var read = plc.ReadFloat("DB1.DBD0");   // 读实数
```
**② 读三菱 MC**（🟧）
```csharp
using HslCommunication.Profinet.Melsec;
var mc = new MelsecMcNet("192.168.0.2", 5000);
var v = mc.ReadInt16("D100");
```
**③ 读欧姆龙 FINS**（🟧）
```csharp
using HslCommunication.Profinet.Omron;
var om = new OmronFinsNet("192.168.0.3", 9600);
var v = om.ReadInt16("D100");
```
**④ 统一封装成 IDevice**
```csharp
// 抽象出 ReadPoints()，内部按配置选西门子/三菱/欧姆龙实现
// Bootstrapper 注册 PlcDevice，DAQ Monitor 不知具体品牌
```

### 🔬 掰开揉碎：地址表示差异是最大坑
- 西门子：`DB1.DBD0`（数据块.双字.字节偏移）
- 三菱：`D100`（数据寄存器）
- 欧姆龙：`D100` 但底层是 FINS 节点号 + 内存区
- **同一"温度"在不同 PLC 地址写法完全不同**——用配置把"业务点名"映射到"设备地址"，别把地址硬写进代码。

### ⭐ 重点 / 🔥 坑
| | 内容 |
|---|---|
| ⭐ 重点 | Hsl 一套库通吃多品牌，国内岗位高频 |
| 🔥 坑 | 地址格式写错（D100 vs DB1.DBW0）、字节序、超时重试 |
| 🔥 坑 | 不同品牌"读多个变量"的 API 形态不同，要查对应文档 |
| 🔥 坑 | 欧姆龙 FINS 要配节点号，连不上先查节点号对不对 |

### 🟢 基础题
用 Hsl 读三菱 D100 的 Int16（一行代码即可）。

### 🟡 进阶题
写一个 `PlcDevice` 配置类：含 `Brand`(Siemens/Melsec/Omron)、`Ip`、`Address`，根据 `Brand` 选择 new 哪个 Hsl 客户端——让"换 PLC 品牌只改配置"。

### 🔴 挑战题
把 M3 的 `PlcDevice`（西门子 S7.Net）和今天的 Hsl `PlcDevice` 都实现同一个 `IDevice` 接口，在 `Bootstrapper` 用配置切换；写测试用 Moq/模拟断言"无论哪个品牌，DataReceived 都抛出统一的 SensorPoint"。

**✅ 答案（基础题）**
`new MelsecMcNet(ip,port).ReadInt16("D100")`。

**🏗️ 项目任务**：写 `PlcDevice : IDevice` 支持多品牌配置（用 Hsl），注册进 `Bootstrapper`，DAQ Monitor 不关心背后是西门子还是三菱。

**🎓 工控导师说**：国内工厂"非标"设备极多，今天对接三菱、明天欧姆龙是常态。用 Hsl 一把梭的最大价值不是"少学几个库"，而是**你的采集层只认 `IDevice`，换品牌只改配置文件里的一行 `Brand=Siemens`**。现场最怕的是"每个品牌写一套代码、地址散落各处、换人就不会维护"。

**💼 职业建议**：简历里写"熟悉 HslCommunication 对接多品牌 PLC"比"会用 S7.Net"含金量高——它暗示你"懂国内工业现场的真实环境"。面试被问"三菱和西门子地址怎么不一样"，能讲出"D100 vs DB1.DBD0 + 用配置映射业务点名"就是加分项。

**✅ 打卡[ ]**

---

## Day 2 — 多品牌协议要点 + 选型 🟡

### 一句话讲清楚
三大主流 PLC 协议各有形态：三菱 MC 帧、欧姆龙 FINS/EIP、西门子 S7。原生库更轻，Hsl 开发更快——岗位里 Hsl 最常见，但你要懂"差异在哪、为什么"。

### 分点精讲
**① 三菱 MC 协议**：串行/以太网 MC 帧，命令字 + 软元件(D/M/R)。
**② 欧姆龙 FINS / EIP**：FINS 帧经 UDP/TCP；EIP 工业以太网。
**③ 原生 vs Hsl**：原生更轻、依赖少；Hsl 开发快、覆盖广。岗位里 Hsl 最常见。
**④ 选型心法**：客户指定品牌用对应原生（依赖最小）；自研产品/快速交付用 Hsl（一套代码通吃）。

### 🔬 掰开揉碎：为什么国内特别爱 Hsl
国外上位机教材多讲 S7.Net/OPC；但国内工厂"八国联军"——西门子、三菱、欧姆龙、汇川、台达混用。Hsl 是**国产工程师写的、中文文档、一个 NuGet 包覆盖主流协议**，对国内现场极度友好。学了它，你对接新设备几乎不用再学新库。

### 🟢 基础题
列出西门子/三菱/欧姆龙读"温度"时各自的地址写法差异。

### 🟡 进阶题
比较"原生 S7.Net"和"Hsl 的 SiemensS7Net"读同一个 DB1.DBD0，代码差异在哪？各适合什么场景？

### 🔴 挑战题
设计一个"协议适配器注册表"：按键 `Brand` 返回对应的 `IPlcClient` 工厂，新增一个品牌（如汇川）只需加一个适配器类——体会"开闭原则"（对扩展开放、对修改封闭）。

**✅ 答案（进阶题要点）**
- 原生 `S7.Net`：`new Plc(CpuType.S71200, ip, 0, 1).Read(...)`，更轻、依赖少、需懂 S7 细节。
- Hsl：`new SiemensS7Net(SiemensPLCS.S1200, ip).ReadFloat(...)`，API 更统一、中文文档、多品牌一套写法。
- 场景：客户强约束/极致轻量→原生；多品牌/快速交付→Hsl。

**🏗️ 项目任务**：在 DAQ Monitor 里建"协议适配器注册表"，把 M3 的 S7.Net 和今天的 Hsl 都挂上去，配置驱动切换品牌。

**🎓 工控导师说**：别迷信"原生一定比国产好"。在工业现场，"能最快对接客户那一堆杂牌设备、还不出 bug"才是真本事。Hsl 这种国产库就是为这个场景生的——用对了是利器，硬凹"只用原生"反而拖慢交付。

**💼 职业建议**：能说清"原生 vs 国产库取舍 + 多品牌地址差异 + 配置驱动切换"的候选人，在接"非标自动化"项目的公司极受欢迎——这类公司占了国内工控岗位的多数。

**✅ 打卡[ ]**

---

## 📌 温故知新 / 跨模块联动
- **M3**：西门子 S7.Net → M13 多品牌；**IDevice** 抽象让换 PLC 品牌 = 换实现。
- **M2**：Modbus 也被 Hsl 一并覆盖，Hsl 是"协议全家桶"。
- **M9**：配置驱动切换品牌 = M9 DI 注册切换，工程素养落地。

## 🧩 完整代码组装（多品牌 PlcDevice 骨架，可直接抄进工程）

> 📂 两个文件: `DaqMonitor.Core/Models/Brand.cs`(枚举) + `DaqMonitor.Core/Devices/PlcDevice.cs`(实现) · namespace `DaqMonitor.Core.Devices`
> 🔧 `dotnet add package HslCommunication`(在 `src/DaqMonitor.Core/`)
> 💡 继承 `DeviceBase`(前置类型定义) + 用 Hsl 的 `IReadWriteNet` 统一接口

```csharp
// ① DaqMonitor.Core/Models/Brand.cs —— 品牌枚举,先建这个文件
namespace DaqMonitor.Core.Models;

public enum Brand { Siemens, Melsec, Omron }
```

```csharp
// ② DaqMonitor.Core/Devices/PlcDevice.cs —— 多品牌 PLC 设备
using System;
using HslCommunication;
using HslCommunication.Profinet.Melsec;
using HslCommunication.Profinet.Omron;
using HslCommunication.Profinet.Siemens;
using DaqMonitor.Core.Models;

namespace DaqMonitor.Core.Devices;

public class PlcDevice : DeviceBase
{
    private readonly IReadWriteNet _client;   // Hsl 的统一读写接口
    private readonly string _addr;            // 主轮询点位地址(如 "D100")
    private CancellationTokenSource? _cts;

    public PlcDevice(int id, string name, Brand brand, string ip, string addr = "D100") : base(id, name)
    {
        _addr = addr;
        _client = brand switch
        {
            Brand.Siemens => new SiemensS7Net(SiemensPLCS.S1200, ip),
            Brand.Melsec   => new MelsecMcNet(ip, 5000),
            Brand.Omron    => new OmronFinsNet(ip, 9600),
            _ => throw new ArgumentOutOfRangeException(nameof(brand))
        };
    }

    public override void Connect()
    {
        // Hsl 多数"随读即连",这里启动后台轮询,数据走事件推送(与 SimulatedDevice 同套路)
        _cts = new CancellationTokenSource();
        _ = PollLoop(_cts.Token);
        State = DeviceState.Online;
    }

    public override void Disconnect()
    {
        _cts?.Cancel();
        State = DeviceState.Offline;
    }

    private async Task PollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var r = _client.ReadFloat(_addr);   // 真实项目按点位配置表映射多地址
                if (r.IsSuccess)
                    RaiseData(Id, r.Content);       // RaiseData(pointId, value)
                else
                    State = DeviceState.Offline;    // 失败不抛,交心跳重连
            }
            catch { /* 通信错交给 M9 心跳重连 */ }
            await Task.Delay(500, ct);
        }
    }

    public override double Read(int addr)
        => _client.ReadFloat(_addr).Content;   // 简化:真实项目缓存 _last 字典

    public override void Write(int addr, double value)
        => _client.Write(_addr, (float)value);
}
```
> 接进工程：`services.AddSingleton<IDevice>(_ => new PlcDevice(2, "PLC-多品牌", Brand.Melsec, "192.168.0.2"));`，UI 零改。

## 🔗 明日预告
**M14 WinForm 与 自定义控件**：前面 DAQ Monitor 用 WPF，但真实工厂 90% 老项目是 WinForm，且"自绘仪表/趋势控件"是 JD 点名的硬技能。M14 让你双修 + 讲透你项目里已有的 GaugeControl。

## 📚 延伸阅读
- HslCommunication · [官方文档(中文)](https://github.com/dathlin/HslCommunication)
- 三菱 MC 协议 · [手册](https://www.mitsubishielectric.com/)

## 📎 关联
- 多设备接入总览：**M1 串口 / M2 Modbus / M3 西门子 / M11 TCP / M13 多品牌**。
