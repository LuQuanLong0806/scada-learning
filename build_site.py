# -*- coding: utf-8 -*-
"""
DAQ Monitor 学习站点生成器
================================
把本目录下的各模块/附录 Markdown 转成一套多页 HTML 站点：

  site/
    index.html              # 首页：模块卡片网格 + 使用说明 + 进度
    assets/site.css         # 共享样式
    assets/site.js          # 打卡(localStorage，按页独立)
    modules/M0.html ...     # 每个模块/附录一个独立页面
    README.md               # 站点说明

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
    dict(slug="roadmap30", kind="support", file="30天作战路线_转行冲刺版.md",
         title="📅 30 天作战路线 · 转行冲刺版", sub="代码练习融入日常",
         what="Day 1-30 每日学习+项目+代码肌肉打卡（W1 主干/W2 落库可视化/W3 TCP上云/W4 简历面试）", src="⭐ 主轴"),
    dict(slug="muscle", kind="support", file="代码肌肉训练手册_30天刷题版.md",
         title="💪 代码肌肉训练手册 · 30 天刷题版", sub="白板手写题库",
         what="20 翻译 + 30 手写 + 10 Debug 题，每天 1h 白板，形成肌肉记忆", src="🔴 每天 1h"),
    dict(slug="audit-v2", kind="support", file="审计_整体串联复核_v2.md",
         title="🔍 整体串联审计 v2 · 由粗到细", sub="阶段→模块→知识点",
         what="4 周作战路线 + 22 模块 + 抽样知识点审计，Top 10 必修问题清单", src="🟦 审计"),
    dict(slug="jd-research", kind="support", file="JD调研_13-15K上位机岗位对照.md",
         title="📊 JD 调研 · 13-15K 上位机岗位对照", sub="必会/高频/加分/罕见 4 档",
         what="薪资地图 + 4 档技能矩阵 + 3 个典型 JD 样本 + 缺口 Top 10 + 投递策略", src="🟦 调研"),
    dict(slug="resume", kind="support", file="简历模板_上位机_13-15K.md",
         title="📄 简历模板 · 上位机 13-15K", sub="3 版本 + STAR + 投递策略",
         what="13K/14-15K/15K+ 三档简历模板 + 项目讲法 STAR + 关键词镜像 + 反问 3 问", src="🟦 简历"),
    dict(slug="interview30", kind="support", file="面试问答_逐字稿_30题.md",
         title="🎤 面试问答 · 逐字稿 30 题", sub="10 大主题 × 3 题",
         what="C#/并发/Modbus/PLC/EF/Prism/TCP/报警/性能/调试 各 3 题，标准答+逐字稿", src="🟦 面试"),
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
    dict(slug="job", kind="support", file="真实岗位调研_13-15K技能对照.md",
         title="真实岗位调研 · 13-15K 技能对照", sub="对标 BOSS 等招聘要求",
         what="真实企业 13-15K 技能要求与本路线的对应关系", src="🟦 调研"),
    dict(slug="audit", kind="support", file="审计_15K完整性复核.md",
         title="质量复核 · 15K 完整性复核", sub="5 维审计结论",
         what="对全部讲义的完整性/连贯性/易懂性/难点/外链复核", src="🟦 复核"),
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
         title="项目二 · CommLab 协议调试助手（规划）", sub="第 2 份简历作品",
         what="与 DAQMonitor 互补的调试工具：复用 IDevice/帧解析，新增报文编辑/从站模拟/抓包回放", src="🟦 规划"),
    dict(slug="interview", kind="support", file="面试高频知识点_速记卡.md",
         title="面试高频知识点 · 速记卡", sub="手机刷 · 23 题",
         what="13–15K 高频题 Q&A：架构/MVVM/并发/串口/Modbus/粘包/工程量/DB/报警/OPC UA/控件/DI/单测/容错/排查/简历讲法", src="🟦 速记"),
]

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
md_engine = markdown.Markdown(extensions=["tables", "fenced_code", "sane_lists"])

def convert(md_text, slug):
    md_engine.reset()
    # 规范首行 h1（若以 # 开头）为该页标题保持原样即可
    html = md_engine.convert(md_text)

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
</body>
</html>"""

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
  <p><b>路线</b>：项目驱动 —— 先有项目骨架，每个模块 = 给简历项目加一种企业级能力，学完即落地。下面每张卡片点开就是一个独立学习页。</p>
</div>
%(legend)s

<div class="wrap">

  <h2 class="section-title">👀 零基础先读（看不懂文档？从这里开始）</h2>
  <div class="cards">
%(prep_card)s
  </div>

  <h2 class="section-title">🎯 主线模块（按顺序学，M0 → M16：基础 → 进阶 → 工程协作 → 工业总线）</h2>
  <div class="cards">
%(module_cards)s
  </div>

  <h2 class="section-title">🧰 工具箱（随时查阅）</h2>
  <div class="cards">
%(support_cards)s
  </div>

  <h2 class="section-title">🚀 怎么用</h2>
  <div class="steps">
    <div class="step"><span class="n">1</span><b>从入门起步</b>先点「实操入门」，学会文件夹命名、用 dotnet CLI 建工程、跑 build/run/test。</div>
    <div class="step"><span class="n">2</span><b>按序学模块</b>从 M0 卡片点进去，左侧是目录，顶部是技术来源图例。</div>
    <div class="step"><span class="n">3</span><b>做练习+打卡</b>每天看讲解→按「练习阶梯」L1→L2→L3 做→勾右上「打卡」，进度自动存本地。</div>
    <div class="step"><span class="n">4</span><b>卡住就说</b>对我说「讲 Day N 的 XX」或「Day N 练习答案」，我展开讲。</div>
    <div class="step"><span class="n">5</span><b>落项目</b>每完成一个模块，把 DAQMonitor 对应代码提交 Git，作品集逐步成形。</div>
  </div>

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

  <div class="foot">本站由 <code>build_site.py</code> 从各 Markdown 源文件生成；Markdown 是单一事实来源，改完重跑脚本即重建。</div>
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
    )
    out = os.path.join(MODDIR, p["slug"] + ".html")
    with io.open(out, "w", encoding="utf-8") as f:
        f.write(page)
    print("page ->", os.path.relpath(out, BASE), "(days=%d)" % total)

# ---------------------------------------------------------------------------
# 2) 生成首页
# ---------------------------------------------------------------------------
prep_card = "\n".join(card_html(p, "modules/") for p in PAGES if p["kind"] == "prep")
module_cards = "\n".join(card_html(p, "modules/") for p in PAGES if p["kind"] == "module")
support_cards = "\n".join(card_html(p, "modules/") for p in PAGES if p["kind"] == "support")
index = INDEX_TPL % dict(css="assets/site.css", legend=LEGEND, prep_card=prep_card,
                         module_cards=module_cards, support_cards=support_cards)
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
- 打开 `site/index.html` 即首页：展示所有模块卡片。
- 点任意卡片 → 进入该模块的独立学习页（`site/modules/Mx.html`）。
- 模块页：左侧本页目录、顶部技术来源图例、每节「打卡」勾选（进度自动存浏览器本地，按页独立）、底部「上一篇 / 下一篇」可顺序学、右上「← 返回首页」。

## 目录结构
```
site/
├─ index.html              # 首页：模块卡片 + 使用说明 + 达标清单
├─ README.md               # 本说明
├─ assets/
│  ├─ site.css             # 共享样式
│  └─ site.js              # 打卡(localStorage)
└─ modules/
   ├─ M0.html  ... M10.html   # 11 个主线模块页
   └─ getting-started / practice-ladder / hardware /
       links / job / audit .html   # 6 个工具箱页
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
print("DONE: total pages =", len(PAGES) + 1)
