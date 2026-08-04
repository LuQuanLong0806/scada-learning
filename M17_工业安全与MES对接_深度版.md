# M17 — 工业安全与 MES/ERP 对接（HttpClient + REST）🔐

> **优先级定位**：🔴 必学（15K 加分项）· 工业网络安全 + MES/ERP WebApi 对接（JD 高频出现："熟悉与上位系统对接"、"了解工业网络安全规范"）
> **技术来源**：🟦 `HttpClient` + `Microsoft.Extensions.Http`（BCL，HttpClientFactory + DI）、🟦 `System.Text.Json`（含 SourceGeneration）、🟧 `Polly`（重试 + 熔断，业界事实标准）、🟦 `System.Net.Http.Json`（`PostAsJsonAsync` 扩展）。
> **前端类比总纲**：HttpClient 就是 `axios`/`fetch`，REST 还是 REST，JWT 还是 Bearer Token，Polly 就是 `axios-retry` + React Query 的 retry/circuit-breaker。前端会的网络层套路，到这里 80% 平移——剩下 20% 是**工业现场**加的硬约束:IT/OT 双网卡、白名单、审计、单向网闸。
> **给简历加的能力**：把上位机从"采集设备数据"升级成"**接入企业 IT 基础设施**"——向 MES 上报完工、从 ERP 拉工单、把报警推到 SCADA，并且**符合工业网络安全规范**（IEC 62443 思想）。这是 JD 里"对接上位系统 / 信息安全"那条的真实落地，13→15K 的硬通货。
> **前置**：M4（持久化，审计日志要落库）、M9（DI 容器 + 异步容错，HttpClient 必须 DI），M7（OPC UA / MQTT 走的是工业协议，这里走的是 IT 协议，互为补充）。

> ⏱️ **阅读路径**(按时间预算选入口)
> - **3 分钟**:看「模块目标」— 知道 MES 对接 = HttpClient + REST + JWT,跟 axios 80% 像
> - **30 分钟**:加看 Day 1 HttpClientFactory + DI + Polly 重试
> - **3 小时**:全文精读 + Day 2 **IT/OT 双网卡/白名单/审计日志** + Day 3 JWT 鉴权
> - 🎯 **面试高频**:**HttpClientFactory 为什么不能直接 new HttpClient(socket 耗尽)** / Polly retry+circuit-breaker / IT/OT 物理隔离 / IEC 62443 思想
> - 🔁 **配套复习**:[代码肌肉 B21 白名单 / B22 AuditLogger](代码肌肉训练手册_30天刷题版.md) · [间隔重复表](记忆与复习机制_间隔重复版.md)

> 📚 **前置语法**(M17 用到的,陌生请查 [C# 语法速查 — 前端视角](CSharp语法速查_前端视角.md))
> - `_client.PostAsJsonAsync("/api/mes/report", payload, ct)` — HttpClient + JSON,速查 §8
> - `services.AddHttpClient<MesClient>(c => c.BaseAddress = new Uri(url))` — HttpClientFactory + DI
> - `record MesReport(string OrderId, double Qty, DateTime Time)` — 不可变 DTO,速查 §12
> - `class MesClient : IMesClient` — 接口抽象(便于 Mock 测试)
> - `try { ... } catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)` — 异常过滤,速查 §9
> - `await Policy.Handle<HttpRequestException>().WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(Math.Pow(2, i))).ExecuteAsync(...)` — Polly 重试

## 模块目标
让上位机：① 用 `HttpClient` 通过 HttpClientFactory + DI 对接 MES/ERP REST API（含 JSON 序列化、JWT、重试、熔断）；② 满足基本的**工业网络安全**要求（IT/OT 双网卡、白名单、审计日志、凭证管理）；③ 知道 IT/OT 边界在哪儿——为什么工厂要把生产网和办公网物理隔离，为什么不能直接 `http://` 走公网。

---

## Day 1 — HttpClient 对接 MES REST API 🟡

### 一句话讲清楚
MES（Manufacturing Execution System，制造执行系统）= 工厂的"车间大脑"，管**工单下发 / 工艺参数 / 质量追溯 / 设备状态**；上位机要把它采到的数据（产量、温度、报警）按 REST 接口上报，比如 `POST http://mes.company.com/api/productions/{id}/complete`。HttpClient 就是干这事的 axios——但**绝不能 `new HttpClient()`**，会 socket 耗尽。

### 前端类比秒懂
| 上位机（C#） | 前端 |
|---|---|
| `HttpClient` | `axios` / `fetch` |
| `HttpClientFactory` + DI | axios 实例池 + React Provider 注入 |
| REST API（`GET/POST/PUT/DELETE`） | RESTful 后端 |
| JWT Bearer Token | `Authorization: Bearer xxx` |
| `Polly`（重试 + 熔断） | `axios-retry` + React Query 的 `retry` / circuit breaker |
| 工厂内网（`http://mes.company.com`） | 公司 VPN 内网 API |
| `System.Text.Json`（源生成） | `JSON.stringify` + zod schema |

### 分点精讲

**① HttpClientFactory + DI（⭐ 第一条铁律）**
老写法 `new HttpClient()` 有两个坑：一是底层 socket 不会立即释放，高频调用会**耗尽 TCP 端口**（Time-Wait 堆积）；二是 DNS 变更不生效（实例长存活，DNS 缓存死）。正确做法是注册到 DI 容器，由 `HttpClientFactory` 统一管理连接池、定时回收、DNS 刷新。

```csharp
// Bootstrapper.cs（Program.cs 或 Startup.cs）
services.AddHttpClient("mes", c =>
{
    c.BaseAddress = new Uri("http://mes.company.com/api/");
    c.Timeout = TimeSpan.FromSeconds(10);                 // 默认 100s 太长，必改
    c.DefaultRequestHeaders.Add("X-Client", "DaqMonitor");
})
.SetHandlerLifetime(TimeSpan.FromMinutes(2))              // 2 分钟换一次底层 Handler，触发 DNS 刷新
.AddPolicyHandler(GetRetryPolicy())                       // 重试策略
.AddPolicyHandler(GetCircuitBreakerPolicy());             // 熔断策略
```
> 命名客户端 `"mes"`——因为后面可能还有 `"erp"`、`"scada"`，各自 BaseAddress/Token 不同。

**② Polly 重试 + 熔断（🟧 业界事实标准）**
```bash
dotnet add package Microsoft.Extensions.Http.Polly
dotnet add package Polly.Extensions.Http
```
```csharp
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
    HttpPolicyExtensions.HandleTransientHttpError()       // 5xx + 408
        .WaitAndRetryAsync(3,
            attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),   // 2/4/8 秒指数退避
            (outcome, delay, retry, ctx) =>
                Console.WriteLine($"第 {retry} 次重试，等 {delay.TotalSeconds}s"));

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy() =>
    HttpPolicyExtensions.HandleTransientHttpError()
        .CircuitBreakerAsync(
            exceptionsAllowedBeforeBreaking: 5,           // 连续 5 次失败就熔断
            durationOfBreak: TimeSpan.FromSeconds(30));   // 熔断 30s 内直接 fail-fast，不打 MES
```
> 熔断意义：MES 挂了就别打死它了，30 秒后再探一下——比让 100 个工位同时疯狂重试把 MES 打瘫强百倍。

**③ JSON 序列化（🟦 `System.Text.Json` + 源生成）**
默认大小写规则和前端不一样：C# 属性 `WorkOrderId` 默认序列化成 `"WorkOrderId"`，MES 一般要 `"workOrderId"`（camelCase）。配 `JsonSerializerOptions`：
```csharp
services.AddHttpClient("mes", c => { /* ... */ })
    .AddHttpMessageHandler(() => new ForceCamelCaseHandler());  // 或者直接在 PostAsJsonAsync 传 options

public static readonly JsonSerializerOptions Camcel = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};
```
> 性能进阶：用 `System.Text.Json.SourceGeneration`（NuGet）在编译期生成序列化代码，比反射快 2-3 倍、内存省一半——大流量上报必上。

**④ 认证（Bearer Token / API Key）**
```csharp
c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
c.DefaultRequestHeaders.Add("X-API-Key", apiKey);   // 部分老 MES 用 API Key
```
> JWT 过期要自动刷新——见挑战题。工厂多走 **Service Account**（服务账号，长期 token + IP 白名单），不走用户交互 OAuth2。

**⑤ 错误处理：千万别 `EnsureSuccessStatusCode` 一刀切**
MES 返回 409（工单已完工）、401（token 过期）、400（参数错）业务语义各不同，全抛 `HttpRequestException` 等于丢掉信息。要按状态码分支转成**业务异常**：
```csharp
public async Task ReportCompletionAsync(string workOrderId, int qty, CancellationToken ct)
{
    var client = _factory.CreateClient("mes");
    var resp = await client.PostAsJsonAsync(
        $"productions/{workOrderId}/complete",
        new { quantity = qty, timestamp = DateTime.UtcNow },
        cancellationToken: ct);

    switch (resp.StatusCode)
    {
        case HttpStatusCode.Conflict:    // 409 工单已完工
            throw new MesConflictException("工单已完工，不能重复上报");
        case HttpStatusCode.Unauthorized:// 401 JWT 过期
            throw new TokenExpiredException("JWT 过期，需重新登录或刷新");
        case HttpStatusCode.BadRequest:  // 400 参数错，读 Body 里的错误信息
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new MesValidationException($"MES 拒绝: {body}");
    }
    resp.EnsureSuccessStatusCode();      // 其余 5xx 走 Polly 重试
}
```

### ⚠️ 重点 & 易踩坑
| 项 | 说明 |
|---|---|
| ⭐ 绝不 `new HttpClient()` | socket 耗尽 + DNS 不刷新，老项目改造第一条；HttpClientFactory + 命名客户端是标准答案 |
| ⭐ 必设 `Timeout` | 默认 100s 太长，MES 卡住会让 UI 卡死；工厂内网一般 5-10s |
| ⭐ 用 `CancellationToken` 透传 | 关窗/退出时 `ct` 取消，否则后台请求拖住进程退不掉 |
| 🔥 JSON 大小写 | C# `WorkOrderId` 默认序列化 `WorkOrderId`，MES 多要 `workOrderId`，必配 `CamelCase` |
| 🔥 HTTPS 证书 | 内网自签证书会让请求报 `SSL 证书链错误`；测试环境可 `HttpClientHandler.ServerCertificateCustomValidationCallback`，**生产绝不关验证** |
| 🔥 代理穿透 | 工厂常走代理，`HttpClient.DefaultProxy` 或 `WebProxy` 要配；否则你以为"连不上 MES"其实卡在代理 |
| 🔥 别 `.Result` / `.Wait()` | 同步阻塞会死锁（SynchronizationContext），UI 线程死锁最痛 |
| ⭐ 4xx 不重试 | `HandleTransientHttpError` 只重试 5xx + 408，4xx 是业务错，重试等于打日志刷屏 |

### 🟢 基础题
注册一个名为 `"erp"` 的 HttpClient，BaseAddress `http://erp.company.com/api/`，超时 8 秒。写一个 `ErpClient.GetWorkOrderAsync(string id)` 用 `GetFromJsonAsync<WorkOrderDto>` 反序列化。

### 🟡 进阶题
给 `"mes"` 客户端加 Polly：3 次指数退避重试 + 连续 5 次失败熔断 30s。故意把 BaseAddress 改成错地址，观察日志里"第 N 次重试"和"熔断中"的输出。

### 🔴 挑战题
实现 JWT 自动刷新：写一个 `DelegatingHandler`，拦截响应——如果 401，就用 refresh_token 调 `/auth/refresh` 拿新 token、替换 `HttpRequestMessage.Headers.Authorization`、重新发一次请求（只重试一次，防死循环）。

**✅ 答案（基础题）**
```csharp
// 注册
services.AddHttpClient("erp", c =>
{
    c.BaseAddress = new Uri("http://erp.company.com/api/");
    c.Timeout = TimeSpan.FromSeconds(8);
});

// 使用
public class ErpClient
{
    private readonly IHttpClientFactory _factory;
    public ErpClient(IHttpClientFactory factory) => _factory = factory;

    public async Task<WorkOrderDto?> GetWorkOrderAsync(string id, CancellationToken ct)
    {
        var client = _factory.CreateClient("erp");
        return await client.GetFromJsonAsync<WorkOrderDto>($"workorders/{id}", ct);
    }
}

public class WorkOrderDto { public string Id { get; set; } = ""; public int Qty { get; set; } public string ProductCode { get; set; } = ""; }
```

### 💡 工控导师说（真实战例）
> 我在某汽车零部件厂，上位机一上线就报"连不上 MES"。查了一下午，发现是**老代码 `new HttpClient()`**——每秒上报一次，TCP 端口被 TIME-WAIT 占满。改成 `AddHttpClient("mes")` 之后，端口稳稳的在几百个，再没出过事。从那以后我看任何 HTTP 代码，第一眼就搜有没有 `new HttpClient(`——看到就 PR 改掉。
> 第二条：MES 那边的同事最恨你**不打日志就疯狂重试**——他们服务器被打瘫过几次。Polly 的 retry 回调里**一定要 `Log` 出来**，最好把 MES 返回的状态码、Body 也带上，联调时双方有共同证据。

### 🎓 职业建议
面试被问"你怎么对接 MES"——
- **15K 答**："HttpClientFactory + 命名客户端，Polly 做重试熔断，System.Text.Json 源生成加速，按状态码分支抛业务异常。"
- **13K 答**："`new HttpClient()` 调 `PostAsync`。" → 立刻被刷。

这两段话的差距就是 2K 月薪。会 HttpClientFactory + Polly 是分水岭。

### ⚠️ 常见坑点预警
| 坑 | 现象 | 对策 |
|---|---|---|
| `new HttpClient()` | 跑几小时后 socket 耗尽报 "Only one usage of each socket address" | 改 HttpClientFactory |
| DNS 变更不生效 | MES 切换了 IP，上位机还连老的 | `SetHandlerLifetime` ≤ 3 分钟 |
| 同步阻塞 | UI 卡死 / 死锁 | 全 `async/await`，禁 `.Result` |
| HTTPS 自签证书 | `Could not establish trust relationship` | 测试关验证 / 生产装根证书 |
| 4xx 重试 | 业务错被无限重试 | 只重试 5xx，4xx 抛业务异常 |

### 📚 延伸阅读
- Microsoft 官方 `IHttpClientFactory` 文档：https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory
- Polly GitHub（含熔断 / 退避完整示例）：https://github.com/App-vNext/Polly
- `System.Text.Json` 源生成：https://learn.microsoft.com/dotnet/standard/serialization/system-text-json-source-generation

### 🏗️ 项目任务
给 DAQ Monitor 加 `MesClient`：每小时把当班产量上报 MES `POST /api/productions/{id}/complete`。要求：HttpClientFactory 注册、Polly 重试熔断、按状态码分支、写 Serilog 日志。

### ✅ 打卡[ ]

---

## Day 2 — 工业网络安全：白名单 + 审计 + IT/OT 隔离 🔴

### 一句话讲清楚
工厂网络分两层：**IT 网**（办公，能联外网，威胁多）和 **OT 网**（生产，设备 PLC/上位机，**断网或单向隔离**）。上位机常"跨两边"——左边读 PLC（OT），右边报 MES（IT）。所以要做三件事：① **白名单**（只允许连指定的 IP/域名）；② **审计日志**（谁在什么时候改了什么设定值，落库可追溯）；③ **凭证管理**（密码不能硬编码）。这套思想来自 **IEC 62443**（工业自动化信息安全国际标准），不是研发拍脑袋。

### 前端类比秒懂
| 工业安全 | 前端 / Web |
|---|---|
| IT/OT 双网卡隔离 | 内网 / 外网双网卡服务器 |
| 工业防火墙白名单 | nginx `allow/deny` + WAF |
| 单向网闸（数据只出不进） | CDN 单向推送 |
| 审计日志（设定值变更） | Sentry / access log / 操作日志 |
| IEC 62443 | GDPR / SOC2 / 等保 |
| HSM / Windows Credential Manager | HashiCorp Vault / AWS Secrets Manager |
| 离线更新 + 签名验证 | 子资源完整性 SRI + 内网镜像 |

### 分点精讲

**① IT/OT 双网卡架构（⭐ 核心概念）**
```
   IT 网 (192.168.1.x)              OT 网 (192.168.10.x)
   ┌──────────┐    ┌──────────┐    ┌──────────┐
   │  MES/ERP │◄───│  上位机   │───►│  PLC/仪表 │
   └──────────┘    │ 双网卡PC │    └──────────┘
                   │ 网卡1:IT │
                   │ 网卡2:OT │
                   └──────────┘
```
上位机插两张网卡：网卡 1 连 IT（汇报 MES），网卡 2 连 OT（读 PLC）。**路由表配死**——OT 网段不走 IT 网卡，反之亦然。这样即使 IT 网中病毒，也难横向打穿到 PLC。这就是 IEC 62443 的"区域与通道"(Zone & Conduit) 思想。

**② 应用层白名单（防止被诱导连陌生地址）**
MES 地址应该来自配置文件，且运行时校验——避免配置被改成恶意地址（比如内鬼把 BaseAddress 改成自己的钓鱼服务器）。
```csharp
public class AllowedHostsValidator
{
    private readonly HashSet<string> _allowed;
    public AllowedHostsValidator(IConfiguration cfg)
        => _allowed = cfg.GetSection("AllowedHosts").Get<string[]>()?.ToHashSet() ?? new();

    public bool IsValid(Uri uri) => _allowed.Contains(uri.Host);
}

// appsettings.json:
// "AllowedHosts": [ "mes.company.com", "erp.company.com" ]

// 使用：HttpClientFactory 包一层 DelegatingHandler 拦截
public class WhitelistHandler : DelegatingHandler
{
    private readonly AllowedHostsValidator _v;
    public WhitelistHandler(AllowedHostsValidator v) => _v = v;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        if (req.RequestUri is not null && !_v.IsValid(req.RequestUri))
            throw new SecurityException($"禁止访问非白名单主机: {req.RequestUri.Host}");
        return base.SendAsync(req, ct);
    }
}
```

**③ 审计日志（谁改了什么设定值）**
工厂里改 PLC 设定值（比如把温度上限从 80℃ 改成 90℃）是**大事**——出了质量事故要追责。每次写操作前后落一条 `AuditLog`，含：操作人、动作、目标点、旧值、新值、时间。M6 的 Serilog 是"运行日志"，这里是"**业务审计**"——分开放。
```csharp
public record AuditEntry(
    string User, string Action, string Target,
    double? OldValue, double? NewValue, DateTime Time);

public class AuditLogger
{
    private readonly IDbContextFactory<AppDb> _factory;
    public AuditLogger(IDbContextFactory<AppDb> factory) => _factory = factory;

    public async Task LogAsync(AuditEntry entry, CancellationToken ct)
    {
        await using var db = _factory.CreateDbContext();
        db.AuditLog.Add(entry);
        await db.SaveChangesAsync(ct);
    }
}

// PlcDevice.Write 前后包一层
public async Task WriteSetpointAsync(int pointId, double newValue, string user, CancellationToken ct)
{
    double oldValue = await ReadAsync(pointId, ct);
    await _plc.WriteAsync(pointId, newValue, ct);
    await _audit.LogAsync(
        new AuditEntry(user, "WriteSetpoint", $"point#{pointId}",
                       oldValue, newValue, DateTime.UtcNow), ct);
}
```
> 注意：审计日志的写库失败**不能让业务回滚**（不然改了设定值却因为日志写不进就回滚 PLC，更乱）——用 `try/catch` 包住，失败进 Serilog 告警即可。

**④ 凭证管理：密码不硬编码**
```csharp
// ❌ 反面教材
var token = "eyJhbGciOi...";  // 提交到 git，第二天公司上头条

// ✅ 正解：环境变量 / Windows Credential Manager / appsettings（不入库）
var token = Environment.GetEnvironmentVariable("MES_JWT");
// 或：DPAPI 加密存 appsettings.json，启动时解密
```
> 进阶：Windows Credential Manager（`CredentialManagement` NuGet）或公司统一 KDC / Vault——这一块由 IT 部门管，你只要做到"密码不出现在源码"就过 60 分及格线。

**⑤ 更新安全：签名验证 + 离线更新**
工厂上位机**绝不开 Windows 自动更新**（更新可能导致蓝屏停线）。补丁走"**测试环境验证 → 离线包签名 → 现场手动装**"流程。你的应用更新同理——OTA 下载后**先验签名**（RSA / Ed25519）再解压执行，防止供应链投毒。

### ⚠️ 重点 & 易踩坑
| 项 | 说明 |
|---|---|
| ⭐ IT/OT 路由配死 | 双网卡不配路由表，Windows 默认会"跨网卡转发"，等于没隔离 |
| ⭐ 白名单要包含 URI Host | 别只校验 BaseAddress，请求时改 Uri 也能绕过——校验每个出站请求 |
| ⭐ 审计日志写失败要降级 | 不能因为日志库挂了就让 PLC 写失败，业务优先 |
| 🔥 凭证不入 git | 用 `dotnet user-secrets` 开发、环境变量部署、K8s Secret 上云 |
| 🔥 HTTPS 内网也要 | "内网就安全"是错觉，内网横向攻击最常见；至少自签 CA 走 HTTPS |
| 🔥 日志脱敏 | Authorization、API Key 进日志前要打码，不然日志泄露=密码泄露 |
| 🔥 时间用 UTC | 审计日志存 UTC + 时区元信息，跨厂区/跨国才不乱 |
| ⭐ 别关 Windows Defender | 工控圈有"为了性能关杀软"的歪风，IEC 62443 直接判不合格 |

### 🟢 基础题
写一个 `AllowedHostsValidator`，从 `appsettings.json` 读 `AllowedHosts` 数组，给一个 URI 判断是否在白名单内。写两个测试用例：白名单内通过、白名单外抛 `SecurityException`。

### 🟡 进阶题
把 `WhitelistHandler`（`DelegatingHandler`）挂到 `"mes"` 客户端上（`services.AddHttpClient("mes").AddHttpMessageHandler<WhitelistHandler>()`），故意把 BaseAddress 改成 `http://evil.com`，验证请求被拦截。

### 🔴 挑战题
给 `PlcDevice.WriteAsync` 加审计：每次写都记一条 `AuditEntry`（旧值 → 新值）。再写一个测试：故意把审计库断开（用 `Microsoft.Data.Sqlite` In-Memory 故意 dispose），验证**业务写入不回滚**、Serilog 告警。

**✅ 答案（基础题）**
```csharp
public class AllowedHostsValidator
{
    private readonly HashSet<string> _allowed;
    public AllowedHostsValidator(IConfiguration cfg)
        => _allowed = cfg.GetSection("AllowedHosts").Get<string[]>()?.ToHashSet() ?? new();
    public bool IsValid(Uri uri) => _allowed.Contains(uri.Host);
}

[Fact]
public void IsValid_Whitelisted_ReturnsTrue()
{
    var cfg = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts:0"] = "mes.company.com"
        }).Build();
    var v = new AllowedHostsValidator(cfg);
    Assert.True(v.IsValid(new Uri("http://mes.company.com/api/x")));
}

[Fact]
public void IsValid_NotWhitelisted_ThrowsInHandler()
{
    var v = new AllowedHostsValidator(new ConfigurationBuilder().Build());
    Assert.False(v.IsValid(new Uri("http://evil.com/x")));
}
```

### 💡 工控导师说（真实战例）
> 某化工厂出过事——操作工把反应釜温度上限从 120℃ 改到 145℃ 想多出产量，结果差点超压爆釜。事后查日志，**上位机没记谁改的、什么时候改的**，甩锅甩了三天。从此我所有项目**设定值改动必落 AuditLog**，且 Serilog 同步打 `[AUDIT]` 标签。这条规矩救过我两次。
> 第二条：某客户被勒索病毒打穿，源头就是上位机的 IT 网卡中招，横向打到 PLC 工程师站。后来我们做了**强制 IT/OT 双网卡 + Windows Defender 不关 + 白名单只放 MES 三件套**，三年没再出过事。安全不是技术问题，是**有没有人较真**。

### 🎓 职业建议
面试问"你了解工业网络安全吗"——
- **15K 答**："了解 IEC 62443 的 Zone & Conduit 思想，我们做 IT/OT 双网卡物理隔离，应用层加白名单 DelegatingHandler，设定值改动落 AuditLog 表，密码走环境变量不入 git。"
- **13K 答**："内网嘛，应该挺安全的。" → 被刷。

工厂面试官（尤其外企、汽车、半导体）非常吃这套——因为他们走过审计（TISAX / IEC 62443 认证），知道一条审计日志能省多少赔偿金。

### ⚠️ 常见坑点预警
| 坑 | 现象 | 对策 |
|---|---|---|
| IT/OT 路由没配死 | OT 网中病毒横向感染 PLC | 路由表 + 防火墙策略双保险 |
| 白名单只校验 BaseAddress | 改 Uri 绕过 | 拦截每个出站 `HttpRequestMessage` |
| 审计库挂导致业务挂 | 改设定值失败 | 审计异步、降级，业务优先 |
| 密码硬编码入 git | 提交即泄露 | user-secrets / 环境变量 / Vault |
| 日志打印完整 Token | 日志泄露=密码泄露 | 打码 `eyJh***.xyz` |

### 📚 延伸阅读
- IEC 62443 系列标准（工业自动化信息系统安全）：https://www.isa.org/standards-and-publications/isa-standards/isa-iec-62443-series-of-standards
- OWASP 工控安全 Top 10：https://owasp.org/www-project-top-ten/
- Microsoft Vault / DPAPI 加密配置：https://learn.microsoft.com/dotnet/core/extensions/configuration-secrets

### 🏗️ 项目任务
DAQ Monitor 加三件套：① `AllowedHostsValidator` + `WhitelistHandler` 挂到所有 HttpClient；② `AuditLogger` + `AuditEntry` 表（EF Core），所有 `PlcDevice.WriteAsync` 前后记一条；③ 把 MES JWT 移到环境变量 `MES_JWT`，启动时读不到就报错退出。

### ✅ 打卡[ ]

---

## 📌 温故知新（跨模块联动）
- **M9 DI 容器 → 这里 HttpClientFactory**：HttpClient 必须通过 `services.AddHttpClient` 注册，由 DI 容器管生命周期——和 M9 的服务注册一脉相承。
- **M4 EF Core → 这里 AuditLog**：`IDbContextFactory` 复用 M4 的库，加一张 `AuditLog` 表；审计查询走 M4 的仓储模式。
- **M6 Serilog → 这里运行日志 + 审计分离**：Serilog 打运行态（含 Polly 重试、HTTP 状态码），`AuditLog` 打业务态（设定值变更）。**两套日志不要混**——运行日志 7 天滚动，审计日志保留 3 年（合规要求）。
- **M7 OPC UA / MQTT → 这里 HttpClient**：M7 走工业协议（OT 侧），M17 走 IT 协议（IT 侧）——上位机是两者之间的"翻译官"。
- **前瞻**：这套白名单 + 审计 + DI 的架构，是后续接入 SCADA / IIoT 平台（如 Azure IoT、AWS IoT）的底座，复用度极高。

## 📚 延伸阅读（卡点时点开）
- `IHttpClientFactory` 官方最佳实践：https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory
- Polly 完整示例（重试 / 熔断 / 超时 / 舱壁）：https://github.com/App-vNext/Polly#resilience-pipelines
- IEC 62443 入门（中文）：https://www.isa.org/standards-and-publications
- OWASP API Security Top 10：https://owasp.org/API-Security/
- 全部模块外链汇总见 `外部链接索引.md`
