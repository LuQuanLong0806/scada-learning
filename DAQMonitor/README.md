# DAQ Monitor（工业数据采集监控系统）

上位机转行 13K~15K 的**项目驱动**实战项目。一个项目覆盖：C# / WPF / 串口 / Modbus / PLC(S7) / 数据库 / TCP / 异步 / 工程化，全部 13K 岗位要求。

## 架构（分层，从 M0 就立好）
- `src/DaqMonitor.Core` —— 领域层：模型（`SensorPoint`/`DeviceState`）、设备统一接口 `IDevice`、点位存储 `PointStore`。**不依赖 UI**。
- `src/DaqMonitor.UI` —— 展示层：WPF 主窗口。引用 Core。

后续模块往里填空：M1 串口、M2 Modbus、M3 多设备、M4 PLC、M5 存储、M6 图表/报警、M7 工程化。

## 如何运行（Day 7 起）
1. 安装 [.NET 8 SDK](https://dotnet.microsoft.com/) 与 Visual Studio 2022（勾选「.NET 桌面开发」）。
2. 用 VS 打开 `DaqMonitor.sln`，或直接：
   ```bash
   dotnet build DaqMonitor.sln
   dotnet run --project src/DaqMonitor.UI
   ```
3. 点「启动采集」按钮，文本框出现「DAQ Monitor 启动」即成功。

## 进度
- [x] M0 脚手架 + C# 热身（Day 1–7）
- [ ] M1 串口通信
- [ ] M2 Modbus
- [ ] M3 TCP/Socket 多设备
- [ ] M4 PLC（S7）
- [ ] M5 数据存储
- [ ] M6 实时可视化 + 报警
- [ ] M7 企业级工程化
- [ ] M8 求职冲刺
