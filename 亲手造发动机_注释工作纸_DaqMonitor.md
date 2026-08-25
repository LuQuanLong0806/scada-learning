# ⚙️ 亲手造发动机 · 注释工作纸(DaqMonitor 卷)

> **玩法**:每行代码只给**中文注释**,不给代码。你开一个空白 .cs 文件,照着注释把代码写出来——**能根据注释写出代码 = 这个文件真正属于你**。
> **覆盖 5 个文件**(🧠核心逻辑档,合计 ~320 行):SimulatedDevice / AcquisitionPipeline / AlarmRule+AlarmEvent+AlarmEngine。
> R4 调库版 ModbusDevice(~100 行)不进工作纸——它有专门的跟敲文档(R4 附录,FluentModbus),照那个走。
> **规则**:🔧 = 脚手架已给(照抄);🧠① = 你要写的行(注释说清干什么,自己想语法)。写完对照原文件 diff,再替换跑测试。

---

## 使用方法(每张工作纸都一样,3 步)

1. **新建空白文件**写到标注的 📂 路径(先别覆盖原文件——把原文件改名 `XXX.参考.cs` 备份)
2. **照注释写代码**,卡壳超 15 分钟 → 翻[逐行导读](项目逐行导读_DaqMonitor_从零到吃透.md)对应站(那里每行都有讲解)
3. **验收三连**:`dotnet build` 0 错误 → `dotnet test` **85 全绿** → 和 `.参考.cs` diff,差异逐个问自己"为什么它那样写"
   - 测试不过 = 你写的零件装不回整机 → 定位修复(这本身就是最好的学习)

---

## 工作纸 1 · SimulatedDevice.cs(80 行,数据源头)

> 📂 `DAQMonitor/src/DaqMonitor.Core/Devices/SimulatedDevice.cs` · 对应[导读第 4 站](项目逐行导读_DaqMonitor_从零到吃透.md) · 验收:85 测试全绿(管道测试用它产数)

```csharp
using DaqMonitor.Core.Models;
using System.Threading;

namespace DaqMonitor.Core.Devices;

// 🔧 类声明:公共类,继承 DeviceBase(第 3 站的基类)
public class SimulatedDevice : DeviceBase
{
    // 🧠① 私有只读字段:这个设备管哪些点位的编号数组(int[])
    // 🧠② 私有只读字段:随机数发生器(new() 目标类型推断)
    // 🧠③ 私有字段:取消令牌源(可空,CancellationTokenSource?)——停止后台循环的"红色按钮"
    // 🧠④ 私有字段:后台循环任务本体(可空,Task?)

    // 🔧 构造函数签名已给:params 可变参数收点位编号
    public SimulatedDevice(int id, string name, params int[] pointIds)
        : base(id, name)
        // 🧠⑤ 一行初始化:点位数组为空时兜底成只含 1 的数组(三元表达式)
        => ____________________;

    // 🔧 重写 Connect
    public override void Connect()
    {
        // 🧠⑥ 状态先置 Connecting
        // 🧠⑦ 睡 50 毫秒模拟握手耗时(Thread.Sleep,真设备连接都有耗时)
        // 🧠⑧ 状态置 Online
    }

    // 🔧 重写 Disconnect
    public override void Disconnect()
    {
        // 🧠⑨ 先调 Stop() 停产数循环(先停业务再改状态的顺序!)
        // 🧠⑩ 状态置 Offline
    }

    // 🔧 重写 Read:合同要求的"主动问一句"
    public override double Read(int addr)
        // 🧠⑪ 返回 0~100 的随机数,保留两位小数(Math.Round + NextDouble)
        => ____________________;

    // 🔧 重写 Write:模拟设备只读
    public override void Write(int addr, double value) { /* 🧠⑫ 空实现+一行注释说明为什么 */ }

    // 🔧 模拟设备独有方法(不在 IDevice 合同里!):开始产数
    public void Start(TimeSpan interval)
    {
        // 🧠⑬ 幂等守卫:循环已在跑就 return(防连点两次开两个循环)
        // 🧠⑭ new 令牌源存字段,取 Token 存局部变量
        // 🧠⑮ Task.Run 启动后台异步循环,任务句柄存 _loop(内部逻辑见下)
        _loop = Task.Run(async () =>
        {
            try
            {
                // 🧠⑯ while 循环:令牌没被取消就一直转
                {
                    // 🧠⑰ foreach 遍历每个点位编号
                    {
                        // 🧠⑱ 造值:10% 概率(NextDouble<0.1)落在 95~120(故意越界触发报警),否则 20~90
                        // 🧠⑲ 按广播按钮 RaiseData:点位号 + 保留两位小数的值(数据从这里上路!)
                    }
                    // 🧠⑳ 异步睡 interval,带令牌(睡梦中能被叫醒)
                }
            }
            // 🧠㉑ catch OperationCanceledException,空处理(正常退场,吞掉)
        }, token);
    }

    // 🔧 停止方法
    public void Stop()
    {
        // 🧠㉒ 按红色按钮(可空条件下 Cancel)
        // 🧠㉓ 最多等循环 500ms 收尾(Wait 带超时,try-catch 忽略)
        // 🧠㉔ 释放令牌源,两个引用字段置 null(下次 Start 能重新启动)
    }
}
```

**对照要点**(写完 diff 时重点看):⑮⑳ 令牌怎么"叫醒"Task.Delay;⑱ 概率分支的括号顺序;㉒㉔ Stop 与 Start 的对称性。

---

## 工作纸 2 · AcquisitionPipeline.cs(80 行,大动脉 ★最重要)

> 📂 `DAQMonitor/src/DaqMonitor.Core/Acquisition/AcquisitionPipeline.cs` · 对应[导读第 5 站](项目逐行导读_DaqMonitor_从零到吃透.md) · 验收:85 全绿(AcquisitionPipelineTests 直接考它)

```csharp
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Models;
using System.Threading.Channels;

namespace DaqMonitor.Core.Acquisition;

// 🔧 密封类,实现 IDisposable
public sealed class AcquisitionPipeline : IDisposable
{
    // 🧠① 无界 Channel<SensorPoint>(线程安全传送带)
    // 🧠② 已注册设备列表(List<IDevice>,Dispose 退订用)
    // 🧠③ 取消令牌源(控制消费循环生死)
    // 🧠④ System.Threading.Timer(定时 Flush)
    // 🧠⑤ 专用锁对象(object)+ 共享攒批列表(List<SensorPoint>)——两行
    // 🧠⑥ 攒批上限(int,只读)

    // 🔧 批量就绪事件(后台线程触发,UI 订阅方自行切线程)+ 错误上报事件
    public event EventHandler<IReadOnlyList<SensorPoint>>? BatchReady;
    public event EventHandler<Exception>? Error;

    // 🔧 构造(间隔, 上限默认500)
    public AcquisitionPipeline(TimeSpan flushInterval, int maxBatch = 500)
    {
        // 🧠⑦ 存上限
        // 🧠⑧ new Timer:到点调 Flush,延迟与周期都是 flushInterval(第3参 null)
        // 🧠⑨ fire-and-forget 启动消费循环(下划线丢弃返回值)
    }

    // 🔧 注册设备
    public void Register(IDevice device)
    {
        // 🧠⑩ 订阅设备的 DataReceived → 本类 OnPoint
        // 🧠⑪ 设备加入列表
    }

    // 🔧 事件回调(设备每广播一次进来一次)
    private void OnPoint(object? sender, DataEventArgs e)
        // 🧠⑫ 一行:把事件参数翻译成 SensorPoint 结构体,Writer.TryWrite 进队(回调只做这一件事!)
        => ____________________;

    // 🔧 后台消费循环
    private async Task ConsumeAsync()
    {
        try
        {
            // 🧠⑬ await foreach 读 Channel 队列(带取消令牌)
            {
                // 🧠⑭ 声明可空批次变量 List<SensorPoint>? = null
                // 🧠⑮ lock 锁内:把这条点攒进 _pending;攒到上限 → 整张列表交出去(batch=_pending),当场换新列表
                // 🧠⑯ 锁外:批次非空才广播 BatchReady(为什么放锁外?→ 第 5 站)
            }
        }
        // 🧠⑰ catch OperationCanceledException(正常退出,吞)
        // 🧠⑱ catch 其他 Exception → Error 事件上报(绝不静默)
    }

    // 🔧 定时冲刷(节流阀)
    private void Flush()
    {
        // 🧠⑲ 与 ⑮⑯ 同款:锁内有货就整张端走换新的,锁外广播(空批次不广播)
    }

    // 🔧 善后
    public void Dispose()
    {
        // 🧠⑳ 五连,顺序:取消令牌 → 释放 Timer → 封 Channel 写端(TryComplete)→ 逐设备退订事件 → 释放令牌源
    }
}
```

**对照要点**:⑮ 的"换列表"手法(batch = _pending; _pending = new());⑯ 广播在锁外;⑫ 回调极简主义。

---

## 工作纸 3 · AlarmRule.cs(14 行)+ AlarmEvent.cs(11 行)+ AlarmEngine.cs(53 行)

> 📂 `DAQMonitor/src/DaqMonitor.Core/Alarms/` 三个文件 · 对应[导读第 7 站](项目逐行导读_DaqMonitor_从零到吃透.md) · 验收:85 全绿(AlarmEngineTests 直接考回滞/边沿)

**3a · AlarmRule(规则单,热身)**

```csharp
using DaqMonitor.Core.Models;

namespace DaqMonitor.Core.Alarms;

// 🔧 公共类 AlarmRule
{
    // 🧠① 属性:管哪个点位(int,自动属性)
    // 🧠② 属性:阈值(double)
    // 🧠③ 属性:报警级别(AlarmLevel 枚举)
    // 🧠④ 属性:方向 bool IsHigh,默认 true(超过阈值报警)
    // 🧠⑤ 属性:回滞带宽 double Hysteresis(防阈值附近抖动)
}
```

**3b · AlarmEvent(广播内容单,热身)**

```csharp
// 🔧 继承 EventArgs 的类,三个 init 只读属性:PointId / Level / Value(照 DataEventArgs 模式写)
```

**3c · AlarmEngine(引擎,主菜)**

```csharp
using DaqMonitor.Core.Models;

namespace DaqMonitor.Core.Alarms;

// 🔧 公共类 AlarmEngine
{
    // 🧠① 规则表 List<AlarmRule>
    // 🧠② 活跃报警集合 HashSet<int>(边沿触发的"记忆")
    // 🧠③ 专用锁对象

    // 🔧 两个事件:AlarmTriggered(上升沿)/ AlarmCleared(下降沿),参数都是 AlarmEvent
    // 🔧 Add(加规则)/ Clear(清空,注意也清 _active)——都 lock

    // 🔧 核心:每条数据评一次
    public void Evaluate(SensorPoint p)
    {
        // 🧠④ 锁内给规则表拍快照(ToList),锁外遍历快照(为什么?→ 第 7 站)
        foreach (var r in snapshot)
        {
            // 🧠⑤ 规则不管这个点位 → continue
            // 🧠⑥ 判断是否越界:IsHigh ? 大于阈值 : 小于阈值(bool breach)
            // 🧠⑦ 判断是否在回滞带内:带宽>0 且 值与阈值差的绝对值 ≤ 带宽(bool inBand)

            // 🧠⑧ if 真越界(breach 且不在带内):
            {
                // 🧠⑨ 锁内:_active.Add 这个点位,返回值取反存 wasActive(一行原子完成"查+加")
                // 🧠⑩ 只有"之前不在"(第一次变坏)才广播 AlarmTriggered(上升沿)
            }
            // 🧠⑪ else if 已回正常(不越界 且 带宽>0):
            {
                // 🧠⑫ 锁内:_active.Remove,返回值存 wasActive
                // 🧠⑬ 只有"之前在报"(真恢复)才广播 AlarmCleared(下降沿)
            }
        }
    }
}
```

**对照要点**:⑨⑫ HashSet.Add/Remove 的返回值语义(不用先 Contains!);④ 快照遍历;⑧ 的双条件。

---

## 完工仪式

5 张纸全过(测试全绿 + diff 能讲出差异原因)后,做两件事:

1. **git commit 你的版本**:`git commit -m "feat: 亲手重写发动机 5 件(工作纸验证通过)"`——这是"我写的"最硬证据;
2. **脱稿讲 3 分钟**:不看任何材料,把"一条数据从 SimulatedDevice 出生到表盘变红"讲一遍(录音更好)——讲得顺,发动机真的在你手里了。

> 然后才是运控卷(运控项目建好后,工作纸会以同款格式发你)。
