# C# 陷阱 · 前端转上位机必看(深度版)

> **优先级定位**:🔴 **必学先看** · 学 M0 之前先读这一份(否则后续会被一堆"前端没的概念"卡到怀疑人生)
> **技术来源**:🟦 C# 语法 + 🟩 .NET BCL(不装包,装好 .NET 就有)
> **给简历加的能力**:让你写的 C# 代码**不像前端写的** —— 不踩 struct 拷贝、不踩 async void 吞异常、不踩多线程竞态,代码能上生产。
> **前置**:无(这是 M0 之前的"防坑训练营")
> **谁该读**:**JS/TS 经验越足,这份越要看** —— 你越熟前端,这些坑踩得越深。

## 模块目标

你不是来学 C# 语法的(`var`/`if`/`for` 这些 1 小时看完 [M0](M0_每日讲义_深度版.md) 就会)。这份讲义**只讲 8 个前端工程师 100% 会踩的 C# 陷阱**:

1. **struct 值类型拷贝** — JS 只有引用类型,C# 的 struct 改了副本原值不变,UI 半天不刷新
2. **多线程并发 + lock** — JS 单线程无竞态,C# 多线程 `i++` 都能丢数
3. **字节序 + 位运算 + CRC** — JS 只有 `|`/`&`,工业协议全是 `^`/`<<`/`>>` 组合拳
4. **`async void` 吞异常** — JS 没这个区分,写错了一次进程崩
5. **强类型 + 可空引用类型** — JS 动态类型,`var x = null` 在 C# 里反复编译报错
6. **`IDisposable` + `using`** — JS 靠 GC,C# 串口/数据库/文件必须手动释放
7. **`delegate`/`event` 不是 DOM 事件** — 概念相似但坑点完全不同
8. **`P-Invoke` / `IntPtr`** — 工业相机/运动控制卡 SDK 全是 C++,前端完全没概念

**学完判据**:8 个陷阱每个都能 30 秒内说出"前端怎么写、C# 怎么写、为什么不同"。

---

## 🕳️ 陷阱 1:struct 值类型拷贝(JS 没有的最大坑)

### 一句话讲清楚
**JS 的所有对象都是引用类型**(改一个变量,另一个也变);**C# 有值类型(struct),赋值 = 拷贝一份新副本**(改副本,原值不变)。这是前端转 C# **第一致命坑**。

### 前端类比秒懂

| 概念 | JS | C# |
|---|---|---|
| 引用类型 | `{}`、`[]`、`class` 实例 | `class` 实例、`string`、`object`、`array` |
| 值类型 | **不存在**(JS 只有引用类型) | **`struct`、`int`、`double`、`bool`、`DateTime`、`enum`** |
| 赋值 `b = a` | b 和 a 指向同一份(改 b,a 也变) | class 同 JS;**struct 是拷贝**(改 b,a 不变) |

```js
// JS: 对象赋值是引用
const a = { x: 1 }; const b = a; b.x = 99; console.log(a.x); // 99(同一份)
```
```csharp
// C#: struct 赋值是拷贝
public struct Point { public int X; }
var a = new Point { X = 1 };
var b = a;            // ← struct 拷贝!b 是独立的一份
b.X = 99;
Console.WriteLine(a.X); // 1 ← a 没变!(和 JS 完全相反)
```

### 🔬 掰开揉碎:为什么 C# 要这么设计?
**性能**。struct 是分配在**栈**上(不是堆),`b = a` 拷贝 16 字节,几乎零开销。class 是分配在**堆**上,`b = a` 只拷贝指针(8 字节),但 GC 压力大。C# 让你选:小且频繁的数据用 struct,大且共享的数据用 class。

### 致命场景(上位机 100% 会踩)
**多线程里把 struct 传进方法修改,外层不变** —— UI 半天不刷新,debug 一整天:
```csharp
// 假设 SensorPoint 是 struct(为了性能,工业点位常用 struct)
public struct SensorPoint { public int Id; public double Value; }

var p = new SensorPoint { Id = 1, Value = 50.0 };
UpdateValue(p);   // 想把 Value 改成 60
Console.WriteLine(p.Value); // 还是 50!← 传进去的是拷贝

void UpdateValue(SensorPoint point) { point.Value = 60; } // 改的是副本
```

**正确写法**(三选一):
```csharp
// 方案 1: 改成 class(最简单)
public class SensorPoint { public int Id; public double Value; }

// 方案 2: 用 ref 关键字(传引用)
void UpdateValue(ref SensorPoint point) { point.Value = 60; }
UpdateValue(ref p);   // 调用方也要加 ref

// 方案 3: 返回新值(函数式风格,SensorPoint 不变)
var updated = p with { Value = 60 };   // C# 10 record/struct with 表达式
```

### ⚠️ 常见坑点预警
| 坑 | 说明 |
|---|---|
| **struct 数组 vs class 数组** | `struct[]` 是值数组(每个元素独立),`class[]` 是引用数组(元素指向对象) |
| **struct 在 `List<T>` 里** | `list[i].Value = 60` **编译报错!** 因为 `list[i]` 返回的是拷贝,要 `var p = list[i]; p.Value = 60; list[i] = p;` |
| **struct 多线程** | struct 拷贝是原子的(读到一半值不会),但跨线程改仍要 `lock` 或 `Interlocked` |
| **struct 当字典 key** | struct 可以,但要实现 `IEquatable<T>`(否则反射很慢) |

### 🧪 三档练习
- 🟢 **基础**:`DateTime` 是 struct 还是 class?`var d1 = DateTime.Now; var d2 = d1; d2 = d2.AddHours(1);` 后 `d1` 变了吗?
  **✅ 答案**:struct,`d1` 不变(同上 struct 拷贝原理)。
- 🟡 **进阶**:在 `List<SensorPoint>` 里(假设 SensorPoint 是 struct),为什么 `points[0].Value = 60` 编译报错?怎么改?
  **✅ 答案**:`points[0]` 返回 struct 副本,修改副本无意义,编译器禁止。改成 `var p = points[0]; p.Value = 60; points[0] = p;` 或换 class。
- 🔴 **挑战**:DAQMonitor 里 `SensorPoint` 该用 struct 还是 class?为什么?
  **✅ 答案**:**建议 class**。理由:① 多线程里频繁修改 Value,struct 拷贝会让 UI 看到的永远是旧值;② List<SensorPoint> 改单个字段麻烦;③ EF Core 对 struct 支持差。struct 的性能优势在 100Hz 以下采集完全用不到。

### 💡 工控导师说(真实战例)
> 我带过一个前端转 C# 的新人,他写的点位表 UI **每秒只刷新 1 次但数值永远是 0**。我看了 5 分钟代码:`SensorPoint` 是 struct,采集线程 `RaiseData(point)` 传进去的是拷贝,UI 订阅事件拿到的也是拷贝。改成 class 立刻好。
> **结论**:**新人项目里默认全用 class**,struct 留给"性能瓶颈优化阶段"再说。工业 99% 场景,class 性能足够。

---

## 🕳️ 陷阱 2:多线程并发 + lock(JS 单线程的人最痛)

### 一句话讲清楚
**JS 是单线程 + 事件循环**,你写 `i++` 永远不会丢数;**C# 是真多线程**,两个线程同时 `i++` **会丢一次**。前端转 C# 必须重新学"锁"。

### 前端类比秒懂
```js
// JS: 单线程,i++ 永远不会丢
let i = 0;
for (let n = 0; n < 1000; n++) setTimeout(() => i++, 0);
setTimeout(() => console.log(i), 1000); // 必然 1000
```
```csharp
// C#: 多线程,i++ 会丢!
int i = 0;
Parallel.For(0, 1000, n => i++);
Console.WriteLine(i); // 可能 997 / 998 / 999... ← 丢!
```

### 🔬 掰开揉碎:为什么 `i++` 会丢数?
`i++` **不是原子操作**,它在 CPU 上是 3 步:
```
1. 读 i 到寄存器
2. 寄存器 +1
3. 写回 i
```
线程 A 读到 i=5,还没写回;线程 B 也读到 i=5;两人都写回 6 → 丢了一次。

**前端没这个问题,因为 JS 单线程,事件循环一次只跑一个回调**。

### 上位机致命场景
**采集线程 + UI 线程同时改/读 List** → 数据丢失或 `InvalidOperationException`:
```csharp
// 错误!采集线程 Add,UI 线程 foreach,会崩
private List<SensorPoint> _points = new();
// 采集线程: _points.Add(p);    // 写
// UI 线程:   foreach (var p in _points) ...   // 读,可能崩
```

### 正确写法(三档)
```csharp
// 1. lock(最简单,够用 95% 场景)
private readonly object _gate = new();
private readonly List<SensorPoint> _points = new();

void Add(SensorPoint p) { lock (_gate) _points.Add(p); }
List<SensorPoint> Snapshot() { lock (_gate) return _points.ToList(); } // 复制一份返回,UI 慢慢读

// 2. ConcurrentQueue / ConcurrentDictionary(BCL 自带,无锁,性能高)
private readonly ConcurrentQueue<SensorPoint> _q = new();
void Add(SensorPoint p) => _q.Enqueue(p);

// 3. System.Threading.Channels(最现代,生产者-消费者异步,DAQMonitor 就用这个)
private readonly Channel<SensorPoint> _ch = Channel.CreateBounded<SensorPoint>(1000);
async Task ProduceAsync(SensorPoint p) => await _ch.Writer.WriteAsync(p);
async Task ConsumeAsync() { await foreach (var p in _ch.Reader.ReadAllAsync()) Process(p); }
```

### ⚠️ 常见坑点预警
| 坑 | 说明 |
|---|---|
| **锁 null / 锁 this** | 永远不要 `lock(null)` 或 `lock(this)`(可能被外部锁死),用 `private readonly object _gate = new();` |
| **lock 里 await** | `lock` 块里**不能** `await`(会死锁),用 `SemaphoreSlim` 替代 |
| **跨线程改 UI** | WPF/WinForms UI 控件只能 UI 线程改,跨线程要 `Dispatcher.Invoke(() => ...)` |
| **锁的粒度太大** | `lock(_gate) { 长耗时数据库写 }` 会阻塞所有 Add,锁要小 |

### 🧪 三档练习
- 🟢 **基础**:写出 4 个并发安全的"计数器"+1 实现。
  **✅ 答案**:`Interlocked.Increment(ref _count)`(最简单);`lock(_gate) _count++`;`ConcurrentQueue` + 计数;`Channel` + 计数。
- 🟡 **进阶**:DAQMonitor 里 `AcquisitionPipeline` 用了哪种?为什么?
  **✅ 答案**:`System.Threading.Channels`(无界)。理由:① 异步非阻塞,采集线程不卡;② 自然生产者-消费者模型;③ 比 ConcurrentQueue 更现代;④ 配合 `BatchReady` 事件批量消费。
- 🔴 **挑战**:为什么不能用 `lock(_gate) { await SaveChangesAsync(); }`?
  **✅ 答案**:`lock` 不允许 `await`(Monitor 不支持 async),编译报错。改用 `SemaphoreSlim(1,1)` + `await sem.WaitAsync()` + `try-finally sem.Release()`。

### 💡 工控导师说
> 多线程是前端转上位机**最长要补的能力**(预计 1-2 周才有直觉)。**别贪多**:`lock` + `ConcurrentQueue` + `Channel` 三件套够你 13-15K 了。面试问"你怎么处理并发采集",你说"`AcquisitionPipeline` 用 `Channel<T>` + 200ms 批量消费,UI 线程零阻塞",面试官眼睛就亮了。

---

## 🕳️ 陷阱 3:字节序 + 位运算 + CRC(协议层)

### 一句话讲清楚
**工业协议(Modbus/TCP/自定义帧)全是字节级 + 位运算**,前端只有 `|`/`&`/`^` 基础,**CRC 手算/字节序/位域**完全没概念。这是面试高频考点,必须肌肉记忆。

### 前端类比秒懂

| 概念 | 前端有吗 | C# 怎么用 |
|---|---|---|
| `|` `&` `^`(或/与/异或) | ✅ 有(位掩码) | 完全一样 |
| `<<` `>>`(位移) | ⚠️ 有但极少用 | 工业协议**天天用**,要熟 |
| 大端 / 小端 | 极少(网络字节序) | **核心知识点**,Modbus 用大端,Intel CPU 是小端 |
| CRC 校验 | 没概念 | **必考**!Modbus CRC16 必须背 |
| 位域/位打包 | 没概念 | 线圈/标志位用 1 byte 存 8 个 bool |

### 🔬 掰开揉碎:大端 vs 小端(0x12345678 怎么存?)

```
地址:    0    1    2    3
大端:   12   34   56   78   ← 高位在前(网络字节序、Modbus 数据域)
小端:   78   56   34   12   ← 低位在前(Intel CPU、Modbus CRC)
```
**关键陷阱**:Modbus 协议**数据域用大端,CRC 用小端**!这是现场翻车 No.1。详见 [M2 Day 1-3](M2_Modbus_深度版.md)。

### 必会位运算组合拳
```csharp
// 1. 把 2 字节拼成 ushort(大端:高位在前)
byte hi = 0x12, lo = 0x34;
ushort value = (ushort)((hi << 8) | lo);   // 0x1234 = 4660

// 2. 把 ushort 拆成 2 字节(大端)
ushort v = 0x1234;
byte hi = (byte)(v >> 8);    // 0x12
byte lo = (byte)(v & 0xFF);  // 0x34

// 3. 位掩码:从一个 byte 取第 n 位
byte flags = 0b1010_1010;
bool bit3 = (flags & (1 << 3)) != 0;   // 取第 3 位 → true

// 4. 异或 ^ : CRC 算法的核心
ushort crc = 0xFFFF;
crc ^= 0x01;   // 把 0x01 异或进 crc 的低字节

// 5. CRC16 右移异或(背!)
ushort crc = 0xFFFF;
foreach (byte b in data) {
    crc ^= b;
    for (int i = 0; i < 8; i++) {
        if ((crc & 0x0001) != 0) { crc >>= 1; crc ^= 0xA001; }
        else crc >>= 1;
    }
}
// 详见 M2 Day 1
```

### ⚠️ 常见坑点预警
| 坑 | 说明 |
|---|---|
| `BitConverter.IsLittleEndian` | CPU 默认小端,**别假设**跨平台一致,用 `BinaryPrimitives.ReadUInt16BigEndian()` |
| `float` 跨 2 寄存器字交换 | 详见 [M2 Day 3 ABCD/CDAB](M2_Modbus_深度版.md) |
| 位运算优先级 | `&` 优先级低于 `==`!`if (x & 0x80 == 0x80)` **错的**,要 `if ((x & 0x80) == 0x80)` |
| `int` 溢出 | 默认 checked 关闭,`int.MaxValue + 1 = int.MinValue`,涉及金额/计数要 `checked` |

### 🧪 三档练习
- 🟢 **基础**:`0x12 << 4 | 0x3` 等于多少?
  **✅ 答案**:`0x123`(`0x12 << 4 = 0x120`,`| 0x3 = 0x123`)。
- 🟡 **进阶**:写一个 `ushort ToUShortBigEndian(byte[] buf, int offset)` 函数。
- 🔴 **挑战**:手算 `01 03 00 00 00 02` 的 Modbus CRC16(不查表)。
  **✅ 答案**:`C4 0B`(详 [M2 Day 1](M2_Modbus_深度版.md))。

### 💡 工控导师说
> 面试让你白板写 Modbus CRC16,前端背景的 100% 卡壳。**别找理由**,"我是搞软件的不管字节"在上位机岗是减分项。**花 1 小时手算 20 遍**,这辈子忘不掉。卡死的时候,打开 [M2](M2_Modbus_深度版.md) 重看 Day 1。

---

## 🕳️ 陷阱 4:`async void` 吞异常(JS 没这个区分)

### 一句话讲清楚
**JS 没有 `async void` 这个区分**,所有 async 函数都返回 Promise;**C# 有 `async Task` 和 `async void` 两种**,后者**吞异常**(进程崩了你都不知道)。

### 前端类比秒懂
```js
// JS: async 函数返回 Promise,异常被 Promise.catch
async function f() { throw new Error("boom"); }
f().catch(e => console.log(e));   // ✓ 能 catch
f();                              // ✗ 异常变 UnhandledPromiseRejection,但进程不崩
```
```csharp
// C# 三种 async,只有前两种正常
async Task GoodAsync() { throw new Exception("boom"); }
await GoodAsync();               // ✓ 抛 await 处可 catch

async Task BadAsync() { throw new Exception("boom"); }
BadAsync();                      // ⚠️ 没 await,异常在 GC 时才抛(UnobservedTaskException)

async void WorseAsync() { throw new Exception("boom"); }
WorseAsync();                    // 💀 异常直接在 SynchronizationContext 抛,AppDomain.UnhandledException,进程崩
```

### 🔬 掰开揉碎:什么时候该用 `async void`?
**只有一个场景**:**UI 事件处理器**(因为事件签名要求 void):
```csharp
private async void BtnStart_Click(object sender, RoutedEventArgs e)
{
    try   // 必须 try-catch!async void 抛异常会崩进程
    {
        await StartAcquisitionAsync();
    }
    catch (Exception ex)
    {
        _log.Error(ex, "启动采集失败");
        MessageBox.Show(ex.Message);
    }
}
```
**其他所有场景一律 `async Task`**(或 `async Task<T>`)。

### 致命场景(前端必踩)
**事件订阅里 `async (s, e) => await xxx`** —— 这其实是 `async void`!
```csharp
// DAQMonitor M7 真实坑(已修正前):
_pipeline.BatchReady += async (s, batch) =>
{
    foreach (var p in batch)
        await _client.PublishAsync(BuildMsg(p));   // ⚠️ async void!
};
```
**问题**:
1. 异常被吞(PublishAsync 失败你看不到)
2. 多次触发会并发重入(BatchReady 每秒触发,上一次还没完)
3. 顺序乱(并发执行不保证顺序)

**正确写法**:
```csharp
// 方案 1: 用 Channel 做队列,后台 consumer 串行处理
private readonly Channel<List<SensorPoint>> _queue = Channel.CreateBounded<List<SensorPoint>>(100);

_pipeline.BatchReady += (s, batch) => _queue.Writer.TryWrite(batch);   // 同步入队

// 启动时启动 consumer
async Task ConsumeAsync(CancellationToken ct)
{
    await foreach (var batch in _queue.Reader.ReadAllAsync(ct))
        foreach (var p in batch)
            try { await _client.PublishAsync(BuildMsg(p)); }
            catch (Exception ex) { _log.Error(ex, "Publish 失败"); }
}
```

### ⚠️ 常见坑点预警
| 坑 | 说明 |
|---|---|
| `async void` 异常吞 | 必须包 try-catch,或改 `async Task` |
| `async void Lambda` | `(s, e) => { await ... }` 在事件订阅时是 async void |
| `.Result` / `.Wait()` | **死锁**(在 UI 线程调会卡死),必须 `await` |
| `fire-and-forget Task` | `_ = Task.Run(...)` 异常会 UnobservedTaskException,要 `.ContinueWith(Log)` 或 try |

### 🧪 三档练习
- 🟢 **基础**:为什么 `async void` 不该用?
  **✅ 答案**:异常在 SynchronizationContext 抛 = 进程崩;调用方无法 await;并发不可控。
- 🟡 **进阶**:把 `BatchReady += async (s, e) => await ...` 改成不会吞异常的版本。
- 🔴 **挑战**:为什么 `.Result` 在 UI 线程会死锁?
  **✅ 答案**:`.Result` 阻塞 UI 线程;异步方法内部 `await` 要回到 UI 线程(SynchronizationContext);两边互等 = 死锁。改法:全异步 `await`,或 `Task.Run(() => ...).Result`(后台线程没 SynchronizationContext 不会死锁)。

### 💡 工控导师说
> 我见过一个新人写的上位机,跑 2 小时崩一次找不到原因。看了半天代码:`DataReceived += async (s, e) => await SaveAsync(p)` —— async void + 偶发网络异常 = 进程崩。改成 `Task.Run + try-catch` 立刻稳。**前端转上位机,把"所有 async 都是 Promise"的直觉清空,重新学 `Task`/`async void`/`SynchronizationContext`**。

---

## 🕳️ 陷阱 5:强类型 + 可空引用类型(JS 动态类型)

### 一句话讲清楚
**JS 是动态类型**(`var x = 1; x = "hello"` 合法);**C# 是强类型**(`var` 是编译期推断锁死,**类型不可变**)。C# 8+ 默认开启 `<Nullable>enable`,**`null` 是另一类型**,前端惯性会反复编译报错。

### 前端类比秒懂
```ts
// TypeScript 接近 C#: 类型推断 + 严格模式
let x = 1;          // number
x = "hello";        // TS 报错!类型不可变

let s: string | null = null;   // 可空类型
s.toUpperCase();    // TS 报错!可能是 null
```
```csharp
// C# 几乎和 TS 一样,但更严
var x = 1;          // int
x = "hello";        // 编译报错!

string? s = null;   // 注意 ? = 可空引用类型
s.ToUpper();        // 编译警告!可能是 null

string t = "abc";   // 不可空
t = null;           // 编译警告!
```

### 🔬 掰开揉碎:`?` 和 `??` 和 `!` 和 `?.`
- `string?` — 可空类型(可能是 null)
- `s?.ToUpper()` — null 条件运算(如果 s 是 null,返回 null 不抛)
- `s ?? "default"` — null 合并(s 是 null 用右边)
- `s!` — null 宽容运算(告诉编译器"我知道不是 null,别警告")— 慎用!

```csharp
string? name = GetName();     // 可能 null
int len = name?.Length ?? 0;  // 安全:是 null 返 0
string upper = name!.ToUpper(); // 强行:不是 null,我保证(但运行时可能 NullReferenceException)
```

### ⚠️ 常见坑点预警
| 坑 | 说明 |
|---|---|
| 前端 `var x = null` | C# 编译报错(null 类型不可推断),要 `string? x = null;` |
| `int` vs `int?` | `int` 是值类型**不可为 null**,`int?` = `Nullable<int>` 包装 |
| `List<T>?` vs `List<T?>` | 前者是 list 可能为 null,后者是 list 里元素可能 null |
| 启动 `<Nullable>enable` | 项目文件加,让编译器当教官,前端必学 |

### 🧪 三档练习
- 🟢 **基础**:写一个 `string? GetGreeting(string? name)` 返回 `"Hello, " + name`,如果 name 是 null 返回 `"Hello, friend"`。
  **✅ 答案**:`return "Hello, " + (name ?? "friend");`
- 🟡 **进阶**:`int? x = null; int y = x ?? -1;` 后 y 等于多少?如果写 `int z = x;` 编译会怎样?
- 🔴 **挑战**:把 DAQMonitor 一个文件改 `<Nullable>enable`,处理所有警告。

### 💡 工控导师说
> 强类型是 C# 最大的礼物(前端 TS 写多了会爱不释手)。**第一天就开 `<Nullable>enable`**,编译器会强制你处理 null。一开始 200 个警告,改一周后代码质量提升一个数量级。

---

## 🕳️ 陷阱 6:`IDisposable` + `using`(资源管理)

### 一句话讲清楚
**JS 有 GC**(对象不用了自动回收);**C# 也有 GC 但托管资源之外的(串口、文件、数据库连接、Bitmap)必须手动 `Dispose`**,否则**文件锁死/连接耗尽/内存泄漏**。

### 前端类比秒懂
```js
// JS: 文件/连接/事件监听器 — 用完要 close,但忘了 GC 兜底(可能慢)
const fs = require('fs');
const fd = fs.openSync('a.txt', 'r');   // 忘 fs.closeSync(fd) → 句柄泄漏
```
```csharp
// C#: 必须 Dispose(实现 IDisposable 的类型)
var port = new SerialPort("COM3");   // 串口
port.Open();
// 忘 port.Dispose() → COM3 被占,下次 Open 抛 "端口被占用"
```

### 🔬 掰开揉碎:`using` 三种写法
```csharp
// 1. using 语句(经典,作用域结束自动 Dispose)
using (var port = new SerialPort("COM3"))
{
    port.Open();
    // ...
}   // ← 自动 port.Dispose()

// 2. using 声明(C# 8+,更简洁)
using var port = new SerialPort("COM3");
port.Open();
// ... 作用域结束自动 Dispose

// 3. await using(异步释放,如 DbContext)
await using var db = new AppDb();
await db.SaveChangesAsync();
```

### 必 Dispose 的 BCL 类型(高频)
- **`SerialPort`** / `TcpClient` / `NetworkStream` / `UdpClient` — 网络
- **`FileStream`** / `StreamReader` / `StreamWriter` — 文件
- **`SqlConnection`** / `DbContext` / `IDbTransaction` — 数据库
- **`Bitmap`** / `Graphics` / `Pen` / `Brush` — GDI+(WPF/WinForms 自绘)
- **`Timer`** / `CancellationTokenSource` — 计时/取消
- **`Mutex`** / `Semaphore` / `SemaphoreSlim` — 同步原语

### ⚠️ 常见坑点预警
| 坑 | 说明 |
|---|---|
| 忘 Dispose 串口 | COM3 被占,下次启动报错 |
| 忘 Dispose DbContext | 连接池耗尽,生产 1 小时挂 |
| 忘 Dispose Bitmap | GDI 句柄泄漏,2 小时后绘图崩 |
| `using` 嵌套 | `using var a = ...; using var b = ...;` OK;但要按声明逆序 Dispose |
| 在 `using` 里 `return` | `using` 仍会 Dispose(try-finally 保证) |

### 🧪 三档练习
- 🟢 **基础**:把 `var port = new SerialPort(...); port.Open();` 改成安全版本。
- 🟡 **进阶**:DAQMonitor `AcquisitionPipeline` 里 `Channel<T>` 要 Dispose 吗?为什么?
  **✅ 答案**:不用,`Channel<T>` 是托管对象;但 `ChannelReader`/`ChannelWriter` 用完调 `Complete()`(不是 Dispose)。
- 🔴 **挑战**:写一个 `IDisposable` 的类,实现 Dispose 模式(含 finalizer、SuppressFinalize)。

### 💡 工控导师说
> **判断标准**:看到 `new Xxx()`(Xxx 实现 IDisposable),立刻想"它在哪 Dispose"。前端转 C# 第一周强迫自己每个 new 都问这问题,1 周就有肌肉记忆。

---

## 🕳️ 陷阱 7:`delegate` / `event` 不是 DOM 事件

### 一句话讲清楚
**概念相似**(订阅/发布),**用法和坑不同**。前端 `addEventListener` 是字符串事件名;**C# event 是类型安全的函数指针**,你要 `+=` 订阅、`-=` 退订(不然内存泄漏,见 [陷阱 1 退订模式](#-掰开揉碎为什么-c-要这么设计))。

### 前端类比秒懂

| 概念 | 前端 DOM | C# event |
|---|---|---|
| 订阅 | `btn.addEventListener('click', fn)` | `btn.Click += fn;` |
| 退订 | `btn.removeEventListener('click', fn)` | `btn.Click -= fn;` |
| 触发 | DOM 自动 | **类内部 `Click?.Invoke(this, e)`**(外部不能触发) |
| 多订阅者 | ✅ | ✅ |
| 类型安全 | 弱(字符串名 + any fn) | 强(签名必须匹配 `EventHandler<T>`) |

```csharp
// C# event 完整模式
public class Device
{
    public event EventHandler<DataEventArgs>? DataReceived;   // 事件声明
    protected void RaiseData(int id, double v) =>
        DataReceived?.Invoke(this, new DataEventArgs { PointId = id, Value = v });
}

// 订阅方
device.DataReceived += OnData;   // 订阅
device.DataReceived -= OnData;   // 必须退订(否则 device 持有 OnData 引用 = 内存泄漏)
```

### ⚠️ 常见坑点预警
| 坑 | 说明 |
|---|---|
| 忘 `-=` 退订 | 内存泄漏(详见 M3 修正版) |
| Lambda 订阅 | `d.DataReceived += (s, e) => ...;` **无法 `-=`**(匿名,没名字),改具名方法或存字段 |
| 跨线程触发 | event 触发在调用线程,跨 UI 要 Dispatcher |
| null 竞态 | `DataReceived?.Invoke(...)` 安全(`?.`),旧代码 `if (DataReceived != null) DataReceived(...)` 中间可能 null |

### 🧪 三档练习
- 🟢 **基础**:`event` 和普通 `Action<T>` 字段有什么区别?
  **✅ 答案**:event 只能在声明类内部触发(封装),外部只能 +=/-=;Action 字段外部能直接调。
- 🟡 **进阶**:为什么 `device.DataReceived += (s, e) => SaveAsync(e.Value)` 是潜在坑?
  **✅ 答案**:① async void(见 [陷阱 4](#-陷阱-4async-void-吞异常js-没这个区分));② 无法 -=。

---

## 🕳️ 陷阱 8:`P-Invoke` / `IntPtr`(工业库是 C++)

### 一句话讲清楚
**工业相机/运动控制卡/数据采集卡**(海康/大恒/固高/雷赛)的 SDK **全是 C++ 写的 dll**,C# 要 `[DllImport]` 调,涉及**指针(IntPtr)、ref/out、内存管理、回调函数**。前端完全没概念,但是面试 13K+ 岗位可能问。

### 前端类比秒懂
- 前端调 Node Native 模块(`.node` 文件,C++ 写的)— 概念相似
- 但 C# 的 P-Invoke 比 Node N-API 更直接,接近"裸调"

### 最小示例
```csharp
using System.Runtime.InteropServices;

public static class MotionCard
{
    // 调用 C++ dll 中的函数
    [DllImport("gts.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern short GT_Open(short cardType, short cardNum, ref short handle);

    [DllImport("gts.dll")]
    public static extern short GT_Close(short handle);
}

// 调用
short handle = 0;
MotionCard.GT_Open(0, 1, ref handle);   // ref = 传引用(C++ 里是 short*)
MotionCard.GT_Close(handle);
```

### 核心概念
- **`IntPtr`** — 平台相关指针(32 位 4 字节,64 位 8 字节),C++ 的 `void*`
- **`ref T`** — 传引用(C++ 的 `T&`/`T*`)
- **`out T`** — 输出参数(类似 `ref` 但调用方不用初始化)
- **`[In]`/`[Out]`** —marshaller 提示
- **`StructLayout`** — 控制 struct 内存布局(对齐 C++ 结构体)
- **`Marshal.AllocHGlobal` / `FreeHGlobal`** — 手动分配/释放非托管内存

### ⚠️ 常见坑点预警
| 坑 | 说明 |
|---|---|
| 字符串编码 | C++ `char*` 是 ANSI,C# 默认 Unicode,要 `[MarshalAs(UnmanagedType.LPStr)]` |
| struct 对齐 | C++ struct 默认对齐可能 1/4/8,要 `[StructLayout(LayoutKind.Sequential, Pack=1)]` |
| 内存泄漏 | `Marshal.AllocHGlobal` 必须配对 `FreeHGlobal`,否则非托管泄漏(GC 兜不住) |
| 回调 | C++ 函数指针要 `[UnmanagedFunctionPointer]` 声明 delegate,防止 GC 回收 |
| x86 vs x64 | dll 位数要和程序匹配,VS 默认 AnyCPU 可能出问题 |

### 🧪 三档练习
- 🟢 **基础**:`IntPtr` 是什么?为什么不用 `int`?
  **✅ 答案**:平台相关指针,32 位是 4 字节,64 位是 8 字节;`int` 永远 4 字节,64 位下指针截断。
- 🟡 **进阶**:`ref` 和 `out` 区别?
  **✅ 答案**:`ref` 调用方必须先初始化,`out` 不用(被调方必须赋值)。

### 💡 工控导师说
> 13K 岗位可能不深挖 P-Invoke,但 15K 岗位面试官问"用过相机 SDK 吗",你能讲清 IntPtr/ref/StructLayout 就稳。**没真做过项目不要紧**,把这份讲义看懂,面试时说"我了解原理,但实际项目里我用的库都封装好了 P-Invoke 层"。

---

## 📌 温故知新(跨模块联动)
- 陷阱 1 struct → M0 Day 2、M4 PointStore 模型设计、M9 性能优化
- 陷阱 2 多线程 → **M0 Day 7**、M9 AcquisitionPipeline、M7 异步、M11 TCP
- 陷阱 3 字节序 → M1 串口、M2 Modbus CRC、M11 TCP 帧解析、M12 工程量
- 陷阱 4 async void → M7 OPC UA、M9 异步测试、所有 UI 事件处理器
- 陷阱 5 强类型 → 全程(`<Nullable>enable` 推荐开启)
- 陷阱 6 IDisposable → M1 串口、M4 DbContext、M11 TcpClient
- 陷阱 7 event → M0 Day 6、M3 PLC 订阅、M9 BatchReady
- 陷阱 8 P-Invoke → M3 PLC(底层)、M14 自定义控件(GDI)、M16 工业总线

## 📚 延伸阅读
- C# 值类型 vs 引用类型:https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/value-types
- async/await 淙深度:Stephen Toub《Async/Await FAQ》https://devblogs.microsoft.com/dotnet/async-faq-where-do-i-start/
- 多线程并发模型:《Concurrency in C# Cookbook》
- P-Invoke 速查:https://www.pinvoke.net/
- 全部外链见 [外部链接索引.md](外部链接索引.md)

## 🏗️ 项目任务(落到 DAQMonitor)
1. 在 `DaqMonitor.Core.csproj` 加 `<Nullable>enable</Nullable>`(项目根的 `<PropertyGroup>`)
2. 修复所有警告(预计 50-200 个,逐个处理,主要在 Models 和 Devices)
3. 检查所有 `new SerialPort` / `new TcpClient` / `new AppDb()` 都有 `using` 或显式 Dispose
4. 检查所有 `event +=` 都有对应 `-=`(尤其在 `Bootstrapper` 启停流程)
5. 把所有 `BatchReady += async (...)` 改成 Channel 模式(陷阱 4 的修正)
6. 提交 Git,commit message:"refactor: 修正前端背景必踩的 8 个 C# 陷阱"

## ✅ 打卡[ ]

---

> **最后一句**:这 8 个陷阱,前端背景的你**至少踩 5 个才会真懂**。别怕踩,**踩了就提交一次 bug fix 到 Git**,面试时讲"我踩过这些坑,这样修",面试官立刻信你不是纸上谈兵。学完这份讲义再进 M0,你会发现 M0 Day 1-7 顺畅得多。
