
<!-- CHUNK 11 -->

> ⚠️ **核心区别**：前端单线程，异步只是"排队等结果"，不会真并行改数据；C# 是真并行，多个线程能同时执行、同时读写内存，所以需要锁。

### 分点精讲

**① Thread —— 最基础的多线程**：🟩
```csharp
// 前端类比：new Worker('worker.js')
Thread t = new Thread(() =>
{
    Console.WriteLine($"后台线程 Id={Thread.CurrentThread.ManagedThreadId}");
});
t.IsBackground = true;   // 后台线程：主程序退出它也退出（否则程序关不掉）
t.Start();
Console.WriteLine($"主线程 Id={Thread.CurrentThread.ManagedThreadId}");
// ⚠️ 输出顺序不固定！多线程执行顺序不可预测
```

**② Task —— 现代 C# 推荐（≈ Promise）**：🟩
```csharp
// ① 启动一个后台任务（前端：new Promise(...)）
Task task = Task.Run(() =>
{
    Thread.Sleep(2000);   // 真的暂停这个线程 2 秒
    Console.WriteLine("Task 完成");
});

// ② 带返回值
Task<int> calc = Task.Run(() => { Thread.Sleep(500); return 42; });
int result = await calc;   // ✅ 用 await 拿结果（推荐）
// int r2 = calc.Result;   // ❌ .Result/.Wait() 会阻塞线程，UI 里用会死锁！
```

**③ async/await —— 最像前端的写法**：🟦
```csharp
// ✅ 正确：返回 Task，用 await
public async Task<string> GetDataAsync()
{
    string data = await Task.Run(() =>
    {
        Thread.Sleep(2000);
        return "数据已就绪";
    });
    return data;
}

// WPF 按钮点击（async 事件处理器）
private async void BtnStart_Click(object sender, RoutedEventArgs e)
{
    StatusText.Text = "加载中...";
    string data = await GetDataAsync();  // UI 不卡顿！await 期间界面还能动
    StatusText.Text = data;
}
```
> ⚠️ `async void` **只能用在事件处理器**（如 `Click`）。其他任何地方一律 `async Task`，否则异常会被吞掉、程序静默崩溃。

**④ ⚠️ 为什么需要锁 —— 经典竞态 Bug**：🟦
```csharp
// ❌ 没锁：结果不是 10000！
private int _count = 0;
for (int i = 0; i < 10000; i++)
    Task.Run(() => _count++);   // 上万个线程同时 ++
// 结果可能是 8234、9567... 随机
```
**为什么？** `_count++` 不是原子操作，实际分三步：①读值→②+1→③写回。两个线程同时读到 5，各自 +1 都写回 6，就丢了一次 +1。

<!-- CHUNK 12 -->

```csharp
// ✅ 加锁就对了：一定是 10000
private int _count = 0;
private readonly object _lock = new object();   // 锁对象，随便 new 一个
for (int i = 0; i < 10000; i++)
    Task.Run(() =>
    {
        lock (_lock)   // 同一刻只有一个线程能进
        {
            _count++;
        }              // 出了 lock，别的线程才能进
    });
```

**⑤ 生产者-消费者（上位机最高频场景）**：🟩
> 串口每秒收 100 条（生产者），UI 慢慢刷新（消费者），中间用**线程安全队列**缓冲。
```csharp
using System.Collections.Concurrent;

// ConcurrentQueue 自带锁，不用手动 lock（前端类比：自动管理的消息队列）
private ConcurrentQueue<SensorPoint> _queue = new();

// 生产者：后台采集线程
private void OnDataReceived(SensorPoint p) => _queue.Enqueue(p);   // 线程安全

// 消费者：WPF DispatcherTimer 定时刷 UI
private void UiTimer_Tick(object? s, EventArgs e)
{
    while (_queue.TryDequeue(out var p))   // 一条条取
    {
        DataGrid.Items.Add(p);
        if (DataGrid.Items.Count > 200)
            DataGrid.Items.RemoveAt(0);    // 只留最近 200 条，防内存爆
    }
}
```

### ⚠️ 前端转 C# 多线程四大坑
| 坑 | 说明 | 解决 |
|---|---|---|
| 跨线程改 UI | 后台线程直接改 `TextBlock.Text` 会抛异常 | WPF：`Dispatcher.Invoke(() => txt.Text = "x")`；WinForms：`Invoke(...)` |
| 死锁 | 两线程互等对方的锁 / `await` 里用 `.Result` | 锁顺序一致；UI 里永远 `await`，别用 `.Result/.Wait()` |
| `async void` 吞异常 | 异常静默丢失、程序莫名崩 | 只在事件处理器用 `async void`，其他用 `async Task` |
| 竞态条件 | 多线程同时读写同一变量 | `lock` 或用 `ConcurrentQueue`/`Interlocked` |

### ✏️ 课后练习（综合大题，直接对标工业场景）
写一个**数据采集模拟器**（WPF）：
1. 后台线程（生产者）每 100ms 生成一条模拟数据（温度+压力+时间戳）；
2. 用 `ConcurrentQueue` 做缓冲区；
3. UI 用 `DispatcherTimer`（消费者）每 500ms 从队列取数显示到 `DataGrid`；
4. 数据超 200 条自动删最旧，保持 ≤200 行；
5. 实时显示队列积压条数。
要求：必须用 `Task.Run` + `ConcurrentQueue` + `Dispatcher.Invoke` 更新 UI。

<!-- CHUNK 13 -->

**✅ 答案（核心骨架）**
```csharp
public partial class MainWindow : Window
{
    private readonly ConcurrentQueue<Reading> _queue = new();
    private readonly DispatcherTimer _uiTimer = new();
    private CancellationTokenSource? _cts;

    public record Reading(double Temp, double Pressure, DateTime Time);

    public MainWindow()
    {
        InitializeComponent();
        _uiTimer.Interval = TimeSpan.FromMilliseconds(500);
        _uiTimer.Tick += UiTimer_Tick;
    }

    private void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        // 生产者：后台任务
        Task.Run(async () =>
        {
            var rnd = new Random();
            while (!token.IsCancellationRequested)
            {
                _queue.Enqueue(new Reading(20 + rnd.NextDouble() * 10,
                                           100 + rnd.NextDouble() * 5, DateTime.Now));
                await Task.Delay(100, token);   // 每 100ms 一条
            }
        }, token);
        _uiTimer.Start();
    }

    private void UiTimer_Tick(object? s, EventArgs e)   // 消费者（已在 UI 线程）
    {
        while (_queue.TryDequeue(out var r))
        {
            DataGrid.Items.Add(r);
            if (DataGrid.Items.Count > 200) DataGrid.Items.RemoveAt(0);
        }
        BacklogText.Text = $"积压：{_queue.Count} 条";
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _uiTimer.Stop();
    }
}
```
> 💡 `DispatcherTimer.Tick` 本身就在 UI 线程，所以这里不用再 `Dispatcher.Invoke`；但如果你是在 `Task.Run` 后台里直接改 UI，就**必须** `Dispatcher.Invoke(() => ...)`。

<!-- CHUNK 14 -->

**🏗️ 项目任务**：给 DAQ Monitor 加一个"模拟采集"后台线程 + `ConcurrentQueue` 缓冲 + `DispatcherTimer` 刷新，把上面骨架接到项目里，形成真实的"后台采集→前台展示"并发闭环。这是 M6 实时监控的核心地基。

**✅ 打卡[ ]**

---

## Day 8 — 周复习 + 立起 WPF 工程

**📌 技术来源**：🟦 前 7 天综合 + 🟩 **WPF**（`<UseWPF>true</UseWPF>`，随 .NET，非 NuGet）

### 复习地图（一条主线串起来）
变量/类型(Day1-2) → 集合/LINQ 处理数据(Day3) → 类/属性封装设备(Day4) → 接口/泛型架构(Day5) → 事件解耦通知(Day6) → 多线程/异步/锁并发(Day7)。这条线后面贯穿 M1~M8。

### ⭐ 自测小测（答案见各 Day）
1. `int?` 和 `int` 区别？
2. LINQ 延迟执行是什么意思？为什么有时要 `.ToList()`？
3. 接口 vs 抽象类怎么选？
4. 事件订阅为什么要 `-=`？
5. `_count++` 多线程下为什么会丢数据？怎么修？
6. 后台线程能直接改 WPF 界面吗？不能的话怎么办？
7. WPF 用 NuGet 装吗？（答：不，`<UseWPF>true</UseWPF>`，属 🟩 生态）

**🏗️ 项目任务（今日落地真实工程）**
```bash
dotnet new sln -n DaqMonitor
dotnet new classlib -n DaqMonitor.Core
dotnet new wpf -n DaqMonitor.UI
dotnet sln add src/DaqMonitor.Core src/DaqMonitor.UI
dotnet add src/DaqMonitor.UI reference src/DaqMonitor.Core
```
- MainWindow 放"启动采集"按钮 + `DataGrid` + 状态栏；
- 把 Day2-7 写的 Core 类型（`SensorPoint`/`DeviceState`/`Alarm`/`IDevice`/`DeviceBase`/`PointStore`/事件）搬进 `DaqMonitor.Core`；
- 接入 Day 7 的"模拟采集+队列+定时刷新"，点按钮就能看到数据实时滚动。
- 工程骨架即成型，`git commit` 一次，作品集第一版诞生。

**✅ 打卡[ ]**

---

## 附录：M0 交付物 & 埋下的 5 个企业级地基
- **本地工程**：`DAQMonitor/src` → `DaqMonitor.Core`（领域模型+设备接口+点位存储）+ `DaqMonitor.UI`（WPF）。
- 从 Day 8 起它就是**真实项目**，后续模块往里加"串口/Modbus/PLC/存储/图表"，最终 = 能拿去面试的工业数据采集监控系统（13K~15K 作品集）。
- 本模块已提前埋下 **5 个企业级地基**：
  1. **分层**（Core/UI 分离）
  2. **接口解耦**（IDevice，UI 不认识具体设备）
  3. **事件通知**（DataReceived，采集→UI 单向推送）
  4. **双索引存储**（PointStore：List 遍历 + Dictionary 快查）
  5. **并发模型**（后台采集 + ConcurrentQueue + DispatcherTimer，真实工业架构）

<!-- CHUNK 15 -->

> 后面 M1~M8 大多是"往这套地基里填空"，你会越来越快。
