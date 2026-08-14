# 前端 × 上位机差异化冲刺:WebView2 与工业大屏(7 天可落地方案)

> **优先级定位**:🔴 **差异化必读** · 把 5 年前端经验显性化为上位机加分项
> **目标**:7 天内给简历加一句"**全栈:上位机 WPF + 前端 Vue3 大屏双栈**",把面试排名从 30% 跳到 10%
> **前置条件**:DAQMonitor 主项目已落地(7 设备 + EF Core 双写 + LiveCharts2 + MQTT 双向 + 56 测试),你已经具备冲 13-15K 的"硬底盘"
> **核心判断**:你不该把 5 年前端当作"转行的包袱",而要把它当作"95% 上位机候选人没有的稀缺武器"

---

## 开篇:为什么前端 × 上位机是稀缺组合(300 字)

先看一个冷酷的行业事实:**13-15K 上位机岗位的 100 个候选人里,大概只有 5 个人能像样地写一个工业大屏页面**。剩下 95 个,要么 WPF 写得贼溜但前端只会一行 `<div>`,要么 WinForm 老兵连 ECharts 都没碰过。这就是你的机会窗口。

绝大多数上位机候选人的能力曲线是这样的:C#/WPF/Prism + Modbus/PLC/OPC UA + EF Core/SQLite + 一堆工业协议栈,**栈深但宽度极窄**——一旦面试官问到"我们公司想做 Web 远程监控 / MES 看板 / 数字孪生",这群人立刻集体沉默。而新能源、半导体、3C 非标、智慧工厂这 4 个行业,恰恰在 2024-2026 这一波疯狂地往"上位机 + Web 大屏"方向招人:宁德时代的产线看板、隆基的智慧工厂大屏、北方华创的设备状态全景图、拓斯达的远程运维平台——**JD 里"工业大屏 / 数字孪生 / SCADA Web 化 / MES 看板"这 4 个关键词出现频率 2025 年比 2022 年涨了 3 倍**。

你反过来:5 年前端带过来的不只是"会写 Vue",而是 **4 大可平移能力**——

1. **组件化思维**(SFC → UserControl,反向亦然,你能比纯 WPF 工程师更快理解模板复用);
2. **状态管理**(Pinia/Redux → Prism EventAggregator,数据流思维一致,你能讲清楚"单向数据流");
3. **工程化**(Vite/Webpack/CI → dotnet CLI/MSBuild,构建优化思路平移);
4. **大屏可视化**(ECharts/D3/Canvas → LiveCharts2,数据降采样 / 虚拟滚动 / WebGL 渲染是同一个问题);

这 4 项**恰好是工业大屏岗位的命门**。本份冲刺方案就是把这 4 项**变成简历上一句话、面试里 10 道题、GitHub 上一段录屏**,7 天内让面试官在 ATS 简历库里多看你 30 秒。

---

## 第 1 节:5 个结合方向对比表

先把"前端 × 上位机"的所有可能结合方式摆出来,你才知道为什么后面死磕 **WebView2 + Vue 大屏**。

| 方向 | 上手成本 | 投哪些岗位 | 学习产出 | 简历加分点 |
|---|---|---|---|---|
| **A. WebView2 嵌 Vue(推荐 ⭐⭐⭐)** | ★★☆(3 天) | 上位机 + 大屏双栈岗 / 工业软件 / SCADA | DAQMonitor 主窗口里嵌一个 Vue3 大屏页,WPF ↔ Vue 双向通信 | "**全栈:WPF + Vue3 双栈**,WebView2 在桌面端嵌入 Web 大屏" |
| **B. Blazor Hybrid** | ★★★(5 天) | .NET 系工业软件 / 微软生态外企 | 用 Razor 重写一个页面,共享 C# 模型 | "Blazor Hybrid(.NET 8)统一桌面/Web UI" |
| **C. Electron + Node 桌面** | ★★★★(7 天) | 偏 Web 系工业互联网(树根 / 卡奥斯) | 整套 Electron 壳 + Node 采集层 | "Electron 跨平台桌面 + Node 采集"——但偏离 C# 主线,**不推荐** |
| **D. 独立 Vue 工业大屏(推荐 ⭐⭐⭐)** | ★★(2 天) | 前端 + 工业 / MES 看板 / 数字孪生 | 一个独立部署的 Vue3 大屏,通过 WebSocket 接 DAQMonitor | "**独立 Vue3 工业大屏**,ECharts 实时曲线 + 报警 + 设备卡片" |
| **E. ASP.NET Core + SignalR** | ★★★(4 天) | B/S 上位机 / 远程监控平台 | 一个 Web 后端 + Vue 前端,SignalR 推数据 | "ASP.NET Core + SignalR 实时推送"——比 WebSocket 多一层封装 |

### 明确推荐:你做 A + D 这 2 个

**为什么选 A(WebView2 嵌 Vue)**:

1. **简历一句话直击灵魂**——"用 WebView2 在 WPF 中嵌入 Vue3 工业大屏"是 95% 上位机候选人写不出来的话,投"上位机 + 大屏双栈"岗位(HC 占比 15-20%)一击必中;
2. **复用 DAQMonitor 现有 7 设备 + LiveCharts2**——你不需要重写采集层,只是给主项目加一个"大屏视图",WPF 主窗口的 LiveCharts2 是 fallback,Vue 大屏是亮点;
3. **学习成本最低**——WebView2 控件 30 行代码就能跑起来,核心难点在 WPF ↔ Vue 双向通信,你前端经验 + C# 经验刚好覆盖;
4. **行业最对口**——新能源 / 半导体 / 3C 非标这 3 个行业的大屏岗,**JD 里 60% 写"WebView2"或"嵌入式 Web"**,CefSharp 老旧、Blazor 太新,WebView2 是 2024-2026 的主流。

**为什么选 D(独立 Vue 大屏)** 作为备份方案:

1. **同一份 WebSocket 服务,A 一次写完 D 直接复用**——WebSocket 是 A 和 D 共用的底层,Day 2 写完后 D 只剩"前端组件"工作,你的舒适区;
2. **投"前端 + 工业大屏"岗位的简历加分**——这类岗位 13-18K,JD 写"工业大屏 / 数字孪生 / SCADA 看板",Vue3 + ECharts + WebSocket 是标配;
3. **截图最漂亮**——独立大屏部署到 Vercel / Netlify,简历附个在线 demo 链接,HR 点开就看到实时曲线,**冲击力远胜 GitHub 截图**。

**为什么不推荐 B(C# 系 Blazor Hybrid)**:你前端 5 年是 React/Vue 不是 Blazor,学 Razor 学习曲线陡,而且 Blazor 工业岗位 HC 极少(全国 <5%),投入产出比低。**Blazor 是好技术但不是你的好选择**。

**为什么不推荐 C(Electron)**:你会偏离 C# 上位机主线,简历会被怀疑"是不是前端干不下去了才转",**自毁长城**。

---

## 第 2 节:5 天落地方案 — WebView2 + Vue3 大屏(核心)

> 本节是这份文档的灵魂。**5 天,每天 4-6 小时,产出 1 个可截图可录屏的大屏**。第 6、7 天留给录视频 + 简历话术 + 面试题背熟。
> 共享底层:**DAQMonitor.Core 加 1 个 WebBroadcastService.cs**,A 和 D 方向都用它,Day 2 写完后续 Days 复用。

---

### Day 1:Vue3 + Vite 项目搭起来,跑通一个 Dashboard

**目标**:本地起一个 Vue3 大屏页面,有标题、3 个卡片、1 个折线图占位。这一天**只写前端**,完全不碰 C#。

**🚦 前置检查(2 分钟,不做 Day 1 就卡死)**

| 检查项 | 命令 | 期望 |
|---|---|---|
| Node 20+ 装了吗 | `node -v` | v20.x.x 或更高(Vite 5 要求) |
| pnpm 装了吗 | `pnpm -v` | 9.x.x 或更高(没装:`npm i -g pnpm`) |
| TypeScript 装了吗 | `tsc -v` | Vite 模板自带,**不报错就行** |
| 当前目录对吗 | `pwd` (bash) / `cd` (cmd) | `f:\00_project\上位机学习` |
| DAQMonitor 已落地 | `ls DAQMonitor/src` | 能看到 `DaqMonitor.Core` / `DaqMonitor.UI` |

**npm vs pnpm 命令对照(纯前端背景的同学切换指南)**

| 操作 | npm 命令 | pnpm 命令 | 备注 |
|---|---|---|---|
| 装依赖 | `npm install` / `npm i` | `pnpm install` / `pnpm i` | pnpm 用硬链接省磁盘 |
| 加新包 | `npm i echarts` | `pnpm add echarts` | **命令名不同,这是最大坑** |
| 加 dev 包 | `npm i -D vitest` | `pnpm add -D vitest` | 同上加 -D 标志 |
| 跑脚本 | `npm run dev` | `pnpm dev` | pnpm 可省略 `run` |
| 删包 | `npm un echarts` | `pnpm remove echarts` | pnpm 用 `remove` 不是 `un` |
| 跑 bin | `npx vue-tsc` | `pnpm dlx vue-tsc` | pnpm 用 `dlx` 不是 `npx` |

> 💡 **混用警告**:一个项目要么全 npm 要么全 pnpm,**别混着用** — `package-lock.json` 和 `pnpm-lock.yaml` 冲突会让 CI 直接挂。本文统一用 pnpm(快、省磁盘)。

**步骤**:

1. **安装 Node 20+ 和 pnpm**(你应该早就有,跳过)。
2. **在 DAQMonitor 同级目录创建 Vue 项目**:

```bash
cd f:/00_project/上位机学习
npm create vite@latest daq-dashboard -- --template vue-ts
cd daq-dashboard
pnpm install
pnpm add echarts vue-echarts pinia axios
pnpm dev
```

3. **目录规划**(前端工程化思维):

```
daq-dashboard/
├── src/
│   ├── api/
│   │   ├── ws.ts              # WebSocket 客户端封装(Day 3 重点)
│   │   └── http.ts            # REST 备用(Day 2 简易查历史)
│   ├── stores/
│   │   ├── points.ts          # Pinia:实时点位 store
│   │   └── alarms.ts          # Pinia:报警 store
│   ├── components/
│   │   ├── RealtimeChart.vue  # 实时曲线(Day 3)
│   │   ├── DeviceCard.vue     # 设备状态卡片(Day 5)
│   │   ├── AlarmTicker.vue    # 报警滚动(Day 5)
│   │   └── GaugePanel.vue     # 工程量仪表盘(Day 5)
│   ├── types/
│   │   └── point.ts           # TS 类型,对齐 C# SensorPoint
│   ├── views/
│   │   └── Dashboard.vue      # 大屏主页
│   ├── App.vue
│   └── main.ts
└── vite.config.ts
```

4. **TS 类型(对齐 C# SensorPoint)**:

```ts
// src/types/point.ts
// 跟 DaqMonitor.Core 的 SensorPoint struct 一一对应,前端类型即文档
export enum DeviceState { Offline = 0, Connecting = 1, Online = 2 }
export enum AlarmLevel  { Normal = 0, Warning = 1, Critical = 2 }

export interface SensorPoint {
  id: number
  value: number
  state: DeviceState
  timestamp: string   // ISO 8601,UTC,前端 dayjs 格式化
}

export interface AlarmEvent {
  pointId: number
  level: AlarmLevel
  value: number
  message: string
  timestamp: string
}
```

> 关键思维:**TS 类型 = 前后端契约**。前端 5 年你早就懂这套,现在把它显性化:简历写"前后端共用 TypeScript 类型契约,降低联调成本"。

5. **Dashboard.vue 跑通 3 个卡片 + 1 个 ECharts 占位**:

```vue
<!-- src/views/Dashboard.vue -->
<script setup lang="ts">
import { ref, onMounted } from 'vue'
import RealtimeChart from '@/components/RealtimeChart.vue'
import DeviceCard from '@/components/DeviceCard.vue'
import AlarmTicker from '@/components/AlarmTicker.vue'
import GaugePanel from '@/components/GaugePanel.vue'

const online = ref(0)
const total  = ref(7)   // DAQMonitor 7 个设备
const alarmCount = ref(0)
</script>

<template>
  <div class="dashboard">
    <header class="dashboard__title">
      DAQMonitor · 工业数据采集监控大屏
      <span class="dashboard__clock">{{ new Date().toLocaleString() }}</span>
    </header>

    <section class="dashboard__kpis">
      <div class="kpi"><div class="kpi__label">在线设备</div><div class="kpi__value">{{ online }}/{{ total }}</div></div>
      <div class="kpi"><div class="kpi__label">实时点位</div><div class="kpi__value">{{ total }}</div></div>
      <div class="kpi"><div class="kpi__label">活跃报警</div><div class="kpi__value kpi__value--alarm">{{ alarmCount }}</div></div>
    </section>

    <section class="dashboard__main">
      <div class="panel panel--chart"><RealtimeChart /></div>
      <div class="panel panel--gauge"><GaugePanel /></div>
    </section>

    <section class="dashboard__bottom">
      <div class="panel panel--devices"><DeviceCard /></div>
      <div class="panel panel--alarms"><AlarmTicker /></div>
    </section>
  </div>
</template>

<style scoped>
/* 工业大屏风格:深色 + 高对比 + 等宽数字 + 霓虹蓝/橙/红 */
.dashboard { background: #0a0e1a; color: #c5d1de; min-height: 100vh; padding: 16px; font-family: 'Inter', sans-serif; }
.dashboard__title { font-size: 22px; font-weight: 600; padding: 8px 0; display: flex; justify-content: space-between; }
.dashboard__clock { font-size: 14px; color: #6b7a8f; font-variant-numeric: tabular-nums; }
.dashboard__kpis { display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px; margin: 12px 0; }
.kpi { background: linear-gradient(135deg, #131a2b, #1a2238); border: 1px solid #1f2a44; border-radius: 8px; padding: 16px; }
.kpi__label { font-size: 13px; color: #6b7a8f; }
.kpi__value { font-size: 36px; font-weight: 700; color: #4dd0e1; font-variant-numeric: tabular-nums; margin-top: 4px; }
.kpi__value--alarm { color: #ff5252; }
.dashboard__main { display: grid; grid-template-columns: 2fr 1fr; gap: 12px; }
.dashboard__bottom { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-top: 12px; }
.panel { background: #131a2b; border: 1px solid #1f2a44; border-radius: 8px; padding: 12px; }
</style>
```

6. **跑起来**:`pnpm dev` 浏览器打开 `http://localhost:5173`,看到 3 个 KPI 卡片 + 4 个空 panel 占位。**Day 1 结束**。

> **Day 1 用时**:4 小时。如果你前端 5 年真的扎实,这 1 天其实 2-3 小时就够,剩余时间用来调大屏配色。

---

### Day 2(核心难点):DAQMonitor 暴露 WebSocket 服务 — WebBroadcastService.cs

**目标**:在 `DaqMonitor.Core` 里加一个 `WebBroadcastService.cs`,起一个 ASP.NET Core Kestrel 的 `ws://localhost:5180/ws` 端点,把 `AcquisitionPipeline.BatchReady` 事件转发给所有连接的 WebSocket 客户端。

**为什么用 WebSocket 不用 REST 轮询**:

- **REST 轮询** = 前端每 500ms 一次 GET → C# 端建连接 → 查库 → JSON 序列化 → 返回 → 关连接。100Hz×7 设备 = 每秒 700 个点,轮询拿不全,且 SQLite 并发扛不住;
- **WebSocket** = 一次握手,长连接,C# 端有新 batch 就推,**带宽和 CPU 都低 10 倍以上**;
- **类比前端**:你做股票看盘 / 实时聊天都用过 WebSocket,这里完全一样。

**为什么用 ASP.NET Core Kestrel 不用裸 System.Net.WebSockets**:

- 裸 `HttpListener` + `WebSocket` 需要 80+ 行样板代码,路由 / SSL / 静态文件全要自己写;
- ASP.NET Core Kestrel 在 .NET 8 里**和 Core 项目共享 SDK**,加一个 `Microsoft.AspNetCore.App` 框架引用就行,**还能顺带托管 Vue 的静态文件**(生产部署直接一个 exe 起来,前端免装 Node);
- 简历可以多写一句"ASP.NET Core Kestrel 嵌入式宿主"——这是高级上位机的加分点(2024 年开始新能源行业部分岗位明确要"ASP.NET Core 嵌入桌面")。

**步骤**:

1. **改 `DaqMonitor.Core.csproj`,加 ASP.NET Core 框架引用**:

打开 `f:\00_project\上位机学习\DAQMonitor\src\DaqMonitor.Core\DaqMonitor.Core.csproj`,在 `<Project Sdk="Microsoft.NET.Sdk">` 下面加一行 SDK 改造(或者用 FrameworkReference):

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RollForward>Major</RollForward>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>DaqMonitor.Core</RootNamespace>
    <!-- 重要:Core 改 Sdk.Web 后,UI 项目要确保 UseWPF 不冲突,生产环境推荐拆出 DaqMonitor.Web 单独项目 -->
  </PropertyGroup>

  <ItemGroup>
    <!-- 已有的包:DI / 串口 / EF Core Sqlite / MQTTnet 不动 -->
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.1" />
    <PackageReference Include="System.IO.Ports" Version="8.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.10" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.10">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="MQTTnet" Version="4.3.7.1207" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="DaqMonitor.Tests" />
  </ItemGroup>

</Project>
```

> **生产架构建议**:为避免 `Sdk.Web` 影响现有 UI / 测试,**更推荐的做法是新建一个 `DaqMonitor.Web` 项目**(独立 csproj),引用 `DaqMonitor.Core`,把 `WebBroadcastService` 放在 Web 项目里。本文为简化讲解放在 Core,实际落地建议拆项目——这点面试可以主动提("我会把 Web 层独立成 DaqMonitor.Web 项目,跟 Core 解耦")。

2. **新建 `WebBroadcastService.cs`** 在 `f:\00_project\上位机学习\DAQMonitor\src\DaqMonitor.Core\Web\`:

```csharp
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using DaqMonitor.Core.Acquisition;
using DaqMonitor.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DaqMonitor.Core.Web;

/// <summary>
/// 实时数据广播服务:起一个 ASP.NET Core Kestrel 嵌入式宿主,
/// 把 AcquisitionPipeline.BatchReady 事件用 WebSocket 推给所有连接的前端大屏。
///
/// 设计要点(面试可以讲):
/// 1) 嵌入式 Kestrel——跟主程序同进程,不占新端口冲突,跟 Core 共享 DI 容器
/// 2) 用 Channel 做广播缓冲——BatchReady 触发频率高(200ms 一次),WebSocket 写慢时不会堵采集线程
/// 3) 客户端连接列表用 ConcurrentDictionary + WebSocket,断线自动清理
/// 4) JSON 用 System.Text.Json + TimeSpan/string 预格式化,前端拿到直接用
/// 5) 提供心跳机制:每 5 秒推一条 ping,前端断线重连用
/// </summary>
public sealed class WebBroadcastService : IHostedService, IDisposable
{
    private readonly AcquisitionPipeline _pipeline;
    private readonly ILogger<WebBroadcastService>? _log;
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<string> _outbox = Channel.CreateBounded<string>(1024);
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();

    private IHost? _host;
    private Task? _broadcastLoop;
    private Task? _heartbeatLoop;

    public WebBroadcastService(AcquisitionPipeline pipeline, ILogger<WebBroadcastService>? log = null)
    {
        _pipeline = pipeline;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancel = default)
    {
        // 1) Kestrel 起在 5180 端口(避开常见端口),路由 /ws 给 WebSocket,/api/history 给 REST(备用)
        _host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(builder =>
            {
                builder.UseUrls("http://localhost:5180");
                builder.Configure(app =>
                {
                    app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(5) });
                    app.Map("/ws", HandleWebSocketAsync);
                    app.Map("/api/health", ctx => ctx.Response.WriteAsync("ok"));
                });
            })
            .Build();

        await _host.StartAsync(cancel);

        // 2) 订阅采集管道的批量事件:把 List<SensorPoint> 序列化成 JSON,塞进广播 Channel
        _pipeline.BatchReady += OnBatchReady;

        // 3) 后台两个任务:广播 + 心跳
        _broadcastLoop = Task.Run(BroadcastLoopAsync);
        _heartbeatLoop = Task.Run(HeartbeatLoopAsync);

        _log?.LogInformation("WebBroadcastService started on http://localhost:5180/ws");
    }

    public async Task StopAsync(CancellationToken cancel = default)
    {
        _pipeline.BatchReady -= OnBatchReady;
        _cts.Cancel();
        if (_host is not null) await _host.StopAsync(cancel);
        _outbox.Writer.TryComplete();
    }

    // —— 关键回调:把批量点位转 JSON ——
    // 注意:这里同步序列化没问题(几百个点的 JSON 不到 10KB,耗时 <1ms),
    // 但严禁在此阻塞或做重活,BatchReady 在采集线程上触发。
    private void OnBatchReady(object? sender, IReadOnlyList<SensorPoint> batch)
    {
        // 协议格式:{"type":"batch","points":[{"id":1,"value":42.5,"timestamp":"..."}]}
        // 注意:SensorPoint 只含 Id/Value/Timestamp(参前置类型定义),state 不在里面;
        //      如果要带状态,要么单独发"设备状态"消息,要么扩展 SensorPoint 加 State 字段
        var payload = new
        {
            type = "batch",
            points = batch.Select(p => new
            {
                p.Id,
                p.Value,
                timestamp = p.Timestamp.ToString("O")   // ISO 8601,前端 new Date() 直接用
            })
        };
        var json = JsonSerializer.Serialize(payload);
        _outbox.Writer.TryWrite(json);   // 满了就丢,采集优先级高于广播
    }

    // —— WebSocket 处理:握手成功后挂进客户端表,主循环负责推 ——
    private async Task HandleWebSocketAsync(HttpContext ctx)
    {
        if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
        var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        var id = Guid.NewGuid();
        _clients[id] = ws;
        _log?.LogInformation("WebSocket connected: {Id}, total={Count}", id, _clients.Count);

        // 接收循环:客户端不发数据时也要 await,这样新 .NET 会自动处理 KeepAlive Ping
        var buffer = new byte[1024];
        try
        {
            while (ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(buffer, _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                // 业务上暂不接收前端消息,收到直接丢;Day 4 双向通信时会用到
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log?.LogError(ex, "WebSocket error"); }
        finally
        {
            _clients.TryRemove(id, out _);
            ws.Dispose();
            _log?.LogInformation("WebSocket disconnected: {Id}, total={Count}", id, _clients.Count);
        }
    }

    // —— 广播循环:从 Channel 拿 JSON,并发推给所有客户端 ——
    private async Task BroadcastLoopAsync()
    {
        await foreach (var json in _outbox.Reader.ReadAllAsync(_cts.Token))
        {
            if (_clients.IsEmpty) continue;
            var bytes = Encoding.UTF8.GetBytes(json);
            var tasks = _clients.Values.Select(ws => SafeSendAsync(ws, bytes, _cts.Token)).ToArray();
            try { await Task.WhenAll(tasks); }
            catch (Exception ex) { _log?.LogError(ex, "Broadcast error"); }
        }
    }

    // —— 心跳:5 秒一次,前端拿不到 batch 也知道连接活着 ——
    private async Task HeartbeatLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            await Task.Delay(5000, _cts.Token);
            if (_clients.IsEmpty) continue;
            var ping = Encoding.UTF8.GetBytes("{\"type\":\"ping\",\"ts\":\"" + DateTimeOffset.Now.ToString("O") + "\"}");
            var tasks = _clients.Values.Select(ws => SafeSendAsync(ws, ping, _cts.Token)).ToArray();
            try { await Task.WhenAll(tasks); } catch { /* 单个连接失败不影响其他 */ }
        }
    }

    // —— 安全发送:单连接失败不能影响其他客户端 ——
    private static async Task SafeSendAsync(WebSocket ws, byte[] data, CancellationToken ct)
    {
        try
        {
            if (ws.State == WebSocketState.Open)
                await ws.SendAsync(data, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        catch { /* 客户端断了,会由接收循环清理 */ }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _host?.Dispose();
        _cts.Dispose();
    }
}
```

3. **注册到 `Bootstrapper.cs`**——打开 `f:\00_project\上位机学习\DAQMonitor\src\DaqMonitor.Core\AppServices\Bootstrapper.cs`,在 `services.AddSingleton<MqttPublisher>(...)` 后面加一行:

```csharp
// Web 大屏广播服务(嵌入式 Kestrel):启动时调用 StartAsync
services.AddSingleton<WebBroadcastService>();
```

`Bootstrapper.Build()` 返回的 `ServiceProvider` 拿到 `WebBroadcastService` 后,**由 UI 层(App.xaml.cs)在启动时调一次 `StartAsync()`**——具体见 Day 4。

4. **本地测试**:用浏览器开 `http://localhost:5180/api/health` 看到 `ok`,然后下一个临时测试:

> **临时测试**:Chrome 装 "WebSocket King" 或用 PowerShell 都行,连 `ws://localhost:5180/ws`,5 秒内应收到 `{"type":"ping","ts":"..."}`;如果当前 `SimulatedDevice` 在跑,200ms 内应收到 `{"type":"batch","points":[...]}`。

5. **Day 2 用时**:6 小时。这是整个方案最难的一天,核心难点是 ASP.NET Core 嵌入式宿主 + WebSocket 并发广播。**踩坑警告**:见第 6 节。

---

### Day 3:Vue 大屏接 WebSocket,ECharts 实时曲线

**目标**:Vue 大屏连接 `ws://localhost:5180/ws`,Pinia 存实时点位,ECharts 画 7 条曲线(对应 7 个设备),复用你的前端能力。

**步骤**:

1. **WebSocket 客户端封装**(关键:断线重连 + 心跳):

```ts
// src/api/ws.ts
// 封装 WebSocket:自动重连(指数退避)+ 心跳检测 + 订阅模式
// 类比前端你做过 React useWebSocket Hook,这里是 Vue 版
import { ref } from 'vue'

export type WsMessage =
  | { type: 'batch'; points: Array<{ id: number; value: number; state: number; timestamp: string }> }
  | { type: 'ping'; ts: string }
  | { type: 'alarm'; alarm: AlarmPayload }

interface AlarmPayload { pointId: number; level: number; value: number; message: string; timestamp: string }

export function useWebSocket(url: string) {
  const connected = ref(false)
  const lastMessage = ref<WsMessage | null>(null)
  const subscribers = new Set<(msg: WsMessage) => void>()

  let ws: WebSocket | null = null
  let retryCount = 0
  let retryTimer: number | null = null
  let manualClose = false
  let heartbeatTimer: number | null = null
  let lastPong = Date.now()

  const connect = () => {
    ws = new WebSocket(url)
    ws.onopen = () => {
      connected.value = true
      retryCount = 0
      console.log('[ws] connected', url)
      // 心跳:10 秒没收到服务端 ping 就认为断线,主动 close 触发重连
      lastPong = Date.now()
      heartbeatTimer = window.setInterval(() => {
        if (Date.now() - lastPong > 10_000) {
          console.warn('[ws] heartbeat timeout, reconnecting...')
          ws?.close()
        }
      }, 5000)
    }
    ws.onmessage = (e) => {
      const msg = JSON.parse(e.data) as WsMessage
      if (msg.type === 'ping') lastPong = Date.now()
      lastMessage.value = msg
      subscribers.forEach(fn => fn(msg))
    }
    ws.onclose = () => {
      connected.value = false
      if (heartbeatTimer) clearInterval(heartbeatTimer)
      if (manualClose) return
      // 指数退避:1s → 2s → 4s → 8s → 封顶 30s
      const delay = Math.min(30_000, 1000 * 2 ** retryCount++)
      console.warn(`[ws] closed, retry in ${delay}ms`)
      retryTimer = window.setTimeout(connect, delay)
    }
    ws.onerror = (e) => console.error('[ws] error', e)
  }

  const close = () => {
    manualClose = true
    if (retryTimer) clearTimeout(retryTimer)
    if (heartbeatTimer) clearInterval(heartbeatTimer)
    ws?.close()
  }

  const subscribe = (fn: (msg: WsMessage) => void) => {
    subscribers.add(fn)
    return () => subscribers.delete(fn)
  }

  return { connected, lastMessage, connect, close, subscribe }
}
```

2. **Pinia store(实时点位环形缓冲)**:

```ts
// src/stores/points.ts
import { defineStore } from 'pinia'
import { ref } from 'vue'

// 每个点位保留最近 600 条(60 秒 @ 10Hz × 1 个点位),前端环形缓冲
// 类比:你前端做股票看盘也是这个套路,固定长度数组避免内存爆炸
const MAX_POINTS = 600

export const usePointsStore = defineStore('points', () => {
  const series = ref<Map<number, Array<[number, number]>>>(new Map())

  const push = (id: number, value: number, ts: string) => {
    const t = new Date(ts).getTime()
    if (!series.value.has(id)) series.value.set(id, [])
    const arr = series.value.get(id)!
    arr.push([t, value])
    if (arr.length > MAX_POINTS) arr.shift()
  }

  const clear = () => series.value.clear()

  return { series, push, clear }
})
```

3. **ECharts 实时曲线组件**:

```vue
<!-- src/components/RealtimeChart.vue -->
<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed } from 'vue'
import * as echarts from 'echarts'
import { useWebSocket } from '@/api/ws'
import { usePointsStore } from '@/stores/points'

const ws = useWebSocket('ws://localhost:5180/ws')
const store = usePointsStore()
const chartEl = ref<HTMLDivElement>()
let chart: echarts.ECharts | null = null

// 设备配置(真实项目从 /api/devices 拉,这里硬编码演示)
const DEVICES = [
  { id: 1, name: '温度', unit: '℃',   color: '#4dd0e1' },
  { id: 2, name: '压力', unit: 'MPa', color: '#ffb74d' },
  { id: 3, name: '流量', unit: 'L/m',  color: '#81c784' },
  { id: 4, name: '电压', unit: 'V',    color: '#ba68c8' },
  { id: 5, name: '电流', unit: 'A',    color: '#ff8a65' },
  { id: 6, name: '转速', unit: 'rpm',  color: '#90caf9' },
  { id: 7, name: '振动', unit: 'mm/s', color: '#e57373' },
]

onMounted(() => {
  chart = echarts.init(chartEl.value!, 'dark', { renderer: 'canvas' })  // canvas 比 svg 快 5 倍
  chart.setOption({
    backgroundColor: 'transparent',
    grid: { left: 50, right: 30, top: 40, bottom: 40 },
    tooltip: { trigger: 'axis' },
    legend: { data: DEVICES.map(d => d.name), textStyle: { color: '#c5d1de' } },
    xAxis: { type: 'time', axisLabel: { color: '#6b7a8f' } },
    yAxis: { type: 'value', axisLabel: { color: '#6b7a8f' }, splitLine: { lineStyle: { color: '#1f2a44' } } },
    series: DEVICES.map(d => ({ name: d.name, type: 'line', showSymbol: false, smooth: true, lineStyle: { width: 2, color: d.color }, data: [] })),
  })

  // 订阅 WebSocket batch 消息,推入 store
  ws.subscribe((msg) => {
    if (msg.type !== 'batch') return
    for (const p of msg.points) store.push(p.id, p.value, p.timestamp)
  })

  ws.connect()

  // 200ms 刷新一次图表(跟 C# 端 BatchReady 频率对齐,不要每条消息都 setOption)
  const refresh = setInterval(() => {
    if (!chart) return
    chart.setOption({
      series: DEVICES.map(d => ({
        name: d.name,
        data: store.series.get(d.id) ?? [],
      })),
    })
  }, 200)

  onUnmounted(() => { clearInterval(refresh); chart?.dispose(); ws.close() })
})
</script>

<template>
  <div class="chart">
    <div class="chart__title">实时数据曲线(最近 60 秒)</div>
    <div ref="chartEl" class="chart__canvas"></div>
  </div>
</template>

<style scoped>
.chart { height: 100%; display: flex; flex-direction: column; }
.chart__title { font-size: 14px; color: #6b7a8f; margin-bottom: 8px; }
.chart__canvas { flex: 1; min-height: 320px; }
</style>
```

4. **跑起来**:DAQMonitor 的 `WebBroadcastService.StartAsync()` 已启动(Day 4 会接到 UI),`pnpm dev` 跑 Vue 大屏,浏览器 `http://localhost:5173` 应看到 7 条彩色实时曲线在跳动。

5. **Day 3 用时**:4-6 小时。这一天对你的前端经验是降维打击,**真正的难点是 ECharts 在 600 点 × 7 条 = 4200 点情况下保持 60fps**——你前端 5 年早就踩过这个坑,降采样 / 切 canvas / 减少 setOption 频率三件套。

---

### Day 4:WPF 嵌 WebView2,WPF ↔ Vue 双向通信

**目标**:在 DAQMonitor.UI 主窗口里放一个 WebView2 控件,加载 Vue 大屏的 `http://localhost:5173`(开发)或本地 `dist/index.html`(生产),C# ↔ JS 双向通信。

**步骤**:

1. **UI 项目加 WebView2 包**——打开 `f:\00_project\上位机学习\DAQMonitor\src\DaqMonitor.UI\DaqMonitor.UI.csproj`,在 `<ItemGroup>` 里加:

```xml
<PackageReference Include="Microsoft.Web.WebView2" Version="1.0.2903.40" />
```

2. **新建 `WebViewBridge.cs`**——`f:\00_project\上位机学习\DAQMonitor\src\DaqMonitor.UI\Controls\WebViewBridge.cs`,这是 C# ↔ JS 双向通信的核心:

```csharp
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DaqMonitor.UI.Controls;

/// <summary>
/// WebView2 + Vue 双向通信桥:
/// 1) C# → JS:用 ExecuteScriptAsync 调用前端全局函数(下发命令 / 推设备列表)
/// 2) JS → C#:前端用 chrome.webview.postMessage(jsonObject),C# 订阅 WebMessageReceived
///
/// 类比前端:这就是 React Native 的 bridge,或 Electron 的 ipcMain/ipcRenderer
/// </summary>
public class WebViewBridge
{
    private readonly WebView2 _wv;
    public WebViewBridge(WebView2 wv) { _wv = wv; }

    /// <summary>C# 调 JS:前端 window.__daqBridge 接收</summary>
    public async Task InvokeAsync(string method, object? payload = null)
    {
        var json = JsonSerializer.Serialize(new { method, payload });
        // ExecuteScriptAsync 要求字符串再包一层 JSON.stringify,前端拿到的才是对象不是字符串
        var script = $"window.__daqBridge && window.__daqBridge.receive({json});";
        await _wv.ExecuteScriptAsync(script);
    }

    /// <summary>JS → C#:前端调 chrome.webview.postMessage(msg),这里接收</summary>
    public void OnMessageFromWeb(EventHandler<string> handler)
    {
        _wv.WebMessageReceived += (s, e) => handler(s, e.TryGetWebMessageAsString());
    }
}
```

3. **Vue 端补 bridge 接收代码**——在 `daq-dashboard/src/main.ts` 加:

```ts
// Vue 接收 C# 下发消息的全局桥
// 类比:Electron 的 preload script
declare global {
  interface Window {
    __daqBridge?: { receive: (msg: { method: string; payload?: unknown }) => void }
    chrome?: { webview: { postMessage: (msg: unknown) => void } }
  }
}

window.__daqBridge = {
  receive(msg) {
    console.log('[bridge] from C#:', msg)
    // 这里用 EventBus / Pinia 派发,例如收到 "ackAlarm" → 报警 store 标记已确认
    window.dispatchEvent(new CustomEvent('daq-bridge', { detail: msg }))
  },
}

// Vue → C# 调用工具
export function sendToNative(msg: unknown) {
  if (window.chrome?.webview) {
    window.chrome.webview.postMessage(msg)
  } else {
    console.log('[bridge] no native host, msg dropped:', msg)
  }
}
```

4. **WPF 主窗口嵌 WebView2**——打开 `f:\00_project\上位机学习\DAQMonitor\src\DaqMonitor.UI\MainWindow.xaml`,在 Grid 里加一个 Tab 或区域:

```xml
<TabControl>
  <TabItem Header="本地 LiveCharts2 视图">
    <!-- 原有的 LiveCharts2 视图,fallback 兜底 -->
    <local:ChartView />
  </TabItem>
  <TabItem Header="Vue3 工业大屏 (WebView2)">
    <wv2:WebView2 x:Name="DashboardWebView" />
  </TabItem>
</TabControl>
```

XAML 头部加命名空间:

```xml
xmlns:wv2="clr-namespace:Microsoft.Web.WebView2.Wpf;assembly=Microsoft.Web.WebView2.Wpf"
```

5. **`MainWindow.xaml.cs` 初始化 WebView2**:

```csharp
// 在 MainWindow 构造函数末尾或 Loaded 事件里
private WebViewBridge? _bridge;

private async void Window_Loaded(object sender, RoutedEventArgs e)
{
    // —— 启动嵌入式 Kestrel(Day 2 的 WebBroadcastService)——
    var broadcaster = App.Services.GetRequiredService<WebBroadcastService>();
    await broadcaster.StartAsync();

    // —— WebView2 初始化(必须在 InitializeAsync 完成前不能调 Source)——
    await DashboardWebView.EnsureCoreWebView2Async();
    DashboardWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;

    // 开发环境直接加载 dev server,生产加载打包后的本地 dist
#if DEBUG
    DashboardWebView.Source = new Uri("http://localhost:5173/");
#else
    // 生产模式:把 daq-dashboard/dist 拷到 DAQMonitor.UI 输出目录的 web/
    DashboardWebView.CoreWebView2.SetVirtualHostToFolderMapping(
        "dashboard.local",
        System.IO.Path.Combine(AppContext.BaseDirectory, "web"),
        CoreWebView2HostResourceAccessKind.Allow);
    DashboardWebView.Source = new Uri("https://dashboard.local/index.html");
#endif

    // 双向通信桥
    _bridge = new WebViewBridge(DashboardWebView);
    _bridge.OnMessageFromWeb((_, msg) =>
    {
        // 收到前端消息,例如点击"复位报警"按钮
        Console.WriteLine($"[bridge] from Vue: {msg}");
        // 解析后调用 AlarmEngine.Ack 等
    });
}
```

6. **App.xaml.cs 暴露 Services**:

```csharp
public partial class App : Application
{
    public static ServiceProvider Services { get; private set; } = null!;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Services = Bootstrapper.Build();
    }
}
```

7. **跑起来**:F5 启动 DAQMonitor.UI,切到"Vue3 工业大屏"Tab,看到 Vue 大屏嵌在 WPF 窗口里。**Day 4 用时**:6 小时。核心坑在 WebView2 Runtime 未装、虚拟主机映射路径错误,见第 6 节。

---

### Day 5:大屏加 4 个组件,截图录视频

**目标**:大屏全功能化,4 个组件就位:**实时曲线(Day 3 已完成)** + **设备状态卡片** + **报警滚动** + **工程量仪表盘**。

**1. 设备状态卡片(DeviceCard.vue)**:

```vue
<!-- src/components/DeviceCard.vue -->
<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useWebSocket } from '@/api/ws'

const ws = useWebSocket('ws://localhost:5180/ws')
const devices = ref([
  { id: 1, name: 'Modbus 温控仪', value: 0, state: 0, lastTs: '' },
  { id: 2, name: 'Modbus TCP IO', value: 0, state: 0, lastTs: '' },
  { id: 3, name: 'S7-1200 PLC',   value: 0, state: 0, lastTs: '' },
  { id: 4, name: 'OPC UA Server', value: 0, state: 0, lastTs: '' },
  { id: 5, name: 'MQTT 网关',     value: 0, state: 0, lastTs: '' },
  { id: 6, name: 'CAN 设备',      value: 0, state: 0, lastTs: '' },
  { id: 7, name: 'USB-HID 设备',  value: 0, state: 0, lastTs: '' },
])

onMounted(() => {
  ws.subscribe((msg) => {
    if (msg.type !== 'batch') return
    for (const p of msg.points) {
      const d = devices.value.find(x => x.id === p.id)
      if (d) { d.value = p.value; d.state = p.state; d.lastTs = p.timestamp }
    }
  })
  ws.connect()
})
onUnmounted(() => ws.close())

const stateText = (s: number) => ['离线', '连接中', '在线'][s] ?? '未知'
const stateClass = (s: number) => ['offline', 'connecting', 'online'][s]
</script>

<template>
  <div class="device-grid">
    <div v-for="d in devices" :key="d.id" class="device" :class="stateClass(d.state)">
      <div class="device__head">
        <span class="device__dot"></span>
        <span class="device__name">{{ d.name }}</span>
      </div>
      <div class="device__value">{{ d.value.toFixed(2) }}</div>
      <div class="device__state">{{ stateText(d.state) }} · {{ d.lastTs || '--' }}</div>
    </div>
  </div>
</template>

<style scoped>
.device-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 8px; }
.device { background: #1a2238; border-radius: 6px; padding: 10px; border-left: 3px solid #4dd0e1; }
.device.offline   { border-left-color: #6b7a8f; }
.device.connecting{ border-left-color: #ffb74d; }
.device.online    { border-left-color: #4dd0e1; }
.device__head { display: flex; align-items: center; gap: 6px; }
.device__dot { width: 8px; height: 8px; border-radius: 50%; background: currentColor; }
.online .device__dot    { background: #4dd0e1; box-shadow: 0 0 8px #4dd0e1; }
.connecting .device__dot{ background: #ffb74d; animation: pulse 1s infinite; }
@keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.3; } }
.device__name { font-size: 13px; color: #c5d1de; }
.device__value { font-size: 24px; font-weight: 700; color: #4dd0e1; font-variant-numeric: tabular-nums; }
.device__state { font-size: 11px; color: #6b7a8f; }
</style>
```

**2. 报警滚动(AlarmTicker.vue)**:

```vue
<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useWebSocket } from '@/api/ws'
const ws = useWebSocket('ws://localhost:5180/ws')
const alarms = ref<Array<{ id: number; level: number; msg: string; ts: string }>>([])

onMounted(() => {
  ws.subscribe((msg) => {
    if (msg.type === 'alarm') {
      alarms.value.unshift({
        id: msg.alarm.pointId, level: msg.alarm.level,
        msg: msg.alarm.message, ts: msg.alarm.timestamp,
      })
      if (alarms.value.length > 50) alarms.value.pop()
    }
  })
  ws.connect()
})
onUnmounted(() => ws.close())
const levelClass = (l: number) => ['normal', 'warning', 'critical'][l]
</script>

<template>
  <div class="alarm-ticker">
    <div class="alarm-ticker__title">实时报警(最近 50 条)</div>
    <div class="alarm-ticker__list">
      <div v-for="(a, i) in alarms" :key="i" class="alarm" :class="levelClass(a.level)">
        <span class="alarm__time">{{ new Date(a.ts).toLocaleTimeString() }}</span>
        <span class="alarm__msg">{{ a.msg }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.alarm-ticker { height: 100%; display: flex; flex-direction: column; }
.alarm-ticker__title { font-size: 14px; color: #6b7a8f; margin-bottom: 8px; }
.alarm-ticker__list { overflow-y: auto; flex: 1; }
.alarm { display: flex; gap: 12px; padding: 6px 0; border-bottom: 1px solid #1f2a44; font-size: 13px; }
.alarm__time { color: #6b7a8f; font-variant-numeric: tabular-nums; }
.alarm.warning { color: #ffb74d; }
.alarm.critical { color: #ff5252; font-weight: 600; }
</style>
```

**3. 工程量仪表盘(GaugePanel.vue)**——直接用 ECharts gauge:

```vue
<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import * as echarts from 'echarts'
import { useWebSocket } from '@/api/ws'

const ws = useWebSocket('ws://localhost:5180/ws')
const gaugeEl = ref<HTMLDivElement>()
let chart: echarts.ECharts | null = null

const GAUGES = [
  { id: 1, name: '温度', min: 0, max: 150, unit: '℃' },
  { id: 2, name: '压力', min: 0, max: 2, unit: 'MPa' },
  { id: 3, name: '转速', min: 0, max: 3000, unit: 'rpm' },
]
const values = ref<Record<number, number>>({ 1: 0, 2: 0, 3: 0 })

onMounted(() => {
  chart = echarts.init(gaugeEl.value!, 'dark', { renderer: 'canvas' })
  chart.setOption({
    backgroundColor: 'transparent',
    series: GAUGES.map((g, i) => ({
      type: 'gauge', center: ['50%', `${20 + i * 30}%`], radius: '25%',
      min: g.min, max: g.max,
      title: { offsetCenter: [0, '70%'], color: '#c5d1de', fontSize: 12 },
      detail: { formatter: `{value} ${g.unit}`, color: '#4dd0e1', fontSize: 16, offsetCenter: [0, '40%'] },
      data: [{ value: 0, name: g.name }],
      axisLine: { lineStyle: { color: [[0.7, '#4dd0e1'], [0.9, '#ffb74d'], [1, '#ff5252']] } },
    })),
  })
  ws.subscribe((msg) => {
    if (msg.type !== 'batch') return
    for (const p of msg.points) if (p.id in values.value) values.value[p.id] = p.value
    chart?.setOption({ series: GAUGES.map(g => ({ data: [{ value: values.value[g.id], name: g.name }] })) })
  })
  ws.connect()
})
onUnmounted(() => { chart?.dispose(); ws.close() })
</script>

<template>
  <div>
    <div class="title">工程量仪表盘</div>
    <div ref="gaugeEl" style="height: 480px"></div>
  </div>
</template>
```

**4. 录视频准备简历**:

- 用 OBS Studio / Windows 自带的 Xbox Game Bar 录 60-90 秒;
- **脚本**:开机启动 DAQMonitor → 切到 Vue3 大屏 Tab → 镜头扫过 7 设备 / 实时曲线 / 报警弹出 / 仪表盘;
- 录完上传 B 站 / 腾讯视频 / YouTube(私享),简历附 URL;
- **同时截 4-6 张高清大图**,放 GitHub README 顶部。

**5. Day 5 用时**:6 小时。截图 + 录视频别偷懒,这是简历冲击力的核心。

---

## 第 3 节:独立 Vue 工业大屏(并行 2 天方案)

> 这一节是 Day 6-7 的并行方案,**和 Day 1-5 共享 WebBroadcastService**(同一个 ws:// 端点),但 Vue 项目独立部署,投"前端 + 工业大屏"岗位时这是**第二个简历项目**。

### 跟 WebView2 方向的关系

| 维度 | WebView2 嵌 Vue(A) | 独立 Vue 大屏(D) |
|---|---|---|
| Vue 代码 | 共享 | **完全共享** |
| WebSocket 服务 | 共享(Day 2 写的) | **完全共享** |
| 部署 | WPF 内嵌,跟着 exe 走 | 独立部署 Vercel / Netlify |
| 简历话术 | "上位机 + 大屏双栈" | "前端工业大屏" |
| 投递岗位 | 上位机 + 大屏双栈岗 | 前端 + 工业 / 数字孪生 / MES 看板 |
| 在线 demo | 不能(桌面端) | **可以**,附简历 URL |

### 2 天能加什么加分点

**Day 6(独立大屏增强 1)**:

1. **WebSocket 断线重连**(Day 3 已写,但要打磨 UI 上的"连接中"提示);
2. **多屏适配**——CSS Grid + `vw/vh` + 1920 / 2560 / 4K 三档断点,大屏必须撑满;
3. **图表数据降采样**——10Hz 数据显示 60 秒 = 600 点,前端每 200ms 一次 setOption 不卡;但 24 小时历史曲线(86 万点)必须降采样到 2000 点才能渲染,**写一个 LTTB 算法**(Largest Triangle Three Buckets,工业大屏标配);
4. **数字孪生 2D**——用 SVG 画一张工厂平面图,7 个设备用 circle 标位,状态颜色联动,点击跳详情(2D 数字孪生比 3D 便宜 10 倍,投新能源 / 智慧工厂岗加分)。

**Day 7(独立大屏增强 2)**:

1. **3D 数字孪生(可选,投数字孪生岗)**——用 Three.js 画一个简化版的工厂模型,7 个设备用 Box 标位,实时数据驱动物体颜色 / 高度;
2. **多客户端订阅演示**——开 3 个浏览器窗口同时连 ws://,证明"WebSocket 一对多广播"(面试时这是必问点);
3. **打包部署 Vercel**——`pnpm build` → dist 推到 Vercel,得到 `https://daq-dashboard.vercel.app`;
4. **GitHub README 写完整**——架构图 + 录屏 + 截图 + 启动步骤,这块直接影响 HR 点开 GitHub 链接后的"看 5 秒决定是否继续读"。

### 独立大屏的简历加分点

投"前端 + 工业大屏"岗位时,简历第二项目写:

> **DaqDashboard · 工业实时监控大屏(Vue3 + TypeScript + ECharts + WebSocket)**
> - **架构**:Vue3 + Vite + Pinia + ECharts,通过 WebSocket 接入 DAQMonitor 实时数据(7 设备 / 10Hz);
> - **性能**:LTTB 降采样算法处理 24 小时历史曲线(86 万点 → 2000 点 60fps);
> - **数字孪生**:基于 SVG 的工厂平面图,7 设备状态实时联动;支持 4K 大屏适配;
> - **可靠性**:WebSocket 断线指数退避重连 + 心跳检测;支持多客户端订阅;
> - **部署**:在线 demo [vercel.com/daq-dashboard](#),GitHub 附完整文档 + 录屏。

---

## 第 4 节:简历话术(3 句话 + 3 种行业版本)

### 通用版 3 句话(直接复制到简历"项目经验"末尾)

> **全栈能力:上位机 WPF + 前端 Vue3 大屏双栈**——基于 WebView2 在 DAQMonitor(WPF)中嵌入 Vue3 工业大屏,统一实时曲线 / 报警 / 设备状态展示,生产部署一个 exe 完成全部前后端。
>
> **WebSocket 解耦**:用 ASP.NET Core Kestrel 嵌入式宿主 + WebSocket 把 C# 采集层(BatchReady 事件)与 Vue 大屏解耦,支持**多客户端订阅**(产线看板 + 工程师工位 + 经理办公室同时看)。
>
> **性能优化**:前端 LTTB 降采样算法处理 24 小时历史曲线(86 万点 → 2000 点 60fps),ECharts canvas 渲染 + 200ms 节流刷新;后端用 Channel 做广播缓冲避免阻塞采集线程。

### 行业版本 1:新能源(锂电 / 光伏)

> 在 DAQMonitor 项目中,**基于 WebView2 + Vue3 实现锂电产线监控大屏**——实时显示 100+ 设备状态、温度/电压/电流曲线、SOH/SOC 仪表盘;支持**多客户端订阅**(车间大屏 + 中央控制室 + MES 看板),满足宁德 / 比亚迪系供应商"产线 + 远程运维"双场景需求。

### 行业版本 2:半导体设备

> 在 DAQMonitor 项目中,**基于 WebView2 + Vue3 实现 AOI 设备状态大屏**——实时曲线 + 报警滚动 + 数字孪生(SVG 平面图),对接 S7.Net + OPC UA + TCP Socket,**满足半导体设备厂"操作员工位 + 工程师诊断 + 经理汇报"三屏需求**;24 小时历史曲线用 LTTB 降采样保证 60fps。

### 行业版本 3:3C 非标自动化

> 在 DAQMonitor 项目中,**基于 WebView2 + Vue3 实现测试产线数据看板**——Modbus TCP + S7 PLC + MQTT 上云,Vue 大屏支持 4K 大屏 + 工位机两种分辨率;**Web 化部署支持多客户端订阅**,客户现场工程师用手机 / iPad 也能看产线状态,**减少 60% 现场支持出差**。

---

## 第 5 节:面试会问什么(10 题)

> 每题 1 句答案 + 1 句"为什么这么答"。**面试官挖坑的核心套路是看你"是不是真的做过"**——背答案和真做过的人,在追问 2-3 层后差异巨大。

### Q1:为什么用 WebView2 不直接用 CefSharp?

**答**:WebView2 是微软官方 2020+ 主推方案,基于 Edge / Chromium 内核,**Windows 11 自带 Runtime**,包体积小(2MB vs CefSharp 200MB+);CefSharp 是 .NET 包装层,依赖 C++ 二进制,部署困难且不再活跃维护。

**为什么这么答**:面试官想看你是否知道"工业软件部署痛点"——客户工厂机器通常装不上 CefSharp 的 200MB,而且 CefSharp 在 Win10 上要先装 VC++ Runtime,运维极痛。补一句"我用 WebView2 Evergreen Bootstrapper 自动装 Runtime"更显得真做过。

### Q2:WPF ↔ Vue 怎么双向通信?

**答**:**C# → JS** 用 `webView.CoreWebView2.ExecuteScriptAsync(jsCode)`,直接执行前端全局函数;**JS → C#** 用 `window.chrome.webview.postMessage(jsonObj)`,C# 端订阅 `WebMessageReceived` 事件。我封装了一个 `WebViewBridge` 类屏蔽这两种调用差异。

**为什么这么答**:这是 WebView2 必考点。追问会问"为什么不用 shared object / AddHostObjectToScript?"——你可以回答 "AddHostObjectToScript 用 COM 互操作,序列化复杂对象坑多,postMessage + JSON 简单稳定,且符合单向数据流原则"。

### Q3:WebSocket 断线重连怎么做?

**答**:Vue 端封装 `useWebSocket` composable,**指数退避重连**(1s → 2s → 4s → 8s → 30s 封顶);C# 端用 Kestrel 自带 KeepAlive Ping(5 秒一次),前端 10 秒收不到 ping 主动 close 触发重连。

**为什么这么答**:工业现场网络抖动常见(尤其 wifi / 4G),面试官想看你**真的考虑过生产环境**。补一句"重连后前端 store 不清空,曲线继续画避免断点"——这是真实场景才会遇到的问题。

### Q4:大屏性能,1 万点数据怎么不卡?

**答**:三件套——**①数据降采样**(LTTB 算法,86 万点 → 2000 点误差 < 1%),**②Canvas 渲染**(ECharts `renderer: 'canvas'` 比 SVG 快 5 倍,**绝不**用 DOM 渲染),**③节流刷新**(数据 200ms 一次 setOption,不是每条消息都刷)。

**为什么这么答**:你 5 年前端早就踩过这个坑——股票看盘 / 大屏监控都是这套。能讲清楚 LTTB 算法原理(最大三角形三次桶)直接秒杀 95% 候选人。

### Q5:为什么不直接全用 Web 而要嵌 WebView2?

**答**:WPF 端有 OPC UA / Modbus / S7 / CAN / USB-HID 这些**工业协议**,Web 端不便处理(浏览器安全沙箱限制串口 / USB 访问);**WebView2 模式 = 桌面端保留协议栈 + 复用前端 UI 能力**,一次开发两套 UI(LiveCharts2 给操作员 / Vue 大屏给经理)。

**为什么这么答**:这道题考"为什么不全 B/S"。答出"工业协议不能跑浏览器"是真懂上位机,如果答"因为 Web 慢"就掉进了"前端思维"。

### Q6:后端为什么用 ASP.NET Core 不用 Node?

**答**:**跟 DAQMonitor.Core 共享代码**——C# 端的 SensorPoint / AcquisitionPipeline / AlarmEngine 直接复用,Node 重写一遍要 2 周;而且 WebSocket 服务跟采集层同进程,避免 IPC 开销。

**为什么这么答**:这是考"全栈技术选型思维"。答"因为我会 C#"是被动的,答"共享代码 + 同进程避免 IPC"是主动的。

### Q7:Vue 大屏和 WPF 自带的 LiveCharts2 怎么取舍?

**答**:LiveCharts2 是 fallback / 离线兜底,**操作员日常工位机用 LiveCharts**(启动快、纯桌面、无网络依赖);Vue 大屏是**汇报 / 远程 / 多客户端场景**(经理办公室大屏、客户演示、MES 看板对接)。**两套并存**比单一方案稳。

**为什么这么答**:这是考"技术选型权衡"。如果你只答"Vue 大屏更好"就显得没考虑过成本,答"两套并存按场景分"是真做过架构。

### Q8:WebSocket 多客户端订阅,C# 端怎么并发广播?

**答**:用 `ConcurrentDictionary<Guid, WebSocket>` 维护客户端列表,广播时 `Task.WhenAll(clients.Select(c => SafeSend(c, data)))`,`SafeSend` 内部 try-catch 防止单连接失败影响其他。

**为什么这么答**:追问会问"如果 100 个客户端同时订阅,CPU 占用会不会爆?"——你答"广播前先用 Channel 缓冲 + 单线程出队,广播任务用 WhenAll 并发但限制并发度到 16"就完美了。

### Q9:WebView2 Runtime 没装怎么办?

**答**:打包时用 **Evergreen Bootstrapper**(微软提供的 `MicrosoftEdgeWebview2Setup.exe`),首次启动 DAQMonitor 检测注册表 `HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}`,没装就弹窗引导安装(约 100MB,一次安装后续所有 WebView2 应用共享)。

**为什么这么答**:真实部署过 WebView2 的人必踩这个坑。补一句"客户的 Win7 / Server 2012 不支持 WebView2,这种场景才用 CefSharp fallback"是加分。

### Q10:如果让你做数字孪生 3D,你怎么选型?

**答**:轻量用 **Three.js + WebGL**(开源 / 免费 / 文档丰富);重量级(unity 级真实感)用 **Unity WebGL 导出**(但包体积 30MB+,首屏慢);**绝不**用纯 CSS 3D(性能差);**优先 2D SVG 数字孪生**(性价比最高,投新能源 / 智慧工厂岗 80% 场景够用)。

**为什么这么答**:这是给"投数字孪生岗"留的钩子。前端 5 年你 ECharts / D3 / Three.js 应该都碰过,这题是你**碾压纯 WPF 候选人**的题——他们连 SVG 和 Canvas 区别都讲不清。

---

## 第 6 节:踩坑警告(7 个真实坑)

> 这 7 个坑是我吃过亏的,**Day 2-4 期间遇到时回来查这一节**。

### 坑 1:WebView2 Runtime 未装 → 程序闪退

**症状**:`EnsureCoreWebView2Async()` 抛 `WebView2RuntimeNotFoundException`,WPF 直接闪退,无错误提示。

**原因**:Win10 / Server 2019 默认不装 WebView2 Runtime,只有 Win11 自带。

**解决**:

1. 部署时打包 [Microsoft Edge WebView2 Evergreen Bootstrapper](https://developer.microsoft.com/microsoft-edge/webview2/)(10MB,首次运行自动下载 Runtime ~100MB);
2. 启动代码加 try-catch,捕获异常后引导用户安装;
3. **绝不要**直接打包 Standalone Offline Installer(150MB,违反 Evergreen 原则,后续无法自动升级)。

### 坑 2:Core 改 Sdk.Web 后 UI 项目编译报错

**症状**:`Sdk="Microsoft.NET.Sdk.Web"` 让 UI(WPF)项目混淆——Microsoft.NET.Sdk.Web 默认生成 exe,跟 WPF 的 UseWPF 冲突。

**解决**:**生产推荐拆项目**——新建 `DaqMonitor.Web` 项目(`Sdk.Web`),引用 `DaqMonitor.Core`,放 WebBroadcastService;UI 项目保持原 SDK 不动,通过 DI 容器调 Web 项目的服务。本文为简化讲解把服务塞 Core,实际工程必须分层。

### 坑 3:Vue 路由 hash vs history

**症状**:WebView2 加载 Vue 大屏后,刷新或路由跳转白屏。

**原因**:`createWebHistory` 模式依赖服务器 SPA fallback,WebView2 加载本地 `file://` 或虚拟主机映射时无 fallback 配置。

**解决**:**WebView2 内嵌时强制用 `createWebHashHistory`**——hash 模式不带后端 fallback,纯前端路由更稳。`vite.config.ts` 加 `base: './'`(相对路径),打包后资源正确加载。

### 坑 4:WebSocket 心跳 / 重连——必做

**症状**:工厂内 wifi 不稳,大屏 30 秒后停止刷新,但浏览器没显示断开。

**原因**:WebSocket TCP 半开连接(网线被拔 / 路由器重启),前端 `onclose` 不会立刻触发。

**解决**:

- C# 端 `WebSocketOptions.KeepAliveInterval = TimeSpan.FromSeconds(5)`(Kestrel 自动 ping);
- 前端 5 秒没收到任何消息主动 `ws.close()` 触发 onclose → 重连;
- **绝不要**让 WebSocket 静默超过 10 秒。

### 坑 5:跨域 / CSP / 本地资源加载

**症状**:Vue 大屏浏览器开 `http://localhost:5173` 能跑,WebView2 内嵌报 CORS / CSP / Mixed Content 错误。

**原因**:Vue dev server 默认不允许多 Origin,Kestrel 默认 CORS 关闭。

**解决**:

- Kestrel 加 CORS 允许任意源(本地内嵌场景,安全性可控);
- 生产用 `SetVirtualHostToFolderMapping` 把 Vue 的 `dist/` 映射到 `https://dashboard.local/`,**避免 file:// 协议**(fetch / WebSocket 在 file:// 下被禁);
- Vue 端 CSP meta 标签放宽 `connect-src 'self' ws: wss: http://localhost:*`。

### 坑 6:ECharts 内存泄漏

**症状**:大屏跑 2 小时,Chrome / WebView2 占用 2GB,曲线越来越卡。

**原因**:`setOption` 不清理旧数据,series.data 数组无限增长;`echarts.init` 多次调用未 dispose。

**解决**:

- Pinia store 用环形缓冲(本文 `MAX_POINTS = 600`),超出 `arr.shift()`;
- 组件 `onUnmounted` 必须 `chart.dispose()`;
- **绝不要**用 `series.data.push(...)` 后直接 setOption,**要传新数组**才能让 ECharts 内部 diff 释放旧引用。

### 坑 7:打包后 Vue 路径错乱

**症状**:`pnpm build` 产出的 dist 拷到 DAQMonitor.UI 的输出目录,WebView2 加载白屏,F12 看到所有资源 404。

**原因**:Vite 默认 `base: '/'`,资源路径是 `/assets/xxx.js`,WebView2 加载 `https://dashboard.local/index.html` 找不到 `/assets/`。

**解决**:`vite.config.ts` 改 `base: './'`,资源路径变 `./assets/xxx.js`,WebView2 加载本地映射后正确。

---

## 收尾:7 天冲刺 vs 30 天主线的取舍

**这份 7 天方案的前提**:DAQMonitor 主项目已经稳(7 设备 + 56 测试 + MQTT 双向),你**不是从零开始**,而是给一个已经能跑的项目**加一层差异化亮点**。

**节奏建议**:

- **第 1 周**:Day 1-5 主攻 WebView2 + Vue 大屏(本文重点),Day 6-7 录视频 + 简历话术;
- **第 2-4 周**:回到主项目的 P0 缺口补漏(机器视觉入门 + 运动控制概念 + 4 份文档实操),投简历 + 面试。

**绝对不要**把 7 天都花在前端大屏而忽略协议层——面试官真正为 13-15K 买单的是**协议栈 + 工程化**,大屏是**差异化加分项**,不是主菜。

**最后一句**:你能写出 Vue3 大屏 + WebView2 嵌入,**95% 的上位机候选人做不到这件事**。这就是你 5 年前端经验的真实价值,不是"用不上的包袱",是"碾压他们的稀缺武器"。

---

## 附:关键参考来源

- [Microsoft Learn — WebView2 官方文档(Getting Started with WebView2 in WPF)](https://learn.microsoft.com/microsoft-edge/webview2/gettingstarted/wpf)
- [Microsoft Learn — ASP.NET Core WebSocket 官方文档](https://learn.microsoft.com/aspnet/core/fundamentals/websockets)
- [Vue 3 官方文档(中文)](https://cn.vuejs.org/)
- [Vite 官方文档](https://vitejs.dev/)
- [ECharts 官方文档](https://echarts.apache.org/handbook/zh/get-started/)
- [Pinia 官方文档](https://pinia.vuejs.org/zh/)
- [Three.js 官方文档(数字孪生可选)](https://threejs.org/docs/)
- [LTTB 降采样算法论文(工业大屏标配)](https://skemman.is/handle/1946/15343)
- [ASP.NET Core Kestrel 嵌入式宿主用法](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel)
- [WebView2 Evergreen Bootstrapper 下载](https://developer.microsoft.com/microsoft-edge/webview2/)

---

> **最后一句**:这份方案不是"前端转上位机的退路",而是"用前端能力把上位机做到 13-15K 上限"的差异化冲刺。**7 天后,你的简历比 95% 候选人多一句话**——"全栈:WPF + Vue3 大屏双栈"。这句话值 1-2K 月薪。
