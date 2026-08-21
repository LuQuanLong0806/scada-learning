# -*- coding: utf-8 -*-
"""
DAQ Monitor 学习站点生成器
================================
把本目录下的各模块/附录 Markdown 转成一套多页 HTML 站点：

  site/
    index.html              # 首页：六大分区入口卡 + 使用说明
    sections/<id>.html      # 分类页：start/modules/projects/practice/career/reference
    assets/site.css         # 共享样式
    assets/site.js          # 打卡(localStorage，按页独立)
    modules/M0.html ...     # 每个模块/附录一个独立页面
    README.md               # 站点说明
  文档内写 [📖 标题](kp:<id>) 可生成知识点弹窗（KPOINTS 字典定义内容）

用法：
  python build_site.py
Markdown 源文件是「单一事实来源」，改完 md 重新跑本脚本即可重建整站。
"""
import io, re, os
import markdown

BASE = os.path.dirname(os.path.abspath(__file__))
SITE = os.path.join(BASE, "site")
ASSETS = os.path.join(SITE, "assets")
MODDIR = os.path.join(SITE, "modules")
os.makedirs(ASSETS, exist_ok=True)
os.makedirs(MODDIR, exist_ok=True)

# ---------------------------------------------------------------------------
# 站点元数据：每个页面 = 一张卡片 + 一个独立 HTML
# kind: "module" 主线模块(参与 M0->M10 顺序) / "support" 工具箱文档
# ---------------------------------------------------------------------------
PAGES = [
    dict(slug="prep", kind="prep", file="零基础前置_教练带练版.md",
         title="零基础前置 · 教练带练（先看这篇）", sub="看不懂文档？从这里开始",
         what="上位机是啥/C#为什么/串口Modbus黑话破冰/字节换算/第一个程序/设备数据怎么变数字", src="🟦 带练入门"),
    dict(slug="M0",  kind="module", file="M0_每日讲义_深度版.md",
         title="M0 · C#/.NET 热身 + 工程骨架", sub="C# 核心 + WPF + 并发",
         what="工程骨架 + 后台采集闭环", src="🟦 C# 语法 · 🟩 .NET 类库"),
    dict(slug="M1",  kind="module", file="M1_串口通信_深度版.md",
         title="M1 · 串口通信", sub="SerialPort 真实/虚拟串口",
         what="真实/虚拟串口设备接入", src="🟩 System.IO.Ports"),
    dict(slug="M2",  kind="module", file="M2_Modbus_深度版.md",
         title="M2 · Modbus RTU/TCP", sub="工业标准协议",
         what="读写保持寄存器/线圈", src="🟧 NModbus4"),
    dict(slug="M3",  kind="module", file="M3_PLC_深度版.md",
         title="M3 · PLC 通信(西门子 S7)", sub="直连 PLC",
         what="直连 PLC（13K 硬通货）", src="🟧 S7.Net"),
    dict(slug="M4",  kind="module", file="M4_数据持久化_深度版.md",
         title="M4 · 数据持久化", sub="EF Core + SQLite",
         what="历史库 + 查询/导出", src="🟧 EF Core + SQLite"),
    dict(slug="M5",  kind="module", file="M5_实时可视化_深度版.md",
         title="M5 · 实时可视化", sub="LiveCharts2",
         what="动态曲线/仪表盘", src="🟧 LiveCharts2"),
    dict(slug="M6",  kind="module", file="M6_报警引擎日志_深度版.md",
         title="M6 · 报警引擎 + 日志", sub="阈值规则 + Serilog",
         what="报警 + 通知 + 日志", src="🟦 语法 · 🟧 Serilog"),
    dict(slug="M7",  kind="module", file="M7_OPCUA_MQTT_深度版.md",
         title="M7 · OPC UA / MQTT 上云", sub="对接 SCADA（15K 加分）",
         what="上云 / 对接 SCADA", src="🟧 OPC UA / MQTT"),
    dict(slug="M8",  kind="module", file="M8_工程化收尾_深度版.md",
         title="M8 · 工程化收尾", sub="MVVM + 安装包 + 简历",
         what="MVVM 重构 + 安装包", src="🟧 + 🟦 MVVM/安装包"),
    dict(slug="M9",  kind="module", file="M9_工程素养_测试DI容错_深度版.md",
         title="M9 · 工程素养", sub="测试 / DI / 统一采集 / 容错",
         what="单测+DI+统一采集+容错（13K→15K 分水岭）", src="🟧 + 🟦 测试/DI/容错"),
    dict(slug="M10", kind="module", file="M10_报表_深度版.md",
         title="M10 · 报表", sub="聚合 + 可视化 + 导出",
         what="聚合+可视化+Excel/PDF 导出", src="🟦 + 🟧 报表/ClosedXML"),
    dict(slug="M11", kind="module", file="M11_TCP_Socket_深度版.md",
         title="M11 · TCP Socket 自定义协议", sub="粘包/拆包 + 私有帧",
         what="对接非 Modbus 的私有 TCP 设备（JD 必会）", src="🟩 System.Net.Sockets"),
    dict(slug="M12", kind="module", file="M12_工程量转换与多数据库_深度版.md",
         title="M12 · 工程量转换 + 企业数据库", sub="量程标定 + SQL Server/MySQL",
         what="原始值→工程量 + 企业级数据库持久化", src="🟦 标定 · 🟧 EF/Dapper"),
    dict(slug="M13", kind="module", file="M13_多品牌PLC与国产库_深度版.md",
         title="M13 · 多品牌 PLC + 国产库", sub="HslCommunication 一把梭",
         what="三菱/欧姆龙 + Hsl 通吃多品牌设备", src="🟧 HslCommunication"),
    dict(slug="M14", kind="module", file="M14_WinForm与自定义控件_深度版.md",
         title="M14 · WinForm + 自定义控件", sub="双修 + 自绘仪表",
         what="维护老 WinForm + 自绘工业控件（JD 点名）", src="🟩 WinForms · 🟦 自绘"),
    dict(slug="M15", kind="module", file="M15_工程协作与联调_深度版.md",
         title="M15 · 工程协作与联调", sub="Git + 敏捷 + 调试工具",
         what="Git/敏捷/示波器逻辑分析仪/联调定位（初级岗硬要求，之前0覆盖）", src="⚫ 工程协作"),
    dict(slug="M16", kind="module", file="M16_更多工业总线_CAN_USB_深度版.md",
         title="M16 · 工业总线 CAN + USB-HID", sub="JD 点名的另两类通信",
         what="CAN 广播总线 + USB-HID 仪器通信（实现 IDevice 接项目）", src="🟡 工业总线"),
    dict(slug="M17", kind="module", file="M17_工业安全与MES对接_深度版.md",
         title="M17 · 工业安全 + MES 对接", sub="HttpClient + IT/OT 分层",
         what="MES REST API + Polly + JWT + 白名单 + 审计 + IEC 62443 思想", src="🟧 HttpClient/Polly"),
    dict(slug="M18", kind="module", file="M18_配方管理_深度版.md",
         title="M18 · 配方管理", sub="工艺参数版本化（制药/食品必点）",
         what="Recipe + 版本化 + 审计 + 回滚 + ISA-88 思想", src="🟦 配方"),
    dict(slug="M19", kind="module", file="M19_问题排查与调试_深度版.md",
         title="M19 · 问题排查与调试", sub="VS 调试器 + dotnet dump + Wireshark",
         what="条件断点/数据断点/内存泄漏/性能/物理工具/Serilog TraceId（13→15K 硬要求）", src="🟧 调试工具链"),
    dict(slug="M8.5", kind="module", file="M8.5_Prism企业级MVVM_深度版.md",
         title="M8.5 · Prism 企业级 MVVM", sub="模块化 / Region / EventAggregator",
         what="PrismBootstrapper + RegionManager + EventAggregator + DialogService（JD 高频）", src="🟧 Prism"),
    dict(slug="M9.5", kind="module", file="M9.5_性能压测与长跑稳定性_深度版.md",
         title="M9.5 · 性能压测 + 长跑稳定性", sub="BenchmarkDotNet + dotnet counters",
         what="BenchmarkDotNet + dotMemory + 4 类内存泄漏 + CircuitBreaker（15K 亮点）", src="🟧 性能工具链"),
    # ---- 转行冲刺专项（support）----
    dict(slug="traps", kind="support", file="C#_陷阱_前端转上位机必看_深度版.md",
         title="⚠️ C# 陷阱 · 前端转上位机必看", sub="8 大坑 + 前端类比",
         what="struct 值拷贝 / 多线程 / 字节序 / async void / IDisposable / event / P-Invoke（前端必踩）", src="🔴 Day 1 必读"),
    dict(slug="csharp-syntax", kind="support", file="CSharp语法速查_前端视角.md",
         title="📚 C# 语法速查 · 前端视角", sub="15 大主题 + JS/TS 对照 + 速查地图",
         what="var/new()/List/Dictionary/out/event/async/LINQ 全覆盖,5 年前端转 C# 的字典,遇到陌生语法随时查", src="📚 随身字典"),
    dict(slug="predefined-types", kind="support", file="前置类型定义_学员粘贴版.md",
         title="📦 前置类型定义 · 学员粘贴版", sub="遇到编译报错找不到类型 → 来这里",
         what="SensorPoint/DeviceBase/IDevice/AlarmRule/AcquisitionPipeline/PointStat 等核心类型集中定义,粘贴即可编译", src="📦 代码不报错"),
    dict(slug="roadmap30", kind="support", file="30天作战路线_转行冲刺版.md",
         title="📅 30 天作战路线 · 转行冲刺版", sub="代码练习融入日常",
         what="Day 1-30 每日学习+项目+代码肌肉打卡（W1 主干/W2 落库可视化/W3 TCP上云/W4 简历面试）", src="⭐ 主轴"),
    dict(slug="muscle", kind="support", file="代码肌肉训练手册_30天刷题版.md",
         title="💪 代码肌肉训练手册 · 30 天刷题版", sub="白板手写题库",
         what="20 翻译 + 30 手写 + 10 Debug 题，每天 1h 白板，形成肌肉记忆", src="🔴 每天 1h"),
    dict(slug="audit-v2", kind="support", file="审计_整体串联复核_v2.md",
         title="🔍 整体串联审计 v2 · 由粗到细", sub="阶段→模块→知识点",
         what="4 周作战路线 + 22 模块 + 抽样知识点审计，Top 10 必修问题清单", src="🟦 审计"),
    dict(slug="audit-v3", kind="support", file="审计_30天路线_v3_2026-08-10.md",
         title="🔍 30 天路线第二轮审计 v3 · 连贯性/正确性/深度", sub="4 维并行审计报告",
         what="P0 闭环断裂修复(4 处) + P1 路径修正 + MQTT/EF Core 概念补强 + P2 讲义缺口清单(M2/M3/M4 多线程)", src="🟦 审计"),
    dict(slug="jd-research", kind="support", file="JD调研_13-15K上位机岗位对照.md",
         title="📊 JD 调研 · 13-15K 上位机岗位对照", sub="必会/高频/加分/罕见 4 档",
         what="薪资地图 + 4 档技能矩阵 + 3 个典型 JD 样本 + 缺口 Top 10 + 投递策略", src="🟦 调研"),
    dict(slug="resume", kind="support", file="简历模板_上位机_13-15K.md",
         title="📄 简历模板 · 上位机 13-15K", sub="3 版本 + STAR + 投递策略",
         what="13K/14-15K/15K+ 三档简历模板 + 项目讲法 STAR + 关键词镜像 + 反问 3 问", src="🟦 简历"),
    dict(slug="interview30", kind="support", file="面试问答_逐字稿_30题.md",
         title="🎤 面试问答 · 逐字稿 30 题", sub="10 大主题 × 3 题",
         what="C#/并发/Modbus/PLC/EF/Prism/TCP/报警/性能/调试 各 3 题，标准答+逐字稿", src="🟦 面试"),
    dict(slug="design-qa40", kind="support", file="面试问答_项目设计决策40问.md",
         title="🏗️ 项目设计决策 · 40 问", sub="面试官的\"为什么\"题库",
         what="两项目 38 个设计决策集中背诵版：备选方案→为什么选它→不这样会怎样，8 大分区 40 问", src="🟦 面试"),
    dict(slug="spaced-repetition", kind="support", file="记忆与复习机制_间隔重复版.md",
         title="🔁 记忆与复习机制 · 间隔重复版", sub="1d/3d/7d/15d 表 + 每周自测 120 题",
         what="艾宾浩斯曲线应用 + 30 天排程 + 每周闭卷自测 + Anki 卡片建议 + 错题本规范", src="🟦 复习机制"),
    dict(slug="webview2-vue-dashboard", kind="support", file="前端×上位机差异化冲刺_WebView2与工业大屏.md",
         title="⚡ 前端 × 上位机差异化冲刺", sub="WebView2 + Vue3 大屏 5-7 天方案",
         what="5 结合方向对比 + Day 26-27 落地代码 + 简历话术 + 面试 10 题 + 踩坑警告", src="🟦 差异化王牌"),
    # ---- 工具箱（support）----
    dict(slug="getting-started", kind="support", file="实操入门_从零搭建企业级工程.md",
         title="实操入门 · 从零搭建企业级工程", sub="从最简单的做起",
         what="文件夹命名 + dotnet CLI 建工程 + git + build/run/test", src="🟦 基础操作"),
    dict(slug="practice-ladder", kind="support", file="练习阶梯_从简单到企业级.md",
         title="练习阶梯 · 从简单到企业级", sub="L1 操作 / L2 代码 / L3 集成",
         what="每个模块拆成三级练习，终点=能跑的企业项目", src="🟦 训练法"),
    dict(slug="hardware", kind="support", file="硬件替代方案与讲解_深度版.md",
         title="附录 · 硬件替代方案与讲解", sub="没硬件怎么练 + 硬件科普",
         what="RS232/485、4-20mA、PLC/DB块、抗干扰 + 四层替代方案", src="🟧 硬件"),
    dict(slug="links", kind="support", file="外部链接索引.md",
         title="附录 · 外部链接索引", sub="难点官方文档汇总",
         what="每模块「📚 延伸阅读」指向的权威链接，卡点直接点开", src="🟦 外链"),
    dict(slug="lib-guide", kind="support", file="速查_工业通讯调库指南.md",
         title="🧰 速查 · 工业通讯调库指南", sub="手搓之外，生产怎么干",
         what="FluentModbus/NModbus4/S7netplus/HSL 选型与用法 + 沙盒验证代码 + 手搓 vs 调库决策", src="🧰 调库"),
    dict(slug="job", kind="support", file="真实岗位调研_13-15K技能对照.md",
         title="真实岗位调研 · 13-15K 技能对照", sub="对标 BOSS 等招聘要求",
         what="真实企业 13-15K 技能要求与本路线的对应关系", src="🟦 调研"),
    dict(slug="audit", kind="support", file="审计_15K完整性复核.md",
         title="质量复核 · 15K 完整性复核", sub="5 维审计结论",
         what="对全部讲义的完整性/连贯性/易懂性/难点/外链复核", src="🟦 复核"),
    dict(slug="audit-quality", kind="support", file="审计_讲义质量评估.md",
         title="质量审计 · 讲义质量评估", sub="自评与改进项",
         what="讲义整体质量的自评结论与待改进清单", src="🟦 审计"),
    dict(slug="jobmap", kind="support", file="岗位驱动_知识点全景图谱与缺口审计.md",
         title="岗位驱动 · 知识点全景图谱与缺口审计", sub="对照真实 JD 查遗漏",
         what="初级→资深中级知识点分级 + 逐项覆盖审计 + 新发现遗漏(CAN/USB/Git/调试工具/Prism)", src="🟦 审计"),
    dict(slug="docs", kind="support", file="文档能力_设计协议操作测试文档_深度版.md",
         title="文档能力 · 设计/协议/操作/测试文档", sub="交付硬要求",
         what="4 类交付文档模板 + DAQ Monitor 范例 + 练习", src="🟦 交付物"),
    dict(slug="license", kind="support", file="依赖与授权_免费开源清单.md",
         title="依赖与授权 · 免费开源清单", sub="付费框架 + 免费替代",
         what="本项目依赖全免费；行业付费框架(DevExpress/Halcon/NI等)的免费替代与自研边界", src="🟦 清单"),
    dict(slug="wpf", kind="support", file="WPF_XAML_速查_深度版.md",
         title="WPF / XAML 入门速查（前端类比版）", sub="前端转上位机必看",
         what="XAML≈JSX、Binding≈v-model、Template≈render、DependencyProperty≈响应式状态", src="🟩 WPF 生态"),
    dict(slug="roadmap-dev", kind="support", file="项目开发全景_每一步是什么为什么_深度版.md",
         title="项目开发全景 · 每一步是什么/为什么", sub="现成 vs 自研 一眼懂",
         what="按真实成长顺序拆解 20 步：做什么/为什么/直接用 vs 自己开发 + 阅读顺序", src="🟦 全景"),
    dict(slug="proj2", kind="support", file="项目二_协议调试助手_规划与路线.md",
         title="项目三 · CommLab 协议调试助手（规划）", sub="第 3 份简历作品",
         what="与 DAQMonitor 互补的调试工具：复用 IDevice/帧解析，新增报文编辑/从站模拟/抓包回放", src="🟦 规划"),
    dict(slug="interview", kind="support", file="面试高频知识点_速记卡.md",
         title="面试高频知识点 · 速记卡", sub="手机刷 · 23 题",
         what="13–15K 高频题 Q&A：架构/MVVM/并发/串口/Modbus/粘包/工程量/DB/报警/OPC UA/控件/DI/单测/容错/排查/简历讲法", src="🟦 速记"),
    # ---- 项目实践（projects）----
    dict(slug="proj-daq", kind="module", file="项目实践_DaqMonitor_00_索引.md",
         title="DAQMonitor · 项目实践总入口", sub="像入职一样边工作边学习",
         what="学完基础直接开工:需求单→自己开发→卡了看参考实现→知识点弹窗回讲义。R0-R8 从零长出完整采集监控系统",
         src="🏗️ 需求实践", kicker="🏗️ 项目实践"),
    dict(slug="proj-r0", kind="module", file="项目实践_DaqMonitor_R0_需求总纲.md",
         title="R0 · 需求总纲", sub="背景/用户故事/架构全景",
         what="项目背景、用户故事、功能架构图、19 子系统全景表(哪些进 R1-R8、哪些 R9+)、里程碑计划与环境前置检查",
         src="🏗️ 需求实践", kicker="🏗️ 项目实践"),
    dict(slug="proj-r1", kind="module", file="项目实践_DaqMonitor_R1_工程骨架与领域模型.md",
         title="R1 · 工程骨架 + 领域模型", sub="sln 三项目 + Models",
         what="slnx 三项目结构(Core/UI/Tests)、RollForward 防坑、领域模型四件套(SensorPoint/DeviceState/AlarmLevel/Alarm)",
         src="🏗️ 需求实践", kicker="🏗️ 项目实践"),
    dict(slug="proj-r2", kind="module", file="项目实践_DaqMonitor_R2_设备抽象与模拟设备.md",
         title="R2 · 设备抽象 + 模拟设备", sub="IDevice 全家 + 首批测试",
         what="IDevice 同步门面/DataEventArgs/DeviceBase/SimulatedDevice(10% 越限触发报警),流程测试起步 2 个",
         src="🏗️ 需求实践", kicker="🏗️ 项目实践"),
    dict(slug="proj-r3", kind="module", file="项目实践_DaqMonitor_R3_协议解析层.md",
         title="R3 · 协议解析层", sub="CRC/帧解析/Modbus",
         what="Crc16 查表、AA55 帧解析状态机、ModbusFrameParser(字节序 ABCD/CDAB)、TcpFrameParser 粘包处理,纯逻辑 13 测试",
         src="🏗️ 需求实践", kicker="🏗️ 项目实践"),
    dict(slug="proj-r4", kind="module", file="项目实践_DaqMonitor_R4_真实设备接入.md",
         title="R4 · 真实设备接入", sub="串口/Modbus/TCP/CAN/USB",
         what="ISerialChannel 抽象 + Loopback/Serial 通道,SerialDevice/ModbusDevice/TcpDevice/PlcDevice/CanDevice/UsbHidDevice 全家",
         src="🏗️ 需求实践", kicker="🏗️ 项目实践"),
    dict(slug="proj-r5", kind="module", file="项目实践_DaqMonitor_R5_采集管道与报警引擎.md",
         title="R5 · 采集管道 + 报警引擎", sub="Channel 批量 + 回滞报警",
         what="AcquisitionPipeline(Channel 缓冲+200ms 批量+构造即启动)、AlarmEngine(回滞+边沿触发)、EngineeringConverter 量程标定",
         src="🏗️ 需求实践", kicker="🏗️ 项目实践"),
    dict(slug="proj-r6", kind="module", file="项目实践_DaqMonitor_R6_持久化双写落库.md",
         title="R6 · 持久化双写落库", sub="EF Core + SQLite",
         what="SensorRecord 实体、AppDb(R6 版)、PointStore 内存索引+Channel 串行落库双写、TestDb 临时文件工厂,50 测试",
         src="🏗️ 需求实践", kicker="🏗️ 项目实践"),
    dict(slug="proj-r7", kind="module", file="项目实践_DaqMonitor_R7_组装诊断与容错.md",
         title="R7 · 组装诊断与容错", sub="DI 组合根 + 重试 + 健康监测",
         what="Bootstrapper 一处注册全站注入、Retry 指数退避+抖动、DeviceHealthMonitor 探活重连、DiagnosticsService 环形日志,56 测试",
         src="🏗️ 需求实践", kicker="🏗️ 项目实践"),
    dict(slug="proj-r8", kind="module", file="项目实践_DaqMonitor_R8_WPF主界面.md",
         title="R8 · WPF 主界面", sub="MVVM 主屏(主屏先行)",
         what="App DI 启动、MainViewModel+RelayCommand、GaugeControl/StatusDot 自定义控件、LiveCharts2 曲线、诊断面板+手动验收清单",
         src="🏗️ 需求实践", kicker="🏗️ 项目实践"),
    dict(slug="proj-mc1", kind="module", file="项目实践_MotionControl_MC1_工程骨架与模拟卡.md",
         title="MC1 · 骨架与模拟卡", sub="net8 迁移/IMotionCard/两轴并发仿真",
         what="net8 工程三件套 + IMotionCard 接口(对齐真卡 SDK 返回码)+ MockMotionCard:每轴 CancellationToken、急停就地冻结、回零、软限位、报警链路",
         src="🏗️ 需求实践", kicker="🏗️ 项目实践"),
    dict(slug="proj-mc2", kind="module", file="项目实践_MotionControl_MC2_卡的行为测试.md",
         title="MC2 · 卡的行为测试", sub="12 个测试钉死全部行为",
         what="tickMs 快进 10 倍 + WaitUntil 轮询:两轴并发、短距离定位、急停冻结、软限位、回零、报警各有一个测试作证,4 秒跑完的回归防线",
         src="🏗️ 需求实践", kicker="🏗️ 项目实践"),
    dict(slug="proj-mc3", kind="module", file="项目实践_MotionControl_MC3_WinForms主界面.md",
         title="MC3 · WinForms 主界面", sub="控件数组/集中刷新/跨线程事件",
         what="两轴控件收数组循环订阅(闭包坑)、按钮状态集中 RefreshUiState 单一真源、卡事件 InvokeRequired+BeginInvoke、定时器边沿检测运动完成、输入防呆自愈、工业配色",
         src="🏗️ 需求实践", kicker="🏗️ 项目实践"),
    dict(slug="proj-mc4", kind="module", file="项目实践_MotionControl_MC4_UI冒烟验收.md",
         title="MC4 · UI 冒烟验收", sub="STA 线程 + 消息泵全流程冒烟",
         what="STA 线程创建真实窗体、Application.DoEvents 手动泵消息,连接→双轴并发→定位→报警→急停→断开全流程跑一遍,跨线程错误当场红(13/13)",
         src="🏗️ 需求实践", kicker="🏗️ 项目实践"),
    dict(slug="proj-mc5", kind="module", file="项目实践_MotionControl_MC5_两轴直线插补.md",
         title="MC5 · 两轴直线插补(可选)", sub="等比推进/一停俱停",
         what="MoveLinear:共用步数 + 等比分步 = 空间直线;所有参与轴共享一个令牌 = 急停一停俱停;速度语义取最远轴;含中段比例断言测试(14/14)",
         src="🏗️ 需求实践", kicker="🏗️ 项目实践"),
    dict(slug="proj-mc6", kind="module", file="项目实践_MotionControl_MC6_轨迹可视化.md",
         title="MC6 · 轨迹可视化(可选)", sub="自绘控件/坐标轴/轨迹图",
         what="自定义 TrajectoryPanel:数据绘制分离、双缓冲、mm→像素等比例映射(Y 轴翻转)、定时器两轴同拍采样防锯齿 —— 插补画出笔直斜线,急停红点冻结",
         src="🏗️ 需求实践", kicker="🏗️ 项目实践"),
    dict(slug="mistakes", kind="support", file="错题本_查漏补缺.md",
         title="📕 错题本 · 查漏补缺", sub="不熟的/易错的,登记+详解+自测",
         what="活页错题册:报出不懂的点 → 固定格式详解(总纲/项目实物/前端类比/易错点/自测) → 分区登记,复习节奏与状态标记", src="🏗️ 需求实践", kicker="🏗️ 项目实践"),
]

# ---------------------------------------------------------------------------
# 分区：首页只做导航枢纽，具体内容进各分区页 site/sections/<id>.html
# ---------------------------------------------------------------------------
SECTION_OF = {
    "start":     ["prep", "getting-started", "roadmap30", "roadmap-dev"],
    "modules":   ["M0", "M1", "M2", "M3", "M4", "M5", "M6", "M7", "M8",
                  "M8.5", "M9", "M9.5", "M10", "M11", "M12", "M13", "M14",
                  "M15", "M16", "M17", "M18", "M19"],
    "projects":  ["proj-daq", "proj-r0", "proj-r1", "proj-r2", "proj-r3", "proj-r4",
                  "proj-r5", "proj-r6", "proj-r7", "proj-r8",
                  "proj-mc1", "proj-mc2", "proj-mc3", "proj-mc4", "proj-mc5", "proj-mc6",
                  "mistakes", "proj2"],
    "practice":  ["practice-ladder", "muscle", "spaced-repetition"],
    "career":    ["jd-research", "resume", "interview30", "design-qa40", "job", "jobmap",
                  "interview", "webview2-vue-dashboard"],
    "reference": ["traps", "csharp-syntax", "predefined-types", "wpf", "hardware",
                  "links", "lib-guide", "docs", "license", "audit", "audit-quality", "audit-v2", "audit-v3"],
}
_slug_to_section = {}
for _sec, _slugs in SECTION_OF.items():
    for _s in _slugs:
        _slug_to_section[_s] = _sec
for p in PAGES:
    p["section"] = _slug_to_section[p["slug"]]   # 未登记的 slug 会 KeyError，防止漏分

# md 互链按“文件名(去 .md) → 页面 slug”重写(页面文件按 slug 命名,与源 md 文件名不一定相同)
_FILE_TO_SLUG = {p["file"][:-len(".md")]: p["slug"] for p in PAGES}

# 模块讲义分区内再分两组（分区页内两个 h2）
for p in PAGES:
    if p["section"] == "modules":
        p["group"] = "核心必学（M0 → M8）" if p["slug"] in (
            "M0", "M1", "M2", "M3", "M4", "M5", "M6", "M7", "M8") else "进阶扩展（M8.5 → M19）"

# 项目实践分区分四组：总入口 → 项目一(DaqMonitor R0-R8) → 模拟运控 → 储备规划
_DAQ_RS = {f"proj-r{i}" for i in range(9)}
for p in PAGES:
    if p["section"] == "projects":
        s = p["slug"]
        if s == "proj-daq":
            p["group"] = "从这里开始 · 怎么做项目"
        elif s in _DAQ_RS:
            p["group"] = "项目一 · DaqMonitor 数据采集（R0 → R8，跟着需求单开发）"
        elif s in {f"proj-mc{i}" for i in range(1, 7)}:
            p["group"] = "项目二 · 模拟运动控制（WinForms · MC1 → MC6）"
        else:
            p["group"] = "储备 · 下一份简历作品（规划）"

SECTIONS = [
    dict(id="start", emoji="🚀", title="入口 · 路线",
         desc="第一次来从这里进：零基础带练 → 实操入门 → 30 天作战路线 → 项目开发全景。"),
    dict(id="modules", emoji="📚", title="模块讲义",
         desc="M0 → M19 共 22 个深度讲义：核心必学 9 个（基础/串口/Modbus/PLC/落库/可视化/报警/上云/收尾）+ 进阶扩展 13 个。"),
    dict(id="projects", emoji="🏗️", title="项目实践",
         desc="学完基础直接做项目：拿需求单像入职一样开发，卡了看参考实现，知识点点开弹窗回讲义重学。"),
    dict(id="practice", emoji="🔁", title="练习 · 复习",
         desc="练习阶梯 + 代码肌肉每日 1h + 间隔重复复习表。"),
    dict(id="career", emoji="💼", title="求职冲刺",
         desc="JD 调研 → 简历 → 面试逐字稿/速记卡 → 薪资路线图。"),
    dict(id="reference", emoji="📖", title="速查 · 参考",
         desc="C# 语法速查 / WPF 速查 / 前置类型定义 / 陷阱清单 / 硬件替代 / 外链索引 / 质量审计。"),
]

# ---------------------------------------------------------------------------
# 知识点弹窗库：项目实践文档里写 [📖 标题](kp:<id>) → 页内弹窗 + 跳转对应讲义
# t=标题  d=摘要(html)  m=跳转模块 slug（可空）
# ---------------------------------------------------------------------------
KPOINTS = {
    "struct-vs-class": dict(t="struct vs class（值类型 vs 引用类型）",
        d="SensorPoint 用 struct：小而高频传递，赋值即拷贝，GC 零压力。SensorRecord 用 class：EF Core 实体需要引用语义。<b>前端类比</b>：struct ≈ primitive/值拷贝，class ≈ object/传引用。",
        m="M0"),
    "event-delegate": dict(t="event / EventHandler 事件机制",
        d="设备采集到数据 → <code>RaiseData</code> 触发 <code>DataReceived</code> → 订阅方（管道/UI）收到通知。<b>前端类比</b>：EventEmitter 的 on/emit，但类型安全、只允许 +=/-= 订阅。",
        m="M0"),
    "idevice": dict(t="IDevice 设备统一抽象",
        d="所有设备（串口/Modbus/PLC/TCP/CAN…）实现同一接口：同步门面 Connect/Disconnect/Read/Write + DataReceived 事件。上层只认 IDevice，换品牌设备 = 换实现类，采集层零改动——这就是面向接口 + 可插拔。",
        m="M0"),
    "sync-facade": dict(t="为什么 IDevice 是同步门面（不是 async）",
        d="接口方法立即返回，真正的 IO/轮询在 Connect() 内部用 Task.Run 起后台循环。<b>面试标准答</b>：设备接口同步语义 + 内部异步实现，上层调用简单，下层不阻塞 UI。",
        m="M0"),
    "taskrun": dict(t="Task.Run / async-await",
        d="Task.Run 把活丢线程池；await 挂起方法不占线程。<b>前端类比</b>：Task ≈ Promise，await ≈ await，Task.WhenAll ≈ Promise.all。陷阱：不 .Result（死锁）、不 async void（异常炸进程）。",
        m="M0"),
    "dispatcher": dict(t="Dispatcher —— 后台线程回 UI 线程",
        d="WPF UI 只能 UI 线程改。后台事件里更新 ObservableCollection 前：<code>Application.Current.Dispatcher.Invoke(() => ...)</code>。<b>前端类比</b>：跨组件改状态要走同一个事件循环。",
        m="M0"),
    "channel": dict(t="Channel<T> 生产者-消费者",
        d="设备事件(100Hz) → Channel 缓冲 → 后台单消费者批量处理。为什么不用 ConcurrentQueue：Channel 是 async-native，没数据时 await 不占线程；高频采集把 CPU 从轮询空转里解放出来。面试高频题。",
        m="M9"),
    "batching": dict(t="定时/定量批量刷盘",
        d="AcquisitionPipeline：Channel 攒数据 + Timer 每 200ms 扫一批 → BatchReady 事件。逐条写库/刷 UI 会把系统打爆，批量是工业系统标配。",
        m="M9"),
    "di": dict(t="DI 依赖注入",
        d="Bootstrapper 一处注册（services.AddSingleton），构造函数到处接收。换实现只改注册一行，测试可注入 Mock。<b>前端类比</b>：Vue 的 provide/inject，但类型安全、生命周期托管。",
        m="M9"),
    "unit-test": dict(t="xUnit 单元测试",
        d="[Fact] 一个用例 / [Theory]+[InlineData] 参数化。Assert.Equal / Assert.Throws。测试是 15K 岗位硬通货：改代码不怕回归。",
        m="M9"),
    "cancel-token": dict(t="CancellationToken 协作式取消",
        d="可取消的异步循环写法:<code>await Task.Delay(ms, token)</code> —— token 被取消时在这里抛 OperationCanceledException,catch 住就地收尾。急停/松手/新指令打断旧任务全靠它。<b>前端类比</b>:AbortController + fetch 的 signal。",
        m=""),
    "winforms-invoke": dict(t="InvokeRequired / BeginInvoke 跨线程更新界面",
        d="WinForms 控件有线程亲和性:后台线程直接碰控件抛 InvalidOperationException。判 <code>InvokeRequired</code> 后用 <code>BeginInvoke(投递 lambda)</code> 切回 UI 线程 —— WPF 里对应 Dispatcher.Invoke/BeginInvoke。<b>前端类比</b>:跨组件改状态要回到主事件循环。",
        m=""),
    "moq": dict(t="Moq 模拟对象",
        d="Mock<IDevice> 造假设备：Setup 指定行为、Verify 断言调用、Raise 触发事件。没有硬件也能测采集逻辑。",
        m="M9"),
    "retry-backoff": dict(t="重试 + 指数退避",
        d="通信失败别裸抛：1s→2s→4s→8s 退避重试，上限后熔断报离线。工业现场网线被踢一脚是常态，系统要自己站起来。",
        m="M9"),
    "hysteresis": dict(t="回滞 Hysteresis —— 防报警风暴",
        d="阈值 100 时值在 99↔101 抖动会狂报警。回滞 = 触发 100 / 恢复 95，中间保持原状态。<b>类比</b>：空调 26° 停机、28° 才重启。生产报警必配。",
        m="M6"),
    "alarm-edge": dict(t="边沿触发 —— 只报一次",
        d="持续超阈值也只触发一次报警（记录 active 状态），恢复后才允许再触发。否则一秒刷几十条，值班员直接无视。",
        m="M6"),
    "crc": dict(t="CRC 校验",
        d="工业帧尾带 CRC16 校验码，收方重算比对——网线干扰改一个字节就能被发现。Modbus RTU 用 CRC16-Modbus 多项式。",
        m="M2"),
    "byte-order": dict(t="字节序 ABCD/CDAB",
        d="两个寄存器拼 float 时，不同设备字节顺序不同（西门子 ABCD、部分仪表 CDAB）。读出来数值离谱（如 10^38）先查字节序。",
        m="M2"),
    "modbus": dict(t="Modbus RTU 协议",
        d="工业事实标准：主问从答，功能码 03 读保持寄存器 / 06 写单寄存器，帧 = 从站地址+功能码+数据+CRC16。",
        m="M2"),
    "serial-frame": dict(t="串口帧协议 AA55",
        d="自定义协议帧：帧头 AA 55 + 长度 + 载荷 + CRC。串口是字节流，要靠帧头+长度+校验切出完整报文——和 TCP 粘包同一个问题。",
        m="M1"),
    "plc-s7": dict(t="S7 协议与 DB 块",
        d="西门子 PLC 通过 S7 协议按地址读 DB 块：DB1.DBD0 = 1号数据块内 0 偏移的 Double Word。地址写错是新手第一天坑。",
        m="M3"),
    "tcp-sticky": dict(t="TCP 粘包/拆包",
        d="TCP 是字节流没有消息边界，一次 Read 可能收到半条或两条半报文。解法：帧头+长度 的状态机解析器（FrameParser）。",
        m="M11"),
    "efcore": dict(t="EF Core + SQLite 持久化",
        d="DbContext 管 DbSet 映射，EnsureCreated 自动建表。domain 的 SensorPoint(struct) 不适合做实体 → 转成 SensorRecord(class) 落库——领域模型和持久化模型分离。",
        m="M4"),
    "dbfactory": dict(t="IDbContextFactory —— DbContext 线程安全",
        d="DbContext 非线程安全，多线程共用必炸。工厂模式每次给一个独立短命实例：await using var db = _factory.CreateDbContext()。",
        m="M4"),
    "dual-write": dict(t="内存索引 + 异步落库双写",
        d="PointStore：内存字典保证实时查询秒回，Channel 串行写 SQLite（满足单写者）。读走内存、写排队——工业采集标配架构。",
        m="M4"),
    "mvvm": dict(t="MVVM 模式",
        d="Model-View-ViewModel：XAML(View) 绑定 ViewModel 的属性/命令，VM 不认识 V。<b>前端类比</b>：ViewModel ≈ Vue 的 data+methods，Binding ≈ v-model，值通知 ≈ 响应式。",
        m="wpf"),
    "binding": dict(t="WPF 数据绑定",
        d="{Binding Temp}：UI 自动读 VM 属性。改了属性界面不动？—— 没实现 INotifyPropertyChanged（或没用 [ObservableProperty] 源生成）。前端转过来第一坑。",
        m="wpf"),
    "relaycommand": dict(t="ICommand / RelayCommand",
        d="把\"点击该干嘛\"变成可绑定的命令属性（CanExecute 控制按钮灰亮）。≈ useCallback 但可绑定、可测试。",
        m="wpf"),
    "livecharts": dict(t="LiveCharts2 实时曲线",
        d="ObservableValue/observable 集合驱动，数据变了曲线自动动。高频刷图要先聚合再喂（逐点刷会卡死），本项目用管道 BatchReady 批量喂。",
        m="M5"),
    "eng-scale": dict(t="工程量转换（量程标定）",
        d="AD 原始值(0~4095) → 物理量(4~20mA → 0~100℃)：线性公式 eng = raw*scale + offset。传感器标定错了，后面全错。",
        m="M12"),
}

# ---------------------------------------------------------------------------
# 共享资源
# ---------------------------------------------------------------------------
CSS = """
:root{
  --blue:#2f6fed; --green:#1f9d55; --orange:#e07b00;
  --ink:#1f2430; --muted:#67708a; --line:#e6e9f0; --bg:#f7f8fb;
  --card:#fff; --soft:#eef3ff; --codebg:#1f2430;
}
*{box-sizing:border-box;}
body{margin:0;font-family:-apple-system,"Segoe UI","PingFang SC","Microsoft YaHei",sans-serif;color:var(--ink);background:var(--bg);line-height:1.7;}
a{color:var(--blue);text-decoration:none;}
a:hover{text-decoration:underline;}
code{background:#eef1f7;padding:1px 5px;border-radius:4px;font-size:13px;}
pre{background:var(--codebg);color:#e6e9f0;padding:14px;border-radius:8px;overflow:auto;}
pre code{background:none;color:inherit;padding:0;font-size:12.5px;line-height:1.55;}
table{border-collapse:collapse;width:100%;margin:14px 0;font-size:14px;}
th,td{border:1px solid var(--line);padding:7px 10px;text-align:left;vertical-align:top;}
th{background:#f1f4fa;}
blockquote{border-left:4px solid var(--blue);background:#f4f8ff;margin:14px 0;padding:10px 16px;color:#33405c;border-radius:0 6px 6px 0;}
ul li,ol li{margin:4px 0;}

/* ===== 顶栏 ===== */
.topbar{position:sticky;top:0;z-index:50;background:#fff;border-bottom:1px solid var(--line);
  padding:10px 20px;box-shadow:0 1px 6px rgba(0,0,0,.05);display:flex;align-items:center;gap:14px;}
.topbar .brand{font-weight:800;font-size:16px;color:var(--ink);}
.topbar .brand .em{color:var(--blue);}
.topbar .spacer{flex:1;}
.topbar .navlink{font-size:13px;color:var(--muted);}
.topbar .navlink:hover{color:var(--blue);}

/* ===== 首页 ===== */
.hero{background:linear-gradient(120deg,#eef3ff,#eafaf0);padding:34px 24px 26px;border-bottom:1px solid var(--line);}
.hero h1{margin:0 0 8px;font-size:27px;}
.hero p{margin:6px 0;color:#33405c;max-width:880px;}
.legend{background:#fff;border-bottom:1px solid var(--line);padding:10px 20px;font-size:13px;display:flex;flex-wrap:wrap;gap:8px;align-items:center;}
.badge{padding:2px 9px;border-radius:12px;font-weight:600;font-size:12px;}
.b-syntax{background:#e8f0ff;color:var(--blue);}
.b-bcl{background:#e6f6ec;color:var(--green);}
.b-nuget{background:#fdf0e0;color:var(--orange);}
.legend code{background:#f0f2f7;}
.mnem{width:100%;color:var(--muted);font-size:12px;margin-top:2px;}
.wrap{max-width:1180px;margin:0 auto;padding:26px 20px 60px;}
.section-title{font-size:19px;margin:28px 0 14px;padding-left:10px;border-left:4px solid var(--blue);}
.cards{display:grid;grid-template-columns:repeat(auto-fill,minmax(310px,1fr));gap:18px;}
.card{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:18px;display:block;
  transition:.15s;color:var(--ink);position:relative;overflow:hidden;}
.card:hover{transform:translateY(-3px);box-shadow:0 8px 22px rgba(47,111,237,.12);text-decoration:none;border-color:#cfe;}
.card .kicker{font-size:12px;color:var(--muted);font-weight:700;letter-spacing:.5px;}
.card h3{margin:4px 0 6px;font-size:18px;color:var(--ink);}
.card .sub{font-size:13px;color:var(--muted);margin:0 0 10px;}
.card .what{font-size:13.5px;color:#33405c;background:var(--soft);border-radius:8px;padding:8px 10px;margin:0 0 10px;}
.card .src{font-size:12px;color:var(--orange);font-weight:600;}
.card .arrow{position:absolute;right:14px;bottom:12px;color:var(--blue);font-size:18px;opacity:.5;}
.card.module .kicker{color:var(--blue);}
.card.support .kicker{color:var(--green);}
.card.prep{border:2px solid var(--blue);background:linear-gradient(120deg,#eef3ff,#eafaf0);}
.card.prep .kicker{color:var(--blue);}
.card.prep:hover{border-color:var(--green);box-shadow:0 8px 22px rgba(47,111,237,.18);}

/* 使用步骤 */
.steps{display:grid;grid-template-columns:repeat(auto-fill,minmax(230px,1fr));gap:14px;margin-top:6px;}
.step{background:#fff;border:1px solid var(--line);border-radius:10px;padding:14px;}
.step .n{display:inline-block;width:24px;height:24px;line-height:24px;text-align:center;background:var(--blue);color:#fff;border-radius:50%;font-size:13px;font-weight:700;margin-bottom:6px;}
.step b{display:block;margin-bottom:3px;}

/* 面试就绪路线图 */
.roadmap{margin:14px 0 6px;font-size:14px;}
.roadmap th{background:#eef3ff;color:var(--blue);}
.roadmap td{vertical-align:top;}
.roadmap .r-now td{background:#f4f6fa;}
.roadmap .r-min td{background:#fff7ec;}
.roadmap .r-stable td{background:#eef4ff;}
.roadmap .r-ideal td{background:#f3f0ff;}
.roadmap td b{color:var(--ink);}
.r-tip{background:#f4f8ff;border-left:4px solid var(--blue);padding:10px 16px;border-radius:0 6px 6px 0;color:#33405c;margin:10px 0 0;}

/* 薪资速查卡 */
.salcards{display:grid;grid-template-columns:repeat(auto-fill,minmax(300px,1fr));gap:16px;margin-top:8px;}
.salcard{background:#fff;border:1px solid var(--line);border-radius:12px;padding:16px 16px 18px;border-top:5px solid var(--blue);}
.salcard.s-min{border-top-color:var(--orange);}
.salcard.s-stable{border-top-color:var(--blue);}
.salcard.s-ideal{border-top-color:#7f77dd;}
.salcard .sal{font-size:23px;font-weight:800;color:var(--ink);line-height:1.2;}
.salcard .stage{font-size:13px;color:var(--muted);margin:3px 0 12px;}
.salcard .klabel{font-size:12px;color:var(--muted);font-weight:700;margin-bottom:7px;}
.chips{display:flex;flex-wrap:wrap;gap:6px;}
.chip{background:var(--soft);color:var(--blue);border-radius:10px;padding:3px 9px;font-size:12px;font-weight:600;}
.salcard.s-min .chip{background:#fff3e3;color:var(--orange);}
.salcard.s-ideal .chip{background:#f0edfd;color:#534ab7;}

/* ===== 模块页 ===== */
.layout{display:flex;align-items:flex-start;}
.toc{position:sticky;top:60px;width:250px;flex:none;max-height:calc(100vh - 70px);overflow:auto;
  padding:18px 12px;border-right:1px solid var(--line);background:#fff;}
.toc .tt{font-size:12px;color:var(--muted);font-weight:700;margin:0 0 8px;letter-spacing:.5px;}
.toc ul{list-style:none;margin:0;padding:0;font-size:13px;}
.toc .l2>a{font-weight:700;color:var(--ink);}
.toc .l3{margin-left:12px;}
.toc .l3>a{color:var(--muted);}
.toc a{display:block;padding:3px 6px;border-radius:6px;}
.toc a:hover{background:var(--soft);text-decoration:none;color:var(--blue);}
.content{flex:1;min-width:0;padding:22px 34px 80px;max-width:1000px;}
.content h1{font-size:25px;background:linear-gradient(90deg,#eef3ff,#eafaf0);padding:12px 16px;border-radius:8px;margin-top:6px;}
.content h2{font-size:21px;margin-top:34px;padding-top:10px;border-top:2px solid var(--line);}
.content h3{font-size:17px;margin-top:24px;color:var(--blue);}
.cb{display:inline-flex;align-items:center;gap:4px;font-size:13px;color:var(--green);font-weight:600;margin-left:6px;}
.cb input{width:16px;height:16px;cursor:pointer;}
.localbar{display:flex;align-items:center;gap:12px;background:#fff;border:1px solid var(--line);
  border-radius:10px;padding:10px 16px;margin:14px 0 4px;}
.barwrap{flex:1;height:12px;background:#eef1f7;border-radius:8px;overflow:hidden;}
#bar{height:100%;width:0;background:linear-gradient(90deg,var(--blue),var(--green));transition:width .3s;}
#ptext{font-size:13px;color:var(--muted);white-space:nowrap;}
.pager{display:flex;justify-content:space-between;gap:12px;margin-top:40px;}
.pager a{flex:1;background:#fff;border:1px solid var(--line);border-radius:10px;padding:12px 16px;color:var(--ink);}
.pager a:hover{text-decoration:none;border-color:var(--blue);background:var(--soft);}
.pager .pg-sub{font-size:12px;color:var(--muted);}
.pager .pg-t{font-weight:700;color:var(--blue);}
.pager .next{text-align:right;}
.foot{color:var(--muted);font-size:12px;text-align:center;padding:24px;}

/* 移动端目录抽屉按钮（桌面隐藏） */
.tocbtn{display:none;}

@media(max-width:820px){
  /* 顶栏紧凑 */
  .topbar{padding:8px 12px;gap:8px;}
  .topbar .brand{font-size:14px;}
  .topbar .navlink{font-size:12px;}
  .tocbtn{display:inline-block;margin-left:6px;padding:5px 10px;border:1px solid var(--line);
    border-radius:8px;font-size:13px;color:var(--blue);background:#fff;cursor:pointer;}
  /* 正文舒适字号、收敛间距 */
  body{font-size:15px;line-height:1.65;}
  .content{padding:16px 14px 70px;}
  .content h1{font-size:21px;padding:10px 12px;}
  .content h2{font-size:18px;}
  .content h3{font-size:16px;}
  /* 卡片/网格单列 */
  .cards{grid-template-columns:1fr;gap:12px;}
  .hero{padding:24px 16px 18px;}
  .hero h1{font-size:22px;}
  .wrap{padding:18px 14px 50px;}
  .steps{grid-template-columns:1fr;}
  .salcards{grid-template-columns:1fr;}
  /* 表格横向滚动，避免撑爆小屏 */
  table{display:block;width:100%;overflow-x:auto;-webkit-overflow-scrolling:touch;}
  pre{font-size:12px;}
  /* 打卡条换行 */
  .localbar{flex-wrap:wrap;}
  /* 目录变抽屉：从左侧滑出 + 遮罩 */
  .layout{display:block;}
  .toc{position:fixed;top:0;left:0;height:100%;width:82%;max-width:320px;z-index:100;
    transform:translateX(-100%);transition:transform .25s ease;box-shadow:2px 0 16px rgba(0,0,0,.15);}
  body.toc-open .toc{transform:translateX(0);}
  .toc-mask{display:none;position:fixed;inset:0;background:rgba(0,0,0,.35);z-index:99;}
  body.toc-open .toc-mask{display:block;}
  .pager{flex-direction:column;}
}

/* ===== 首页分区入口卡 ===== */
.entries{display:grid;grid-template-columns:repeat(auto-fill,minmax(320px,1fr));gap:18px;margin-top:6px;}
.entrycard{display:block;background:var(--card);border:1px solid var(--line);border-left:6px solid var(--blue);
  border-radius:14px;padding:20px 20px 16px;color:var(--ink);transition:.15s;}
.entrycard:hover{transform:translateY(-3px);box-shadow:0 10px 26px rgba(47,111,237,.14);text-decoration:none;border-color:#cfe;}
.entrycard .ico{font-size:30px;line-height:1;}
.entrycard h3{margin:8px 0 4px;font-size:20px;}
.entrycard .desc{font-size:13.5px;color:#33405c;margin:0 0 10px;}
.entrycard .cnt{font-size:12px;color:var(--muted);font-weight:700;}

/* ===== 分类页 ===== */
.sechero{background:linear-gradient(120deg,#eef3ff,#eafaf0);padding:30px 24px 20px;border-bottom:1px solid var(--line);}
.sechero h1{margin:0 0 8px;font-size:24px;}
.sechero p{margin:4px 0;color:#33405c;max-width:920px;}

/* ===== 知识点弹窗 ===== */
.kplink{border-bottom:1px dashed var(--blue);font-weight:600;cursor:pointer;}
.kpmask{display:none;position:fixed;inset:0;background:rgba(15,20,40,.45);z-index:200;}
.kpmodal{display:none;position:fixed;z-index:201;top:50%;left:50%;transform:translate(-50%,-50%);
  width:min(540px,92vw);max-height:78vh;overflow:auto;background:#fff;border-radius:14px;padding:22px 24px;
  box-shadow:0 18px 50px rgba(0,0,0,.25);}
.kpmodal .kptitle{font-size:19px;font-weight:800;margin:0 0 10px;color:var(--blue);}
.kpmodal .kpbody{font-size:14.5px;color:#28304a;}
.kpmodal .kpbody code{font-size:13px;}
.kplinkmore{margin:12px 0 0;}
.kpclose{position:absolute;top:8px;right:12px;border:none;background:none;font-size:24px;color:var(--muted);cursor:pointer;line-height:1;}
@media(max-width:820px){
  .entries{grid-template-columns:1fr;}
}
"""

JS = """
// 打卡：按页面独立存储(localStorage)
function key(slug,n){return 'daq_'+slug+'_day_'+n;}
function toggleDay(slug,n){
  var cb=document.querySelector('.daycb[data-day="'+n+'"]');
  if(cb.checked){localStorage.setItem(key(slug,n),'1');} else {localStorage.removeItem(key(slug,n));}
  syncAll(slug);
}
function syncAll(slug){
  var done=0,total=0;
  document.querySelectorAll('.daycb').forEach(function(cb){
    total++;
    var n=cb.getAttribute('data-day');
    var on=localStorage.getItem(key(slug,n))==='1';
    cb.checked=on; if(on)done++;
  });
  var pct=total?Math.round(done/total*100):0;
  var bar=document.getElementById('bar'); if(bar)bar.style.width=pct+'%';
  var pt=document.getElementById('ptext'); if(pt)pt.textContent='本页打卡 '+done+' / '+total+' （'+pct+'%）';
}
function toggleToc(){document.body.classList.toggle('toc-open');}
window.addEventListener('DOMContentLoaded',function(){
  // 页面 slug 由 body data-slug 提供
  var slug=document.body.getAttribute('data-slug')||'page';
  syncAll(slug);
  // 手机上点目录项后自动收起抽屉
  document.querySelectorAll('.toc a').forEach(function(a){
    a.addEventListener('click',function(){document.body.classList.remove('toc-open');});
  });
});
"""

LEGEND = """
<div class="legend">
  <b>📖 3 类技术来源（必认，不然云里雾里）：</b>
  <span class="badge b-syntax">🟦 C# 语法</span> 语言自带，装好 .NET 就有，<b>不装包</b>
  <span class="badge b-bcl">🟩 .NET 类库/BCL</span> 微软标准库，<code>using</code> 即用，<b>默认不装包</b>
  <span class="badge b-nuget">🟧 第三方 NuGet</span> 必须 <code>dotnet add package 包名</code>
  <span class="mnem">口诀：语法天生物 · BCL 随 .NET · 第三方要装包</span>
</div>"""

# ---------------------------------------------------------------------------
# Markdown -> HTML, 赋 id, 提取 TOC, 打卡复选框
# ---------------------------------------------------------------------------
md_engine = markdown.Markdown(extensions=["tables", "fenced_code", "sane_lists", "md_in_html"])

def convert(md_text, slug):
    md_engine.reset()
    # 规范首行 h1（若以 # 开头）为该页标题保持原样即可
    html = md_engine.convert(md_text)

    # 链接重写:站点内的 [X](Y.md) → 指向目标页的 slug.html(页面按 slug 命名,不一定等于文件名)
    # 不重写跨网络/外链(http://, https://, #anchor, mailto:, 等)
    html = re.sub(r'href="(?!https?://|mailto:|/)([^"]+?)\.md(#([^"]*))?"',
                  lambda m: 'href="%s.html%s"' % (
                      _FILE_TO_SLUG.get(m.group(1), m.group(1)),
                      '#' + m.group(3) if m.group(3) else ''),
                  html)

    # 知识点弹窗链接：md 里写 [📖 标题](kp:<id>) → 点击弹窗（内容见 KPOINTS）
    html = re.sub(r'href="kp:([A-Za-z0-9_-]+)"',
                  lambda m: ('href="#" class="kplink" data-kp="%s" '
                             'onclick="openKp(\'%s\');return false;"') % (m.group(1), m.group(1)),
                  html)

    nav = []
    ctr = {"1": 0, "2": 0, "3": 0}
    def repl_h(m):
        tag, content = m.group(1), m.group(2)
        lvl = tag[1]
        ctr[lvl] += 1
        cid = "sec-%s-%d" % (lvl, ctr[lvl])
        clean = re.sub(r"<[^>]+>", "", content)
        if lvl in ("2", "3"):
            nav.append((int(lvl), cid, clean))
        return '<%s id="%s">%s</%s>' % (tag, cid, content, tag)
    html = re.sub(r"<(h[123])>(.*?)</\1>", repl_h, html, flags=re.S)

    # 打卡复选框（按页 key）
    day = [0]
    def cb(m):
        day[0] += 1
        return ('<label class="cb"><input type="checkbox" class="daycb" data-day="%d" '
                'onchange="toggleDay(\'%s\',%d)"> 打卡</label>') % (day[0], slug, day[0])
    html = re.sub(r"打卡\s*\[\s*\]", cb, html)
    total = max(day[0], 0)

    # 构建页内 TOC
    toc = ['<p class="tt">本页目录</p><ul>']
    cur_h2 = None
    for lvl, cid, title in nav:
        if lvl == 2:
            if cur_h2 is not None:
                toc.append("</ul></li>")
            toc.append('<li class="l2"><a href="#%s">%s</a><ul>' % (cid, title))
            cur_h2 = cid
        else:
            toc.append('<li class="l3"><a href="#%s">%s</a></li>' % (cid, title))
    if cur_h2 is not None:
        toc.append("</ul></li>")
    toc.append("</ul>")
    return html, "\n".join(toc), total


def local_bar(total):
    if total <= 0:
        return ""
    return ('<div class="localbar"><div class="barwrap"><div id="bar"></div></div>'
            '<div id="ptext">本页打卡 0 / %d （0%%）</div></div>' % total)


# ---------------------------------------------------------------------------
# 页面模板
# ---------------------------------------------------------------------------
MODULE_TPL = """<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>%(title)s · DAQ Monitor 学习站</title>
<link rel="stylesheet" href="%(css)s">
</head>
<body data-slug="%(slug)s">
<div class="topbar">
  <a class="brand" href="../index.html"><span class="em">DAQ Monitor</span> · 学习站</a>
  <span class="spacer"></span>
  <button class="tocbtn" onclick="toggleToc()">☰ 目录</button>
  <a class="navlink" href="../sections/%(section)s.html">← 分类</a>
  <a class="navlink" href="../index.html">← 返回首页</a>
</div>
%(legend)s
%(localbar)s
<div class="toc-mask" onclick="toggleToc()"></div>
<div class="layout">
  <aside class="toc">%(toc)s</aside>
  <main class="content">
%(content)s
<div class="pager">
  %(prev)s
  %(next)s
</div>
  <div class="foot">打卡状态自动保存在浏览器本地（localStorage），按页独立。</div>
  </main>
</div>
<script src="%(js)s"></script>
%(kpblock)s
</body>
</html>"""

CAREER_EXTRA = """
  <h2 class="section-title">🗺️ 面试就绪路线图 · 学到哪能拿多少</h2>
  <p style="color:#33405c;max-width:920px;margin:0 0 4px;">你的 DAQMonitor 工程已经端到端跑通（采集·并发·曲线·历史·报警），这是转行最大的筹码。下面按「学到哪个阶段 → 对应岗位级别 → 预估薪资 → 还差什么」对照 —— 目标是<b>边学边投、面中补漏</b>，不用等"全学完"。</p>
  <table class="roadmap">
    <thead><tr><th>阶段</th><th>对应级别</th><th>预估薪资</th><th>还需掌握（对应模块）</th></tr></thead>
    <tbody>
      <tr class="r-now"><td><b>当前</b><br>工程已跑通</td><td>—（暂不直接面）</td><td>—</td><td>项目已能演示；缺 JD「必会」知识点</td></tr>
      <tr class="r-min"><td><b>最低可投线</b><br>保底起步</td><td>初级 / 助理上位机</td><td><b>10–13K</b></td><td>M11 TCP/Socket + M12 工程量转换 + 企业数据库</td></tr>
      <tr class="r-stable"><td><b>稳妥线</b><br>推荐目标</td><td>中级上位机</td><td><b>13–15K</b></td><td>+ M8 配置/更新/HandyControl + M10 报表</td></tr>
      <tr class="r-ideal"><td><b>理想线</b><br>加分拉满</td><td>中级偏上</td><td>13–15K 稳 / 冲 15K+</td><td>+ M13 多品牌PLC + M14 WinForm/控件 + 文档能力</td></tr>
    </tbody>
  </table>
  <p class="r-tip">💡 策略：<b>最快投简历 = 过完 M11 + M12</b>（JD 标"必会"的两块硬伤）。走到"稳妥线"就能冲你定的 13–15K。边投边学、面中补漏，比等全学完更省时间。</p>

  <h2 class="section-title">💰 薪资 ↔ 阶段 ↔ 知识点 · 一眼速查</h2>
  <p style="color:#33405c;max-width:920px;margin:0 0 4px;">同一张图看明白「拿多少工资 = 学到哪个阶段 = 要会哪些知识点」，按需对照补强。</p>
  <div class="salcards">
    <div class="salcard s-min">
      <div class="sal">10–13K</div>
      <div class="stage">阶段：最低可投线（保底起步）</div>
      <div class="klabel">需掌握知识点</div>
      <div class="chips">
        <span class="chip">M11 · TCP/Socket 自定义协议</span>
        <span class="chip">M12 · 工程量转换 / 量程标定</span>
        <span class="chip">M12 · 企业数据库 SQL Server / MySQL</span>
      </div>
    </div>
    <div class="salcard s-stable">
      <div class="sal">13–15K</div>
      <div class="stage">阶段：稳妥线（推荐目标）</div>
      <div class="klabel">在 10–13K 基础上再加</div>
      <div class="chips">
        <span class="chip">M8 · 配置实操（Ini/XML/JSON）</span>
        <span class="chip">M8 · 自动更新 / 部署</span>
        <span class="chip">M8 · HandyControl</span>
        <span class="chip">M10 · 报表聚合 + 导出</span>
      </div>
    </div>
    <div class="salcard s-ideal">
      <div class="sal">15K+</div>
      <div class="stage">阶段：理想线（加分拉满）</div>
      <div class="klabel">在 13–15K 基础上再加</div>
      <div class="chips">
        <span class="chip">M13 · 多品牌 PLC + 国产库</span>
        <span class="chip">M14 · WinForm + 自定义控件</span>
        <span class="chip">文档能力 · 设计/协议/操作/测试</span>
      </div>
    </div>
  </div>

  <h2 class="section-title">✅ 13~15K 简历项目「达标清单」</h2>
  <div class="cards">
    <div class="card support"><div class="kicker">达标项</div><h3>多设备接入</h3><div class="what">串口 / Modbus / PLC 至少两种（M1–M3）</div></div>
    <div class="card support"><div class="kicker">达标项</div><h3>并发采集</h3><div class="what">后台线程 + 队列缓冲 + UI 刷新，无卡顿无泄漏（M0 Day7）</div></div>
    <div class="card support"><div class="kicker">达标项</div><h3>实时可视化</h3><div class="what">动态曲线 / 仪表盘（M5）</div></div>
    <div class="card support"><div class="kicker">达标项</div><h3>数据持久化</h3><div class="what">历史库 + 查询/导出（M4 + M10）</div></div>
    <div class="card support"><div class="kicker">达标项</div><h3>报警</h3><div class="what">阈值规则 + 通知 + 日志（M6）</div></div>
    <div class="card support"><div class="kicker">达标项</div><h3>工程素养</h3><div class="what">分层/接口/MVVM/配置/异常/单测/DI/容错/打包（M0,M8,M9）</div></div>
    <div class="card support"><div class="kicker">加分</div><h3>上云</h3><div class="what">OPC UA / MQTT 对接 SCADA（M7）</div></div>
  </div>
"""

INDEX_TPL = """<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>DAQ Monitor · 上位机学习站（首页）</title>
<link rel="stylesheet" href="%(css)s">
</head>
<body>
<div class="topbar">
  <span class="brand"><span class="em">DAQ Monitor</span> · 上位机学习站</span>
  <span class="spacer"></span>
  <a class="navlink" href="README.md">站点 README</a>
  <a class="navlink" href="../README_学习总纲.md">学习总纲</a>
</div>

<div class="hero">
  <h1>📘 DAQ Monitor · 上位机学习站</h1>
  <p><b>北极星目标</b>：学完 = 上位机 <b>13~15K</b> 水平 + 简历上能放得出手的企业级项目「DAQ Monitor 工业数据采集监控系统」。</p>
  <p><b>路线</b>：先在「模块讲义」按序学基础 → 到「项目实践」像入职一样拿需求单开发项目；讲义与项目共用同一套类型，知识点可互相跳转。</p>
</div>
%(legend)s

<div class="wrap">

  <h2 class="section-title">🧭 内容入口（按需进，别在首页迷路）</h2>
  <div class="entries">
%(section_cards)s
  </div>

  <h2 class="section-title">🚀 怎么用</h2>
  <div class="steps">
    <div class="step"><span class="n">1</span><b>零基础起步</b>「入口·路线」里先看教练带练和实操入门，学会 dotnet CLI 建工程、跑 build/run/test。</div>
    <div class="step"><span class="n">2</span><b>按序学讲义</b>「模块讲义」M0 → M8 核心必学；学完想直接做项目，跳到「项目实践」。</div>
    <div class="step"><span class="n">3</span><b>入职式开发</b>「项目实践」拿需求单自己写 → 卡了看参考实现 → 点知识点亮牌回讲义重学。</div>
    <div class="step"><span class="n">4</span><b>练习+打卡</b>配合「练习·复习」每天 1h 代码肌肉 + 间隔重复，每节勾「打卡」自动存进度。</div>
    <div class="step"><span class="n">5</span><b>边做边投</b>「求职冲刺」对照 JD 路线图，过完 M11+M12 就能投，面中补漏。</div>
  </div>

  <div class="foot">本站由 <code>build_site.py</code> 从各 Markdown 源文件生成；Markdown 是单一事实来源，改完重跑脚本即重建。</div>
</div>
</body>
</html>"""

SECTION_TPL = """<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>%(title)s · DAQ Monitor 学习站</title>
<link rel="stylesheet" href="../assets/site.css">
</head>
<body>
<div class="topbar">
  <a class="brand" href="../index.html"><span class="em">DAQ Monitor</span> · 学习站</a>
  <span class="spacer"></span>
  <a class="navlink" href="../index.html">← 返回首页</a>
</div>
<div class="sechero">
  <h1>%(emoji)s %(title)s</h1>
  <p>%(desc)s</p>
</div>
<div class="wrap">
%(extra)s
%(groups)s
  <div class="foot">打卡状态自动保存在浏览器本地（localStorage），按页独立。</div>
</div>
</body>
</html>"""

def card_html(p, prefix):
    if p["kind"] == "prep":
        cls, kicker = "prep", "⭐ 必读先看"
    elif p["kind"] == "module":
        cls, kicker = "module", "主线模块"
    else:
        cls, kicker = "support", "工具箱"
    kicker = p.get("kicker", kicker)   # 页面可自定义卡片角标
    href = "%s%s.html" % (prefix, p["slug"])
    return ('<a class="card %s" href="%s">'
            '<div class="kicker">%s</div>'
            '<h3>%s</h3>'
            '<div class="sub">%s</div>'
            '<div class="what">%s</div>'
            '<div class="src">%s</div>'
            '<div class="arrow">→</div>'
            '</a>') % (cls, href, kicker, p["title"], p["sub"], p["what"], p["src"])

# ---------------------------------------------------------------------------
# 知识点弹窗块（只注入含 kp 链接的页面）
# ---------------------------------------------------------------------------
import json as _json

def build_kp_block():
    title_of = {p["slug"]: p["title"] for p in PAGES}
    data = {}
    for kid, k in KPOINTS.items():
        entry = dict(t=k["t"], d=k["d"])
        if k.get("m") and k["m"] in title_of:
            entry["m"] = k["m"]
            entry["lt"] = title_of[k["m"]]
        data[kid] = entry
    return """
<div id="kpmask" class="kpmask" onclick="closeKp()"></div>
<div id="kpmodal" class="kpmodal" role="dialog" aria-modal="true">
  <button class="kpclose" onclick="closeKp()" aria-label="关闭">×</button>
  <div id="kptitle" class="kptitle"></div>
  <div id="kpbody" class="kpbody"></div>
</div>
<script>
var KP_DATA = %(kpjson)s;
function openKp(id){
  var k = KP_DATA[id]; if(!k) return;
  document.getElementById('kptitle').textContent = k.t;
  var more = k.m ? '<p class="kplinkmore"><a href="../modules/' + k.m + '.html">📖 跳转「' + k.lt + '」看详解 →</a></p>' : '';
  document.getElementById('kpbody').innerHTML = k.d + more;
  document.getElementById('kpmask').style.display = 'block';
  document.getElementById('kpmodal').style.display = 'block';
}
function closeKp(){
  document.getElementById('kpmask').style.display = 'none';
  document.getElementById('kpmodal').style.display = 'none';
}
document.addEventListener('keydown', function(e){ if(e.key === 'Escape') closeKp(); });
</script>
""" % dict(kpjson=_json.dumps(data, ensure_ascii=False))

KP_BLOCK = build_kp_block()

# ---------------------------------------------------------------------------
# 1) 生成各模块/附录页面
# ---------------------------------------------------------------------------
prev_next = {}
for i, p in enumerate(PAGES):
    prev_p = PAGES[i - 1] if i > 0 else None
    next_p = PAGES[i + 1] if i < len(PAGES) - 1 else None

    with io.open(os.path.join(BASE, p["file"]), "r", encoding="utf-8") as f:
        md_text = f.read().strip()

    html, toc, total = convert(md_text, p["slug"])

    def pager_link(other, direction):
        if not other:
            return '<a style="visibility:hidden"></a>'
        if direction == "prev":
            return ('<a class="prev" href="%s.html"><div class="pg-sub">← 上一篇</div>'
                    '<div class="pg-t">%s</div></a>') % (other["slug"], other["title"])
        else:
            return ('<a class="next" href="%s.html"><div class="pg-sub">下一篇 →</div>'
                    '<div class="pg-t">%s</div></a>') % (other["slug"], other["title"])

    page = MODULE_TPL % dict(
        title=p["title"], slug=p["slug"], css="../assets/site.css", js="../assets/site.js",
        legend=LEGEND, localbar=local_bar(total), toc=toc, content=html,
        prev=pager_link(prev_p, "prev"), next=pager_link(next_p, "next"),
        section=p["section"],
        kpblock=KP_BLOCK if 'class="kplink"' in html else "",
    )
    out = os.path.join(MODDIR, p["slug"] + ".html")
    with io.open(out, "w", encoding="utf-8") as f:
        f.write(page)
    print("page ->", os.path.relpath(out, BASE), "(days=%d)" % total)

# ---------------------------------------------------------------------------
# 2) 生成分类页 site/sections/<id>.html + 首页
# ---------------------------------------------------------------------------
SEC_DIR = os.path.join(SITE, "sections")
os.makedirs(SEC_DIR, exist_ok=True)

for sec in SECTIONS:
    # 按 SECTION_OF 声明顺序渲染(分组要求同组页面连续;PAGES 定义序里 proj2 在 proj-daq 之前)
    _by_slug = {p["slug"]: p for p in PAGES}
    pages = [_by_slug[s] for s in SECTION_OF[sec["id"]]]
    # 分组渲染（无 group 的页面归为一组、不显示组标题）
    groups_html, cur_group, cards = [], None, []
    for p in pages:
        g = p.get("group")
        if g != cur_group:
            if cards:
                groups_html.append('<div class="cards">%s</div>' % "\n".join(cards))
                cards = []
            if g:
                groups_html.append('<h2 class="section-title">%s</h2>' % g)
            cur_group = g
        cards.append(card_html(p, "../modules/"))
    if cards:
        groups_html.append('<div class="cards">%s</div>' % "\n".join(cards))

    extra = CAREER_EXTRA if sec["id"] == "career" else ""
    page = SECTION_TPL % dict(emoji=sec["emoji"], title=sec["title"], desc=sec["desc"],
                              extra=extra, groups="\n".join(groups_html))
    out = os.path.join(SEC_DIR, sec["id"] + ".html")
    with io.open(out, "w", encoding="utf-8") as f:
        f.write(page)
    print("section ->", os.path.relpath(out, BASE), "(%d pages)" % len(pages))

def entry_card_html(sec):
    n = len([p for p in PAGES if p["section"] == sec["id"]])
    return ('<a class="entrycard" href="sections/%s.html">'
            '<div class="ico">%s</div>'
            '<h3>%s</h3>'
            '<div class="desc">%s</div>'
            '<div class="cnt">%d 个页面 →</div>'
            '</a>') % (sec["id"], sec["emoji"], sec["title"], sec["desc"], n)

section_cards = "\n".join(entry_card_html(s) for s in SECTIONS)
index = INDEX_TPL % dict(css="assets/site.css", legend=LEGEND, section_cards=section_cards)
with io.open(os.path.join(SITE, "index.html"), "w", encoding="utf-8") as f:
    f.write(index)
print("index ->", os.path.relpath(os.path.join(SITE, "index.html"), BASE))

# ---------------------------------------------------------------------------
# 3) 写入共享资源
# ---------------------------------------------------------------------------
with io.open(os.path.join(ASSETS, "site.css"), "w", encoding="utf-8") as f:
    f.write(CSS)
with io.open(os.path.join(ASSETS, "site.js"), "w", encoding="utf-8") as f:
    f.write(JS)
print("assets -> site.css / site.js")

# ---------------------------------------------------------------------------
# 4) 站点 README
# ---------------------------------------------------------------------------
readme = """# DAQ Monitor 学习站（site/）

把分散的 Markdown 讲义集成成**一个统一的 HTML 学习站点**，有首页入口和模块导航，不用再一个文件一个文件找。

> **👀 零基础先看**：打开 `site/index.html` 后，首页最上方有一张蓝色高亮卡片「零基础前置 · 教练带练（先看这篇）」，点它进入 `modules/prep.html`。看不懂其他模块文档时，从这里补最基础的常识（上位机是啥、C# 为什么、串口/Modbus 黑话、字节换算、第一个程序）。

## 入口
- 打开 `site/index.html` 即首页：只有 **6 张分区入口卡**，不再堆全部内容。
- 分区页（`site/sections/<id>.html`）：入口·路线 / 模块讲义 / 项目实践 / 练习·复习 / 求职冲刺 / 速查·参考。
- 内容页（`site/modules/Mx.html`）：左侧本页目录、顶部技术来源图例、每节「打卡」勾选（进度自动存浏览器本地，按页独立）、底部「上一篇 / 下一篇」顺序学、右上「← 分类 / ← 返回首页」。
- **知识点弹窗**：项目实践等文档里带下划线亮色的知识点链接，点击弹窗看摘要 + 一键跳转对应讲义（内容在 `build_site.py` 的 `KPOINTS` 定义，md 里写 `[📖 标题](kp:<id>)`）。

## 目录结构
```
site/
├─ index.html              # 首页：六大分区入口
├─ sections/               # 6 个分类页
│  ├─ start.html  modules.html  projects.html
│  └─ practice.html  career.html  reference.html
├─ README.md               # 本说明
├─ assets/
│  ├─ site.css             # 共享样式
│  └─ site.js              # 打卡(localStorage)
└─ modules/                # 全部内容页（M0~M19 / 项目实践 R0-R8 / 工具箱）
```

## 模块一览（M0 → M10）
| 模块 | 学什么 | 给项目加的能力 |
|---|---|---|
| M0 | C# 核心 + WPF + 并发 | 工程骨架 + 后台采集闭环 |
| M1 | 串口通信 | 真实/虚拟串口设备接入 |
| M2 | Modbus RTU/TCP | 工业标准协议读写寄存器 |
| M3 | PLC 通信(西门子 S7) | 直连 PLC |
| M4 | 数据持久化 | 历史库 + 查询/导出 |
| M5 | 实时可视化 | 动态曲线/仪表盘 |
| M6 | 报警引擎 + 日志 | 阈值规则 + Serilog |
| M7 | OPC UA / MQTT | 上云/对接 SCADA |
| M8 | 工程化收尾 | MVVM + 安装包 + 简历 |
| M9 | 工程素养 | 单测/DI/统一采集/容错 |
| M10 | 报表 | 聚合+可视化+导出 |

## 如何重新生成
Markdown 源文件（本目录的 `M0_*~.md`、`硬件替代方案…md` 等）是**单一事实来源**。
改完 Markdown 后，在本目录执行：

```
python build_site.py
```

即可重建 `site/` 整站（需要 Python 的 `markdown` 库：`pip install markdown`）。

## 提示
- 直接双击 `index.html` 即可在浏览器打开（file:// 可用，打卡也基于 localStorage 正常工作）。
- 若想用本地服务器预览：`python -m http.server` 后访问 `http://localhost:8000/site/`。
- 真实工程在 `../DAQMonitor/`（Core + UI），随模块逐步长出能力。
"""
with io.open(os.path.join(SITE, "README.md"), "w", encoding="utf-8") as f:
    f.write(readme)
print("site README ->", os.path.relpath(os.path.join(SITE, "README.md"), BASE))
print("DONE: total pages =", len(PAGES) + 1 + len(SECTIONS))
