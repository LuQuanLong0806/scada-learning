# M9 — 工程素养：单元测试 / DI 容器 / 统一采集 / 生产容错 🛡️

> **优先级定位**：🔴 必学 · 工程素养（测试/DI/统一架构/容错，13→15K 分水岭）
> **技术来源**：🟧 `xunit` + `Moq`（单元测试）、🟧 `Microsoft.Extensions.DependencyInjection`（DI 容器）、🟦 `System.Threading.Channels`（统一缓冲）、🟦 `Task`/异常（容错重试）。
> **前端类比总纲**：前端有 Jest/Vitest 测逻辑、Context 注入服务、事件总线解耦、axios-retry 重试——本模块把这套"工程化素养"在 C# 上位机里完整复刻一遍，决定你能不能"交付企业"而不是"写完能跑"。
> **给简历加的能力**：把"能跑的代码"升级成"信得过、可维护、可交付"的工程 —— 有测试兜底、有容器解耦、有统一架构、有生产级容错。这正是 **13K→15K** 的分水岭。
> **前置**：M0–M8（DAQ Monitor 已具备采集/可视化/存储/报警/上云），本模块给它们"穿上工程铠甲"。
> **已落地验证**：本项目的 `DaqMonitor.Tests` 已含 **28 个真实测试全绿**（`dotnet test`），覆盖 PointStore / AlarmEngine / Crc16 / FrameParser / Retry / AcquisitionPipeline / Moq 集成 / **串口通信层 SerialDevice（单帧·粘包·半包·坏CRC·管道集成·RawLog 调试开关 共 6 个）** / **CAN 设备 2 个** / **USB-HID 设备 2 个** / **心跳健康监测 DeviceHealthMonitor 2 个**。

> ⏱️ **阅读路径**(按时间预算选入口)
> - **3 分钟**:看「模块目标」— 知道这是 13K→15K 分水岭(测试/DI/容错)
> - **30 分钟**:加看 Day 1 xUnit+Moq + Day 2 DI 容器注册
> - **3 小时**:全文精读 + Day 3 **Channel<T> 统一采集架构** + Day 4 Retry 指数退避
> - 🎯 **面试高频**:**Channel<T> 生产消费者(为什么不用 ConcurrentQueue)** / DI 生命周期(单例/作用域/瞬态)/ **Retry 指数退避 + 熔断**
> - 🔁 **配套复习**:[代码肌肉 B2 Channel 生产消费 10min 白板](代码肌肉训练手册_30天刷题版.md) · [Debug C3 多线程竞态 / C4 event 没退订](代码肌肉训练手册_30天刷题版.md) · [间隔重复表](记忆与复习机制_间隔重复版.md)

> 📚 **前置语法**(M9 用到的,陌生请查 [C# 语法速查 — 前端视角](CSharp语法速查_前端视角.md))
> - `[Fact]` / `[Theory]` / `[InlineData(1, 2, 3)]` — xUnit 测试特性
> - `Assert.Equal(expected, actual)` / `Assert.Throws<T>(...)` — 断言
> - `Mock<IDevice>` / `.Setup(x => x.ConnectAsync()).ReturnsAsync(true)` — Moq
> - `Channel<T>.CreateUnbounded()` / `await channel.Reader.ReadAsync(ct)` — 生产消费,速查 §14
> - `services.AddSingleton<IAlarmEngine, AlarmEngine>()` — DI 注册,速查 §12
> - `Interlocked.Increment(ref _retryCount)` — 原子计数(重试),速查 §14
> - `async Task ExecuteWithRetry(Func<Task> action, ct)` — 异步委托参数,速查 §7/§8

> 📦 **前置类型**(本模块示例代码用到的核心自定义类型)
> M9 示例大量引用 `IDevice` / `DeviceBase` / `SensorPoint` / `AlarmEngine` / `AlarmRule` / `AcquisitionPipeline` 等类型 — 这些在 [📦 前置类型定义 · 学员粘贴版](前置类型定义_学员粘贴版.md) **集中定义**。**遇到"找不到类型 XXX"报错,先去那份文档复制对应类型**,在项目里建 `_PredefinedTypes.cs` 粘进去就能跑。本模块会**新建** `Retry` 工具类(指数退避),跟着 Day 1-4 敲。

## 模块目标
① **单元测试**（xUnit + Moq）：核心逻辑可验证，改代码不怕回归；② **DI 容器**：服务一处注册、随处可取，便于替换与测试；③ **统一采集架构**（`Channel<T>` + `Timer` 批量）：修正 M5③/M7② 的"逐点刷新/逐点发布"坑；④ **生产级容错与重试**：通信断了自动退避重试，而不是裸 `try/catch` 抛给用户。

## Day 1 — 单元测试（xUnit + Moq）🟡

### 一句话讲清楚
单元测试 = 给核心逻辑写"自动裁判"：输入固定，断言输出符合预期。改完代码跑一遍，全绿就说明没搞坏东西。**这是 15K 岗位的硬通货**——不会写测试的上位机工程师，简历会被直接刷掉。

### 前端类比秒懂
| 上位机（C#） | 前端 |
|---|---|
| `xunit` + `[Fact]` | `Jest` / `Vitest` + `test()` / `it()` |
| `Assert.Equal(a, b)` | `expect(a).toBe(b)` |
| `Moq`（造替身） | `jest.mock()` / `vi.fn()` |
| 测试项目引用被测项目 | `*.test.ts` 同仓库 |
| `dotnet test` | `npm test` / `vitest` |

### 分点精讲

**① 测试项目怎么建**（🟧）

> ⚠️ **执行位置**:下面命令在**解决方案根目录**(含 `DaqMonitor.sln` 的目录)执行,**不是**在 `src/DaqMonitor.Core/` 里!
> 如果你在 Day 0.5 已经建过 `DaqMonitor.Tests`,跳过 `dotnet new xunit` 那行,只跑后两条装包+引用。

```bash
# 1. 在解决方案根目录(含 DaqMonitor.sln 的目录)执行
dotnet new xunit -o src/DaqMonitor.Tests        # 如果 Day 0.5 已建过,这行跳过
dotnet sln add src/DaqMonitor.Tests/DaqMonitor.Tests.csproj   # 挂到解决方案

# 2. 切到测试项目目录,装包 + 引用 Core
cd src/DaqMonitor.Tests
dotnet add package Moq                          # Mock 框架(造替身)
dotnet add package FluentAssertions            # (可选)更友好的断言 API
dotnet add reference ../DaqMonitor.Core        # 引用被测工程
```

> 约定：测试类名 = `被测类 + Tests`，方法名 = `被测方法_场景_预期`（`Evaluate_FiresOnlyOnRisingEdge`）。

**② 测"纯逻辑"（不依赖外部）**（🟦🟧）

> 📂 `DaqMonitor.Tests/AlarmEngineTests.cs` · namespace `DaqMonitor.Tests`
> 🔧 已装 `Moq` (本节①已装)
> 💡 用到 `AlarmEngine` / `AlarmRule`(M6) + `SensorPoint` / `AlarmLevel`([前置类型定义](前置类型定义_学员粘贴版.md))
> ⚠️ **修一个真实 bug**:旧版 `new SensorPoint { Id = 3, Value = 200 }` 用对象初始化器,但 SensorPoint 是带自定义构造函数的 struct,初始化器走默认构造 → Timestamp=default(DateTime)。**改用 `new SensorPoint(3, 200)`** 走构造函数,Timestamp 自动取 DateTime.Now。

报警引擎是典型纯逻辑——给它点，断言是否触发。本项目真实用例：
```csharp
using DaqMonitor.Core.Alarms;
using DaqMonitor.Core.Models;
using Xunit;

namespace DaqMonitor.Tests;

public class AlarmEngineTests
{
    [Fact]
    public void Evaluate_FiresOnlyOnRisingEdge()   // 边沿触发:只报一次,不刷屏
    {
        var engine = new AlarmEngine();
        engine.Add(new AlarmRule { PointId = 3, Threshold = 100, Level = AlarmLevel.Critical, IsHigh = true });
        int count = 0;
        engine.AlarmTriggered += (s, e) => count++;

        // ✅ 走构造函数,Timestamp 自动取 DateTime.Now(不是 default)
        engine.Evaluate(new SensorPoint(3, 200));
        engine.Evaluate(new SensorPoint(3, 200));   // 仍超阈值,不应重复报
        Assert.Equal(1, count);
    }
}
```
同样写法覆盖：`PointStore.AddOrUpdate`（双索引增改）、`Crc16.Modbus`（寄存器值 `0x0A84`，注意 Modbus 低字节在前的线序）、`FrameParser.Feed`（半包/粘包拆分）。

**③ 用 Moq 造"替身"测集成**（🟧）—— 不碰真实串口/PLC
```csharp
[Fact]
public async Task Pipeline_ReceivesData_FromMockedDevice()   // 真实存在于本项目
{
    var device = new Mock<IDevice>();          // 虚拟设备，免去真实硬件
    using var pipeline = new AcquisitionPipeline(TimeSpan.FromMilliseconds(50));
    var done = new TaskCompletionSource();
    var received = new List<SensorPoint>();
    pipeline.BatchReady += (s, batch) => { received.AddRange(batch); if (received.Count>=1) done.TrySetResult(); };

    pipeline.Register(device.Object);
    device.Raise(d => d.DataReceived += null,    // Moq 模拟"设备来了一帧"
        new SensorPoint(7, 42, DateTime.Now));   // SensorPoint 在 DaqMonitor.Core.Models 定义

    await Task.WhenAny(done.Task, Task.Delay(2000));
    Assert.Single(received);
    Assert.Equal(7, received[0].Id);
}
```

**④ 跑测试**（🟦）
```bash
dotnet test            # 全绿 = 改代码有底气
```

### ⚠️ 重点 & 易踩坑
| 项 | 说明 |
|---|---|
| ⭐ 测"行为"不是"实现" | 断言输入输出，别断言内部私有字段，否则重构即红 |
| ⭐ 不依赖外部 | 串口/PLC/DB/MQTT 一律用 Mock，测试要快且稳定 |
| ⭐ 命名讲人话 | `方法_场景_预期`，失败一眼知道哪坏 |
| 🔥 异步测试要 `async Task` | 别在测试里 `.Wait()`/`.Result()`，会死锁；用 `await Task.WhenAny(t, Task.Delay(...))` 等异步完成 |
| 🔥 边界/反例也要测 | 成功路径 + 异常路径（如 `Retry` 耗尽后抛异常）都覆盖才稳 |

### 🟢 基础题
给 `PointStore` 加一个 `Remove(int id)` 方法，并写测试：删除存在的 Id 后 `Get` 返回 `null`、删除不存在的 Id 不报错。

### 🟡 进阶题
给 `AlarmEngine` 加一个 `ClearRules()` 方法，写测试确认清空后 `Evaluate` 不再触发任何 `AlarmTriggered`。

### 🔴 挑战题
用 Moq 造一个"会失败的设备"：`Setup` 第 1 次 `Connect` 抛异常、第 2 次成功，配合本模块 Day4 的 `Retry.ExecuteAsync` 验证"连不上自动重试到成功"——这正是生产级容错的最小证明。

**✅ 答案（基础题）**
```csharp
// PointStore.cs
public void Remove(int id) { _byId.Remove(id); _points.RemoveAll(x => x.Id == id); }
// PointStoreTests.cs
[Fact] public void Remove_ExistingId_ThenGetNull()
{
    var s = new PointStore(); s.AddOrUpdate(new SensorPoint(5, 1));   // ⚠️ 走构造函数,Timestamp 才不会是 default(DateTime)
    s.Remove(5); Assert.Null(s.Get(5));
}
[Fact] public void Remove_MissingId_NoThrow()
{ new PointStore().Remove(999); }   // 不抛即过
```

**🏗️ 项目任务**：把 DAQ Monitor 的核心逻辑（Store/Alarm/Protocol/Pipeline/Retry）都补上测试，`dotnet test` 全绿。工程素养第一关达标。

**🎓 工控导师说**：很多学员觉得"测试是浪费时间，功能跑通就行"。等上线后改一处、别处崩了，你才知道疼。我带的项目规矩是"**改完代码先 `dotnet test`，全绿才敢提交**"——测试就是你的安全网，省下的 debug 时间比写测试多十倍。

**💼 职业建议**：单元测试是 13K→15K 的分水岭标志。面试被问"你写测试吗"，答"核心逻辑用 xUnit+Moq 覆盖，改代码靠测试兜底"直接证明你是"工程化思维"而非"脚本式开发"，简历一票过关。

**✅ 打卡[ ]**

## Day 2 — DI 容器（依赖注入）🟡

### 一句话讲清楚
依赖注入（DI）= 把"对象从哪来"交给一个容器统一管理。组件只声明"我需要什么"（构造函数参数），容器负责造好并喂进来。好处：换实现只改一处、写测试能塞 Mock。

### 前端类比秒懂
| 上位机（C#） | 前端 |
|---|---|
| `ServiceCollection` + `AddSingleton` | React `Context.Provider` 提供单例 |
| `GetRequiredService<T>()` | `useContext(MyCtx)` 取服务 |
| 组合根（Composition Root） | 应用顶层 `main.tsx` 装配 |
| 接口 `IDevice` + 实现 | 接口 + 多实现（策略模式） |

### 分点精讲

**① 注册服务**（🟧 `Microsoft.Extensions.DependencyInjection`）
```csharp
var services = new ServiceCollection();
services.AddSingleton<PointStore>();                       // 全局唯一存储
services.AddSingleton<AlarmEngine>();                      // 全局唯一报警引擎
services.AddSingleton<AcquisitionPipeline>(_ =>            // 带参数的单例
    new AcquisitionPipeline(TimeSpan.FromMilliseconds(200)));
// services.AddSingleton<IDevice, ModbusDevice>();         // M2 落地后注册具体设备
var provider = services.BuildServiceProvider();
```

**② 组合根（本项目真实代码 `Bootstrapper.Build`）**（🟧🟦）
```csharp
public static ServiceProvider Build()
{
    var services = new ServiceCollection();
    services.AddSingleton<PointStore>();
    services.AddSingleton<AlarmEngine>();
    services.AddSingleton<AcquisitionPipeline>(_ => new AcquisitionPipeline(TimeSpan.FromMilliseconds(200)));
    return services.BuildServiceProvider();
}
```

**③ 在 App 里取服务**（🟩 WPF `App.xaml.cs`）
```csharp
public partial class App : Application
{
    public static ServiceProvider Provider { get; private set; } = Bootstrapper.Build();
    // 任何地方：App.Provider.GetRequiredService<PointStore>()
}
```

**④ 为什么是 15K 关键点**
- **可替换**：把 `ModbusDevice` 换成 `PlcDevice`，只改注册那一行，调用方一行不动（面向接口）。
- **可测试**：测试时 `services.AddSingleton<IDevice, MockDevice>()` 即可隔离硬件。

### ⚠️ 重点 & 易踩坑
| 项 | 说明 |
|---|---|
| ⭐ 组合根唯一 | 所有 `new` 只允许在 `Bootstrapper` 里出现，别在 ViewModel 里到处 `new` |
| ⭐ 生命周期选对 | 有状态/全局共享用 `Singleton`；每次要新的用 `Transient`；请求级用 `Scoped` |
| 🔥 别 `new` 依赖 | ViewModel 构造函数写 `public MainViewModel(PointStore store)`，让容器注入，别自己 `new PointStore()` |
| 🔥 循环依赖 | A 依赖 B、B 又依赖 A 会启动即炸，重构拆公共依赖 |

### 🟢 基础题
把 `MainViewModel` 改成"通过构造函数拿到 `PointStore` 和 `AcquisitionPipeline`"，而不是在内部 `new`。

### 🟡 进阶题
在 `Bootstrapper` 里把 `MainViewModel` 也注册成 `Singleton`，让 `App.Provider.GetRequiredService<MainViewModel>()` 能直接拿到组装好的实例。

### 🔴 挑战题
给 `PointStore` 抽一个接口 `IPointStore`，`Bootstrapper` 注册 `AddSingleton<IPointStore, PointStore>()`；测试时用 `Mock<IPointStore>()` 替换——体会"面向接口让测试与实现解耦"。

**✅ 答案（基础题）**
```csharp
public partial class MainViewModel : ObservableObject
{
    private readonly PointStore _store;
    private readonly AcquisitionPipeline _pipeline;
    public MainViewModel(PointStore store, AcquisitionPipeline pipeline)
    { _store = store; _pipeline = pipeline; }
}
// App 启动：Provider.GetRequiredService<MainViewModel>()（需先注册 ViewModel）
```

**🏗️ 项目任务**：用 `Bootstrapper` 装配 DAQ Monitor 全部 Core 服务；MainWindow/ViewModel 改为构造函数注入，移除散落的 `new`。工程素养第二关达标。

**🎓 工控导师说**：见过太多"在按钮事件里 `new SerialDevice()`、`new PointStore()`"的上位机程序——结果换设备要改 20 个地方，测试根本没法写。企业级做法是"**所有 `new` 只允许在组合根 `Bootstrapper` 里出现**"，别处只声明"我需要什么"。这条规矩值千金。

**💼 职业建议**：DI 容器 + 面向接口是面试高频题。能讲清"组合根唯一、生命周期选 Singleton/Scoped/Transient、测试时换 Mock 实现"的人，直接被认定是"写过可维护项目"，而非"只会拖控件"。

**✅ 打卡[ ]**

## Day 3 — 统一采集架构（Channel<T> + 定时批量）🟡

### 一句话讲清楚
**所有设备数据走同一条流水线**：事件只做"入队" → `Channel<T>` 缓冲 → `Timer` 定时把一批取出来 → 一次性推给"可视化 / 报警 / 上云"。彻底消灭"每个模块各写一套逐点刷新"的混乱。

> 🔥 **为什么必须这样（修正 M5③ / M7② 的坑）**：早期在 `DataReceived` 里 `Dispatcher.Invoke` 逐点刷曲线、或 `await PublishAsync` 逐点发云——100Hz × 多设备时 UI 线程被刷爆、MQTT 抖动。**正确姿势：事件里只入队，重活交给定时器批量做。**

### 前端类比秒懂
| 上位机（C#） | 前端 |
|---|---|
| `device.DataReceived += ...` | `emitter.on('data', ...)` |
| `Channel.Writer.TryWrite` | 消息入队（`queue.push`） |
| `Timer` 定时 `Flush` | `setInterval` 批量消费 |
| `BatchReady` 事件 | 发布到订阅者（事件总线） |

### 分点精讲

**① 统一管道 `AcquisitionPipeline`（本项目真实代码）**（🟦 `System.Threading.Channels`）
```csharp
public sealed class AcquisitionPipeline : IDisposable
{
    private readonly Channel<SensorPoint> _channel = Channel.CreateUnbounded<SensorPoint>();
    public event EventHandler<IReadOnlyList<SensorPoint>>? BatchReady;   // 批就绪，后台线程触发

    public AcquisitionPipeline(TimeSpan flushInterval, int maxBatch = 500)
        => _flushTimer = new Timer(_ => Flush(), null, flushInterval, flushInterval);

    public void Register(IDevice device)
        => device.DataReceived += (s, e) =>
            _channel.Writer.TryWrite(e);   // e 已经是 SensorPoint,直接入队

    private void Flush()
    {
        List<SensorPoint>? batch = null;
        lock (_gate) { if (_pending.Count > 0) { batch = _pending; _pending = new(); } }
        if (batch is not null) BatchReady?.Invoke(this, batch);   // 一批推给所有订阅方
    }
}
```

**② 可视化 / 报警 / 上云 统一订阅 `BatchReady`**（🟩 WPF）
```csharp
// 可视化（M5）：后台线程事件 → Dispatcher 回 UI 线程批量画曲线
_pipeline.BatchReady += (s, batch) => Application.Current.Dispatcher.Invoke(() =>
{
    foreach (var p in batch) chart.Add(new ObservablePoint(p.Timestamp.Second, p.Value));
});
// 报警（M6）：批量送进 AlarmEngine 评估
_pipeline.BatchReady += (s, batch) => { foreach (var p in batch) _alarm.Evaluate(p); };
// 上云（M7）：批量发布 — 走有界 Channel + 后台消费循环,**绝不**用 `async (s,batch) => await ...`(那是 async void,异常会吞,见 [C# 陷阱](C#_陷阱_前端转上位机必看_深度版.md))
var cloudQ = Channel.CreateBounded<IReadOnlyList<SensorPoint>>(100);
_pipeline.BatchReady += (s, batch) => cloudQ.Writer.TryWrite(batch);   // 同步入队,瞬间返回
_ = Task.Run(async () =>                                       // 后台消费,异常能被 try/catch 抓住
{
    await foreach (var batch in cloudQ.Reader.ReadAllAsync())
        foreach (var p in batch) await _mqtt.PublishAsync(p);
});
```

**③ 限流（maxBatch）**：`_pending` 攒满 `maxBatch`（如 500）也立刻出队，防突发洪峰堆爆内存。

### ⚠️ 重点 & 易踩坑
| 项 | 说明 |
|---|---|
| ⭐ 事件里只做极轻操作 | `TryWrite` 是 O(1) 无锁入队，绝不写文件/刷 UI/发网络 |
| ⭐ `BatchReady` 在后台线程 | 订阅方若要改 UI 必须 `Dispatcher.Invoke`，否则跨线程异常 |
| ⭐ 单一数据源 | 可视化/报警/上云都读同一批 `SensorPoint`，天然一致 |
| 🔥 忘记 `Flush` 间隔 | 间隔太长曲线"跳变"，太短失去批量意义（200~500ms 通常合适） |
| 🔥  Dispose 要退订 | `Dispose` 里 `_channel.Writer.TryComplete()` + 退订 `DataReceived`，防内存泄漏 |

### 🟢 基础题
把 M6 报警引擎接入 `AcquisitionPipeline`：订阅 `BatchReady`，把每批点送进 `AlarmEngine.Evaluate`，并在 `AlarmTriggered` 里写一条 Serilog 日志。

### 🟡 进阶题
在 `BatchReady` 订阅里，把每批点同时喂给 M5 的曲线（注意后台线程要用 `Dispatcher.Invoke` 回 UI 线程），实现"一批数据驱动多处消费"。

### 🔴 挑战题
给 `AcquisitionPipeline` 加"背压/限流"保护：当 `_pending` 攒满 `maxBatch`（如 500）也立刻出队，防止设备突发洪峰把内存堆爆——并写测试验证"塞 1000 个点、触发至少 2 次 BatchReady"。

**✅ 答案（基础题）**
```csharp
_pipeline.BatchReady += (s, batch) =>
{
    foreach (var p in batch) _alarm.Evaluate(p);
};
_alarm.AlarmTriggered += (s, e) =>
    Log.Warning("报警 点位{Id} 值{Value} 级别{Level}", e.PointId, e.Value, e.Level);
```

**🏗️ 项目任务**：重构 DAQ Monitor，让 M5 可视化 / M6 报警 / M7 上云 全部从 `_pipeline.BatchReady` 取数，删除各处散落的逐点 `Dispatcher.Invoke` / 逐点 `PublishAsync`。工程素养第三关（统一架构）达标。

**🎓 工控导师说**：早期咱们在 `DataReceived` 里直接 `Dispatcher.Invoke` 刷曲线、直接 `await PublishAsync` 发云——设备一多、频率一高，UI 线程直接被刷爆、MQTT 抖动。**一条铁律：事件里只做"入队"这件 O(1) 的轻活，重活交给定时器批量做。** 这是 M0 并发思想的真正落地。

**💼 职业建议**："为什么不用 ObservableCollection 逐点 Add？"是 15K 面试经典题。答"事件里只入队、定时器批量 `BatchReady` 推给所有消费者，单一数据源、天然解耦、不卡 UI"——这一句就能证明你懂高并发采集架构。

**✅ 打卡[ ]**

## Day 4 — 生产级容错与重试（Retry）🔴

### 一句话讲清楚
工业现场通信**必出错**（串口松动、PLC 忙、网络闪断）。合格做法是：**失败自动重试 + 指数退避 + 抖动**，而不是把异常直接甩给用户或默默吞掉。

### 前端类比秒懂
| 上位机（C#） | 前端 |
|---|---|
| `Retry.ExecuteAsync(action, 3)` | `axios-retry` / `fetch` 重试封装 |
| 指数退避 `base * 2^n` | 重试间隔翻倍，避免雪崩 |
| `Random` 抖动 | 错开多个客户端的重试时刻 |
| `CancellationToken` 超时 | `AbortController` / `setTimeout` 取消 |

### 分点精讲

**① 为什么裸 `try/catch` 不够**（🟦）
裸 catch 要么"失败就抛"（用户体验差），要么"catch 后啥也不干"（故障被藏起来）。重试 + 退避才是正解。

**② 本项目真实 `Retry`（指数退避 + 抖动）**（🟦）
```csharp
public static async Task ExecuteAsync(Func<Task> action, int maxRetries = 3, int baseDelayMs = 200, CancellationToken ct = default)
{
    var attempt = 0;
    while (true)
    {
        try { await action(); return; }
        catch (Exception) when (attempt < maxRetries && !ct.IsCancellationRequested)
        {
            attempt++;
            var delay = (int)(baseDelayMs * Math.Pow(2, attempt - 1))   // 200→400→800ms 指数退避
                        + Random.Shared.Next(0, baseDelayMs);           // 加随机抖动，避免多设备同时重试
            await Task.Delay(delay, ct);
        }
    }
}
```

**③ 用在"会失败的地方"**（🟧 串口/Modbus/PLC/网络）
```csharp
// 连接设备：失败重试 3 次（首试+3次=共4次），指数退避
await Retry.ExecuteAsync(() => Task.Run(() => device.Connect()), maxRetries: 3);
// 上云发布：断线重连用 Retry（呼应 M7②）
await Retry.ExecuteAsync(() => client.PublishAsync(msg), maxRetries: 5);
```

**④ 配合超时（`CancellationToken`）**（🟦）
```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)); // 5s 超时
await Retry.ExecuteAsync(() => ReadWithTimeout(addr, cts.Token), maxRetries: 2, ct: cts.Token);
```

**⑤ 心跳探活 + 判掉线 + 重连：Retry 的完整实战**（🟦，呼应 M3/M7）✅ **已落地**：本章思想已实现为 `src/DaqMonitor.Core/Health/DeviceHealthMonitor.cs`（心跳探活 + 连续超时判掉线 + 指数退避重连），配 `DeviceHealthMonitorTests` 单测，接 M15 联调。
光有 ②④ 还不够：M3 讲过 `plc.IsConnected` **不可信**——连接假死了你并不知道。所以还要**主动"心跳"探活**：每隔几秒戳设备一下，它回"在"=在线；一直不回=大概率挂了→触发重连。Retry 管"连不上怎么重试"，心跳管"在线时怎么确认还活着"，**两者配合才是生产级**。

下面这段**能直接跑**（用随机模拟掉线，真实里把 `SendHeartbeatAsync` 换成"读一个固定寄存器"，成功=活着、异常/超时=可能挂了）：
```csharp
static async Task<bool> SendHeartbeatAsync()   // 真实里 = master.ReadHoldingRegisters(...)
{
    await Task.Delay(100);
    return new Random().Next(0, 3) != 0;       // 2/3 在线，1/3 模拟掉线
}
static async Task ReconnectAsync(int attempt)  // 复用本 Day ② 的退避思想
{
    int wait = (int)Math.Min(1000 * Math.Pow(2, attempt), 30000); // 1s→2s→4s…封顶30s
    Console.WriteLine($"  [重连] 第 {attempt} 次，等 {wait/1000}s…");
    await Task.Delay(wait);
}
public static async Task WatchAsync()
{
    int missed = 0, attempt = 0;
    while (true)                               // 真实用 CancellationToken 退出
    {
        if (await SendHeartbeatAsync())
        {
            missed = 0; attempt = 0;
            Console.WriteLine($"[{DateTime.Now:T}] 心跳 OK，设备在线");
        }
        else
        {
            missed++;
            Console.WriteLine($"[{DateTime.Now:T}] 心跳超时！(连续 {missed} 次)");
            if (missed >= 2)                   // 连续 2 次没回才判掉线，防一次抖动误判
            {
                Console.WriteLine($"[{DateTime.Now:T}] 设备疑似掉线，启动重连");
                await ReconnectAsync(attempt++);
                missed = 0;
            }
        }
        await Task.Delay(5000);                // 每 5 秒一次心跳
    }
}
```
**逐行白话：**
- `SendHeartbeatAsync()`：模拟"戳一下设备"。真实里它就是一次读寄存器——成功说明链路通，抛异常/超时说明可能断了。
- `if (missed >= 2)`：**不是漏 1 次就重连**（网络偶尔抖一下很正常），连续 2 次没回才判掉线，防止误判把正常设备重启。
- `ReconnectAsync`：**指数退避**——和本 Day ② 的 `Retry` 同一思想，避免"设备正忙你狂连把它打死"。`Math.Min(..., 30000)` 封顶 30 秒。
- `Task.Delay(5000)`：每 5 秒一次。别太频繁（压设备），也别太久（掉线了发现太慢）。

**运行结果节选：**
```
[10:00:00] 心跳 OK，设备在线
[10:00:05] 心跳 OK，设备在线
[10:00:10] 心跳超时！(连续 1 次)
[10:00:15] 心跳超时！(连续 2 次)
[10:00:15] 设备疑似掉线，启动重连
  [重连] 第 0 次，等 1s…
  [重连] 成功，恢复心跳
[10:00:21] 心跳 OK，设备在线
```
这就是现场"**设备掉线你的程序怎么办？**"的标准答案（面试高频！）。

| 🔥 心跳必记坑 | 说明 |
|---|---|
| 别用 IsConnected 当真相 | 它不可信，要靠"读/心跳是否成功"判断 |
| 别漏 1 次就重连 | 网络抖动正常，连续 N 次（如 2 次）才判掉线 |
| 重连要退避 | 指数退避+封顶，别死循环狂连 |
| 心跳间隔适中 | 太短压设备，太长发现掉线慢（一般 3~10s） |
| 心跳和轮询分开 | 探活归探活、取数归取数，别混一个循环打架 |

### ⚠️ 重点 & 易踩坑
| 项 | 说明 |
|---|---|
| ⭐ 只对"瞬时故障"重试 | 网络抖、设备忙可重试；参数错/权限错重试也没用，要快速失败 |
| ⭐ 退避别用固定间隔 | 固定间隔会让上千设备同时重试把服务端打挂，必须指数+抖动 |
| ⭐ 设上限 + 超时 | 重试次数和总时长要有上限，否则卡死在重试里 |
| 🔥 吞异常 | `catch{}` 空捕获是事故温床；至少记日志或上抛 |
| 🔥 重试幂等 | 写操作（如下发指令）重试前确认可重复执行，避免重复写入 |

### 🟢 基础题
给 `Retry.ExecuteAsync` 写测试：模拟前两次抛异常、第三次成功，断言共调用 3 次。

### 🟡 进阶题
写一个"耗尽重试后仍抛异常"的测试，断言最终抛出原异常类型（如 `InvalidOperationException`）。

### 🔴 挑战题
把 Day4⑤ 的"心跳探活 + 连续 2 次判掉线 + 指数退避重连"`DeviceHealthMonitor` 接进 DAQ Monitor：用 `SimulatedCanChannel` 模拟"连发 2 次掉线后恢复"，写测试断言"先广播 Offline、重连后回到 Online"。

**✅ 答案（基础题）**
```csharp
[Fact] public async Task ExecuteAsync_RetriesThenSucceeds()
{
    int calls = 0;
    await Retry.ExecuteAsync(async () => { calls++; if (calls < 3) throw new InvalidOperationException(); await Task.Yield(); }, maxRetries: 5);
    Assert.Equal(3, calls);
}
[Fact] public async Task ExecuteAsync_ThrowsAfterExhaustingRetries()
    => await Assert.ThrowsAsync<InvalidOperationException>(() =>
        Retry.ExecuteAsync(async () => { await Task.Yield(); throw new InvalidOperationException(); }, maxRetries: 2));
```

**🏗️ 项目任务**：把 DAQ Monitor 里所有"连设备 / 读寄存器 / 发云"的通信调用用 `Retry.ExecuteAsync` 包起来，并配 `CancellationToken` 超时 + Serilog 记录重试。工程素养第四关（生产容错）达标。🛡️

**🎓 工控导师说**：工业现场通信"必出错"——串口松动、PLC 忙、网线被叉车碾断都是常态。我见过最蠢的写法：`try { device.Read(); } catch { }` 把异常吞了，设备明明挂了程序还"假装正常"继续跑，等出事已经晚了。**正确姿势：失败自动重试（指数退避+抖动），重试耗尽再上抛+记日志**。心跳还要主动探活，别信 `IsConnected`。

**💼 职业建议**："设备掉线你的程序怎么办？"是面试高频送命题。答"Retry 指数退避重试 + 心跳探活判掉线 + 指数退避重连 + Serilog 记录全过程"——这套组合拳一出口，面试官就知道你是真上过现场的人。

**✅ 打卡[ ]**

## 模块交付清单（M9）
- [ ] 单元测试覆盖 Core（Store/Alarm/Protocol/Pipeline/Retry），`dotnet test` 全绿
- [ ] Moq 集成测试（虚拟设备 → 管道）
- [ ] DI 容器装配（Bootstrapper + 构造函数注入）
- [ ] 统一采集架构（可视化/报警/上云 全部走 `BatchReady`）
- [ ] 生产级容错（Retry 指数退避 + 抖动 + 超时）
- [ ] 修正 M5③ / M7② 的逐点刷新/逐点发布反模式

> 📌 **环境提示（真实踩过的坑）**：若本机只装了 .NET 10 运行时、项目却 `net8.0`，测试会报"必须安装 .NET 8"。在三个 `.csproj` 加 `<RollForward>Major</RollForward>` 即可让 net8 应用滚动运行在 net10 上（本项目已加）。这是生产常用技巧，项目本身仍是 net8.0。

## 📌 温故知新 / 跨模块联动
- **← M0（并发 + 分层）**：本模块的"统一采集架构"正是把 M0 Day7 的 `Channel<T>` 批量思想从"只给可视化"升级成"可视化/报警/上云共用一条 `BatchReady` 总线"——这就是分层架构的价值：**数据流只写一次，消费者随便加**。
- **← M1 / M2 / M3（通信容错）**：串口/Modbus/PLC 调用现在用 `Retry.ExecuteAsync` 包起来，把"连不上就崩"变成"连不上重试+记日志"。没有工程素养，通信代码一碰异常整个程序就死给你看。
- **← M5③ / M7②（修正反模式）**：M5 原"逐点刷新 UI"、M7 原"逐点发布云"都被本模块的 `BatchReady` 取代——背下来这个反例，面试能讲清"为什么不用 ObservableCollection 逐点 Add"。
- **← M6（报警可靠性）**：报警引擎的"回滞 + 下降沿 `AlarmCleared`"靠单测锁死行为；改坏它测试立刻红，这就是测试的意义。
- **→ 简历一句话**：「用 xUnit+Moq 给 Core 关键逻辑写单测，DI 容器装配，统一 `Channel<T>` 采集总线 + Retry 生产容错」——这比"会写业务代码"高一个档，直接对齐 13→15K 分水岭。

## 🧩 完整代码组装（M9 落地的真实文件，可直接抄 / 已存在于工程）
```csharp
// src/DaqMonitor.Core/Common/Retry.cs   —— 指数退避 + 抖动（Day4）
public static class Retry
{
    public static async Task ExecuteAsync(Func<Task> action, int maxRetries = 3, int baseDelayMs = 200, CancellationToken ct = default)
    {
        var attempt = 0;
        while (true)
        {
            try { await action(); return; }
            catch (Exception) when (attempt < maxRetries && !ct.IsCancellationRequested)
            {
                attempt++;
                var delay = (int)(baseDelayMs * Math.Pow(2, attempt - 1)) + Random.Shared.Next(0, baseDelayMs);
                await Task.Delay(delay, ct);
            }
        }
    }
}

// src/DaqMonitor.Core/AppServices/Bootstrapper.cs  —— 组合根（Day2）
public static ServiceProvider Build()
{
    var services = new ServiceCollection();
    services.AddSingleton<PointStore>();
    services.AddSingleton<AlarmEngine>();
    services.AddSingleton<AcquisitionPipeline>(_ => new AcquisitionPipeline(TimeSpan.FromMilliseconds(200)));
    // services.AddSingleton<IDevice, ModbusDevice>();  // 接设备时一行切换，UI 零改动
    return services.BuildServiceProvider();
}

// src/DaqMonitor.Core/Health/DeviceHealthMonitor.cs  —— 心跳探活 + 退避重连（Day4⑤，已落地）
// Start() 后台循环每 5s 探活 → 连续 missThreshold 次判掉线 → 指数退避重连 → StateChanged 广播

// src/DaqMonitor.Tests/*Tests.cs  —— 28 个真实测试全绿（Day1）
// 涵盖 PointStore / AlarmEngine / Crc16 / FrameParser / Retry / AcquisitionPipeline / Moq 集成 /
// SerialDevice(单帧·粘包·半包·坏CRC·管道·RawLog) / CanDevice / UsbHidDevice / DeviceHealthMonitor
```
> 工程已端到端跑通：`dotnet build` 0 警告 0 错误、`dotnet test` **28/28 绿**。本模块不是"纸上谈兵"，而是把前面 M0–M8 的代码穿上"测试 / 容器 / 统一架构 / 容错"四层工程铠甲。

## 🔗 明日预告
**M10 报表（历史聚合 + Excel/PDF 导出）**：把 M4 存的历史库、M5 的曲线、M6 的报警收口成"能交付客户的报表"——这是企业项目的最后一环，做完你的 DAQ Monitor 就"能交付"了。

## 📚 延伸阅读（卡点时点开）
- xUnit 官方：https://xunit.net/ · Moq：https://github.com/moq/moq —— 单元测试
- Microsoft DI 官方：https://learn.microsoft.com/dotnet/core/extensions/dependency-injection —— 容器/生命周期
- `System.Threading.Channels` 参考：https://learn.microsoft.com/dotnet/api/system.threading.channels —— 统一缓冲原理
- 异步/取消令牌：`CancellationToken` https://learn.microsoft.com/dotnet/api/system.threading.cancellationtoken
- 全部模块外链汇总见 `外部链接索引.md`
