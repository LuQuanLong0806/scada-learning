# DAQ Monitor ⚡ 工业数据采集监控系统

> **上位机转行 13-15K 项目驱动实战**:一个项目覆盖 C# / WPF / EF Core / 串口 / Modbus / PLC / TCP / MQTT / 工业协议,JD 全部硬通货。
> **当前状态**:W1+W2+W3 必做完成,56 测试通过,简历可投 13K。

---

## 🎨 UI 原型(开发前先画图,避免改 UI 改代码)

完整 5 张原型图(主界面/报警历史/报表导出/设备管理/配方管理)+ Excalidraw/Figma 实操指南见 **[docs/原型图设计指南.md](docs/原型图设计指南.md)**。

### 主界面(实时监控)— 经典 3 段式布局

```
┌──────────────────────────────────────────────────────────────────┐
│ ⚡ DAQ Monitor v1.0    [● 采集中]    2026-08-04 14:32:15   admin  │ ← Header 50px
├────────┬───────────────────────────────────────────┬─────────────┤
│ 📊 实时 │  📍 实时曲线(60s 滚动)                │ ⚠ 实时报警  │
│ 📅 历史 │  ┌─────────────────────────────────────┐ │ ─────────── │
│ 🔔 报警 │  │ 80 ┤       ╱╲                          │ │ 🔴 严重 2   │
│ 🔧 设备 │  │ 60 ┤     ╱   ╲     ╱╲                  │ │ PLC.T>90℃  │
│ 📋 报表 │  │ 40 ┤   ╱       ╲ ╱   ╲                │ │ 🔴 严重 1   │
│ ⚙ 设置 │  │ 20 ┤ ╱                 ──              │ │ TCP.P<1MPa │
│        │  │  0 └──────────────────────              │ │ 🟡 警告 3   │
│ ────  │  └─────────────────────────────────────┘ │ ─────────── │
│ 设备   │                                           │ 📋 操作日志 │
│ 状态:  │  📍 实时点位表                          │ 14:32 PLC 联│
│ 🟢 PLC │  ┌─────────────────────────────────────┐ │ 14:31 启动   │
│ 🟢 Mod │  │ 点位          值      状态  趋势      │ │ 14:30 Modbus │
│ 🟡 TCP │  │ PLC.DB1.DBD4  72.5℃   🟢    ↗        │ │             │
│ 🟢 Ser │  │ Modbus.Addr1  68.3℃   🟢    →        │ │             │
│        │  │ TCP.Point1    1.2MPa  🔴    ↗        │ │             │
│ 4 在线 │  └─────────────────────────────────────┘ │             │
│ 1 警告 │                                           │             │
├────────┴───────────────────────────────────────────┴─────────────┤
│ CPU 12% │ 内存 145MB │ 已采 12,348 点 │ 上云 ✅ │ [启动采集]    │ ← Footer 30px
└──────────────────────────────────────────────────────────────────┘
```

**5 大设计原则**(每条都有道理,面试官必问):
1. **信息密度优先** — 操作工一眼看完全部关键数据,不滚动
2. **报警颜色严格** — 🔴严重 / 🟡警告 / 🟢正常 / ⚪离线,IEC 60073 标准
3. **盲操作友好** — 关键按钮 > 48px,图标+文字双表达(色盲也能识别)
4. **7×24 不疲劳** — 深色模式优先(夜班),高对比,无闪烁动画
5. **状态可追溯** — 当前值 + 趋势曲线 + 历史报警 3 件套并排

---

## 🏗️ 架构(分层 + 单向依赖)

```
┌────────────────────────────────────────────┐
│  DaqMonitor.UI (WPF)                       │  ← 展示层
│  └─ MainWindow.xaml / ViewModels / Views   │
└──────────────┬─────────────────────────────┘
               │ (引用)
┌──────────────▼─────────────────────────────┐
│  DaqMonitor.Core (类库)                    │  ← 领域层(不依赖 UI)
│  ├─ Devices/      IDevice 抽象 + 7 实现    │
│  ├─ Acquisition/  Channel<T> 生产消费      │
│  ├─ Store/        EF Core + SQLite 双写    │
│  ├─ Alarms/       回滞报警引擎             │
│  ├─ Cloud/        MQTT 双向上云            │
│  ├─ Protocol/     CRC16 + 帧解析器         │
│  ├─ Engineering/  工程量标定               │
│  ├─ Reporting/    ClosedXML Excel          │
│  ├─ Resilience/   Retry 指数退避           │
│  └─ Diagnostics/  性能计数器               │
└────────────────────────────────────────────┘
               ↑ (测试)
┌──────────────┴─────────────────────────────┐
│  DaqMonitor.Tests (xUnit)                  │  ← 测试层
│  └─ 56 测试通过                             │
└────────────────────────────────────────────┘
```

**关键设计**:
- **Core 不依赖 UI** — 换 WPF→Blazor→控制台都不影响核心逻辑
- **IDevice 抽象** — 7 种设备(Simulated/Serial/Modbus/PLC/TCP/CAN/HID)统一接口
- **Channel<T> 生产消费** — 采集线程 → Channel → UI 线程,解耦 + 背压
- **双写一致性** — 内存索引(实时查询)+ SQLite(历史查询)

---

## 🚀 如何运行

### 前置
- [.NET 8 SDK](https://dotnet.microsoft.com/)
- Visual Studio 2022(勾选「.NET 桌面开发」)或 VS Code + C# Dev Kit

### 三步跑起来
```bash
git clone https://github.com/LuQuanLong0806/scada-learning.git
cd scada-learning/DAQMonitor
dotnet build DaqMonitor.sln                # 编译,0 错误
dotnet test                                 # 56/56 测试通过
dotnet run --project src/DaqMonitor.UI      # 启动 WPF,点"启动采集"
```

看到「实时点位表」跳动 + 偶发报警 = 跑通。

---

## 📦 已落地功能(对应学习模块)

| 模块 | 功能 | 实现位置 |
|---|---|---|
| **M0** | 分层骨架 + 并发闭环 | 整个 sln 结构 |
| **M1** | 串口通信 | `Devices/SerialDevice.cs` |
| **M2** | Modbus RTU/TCP | `Devices/ModbusDevice.cs` + `Protocol/Crc16.cs` |
| **M3** | PLC(S7) | `Devices/PlcDevice.cs`(S7.Net) |
| **M4** | 数据持久化(双写) | `Store/PointStore.cs` + `AppDbContext.cs` |
| **M5** | 实时可视化 | `UI/Views/ChartView.xaml`(LiveCharts2) |
| **M6** | 报警引擎 + Serilog | `Alarms/AlarmEngine.cs`(回滞机制) |
| **M7** | MQTT 双向上云 | `Cloud/MqttPublisher.cs`(MQTTnet 4.3.7) |
| **M9** | DI + 测试 + Retry | `AppServices/Bootstrapper.cs` + `Resilience/Retry.cs` |
| **M9.5** | 性能压测 | `Diagnostics/DiagnosticsService.cs` |
| **M11** | TCP 自定义协议 | `Devices/TcpDevice.cs` + `Protocol/TcpFrameParser.cs` |
| **M12** | 工程量转换 | `Engineering/EngineeringConverter.cs` |
| **M16** | CAN + USB-HID | `Devices/CanDevice.cs` + `UsbHidDevice.cs` |
| **M17** | MES 对接(选做) | 待 Day 20 落地 |
| **M18** | 配方管理(选做) | 待 Day 21 落地 |
| **M19** | 调试能力 | 跨模块,Serilog + TraceId |

---

## 🧪 测试覆盖(56 个,全绿)

```
 dotnet test → 已通过! - 失败: 0,通过: 56,已跳过: 0,总计: 56
```

覆盖:PointStore 持久化 / AlarmEngine 回滞 / Crc16 / FrameParser / ModbusFrameParser / TcpFrameParser 粘包 / Retry / EngineeringConverter / AcquisitionPipeline / 心跳监测 / CAN / USB-HID / 串口粘包半包坏 CRC。

---

## 🛠️ 技术栈

| 类别 | 技术 | 版本 |
|---|---|---|
| 运行时 | .NET | 8 (LTS) |
| UI | WPF | .NET 8 |
| MVVM | CommunityToolkit.Mvvm | 8.x(后续 Day 12-14 切 Prism) |
| ORM | EF Core + SQLite | 8.x |
| 实时图表 | LiveCharts2 | 2.x |
| 日志 | Serilog | 3.x |
| Modbus | NModbus4 | 3.x |
| PLC | S7.Net Plus | 1.x |
| MQTT | MQTTnet | 4.3.7 |
| Excel | ClosedXML | 0.10x |
| 测试 | xUnit + Moq | latest |

---

## 📚 相关文档

- [UI 原型图设计指南](docs/原型图设计指南.md) — 5 张原型 + Excalidraw/Figma 实操
- [30 天作战路线](../30天作战路线_转行冲刺版.md) — 每日学习+项目+代码肌肉打卡
- [代码肌肉训练手册](../代码肌肉训练手册_30天刷题版.md) — 60 道手写题题库
- [面试逐字稿 30 题](../面试问答_逐字稿_30题.md) — 10 大主题标准答
- [简历模板](../简历模板_上位机_13-15K.md) — 13K/14-15K/15K+ 三档

---

## 📝 License

MIT — 学习项目,自由使用。
