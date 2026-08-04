# DAQ Monitor 学习站（site/）

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
