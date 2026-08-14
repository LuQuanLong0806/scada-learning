# C# 语法速查 — 前端视角

> **这份文档是干什么的**
> 22 个模块里有大量"未解释就使用"的 C# 语法(`new()` / `List<>` / `Dictionary<>` / `out` / `?.` / `=>` / `async`...)。如果你 5 年前端的底子,这些**不是 C# 难,是没人用 JS/TS 类比给你讲过**。
>
> **怎么用**:
> - 学习时遇到陌生语法 → Ctrl+F 搜这里
> - 每个章节独立,不需要从头读
> - 重点看 ⭐ 标记的"易踩坑"
>
> **类比原则**:每个 C# 概念都给 JS/TS 对照,但**不是 1:1 翻译** — 不同处会显式标 ⚠️

---

## 目录

1. [类型推断:`var` / `new()` / target-typed new](#1-类型推断)
2. [集合:`List<T>` / `Dictionary<K,V>` / `HashSet<T>` / `Queue<T>`](#2-集合)
3. [对象初始化器:`{ A = 1, B = 2 }`](#3-对象初始化器)
4. [属性:`{ get; set; }` 的 5 种形态](#4-属性)
5. [可空类型与 null 安全:`int?` / `?.` / `??`](#5-可空类型与-null-安全)
6. [方法参数:`ref` / `out` / `in` / `params`](#6-方法参数)
7. [Lambda / 委托 / 事件:`Action` / `Func<T>` / `event`](#7-lambda--委托--事件)
8. [`async` / `await` / `Task` / `CancellationToken`](#8-async--await)
9. [模式匹配:`is` / `switch expression`](#9-模式匹配)
10. [字符串与插值:`$"..."` / `@$"..."`](#10-字符串)
11. [元组:`(int x, int y)` / 解构](#11-元组)
12. [泛型 / interface / class / struct / record](#12-泛型--interface--class--struct--record)
13. [`using` / `namespace` / 文件组织](#13-using--namespace)
14. [`lock` 与线程安全集合](#14-lock-与线程安全)
15. [扩展方法 / LINQ 一行流](#15-扩展方法--linq)

---

## 📦 本文档用到的核心类型(粘贴即可编译)

> ### 🚦 使用说明(小白必读)
> **场景 A:你正在跟练 DAQMonitor 项目**(已建好 `DaqMonitor.Core/Models/SensorPoint.cs`)
> → **不要重复粘贴**!项目里已经有定义了。直接看 §1-§15 示例即可。
> 看到示例里的 `SensorPoint` 就当它是项目里的那个(虽然项目的 SensorPoint 是 readonly struct + 构造函数,本文档为了演示用 class + setter,语法演示用,**不要替换项目里的版本**)。
>
> **场景 B:你只是在空白项目里练 C# 语法**(没用 DAQMonitor)
> → 新建一个文件 `PredefinedTypes.cs`,放在项目根目录(跟 `Program.cs` 同级),粘贴下面的类型,**确保文件顶部的 namespace 是你的项目名**。
> 这样 §1-§15 示例代码就能找到 `SensorPoint` 等类型了。
>
> **如果看到"类型 SensorPoint 已存在"错误**:说明你的项目里已有定义,**不要重复粘贴**,直接用现有的。
>
> **如果看到"找不到类型 SensorPoint"错误**:检查 namespace 是否对齐 —— 示例代码隐式用全局命名空间,如果你的 `PredefinedTypes.cs` 写了 `namespace Foo`,示例代码也要 `using Foo;`。

```csharp
public class SensorPoint     // 或 struct,这里用 class 便于演示引用语义
{
    public int Id { get; set; }
    public double Value { get; set; }
    public string Name { get; set; } = "";
}

public class Device
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DeviceState State { get; set; }
}

public enum DeviceState { Offline, Connecting, Online }
```
> 想要更完整的"业务类型集合"(含 IDevice/DeviceBase/AlarmRule 等),见 [📦 前置类型定义 · 学员粘贴版](前置类型定义_学员粘贴版.md)。

---

## 1. 类型推断

### `var` — 让编译器推

```csharp
var x = 10;              // int
var name = "Tom";        // string
var points = new List<SensorPoint>();   // List<SensorPoint>
```

**JS/TS 类比**:就像 TS 里 `let x = 10` 不写类型 — 但 **C# 是编译时确定**,不是动态。

⚠️ **关键区别**:
```csharp
var x = 10;
x = "hello";   // ❌ 编译错误,C# 的 var 不是 JS 的 let
```

JS 的 `let` 类型可以变,C# 的 `var` 一旦推断完成就锁死类型。

### `new()` — target-typed new(C# 9.0+)

```csharp
List<SensorPoint> points = new();          // ✅ 等价 new List<SensorPoint>()
Dictionary<int, string> map = new() { [1] = "a" };   // ✅ 也能跟初始化器
SensorPoint p = new() { Id = 1, Value = 10 };        // ✅
```

**等价于**:
```csharp
List<SensorPoint> points = new List<SensorPoint>();
```

**用法判定**:左边已经写了类型时,右边用 `new()` 是简写。**不能**这样:
```csharp
var p = new();   // ❌ 编译器不知道 new 的是什么
```

⭐ **易踩坑**:看到 `= new()` 不要慌,看左边类型就知道是什么。

---

## 2. 集合

### `List<T>` — 动态数组(JS Array / TS `T[]`)

```csharp
List<int> nums = new() { 1, 2, 3 };
nums.Add(4);                    // push
nums.AddRange(new[] { 5, 6 });  // push(...arr)
nums.Count;                     // .length
nums[0];                        // 索引访问
nums.Contains(3);               // .includes(3)
nums.IndexOf(3);                // .indexOf(3)
nums.Remove(3);                 // splice(indexOf(3), 1)
nums.RemoveAt(0);               // splice(0, 1)
nums.Clear();                   // .length = 0
nums.ToArray();                 // 转数组
foreach (var n in nums) { ... }   // for...of
```

**JS 对照**:

| C# | JS/TS |
|----|-------|
| `nums.Add(x)` | `nums.push(x)` |
| `nums.Count` | `nums.length` |
| `nums.Count(x => x > 5)` | `nums.filter(x => x > 5).length` |
| `nums.Select(x => x * 2)` | `nums.map(x => x * 2)` |
| `nums.Where(x => x > 5)` | `nums.filter(x => x > 5)` |
| `nums.Sum()` | `nums.reduce((a, b) => a + b, 0)` |
| `nums.First()` | `nums[0]` (空集合抛异常!) |
| `nums.FirstOrDefault()` | `nums[0] ?? null` |

### `Dictionary<K,V>` — 哈希表(JS Map / TS Record)

```csharp
Dictionary<int, SensorPoint> byId = new();
byId[1] = new SensorPoint { Id = 1, Value = 10 };  // set
byId.Add(2, new SensorPoint { ... });              // 显式 add(重复 key 抛异常)

var p = byId[1];                                    // ⚠️ key 不存在会抛 KeyNotFoundException!
bool ok = byId.TryGetValue(2, out var p2);         // 安全取(不存在不抛异常)
bool has = byId.ContainsKey(3);                    // .has(3)

foreach (var kv in byId)
{
    Console.WriteLine($"{kv.Key}: {kv.Value}");
}

byId.Count;            // .size
byId.Remove(1);        // .delete(1)
byId.Clear();          // .clear()
```

⚠️ **最常踩坑**:`byId[key]` 不像 JS 的 `obj[key]` 返回 undefined,**会直接抛异常**。不确定 key 时一定要用 `TryGetValue`。

### `HashSet<T>` — 去重集合(JS Set)

```csharp
HashSet<string> tags = new() { "alarm", "warn" };
tags.Add("alarm");        // 不重复加,返回 false
tags.Contains("alarm");   // true
tags.Remove("alarm");

// 转换去重
var unique = new HashSet<int>(new[] { 1, 1, 2, 3 }).ToList();   // [1, 2, 3]
```

JS 类比:`new Set([1, 1, 2, 3])`,但 C# 的 `HashSet<T>` 是泛型强类型。

### `Queue<T>` / `Stack<T>` — 队列 / 栈

```csharp
Queue<Alarm> q = new();
q.Enqueue(alarm);          // push 到队尾
var next = q.Dequeue();    // 从队首取(FIFO)
q.Peek();                  // 看队首不取

Stack<int> s = new();
s.Push(1);
var top = s.Pop();         // LIFO
```

⚠️ JS 里队列和栈都用 Array 的 push/pop/shift/unshift,C# 必须显式选 `Queue<T>` 或 `Stack<T>`。

### `IEnumerable<T>` — "可遍历的"(JS Iterable)

`List<T>` / `Dictionary<K,V>` / `HashSet<T>` / 数组都实现 `IEnumerable<T>`。意思是"可以用 foreach 遍历"。

```csharp
IEnumerable<int> GetNumbers()
{
    yield return 1;        // 惰性,像 JS 的 generator
    yield return 2;
}

foreach (var n in GetNumbers()) { ... }
```

JS 类比:Iterable / Iterator / `function*` generator。

### 何时选哪个(决策表)

| 场景 | 用什么 | 为什么 |
|------|--------|--------|
| 时序数据(温度采样点) | `List<T>` | 顺序、按索引、可重复 |
| 按 Id 查设备 | `Dictionary<int, Device>` | O(1) 查找 |
| 去重(设备名) | `HashSet<string>` | 唯一性 |
| FIFO 队列(报警待处理) | `Queue<Alarm>` | 先进先出 |
| LIFO 栈(撤销操作) | `Stack<Action>` | 后进先出 |
| 不可变常量 | `ReadOnlyCollection<T>` / `IReadOnlyList<T>` | 安全 |
| 跨线程并发 | `ConcurrentDictionary<K,V>` | lock-free |

---

## 3. 对象初始化器

### 创建对象 + 给属性赋值

```csharp
public class SensorPoint
{
    public int Id { get; set; }
    public double Value { get; set; }
}

// 方式 1:对象初始化器(常用)
var p = new SensorPoint { Id = 1, Value = 10 };

// 等价于
var p = new SensorPoint();
p.Id = 1;
p.Value = 10;

// 顺序不重要,逗号分隔
var p2 = new SensorPoint { Value = 10, Id = 1 };
```

**JS 类比**:
```typescript
// TS
const p: SensorPoint = { id: 1, value: 10 };
```

⚠️ **关键区别**:
- TS 的对象字面量**可以加任意字段**(取决于 strict 配置)
- C# **只能赋已定义的属性** — `new SensorPoint { Foo = 1 }` 编译失败

### 集合初始化器

```csharp
List<int> nums = new() { 1, 2, 3, 4 };
List<SensorPoint> points = new()
{
    new() { Id = 1, Value = 10 },
    new() { Id = 2, Value = 20 }
};

Dictionary<int, string> map = new()
{
    [1] = "one",       // 索引初始化器
    [2] = "two"
};
```

---

## 4. 属性

C# 的属性是"看起来像字段,实际是方法(get/set)"。前端可以理解为 JS 的 `Object.defineProperty` getter/setter。

### 5 种形态

```csharp
public class Device
{
    // ① 自动属性(最常用) — 编译器自动生成私有字段
    public int Id { get; set; }

    // ② 只读属性 — 只能在构造函数里赋值
    public string SerialNumber { get; }

    // ③ init-only — 只能在对象初始化器里赋值(C# 9.0+)
    public string Name { get; init; }

    // ④ 私有 set — 外部只读,内部可写
    public DeviceState State { get; private set; }

    // ⑤ 表达式体属性 — 计算属性
    public bool IsOnline => State == DeviceState.Connected;

    // ⑥ 带逻辑的 get/set(完整写法)
    private double _value;
    public double Value
    {
        get => _value;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException();
            _value = value;
        }
    }
}
```

**JS 类比**:
```javascript
class Device {
    constructor() {
        this._id = 0;
    }
    get id() { return this._id; }
    set id(v) { this._id = v; }
}
```

⭐ **易踩坑**:`public int X { get; set; }` 不是字段,是两个方法(get_X / set_X)。**永远用属性,不要用 public 字段** — 这是 C# 的强约定。

### init-only(C# 9.0+,record 友好)

```csharp
public record SensorConfig(string Name, double Threshold);

var c = new SensorConfig("温度", 50.0);
c.Name = "压力";   // ❌ init-only,创建后不能改
```

类似 TS 的 `readonly`,但更灵活 — 只能在 `new() { }` 初始化器里赋。

---

## 5. 可空类型与 null 安全

### 引用类型可空(C# 8.0+)

```csharp
string name = "Tom";     // 不可空,编译器保证一定有值
string? maybeName = null; // 可空引用类型(注意 ? 后缀)

if (maybeName != null)
{
    Console.WriteLine(maybeName.Length);   // 编译器知道这里不是 null
}
```

**TS 类比**:`string` vs `string | null`。但 C# 的 `?` 编译时强制,运行时**不会**自动抛错(只是 warning)。

### 值类型可空 `int?` / `double?`

```csharp
int x = 10;
int? maybeX = null;        // 注意:int 是值类型,默认 0,不能是 null
                            // 加 ? 包成 Nullable<int>

if (maybeX.HasValue)
    Console.WriteLine(maybeX.Value);

int fallback = maybeX ?? 0;   // ?? 是 null-coalescing(类似 ??)
```

⚠️ 值类型 vs 引用类型的 `?` 含义**不一样**:
- `int?` = `Nullable<int>`(包装类型,新增 HasValue/Value)
- `string?` = 还是 string,但编译器开启 null 检查

### 三大 null 操作符

```csharp
// ① ?. null 条件成员访问
device?.Name                  // device 是 null 时返回 null,不抛
list?.Count                   // 同上
dictionary?[1]                // 索引器也有 ?.

// ② ?? null 合并
var name = maybeName ?? "默认";

// ③ ??= null 合并赋值(C# 8.0+)
config ??= new Config();      // config 为 null 才赋新值

// ④ ! null 抑制(我知道不是 null,别警告)
var x = maybeName!.Length;    // 强行解引用,出事自己负责
```

**JS/TS 类比**:`?.` 完全一致,`??` 完全一致,`??=` 类似 `||=`。

⭐ **易踩坑**:看到 `device?.Name.Length` 注意 — 如果 device 为 null,**整个表达式**返回 null,不是只 `device?.Name` 返回 null 然后 `.Length` 抛异常。C# 的 `?.` 是短路到底的。

---

## 6. 方法参数

### `ref` — 引用传递(双向)

```csharp
void Increment(ref int x) { x++; }

int n = 5;
Increment(ref n);     // 必须写 ref
Console.WriteLine(n); // 6
```

JS 没有,因为 JS 对象是引用但基础类型是值。`ref` 让值类型也按引用传。

### `out` — 输出参数(方法返回多个值)

```csharp
bool TryParse(string s, out int result)
{
    if (int.TryParse(s, out result)) return true;
    result = 0;
    return false;
}

if (TryParse("42", out var num))    // out var 是 C# 7.0+ 简写
{
    Console.WriteLine(num);
}
```

**JS 类比**:JS 没有原生 out,只能用解构返回对象或数组:
```javascript
function tryParse(s) {
    if (...) return [true, parseInt(s)];
    return [false, 0];
}
const [ok, num] = tryParse("42");
```

C# 的 `out` 就是这个意思,但语法上更直接。

⭐ **必背**:`TryGetValue` / `TryParse` / `int.TryParse` 是最常见的 out 用法。

### `in` — 只读引用(性能优化)

```csharp
double Calculate(in BigStruct data)   // 不拷贝,但不能改
{
    return data.X + data.Y;
}
```

性能优化用,struct 大时避免拷贝。日常少见。

### `params` — 可变参数(JS 的 `...args`)

```csharp
int Sum(params int[] nums) => nums.Sum();

Sum(1, 2, 3);      // 等价 Sum(new[] { 1, 2, 3 })
Sum(1, 2, 3, 4, 5);
```

**JS 类比**:`function sum(...nums)` 完全一致。

---

## 7. Lambda / 委托 / 事件

### Lambda 表达式

```csharp
Func<int, int> square = x => x * x;          // 输入 int,返回 int
Action<string> log = msg => Console.WriteLine(msg);   // 无返回
Func<int, int, int> add = (a, b) => a + b;

// 多语句
Func<int, int> abs = x =>
{
    if (x < 0) return -x;
    return x;
};
```

**JS 类比**:`const square = x => x * x` 完全一致。

### `Action` / `Func<T>` — 委托类型

C# 的"函数类型"必须显式声明:

| C# 类型 | 含义 | TS 等价 |
|---------|------|---------|
| `Action` | 无参数无返回 | `() => void` |
| `Action<int>` | 一个参数无返回 | `(x: number) => void` |
| `Action<int, string>` | 两参数无返回 | `(x: number, s: string) => void` |
| `Func<int>` | 无参数返回 int | `() => number` |
| `Func<int, string>` | 输入 int 返回 string | `(x: number) => string` |
| `Func<int, int, int>` | 两 int 入,一 int 出 | `(a: number, b: number) => number` |
| `Predicate<int>` | 输入 int 返回 bool | `(x: number) => boolean` |

⭐ **易踩坑**:`Func<>` 最后一个泛型参数是返回类型,前面都是入参。

### `event` — 事件(发布订阅)

```csharp
public class Device
{
    // 声明事件(基于 EventHandler 委托)
    public event EventHandler<DataEventArgs>? DataReceived;

    public void SimulateReceive(double value)
    {
        // 触发事件(注意 ?.Invoke,避免空引用异常)
        DataReceived?.Invoke(this, new DataEventArgs { Value = value });
    }
}

public class DataEventArgs : EventArgs
{
    public double Value { get; set; }
}

// 订阅
device.DataReceived += (sender, e) =>
{
    Console.WriteLine($"收到 {e.Value}");
};

// 取消订阅
device.DataReceived -= handler;
```

**JS 类比**:像 EventEmitter / Node.js 的 `EventEmitter`:
```javascript
const emitter = new EventEmitter();
emitter.on('data', (e) => console.log(e.value));
emitter.emit('data', { value: 10 });
```

⚠️ **关键区别**:
- C# 的 `event` 只能在声明它的类内部 `Invoke`,外部只能 `+=` / `-=`
- C# 的 event 是**多播委托**(一个事件多个订阅者)

⭐ **必背**:
- `event EventHandler<T>?` 声明
- `Event?.Invoke(this, args)` 触发
- `event += handler` / `event -= handler` 订阅/退订

---

## 8. async / await

### `Task` — 异步操作(JS Promise)

```csharp
// 等价 JS:async function 返回 Promise
public async Task<int> GetCountAsync()
{
    await Task.Delay(100);    // 等价 await new Promise(r => setTimeout(r, 100))
    return 42;
}

// 调用
int count = await GetCountAsync();
```

| C# | JS |
|----|-----|
| `Task` | `Promise<void>` |
| `Task<T>` | `Promise<T>` |
| `void`(async) | `Promise<void>`(但 async void 危险,见下) |
| `await task` | `await promise` |
| `Task.Delay(100)` | `new Promise(r => setTimeout(r, 100))` |
| `Task.Run(() => ...)` | `new Promise(resolve => queueMicrotask(...))` |
| `Task.WhenAll(t1, t2)` | `Promise.all([p1, p2])` |
| `Task.WhenAny(t1, t2)` | `Promise.race([p1, p2])` |
| `CancellationToken` | `AbortSignal` |

### ⭐ async void 陷阱(必踩)

```csharp
// ❌ async void — 异常会**直接崩溃进程**,无法 catch
public async void DoSomething() { throw new Exception(); }

try { DoSomething(); }   // 抓不到!异常直接逃逸到 SynchronizationContext
catch (Exception) { }

// ✅ async Task — 异常能正常捕获
public async Task DoSomethingAsync() { throw new Exception(); }

try { await DoSomethingAsync(); }
catch (Exception) { /* 抓得到 */ }
```

**唯一允许 async void 的场景**:WPF 事件处理器(button_Click)。其他都 async Task。

### `.Result` / `.Wait()` 死锁陷阱

```csharp
// ❌ UI 线程上调 .Result — 死锁!
public void ButtonClick()
{
    var result = SomeAsyncMethod().Result;   // 死锁:等 UI 线程,但 UI 线程被这个调用占着
}

// ✅ 一路 async
public async void ButtonClick()    // WPF 事件允许 async void
{
    var result = await SomeAsyncMethod();
}
```

### `CancellationToken` — 取消令牌

```csharp
public async Task RunAsync(CancellationToken ct)
{
    for (int i = 0; i < 1000; i++)
    {
        ct.ThrowIfCancellationRequested();   // 检查是否取消
        await Task.Delay(100, ct);
    }
}

// 调用方
using var cts = new CancellationTokenSource();
var task = RunAsync(cts.Token);
cts.Cancel();    // 5 秒后取消
```

**JS 类比**:AbortController + AbortSignal:
```javascript
const controller = new AbortController();
fetch(url, { signal: controller.signal });
controller.abort();
```

---

## 9. 模式匹配

### `is` 模式

```csharp
object obj = "hello";
if (obj is string s && s.Length > 3)
{
    Console.WriteLine(s);     // s 已声明 + 赋值,作用域在 if 内
}

if (obj is int or long or double)
{
    // 数字类型
}
```

### switch expression(C# 8.0+,前端最爱)

```csharp
// 像是 TS 的 switch expression + 解构
string Describe(DeviceState state) => state switch
{
    DeviceState.Idle => "空闲",
    DeviceState.Moving => "运动中",
    DeviceState.Alarm => "报警",
    _ => "未知"     // _ 是默认
};

// 类型模式
string Describe(object o) => o switch
{
    null => "null",
    int i when i > 0 => $"正整数 {i}",
    int i => $"非正整数 {i}",
    string s => $"字符串 {s}",
    _ => "其他"
};
```

**TS 类比**:
```typescript
const describe = (state: DeviceState): string => {
    switch (state) {
        case 'Idle': return '空闲';
        case 'Moving': return '运动中';
        default: return '未知';
    }
};
```

C# 的 switch expression 更紧凑,而且**模式匹配是类型安全的**(漏 case 编译警告)。

---

## 10. 字符串

### 插值 `$"..."`(JS 模板字符串)

```csharp
var name = "Tom";
var age = 25;
Console.WriteLine($"我叫 {name},今年 {age} 岁");     // 我叫 Tom,今年 25 岁

// 表达式
Console.WriteLine($"明年 {age + 1}");                 // 明年 26

// 格式化(:N2 = 2 位小数)
double pi = 3.14159;
Console.WriteLine($"{pi:F2}");                        // 3.14
Console.WriteLine($"{DateTime.Now:yyyy-MM-dd}");      // 2026-08-04
```

**JS 类比**:`Hello ${name}` 完全一致。

### Verbatim 字符串 `@"..."`(忽略转义)

```csharp
// 多行字符串 + 反斜杠不转义(像文件路径)
string path = @"C:\Users\admin\daq.db";

// 等价于
string path2 = "C:\\Users\\admin\\daq.db";

// 多行
string json = @"
{
    ""name"": ""Tom"",
    ""age"": 25
}";
```

**JS 类比**:像 String.raw:
```javascript
const path = String.raw`C:\Users\admin\daq.db`;
```

### `$@` 组合(插值 + verbatim,常用于多行 SQL 或 JSON)

```csharp
string sql = $@"
    SELECT * FROM Users
    WHERE Age > {minAge}
    ORDER BY Name";
```

### 字符串构建 `StringBuilder`(性能)

```csharp
// 大量拼接时用(避免创建大量中间字符串)
var sb = new StringBuilder();
for (int i = 0; i < 1000; i++)
    sb.Append($"Line {i}\n");
string result = sb.ToString();
```

---

## 11. 元组

### 创建与解构

```csharp
// 创建(C# 7.0+)
var point = (X: 10, Y: 20);
Console.WriteLine(point.X);    // 10
Console.WriteLine(point.Y);    // 20

// 解构
var (x, y) = GetPosition();
(int x2, int y2) = GetPosition();

// 方法返回多值(替代 out)
(int X, int Y) GetPosition()
{
    return (10, 20);
}
```

**JS 类比**:JS 早就有了元组解构:
```javascript
const [x, y] = getPos();       // JS 数组解构
const {x, y} = getPos();       // JS 对象解构
```

C# 元组比 JS 的"假装元组"更严格 — `(int X, int Y)` 是真正的值类型,有命名字段。

### 弃元 `_`

```csharp
var (_, y) = GetPosition();      // 只要 Y,_ 表示不要
if (int.TryParse("42", out _))   // 不关心结果值
{
}
```

⭐ **现代 C# 风格**:能用元组就别用 `out`,代码更易读。

---

## 12. 泛型 / interface / class / struct / record

### class vs struct

```csharp
// 引用类型(堆) — 类似 JS 的 class
public class Sensor
{
    public int Id { get; set; }
}

// 值类型(栈)— 复制时整体拷贝
public struct SensorReading
{
    public double Value;
    public DateTime Time;
}

Sensor s1 = new Sensor { Id = 1 };
Sensor s2 = s1;       // 引用拷贝,s1.Id 改了 s2 也变
s2.Id = 99;           // s1.Id 也是 99 ⚠️

SensorReading r1 = new SensorReading { Value = 10 };
SensorReading r2 = r1;   // 值拷贝
r2.Value = 99;            // r1.Value 还是 10 ✅
```

**JS 类比**:JS 全是引用,没有 struct。struct 是 C# 为了性能(避免堆分配)而存在。

⭐ **易踩坑**:struct 拷贝陷阱 — `r2 = r1` 之后改 r2 不影响 r1(和 class 相反)。

### interface — 像抽象类 / TS interface

```csharp
public interface IDevice
{
    string Name { get; }
    Task ConnectAsync();
    event EventHandler<DataEventArgs>? DataReceived;
}

public class SerialDevice : IDevice    // 实现接口
{
    public string Name { get; }
    public event EventHandler<DataEventArgs>? DataReceived;

    public SerialDevice(string port) { Name = port; }

    public async Task ConnectAsync() { ... }
}
```

**TS 类比**:几乎完全一致 — interface 定义契约,class implements。

### 泛型 — 像 TS `<T>`

```csharp
public class Repository<T> where T : class   // 泛型约束(T 必须是引用类型)
{
    private List<T> _items = new();
    public void Add(T item) => _items.Add(item);
    public T Get(int id) => _items[id];
}

var deviceRepo = new Repository<Device>();
var userRepo = new Repository<User>();
```

**TS 类比**:`class Repository<T extends object>`。

### record(C# 9.0+,不可变值对象)

```csharp
// 注意:本节用 PointRecord 演示,避免和 SensorPoint(class,见本文档顶部)重名冲突
public record PointRecord(int Id, double Value);

var p1 = new PointRecord(1, 10.0);
var p2 = new PointRecord(1, 10.0);
Console.WriteLine(p1 == p2);     // True!record 重写了 == 基于值相等

// with 表达式(基于现有创建新副本,改某字段)
var p3 = p1 with { Value = 20 };  // p3 = PointRecord(1, 20)
```

⭐ **何时用 record**:
- 不可变数据(DTO / 配置 / 消息)
- 想要基于值的相等比较
- 想要 with 表达式创建副本

class 适合"有身份、有状态变化"的对象,record 适合"数据快照"。

---

## 13. using / namespace

### `using` — 导入命名空间(像 import)

```csharp
using System;                    // 基础类型 Console / DateTime
using System.Collections.Generic; // List / Dictionary
using System.Linq;               // LINQ 扩展方法
using System.Threading.Tasks;    // Task / async
using DaqMonitor.Core.Motion;    // 项目内模块

// 别名
using Motion = DaqMonitor.Core.Motion;
using Dict = System.Collections.Generic.Dictionary<int, string>;

// 全局 using(C# 10.0+,放在 Program.cs 顶部一次,全工程有效)
global using System;
global using System.Linq;
```

**JS 类比**:`import { Console } from 'system'` 类似,但 C# 的 using 是**命名空间**(可包含多个类型),不是单个 export。

### `using static`(C# 6.0+)

```csharp
using static System.Math;     // 直接用 Math 的静态成员

double x = Sqrt(2);            // 不用写 Math.Sqrt
```

### `namespace` — 命名空间声明

```csharp
// 文件作用域命名空间(C# 10.0+,推荐)
namespace DaqMonitor.Core.Motion;     // 整个文件属于这个 namespace

public class AxisController { ... }

// 传统写法(块作用域)
namespace DaqMonitor.Core.Motion
{
    public class AxisController { ... }
}
```

### `using` 语句 — 资源释放(Dispose)

```csharp
// 自动 Dispose(IDbConnection / FileStream / Timer 等)
using (var conn = new SQLiteConnection(connStr))
{
    conn.Open();
    // 用 conn
}   // 自动调 conn.Dispose()

// 现代简写(C# 8.0+)
using var conn = new SQLiteConnection(connStr);
conn.Open();
// 函数结束自动 Dispose
```

**JS 类比**:JS 没有原生 using,但有 `try-finally`:
```javascript
const conn = createConn();
try { ... } finally { conn.close(); }
```

C# 的 `IDisposable` 模式 = 强制约定 try-finally。

⭐ **必背**:`IDbConnection` / `FileStream` / `StreamReader` / `Timer` / `Mutex` / 自定义带 timer 的类 都必须 `using`。

---

## 14. lock 与线程安全

### `lock` — 互斥锁

```csharp
private readonly object _lock = new();

public void Add(double value)
{
    lock (_lock)
    {
        _values.Add(value);
        _lastUpdate = DateTime.Now;
    }
}

public double[] GetSnapshot()
{
    lock (_lock)
    {
        return _values.ToArray();
    }
}
```

**JS 类比**:JS 是单线程,不需要 lock。但 Worker 之间共享内存(SharedArrayBuffer)需要 Atomics。C# 的 lock 是经典的 monitor。

⚠️ **关键规则**:
- `lock` 的对象必须是 **`private readonly object`** — 不要 lock(this)、lock(typeof(X)、lock(string)
- lock 持有时间尽量短 — 不要在 lock 里调外部代码(可能死锁)

### 并发集合(免 lock)

```csharp
// 跨线程安全的集合
var dict = new ConcurrentDictionary<int, string>();
dict.TryAdd(1, "hello");
dict.TryGetValue(1, out var v);

var queue = new ConcurrentQueue<int>();
queue.Enqueue(1);
queue.TryDequeue(out var item);

var bag = new ConcurrentBag<int>();    // 无序
```

⭐ **优先用并发集合**,而不是 `Dictionary + lock`。

### `Interlocked`(原子操作)

```csharp
private int _counter;
Interlocked.Increment(ref _counter);      // 原子 ++
Interlocked.Add(ref _counter, 10);        // 原子 +=
Interlocked.Exchange(ref _counter, 0);    // 原子赋值
```

---

## 15. 扩展方法 / LINQ

### 扩展方法(给已有类型加方法)

```csharp
// 定义(static class + this 参数)
public static class StringExtensions
{
    public static bool IsNullOrEmpty(this string? s) => string.IsNullOrEmpty(s);
    public static string Truncate(this string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max);
}

// 使用(像原生方法)
string? name = null;
if (name.IsNullOrEmpty()) { ... }
"hello world".Truncate(5);    // "hello"
```

**JS 类比**:JS 直接改 prototype,但危险。C# 的扩展方法更安全 — 只在视觉上是方法,实际是静态调用。

### LINQ(集合操作一站式)

```csharp
var nums = new List<int> { 1, 2, 3, 4, 5, 6 };

// 过滤
var evens = nums.Where(n => n % 2 == 0);              // [2, 4, 6]

// 映射
var doubled = nums.Select(n => n * 2);                // [2, 4, 6, 8, 10, 12]

// 聚合
var sum = nums.Sum();                                 // 21
var avg = nums.Average();                             // 3.5
var max = nums.Max();                                 // 6
var count = nums.Count();                             // 6

// 排序
var sorted = nums.OrderBy(n => n).ToList();
var desc = nums.OrderByDescending(n => n).ToList();

// 分组
var grouped = nums.GroupBy(n => n % 2 == 0 ? "even" : "odd");
// [[odd, [1,3,5]], [even, [2,4,6]]]

// 取首/尾
var first = nums.First();                             // 1(空集合抛异常!)
var firstOrDef = nums.FirstOrDefault();               // 0(空集合返回默认值)
var last = nums.Last();

// 元素判断
var any = nums.Any(n => n > 3);                       // true
var all = nums.All(n => n > 0);                       // true

// 拼接
var combined = string.Join(", ", nums);               // "1, 2, 3, 4, 5, 6"

// 转 Dictionary
var dict = nums.ToDictionary(n => n, n => n * n);     // {1:1, 2:4, 3:9, ...}
```

**JS 对照**:

| C# LINQ | JS 数组方法 |
|---------|-------------|
| `.Where(f)` | `.filter(f)` |
| `.Select(f)` | `.map(f)` |
| `.SelectMany(f)` | `.flatMap(f)` |
| `.OrderBy(k)` | `.sort((a,b) => ...)` |
| `.GroupBy(k)` | 自己 reduce |
| `.Any(f)` / `.All(f)` | `.some(f)` / `.every(f)` |
| `.First()` / `.FirstOrDefault()` | `[0]` |
| `.Take(n)` / `.Skip(n)` | `.slice(0, n)` / `.slice(n)` |
| `.Distinct()` | `[...new Set(arr)]` |
| `.Sum()` / `.Max()` / `.Average()` | `.reduce(...)` |
| `.Aggregate(seed, func)` | `.reduce(func, seed)` |

⭐ **现代 C# 必备** — 看到 `.Where().Select().ToList()` 不要慌,就是 filter + map + 数组化。

---

## 速查地图:看到陌生语法去哪查

| 你看到的 | 章节号 | 一句话解释 |
|---------|--------|-----------|
| `var x = ...` | §1 | 让编译器推类型 |
| `X x = new()` | §1 | 简写 `new X()` |
| `List<T>` / `Dictionary<K,V>` | §2 | 数组 / 哈希表 |
| `.TryGetValue(k, out var v)` | §2/§6 | 安全取值,不存在不抛 |
| `new X { A = 1 }` | §3 | 创建对象同时赋属性 |
| `{ get; set; }` | §4 | 属性(自动 getter/setter) |
| `=> ...` | §4/§7/§9 | lambda / 表达式体 |
| `string?` / `int?` | §5 | 可空类型 |
| `?.` / `??` / `??=` | §5 | null 安全操作符 |
| `ref` / `out` / `params` | §6 | 参数修饰符 |
| `Action` / `Func<T>` | §7 | 函数类型(委托) |
| `event E?` / `E?.Invoke` | §7 | 发布订阅 |
| `async Task<T>` / `await` | §8 | 异步 |
| `CancellationToken` | §8 | 取消令牌 |
| `switch expression` | §9 | 模式匹配 |
| `$"..."` / `@"..."` | §10 | 插值 / verbatim 字符串 |
| `(int x, int y) = ...` | §11 | 元组解构 |
| `record` | §12 | 不可变值对象 |
| `using` 语句 | §13 | 资源释放 |
| `lock (_obj)` | §14 | 互斥锁 |
| `.Where().Select()` | §15 | LINQ(filter/map) |

---

## 学习路径建议

如果你完全没接触过 C#:
1. **先精读 §1-§5**(类型推断 / 集合 / 初始化 / 属性 / null) — 这些是每个文件都会出现的
2. **再读 §6-§9**(参数 / 委托 / async / 模式匹配) — 上位机通讯代码的核心
3. **§10-§15 按需查** — 不是每个模块都用

如果你已经能看懂 60% 的代码:
- 把这份文档**当字典**,遇到陌生语法 Ctrl+F
- 不要从头到尾读,效率低

---

## 这份文档不是终点

如果有这里**没讲到**的语法让你云里雾里,直接在 22 模块的 .md 文件里加 `<details>` 提问,或直接告诉我补这一节。这份速查会持续更新。
