# MC3 · WinForms 主界面(控件数组 + 集中刷新 + 跨线程事件)

> **系列导航**:[MC1 骨架与模拟卡](项目实践_MotionControl_MC1_工程骨架与模拟卡.md) → [MC2 卡的行为测试](项目实践_MotionControl_MC2_卡的行为测试.md) → **MC3 WinForms 主界面** → [MC4 UI 冒烟验收](项目实践_MotionControl_MC4_UI冒烟验收.md) → [MC5 两轴直线插补(可选)](项目实践_MotionControl_MC5_两轴直线插补.md) → [MC6 轨迹可视化(可选)](项目实践_MotionControl_MC6_轨迹可视化.md)
> **定位**:v1 的界面"功能都在但一身的病" —— 速度写死、按钮状态靠缘分、复制粘贴 handler、事件 Thread.Sleep 卡界面。本篇把 UI 整个重做:**只收集输入、调 IMotionCard、把事件刷回界面**,不含任何运动算法 —— 修掉坑⑥⑦⑨⑩,顺手立起"换真卡零改动"的结构。
> **前置**:MC2(卡 + 12 个测试全绿)。
> **预计开发时长**:跟敲 1 天(Designer 较长,分两次敲不丢人)。**先只看「📋 需求单」自己写,卡住再看「🛠️ 参考实现」对答案。**

---

## 🎯 本篇交付物

1. 完整可用的两轴运控主界面:顶栏(连接 + 急停)/ 两轴操作栏 / 报警 + 日志栏;
2. `dotnet build` 0 错 0 警,MC2 的 12 个测试照旧全绿(界面层不许把卡改坏);
3. 手动验收清单逐条可过(见「✅ 验证」末尾)。

---

## 📋 需求单(产品经理视角 —— 先自己想怎么做)

### 本篇功能需求 FR 表

| 编号 | 需求 | 验收标准 |
|---|---|---|
| FR-U01 | 三区布局:顶栏连接控制 + 急停;主体三栏 = 轴1 / 轴2 / 报警+日志 | 急停是全窗体唯一红色按钮,右上角锚定 |
| FR-U02 | 两轴控件收进数组,一段循环完成全部事件订阅 | 加第三轴只需改数组,不复制粘贴 handler |
| FR-U03 | 按钮可用性集中在一个 `RefreshUiState()` 里统一计算 | 未连接/未使能/有报警时,对应按钮准确灰掉;急停永远可用 |
| FR-U04 | 卡的三个事件全走 `InvokeRequired + BeginInvoke` 切回 UI 线程 | 后台事件刷界面永不抛跨线程异常 |
| FR-U05 | 速度/目标手工输入**防呆 + 自愈** | 速度非法自动回退 50 并纠正文本框;目标非法就地日志报错,永不崩溃 |
| FR-U06 | 报警框与日志框视觉彻底区分;日志同时落盘 | 淡黄底红字报警 / 黑底绿字日志;`logs\motion_日期.txt` 生成 |
| FR-U07 | 100ms 定时器轮询 + "运动完成"边沿检测 | 完成只在停止那一刻报一次,不刷屏 |
| FR-U08 | 关窗前断开卡 | 关窗后进程干净退出,日志文件不被占用 |

**先自己想**:
① 两根轴 × 9 个控件 = 18 个两两对应,怎么订阅才不用复制粘贴 18 个 handler?提示:v1 的坑⑨就是复制粘贴出来的;
② 循环里订阅 lambda,有个 C# 经典坑会让两个轴的按钮都以为自己是"轴 3",是什么、怎么躲?
③ "按钮什么时候能点"这条规则,写在每个 click handler 里(v1 的做法)和写在一个统一函数里,改需求时哪个会漏?
④ 卡的事件在后台线程触发,直接碰控件会抛异常 —— 三个事件处理器怎么写成"一个模式";
⑤ "运动完成"卡并没有事件上报,界面怎么知道运动结束了?(提示:你已有 100ms 定时器);
⑥ 操作员在速度框里输入 "abc" 再按点动,你的程序应该弹窗?崩溃?还是默默修好继续干?

---

## 📚 本篇知识点(不懂再点回去学)

- [📖 WinForms 跨线程访问控件](kp:winforms-invoke) —— InvokeRequired/BeginInvoke 的原理与用法
- [📖 event / EventHandler 事件机制](kp:event-delegate) —— 订阅卡的事件并转发给界面
- [📖 DI 依赖注入](kp:di) —— 构造函数注入 IMotionCard,测试/真卡随便换

---

## 🛠️ 参考实现(卡住/写完再看)

### 步骤 1:线程安全日志 LogHelper

**设计思路一句**:所有关键动作落盘 —— 断网车间出了事,现场人员能给你的往往只有日志文件;多线程同时写必须加锁串行化。

```csharp
// 📂 文件:src/MotionControl/Common/LogHelper.cs
namespace MotionControlProject.Common;

/// <summary>
/// 文件日志 —— 上位机的"黑匣子"。
/// 断网车间里出了问题,现场人员能给你的往往只有这个日志文件,所以关键动作必须落盘。
///
/// v1 坑:直接 File.AppendAllText 没加锁 —— 多线程同时写文件时,两行日志会穿插成乱码。
/// v2:所有写入包在 lock 里串行化,一行永远是一行。
/// </summary>
public static class LogHelper
{
    /// <summary>写文件互斥锁:静态类全局唯一,任何线程的写入排队通过。</summary>
    private static readonly object Gate = new();

    /// <summary>日志目录:exe 旁的 logs\,按天分文件,方便按日期回溯故障。</summary>
    private static string LogDir => Path.Combine(AppContext.BaseDirectory, "logs");

    /// <summary>
    /// 写一条日志并落盘。
    /// level 用 "INFO"/"WARN"/"ERROR"(对齐 -5 左对齐,日志列才整齐)。
    /// </summary>
    public static void Log(string message, string level = "INFO")
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level,-5}] {message}";
        lock (Gate)   // 没这把锁,后台线程和 UI 线程同时写就是乱码
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(
                Path.Combine(LogDir, $"motion_{DateTime.Now:yyyyMMdd}.txt"),
                line + Environment.NewLine);
        }
    }
}
```

💡 三个门道:**静态类 + 静态锁**(全进程一把锁,谁写都排队);**按天分文件**(故障回溯按日期找);**`{level,-5}` 左对齐**(INFO/WARN/ERROR 长度不同,补齐 5 位,日志列才不会歪)。

### 步骤 2:程序入口 Program.cs

```csharp
// 📂 文件:src/MotionControl/Program.cs
namespace MotionControlProject;

/// <summary>程序入口。net8 WinForms 模板写法:ApplicationConfiguration.Initialize() 负责高 DPI / 字体 / 默认样式。</summary>
internal static class Program
{
    [STAThread]   // WinForms 硬要求:UI 线程必须是 STA(剪贴板、文件对话框等 COM 组件依赖)
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        // 想接真卡时,只改这一行:new UI.MainForm(new Device.XxxRealCard("192.168.0.10"))
        Application.Run(new UI.MainForm());
    }
}
```

💡 `[STAThread]` 别删:WinForms 大量依赖 COM(剪贴板、拖放、文件对话框),STA 单线程公寓是它们的运行前提。`Application.Run` 启动消息循环 —— WinForms 的一切(按钮点击、Paint、BeginInvoke 回调)都从这个循环里派发,这个概念 MC4 冒烟测试还要用到。

### 步骤 3:主窗体 MainForm.cs(逻辑全在这)

**设计思路一句**:窗体 = 纯"翻译官" —— 用户输入翻译成卡指令,卡事件翻译成界面变化;两轴逻辑写成"控件数组 + 循环",按钮状态集中一处计算。

```csharp
// 📂 文件:src/MotionControl/UI/MainForm.cs
using MotionControlProject.Common;
using MotionControlProject.Device;

namespace MotionControlProject.UI;

/// <summary>
/// 主窗体 —— 只做三件事:收集用户输入 → 调 IMotionCard → 把卡的事件刷回界面。
/// 本文件不含任何运动算法:换一张真卡(实现 IMotionCard 即可),这里一行都不用改。
///
/// 相比 v1 的结构改进:
/// 1. 两轴控件收进数组,事件订阅用一段循环 —— v1 每个按钮复制粘贴一个 handler,改一处漏一处;
/// 2. 按钮可用性集中在一个 RefreshUiState() 里统一算 —— v1 里 btnMoveAbs1 被禁了两次、
///    btnMoveAbs2 一次都没禁过,就是"各改各的"的恶果;
/// 3. 卡的事件都在后台线程触发,统一用 InvokeRequired + BeginInvoke 切回 UI 线程再碰控件
///    (v1 已有此好习惯,v2 保留并发扬到所有事件)。
/// </summary>
public partial class MainForm : Form
{
    private readonly IMotionCard _card;

    // ———— 控件数组:下标 0 = 轴 1,下标 1 = 轴 2 ————
    private readonly Button[] _btnEnable;
    private readonly Button[] _btnDisable;
    private readonly Button[] _btnJogForward;
    private readonly Button[] _btnJogBackward;
    private readonly Button[] _btnMoveAbs;
    private readonly Button[] _btnHome;
    private readonly TextBox[] _txtPos;
    private readonly TextBox[] _txtSpeed;
    private readonly TextBox[] _txtAbs;

    /// <summary>"运动完成"检测:记录上一帧(100ms 前)是否在运动,状态从运动翻转为静止时打一条完成日志。</summary>
    private readonly bool[] _wasMoving = new bool[2];

    /// <summary>默认构造:生产环境用模拟卡。Designer 必须有无参构造才能在 VS 里打开设计器。</summary>
    public MainForm() : this(new MockMotionCard()) { }

    /// <summary>依赖注入入口:测试或接真卡时从这里塞入任意 IMotionCard 实现。</summary>
    public MainForm(IMotionCard card)
    {
        InitializeComponent();
        _card = card;

        // Designer 生成的两轴控件按轴序收进数组,后面所有"每轴逻辑"都能写成循环
        _btnEnable      = new[] { btnEnable1, btnEnable2 };
        _btnDisable     = new[] { btnDisable1, btnDisable2 };
        _btnJogForward  = new[] { btnJog1Forward, btnJog2Forward };
        _btnJogBackward = new[] { btnJog1Backward, btnJog2Backward };
        _btnMoveAbs     = new[] { btnMoveAbs1, btnMoveAbs2 };
        _btnHome        = new[] { btnHome1, btnHome2 };
        _txtPos         = new[] { txtPos1, txtPos2 };
        _txtSpeed       = new[] { txtSpeed1, txtSpeed2 };
        _txtAbs         = new[] { txtAbs1, txtAbs2 };

        // ———— 事件订阅全部集中在这里,Designer 文件只管"长相" ————

        btnConnect.Click    += (s, e) => Connect();
        btnDisconnect.Click += (s, e) => DisconnectCard();
        btnEstop.Click      += (s, e) => EmergencyStop();
        btnClearAlarm.Click += (s, e) => ClearAlarms();

        for (var i = 0; i < 2; i++)
        {
            var axis = i;   // 闭包捕获副本的经典坑:for 的 i 是所有循环共享的变量,
                            // 不复制一份,两个按钮的 lambda 里拿到的都会是循环结束后的 2

            _btnEnable[i].Click      += (s, e) => SetAxisEnabled(axis, true);
            _btnDisable[i].Click     += (s, e) => SetAxisEnabled(axis, false);

            // 点动:按下启动、松开停止 —— MouseDown/MouseUp 而不是 Click(v1 的正确直觉,v2 保留)
            _btnJogForward[i].MouseDown  += (s, e) => StartJog(axis, forward: true);
            _btnJogForward[i].MouseUp    += (s, e) => StopJog(axis);
            _btnJogBackward[i].MouseDown += (s, e) => StartJog(axis, forward: false);
            _btnJogBackward[i].MouseUp   += (s, e) => StopJog(axis);

            _btnMoveAbs[i].Click += (s, e) => MoveAbs(axis);
            _btnHome[i].Click    += (s, e) => Home(axis);
        }

        // 卡 → 界面:三个事件分别在"位置变化 / 报警变化 / 急停"时被后台线程触发
        _card.PositionChanged  += OnPositionChanged;
        _card.AlarmChanged     += OnAlarmChanged;
        _card.EmergencyStopped += OnEmergencyStopped;

        // 100ms 界面轮询:刷新按钮状态 + 检测"运动完成"。
        // 为什么用轮询而不是事件?模拟卡没有"运动完成"事件,真卡 SDK 也常常只有状态位 ——
        // "定时查状态 + 边沿检测"是上位机最常用、最稳的完成检测手段(采集项目的管道心跳同理)。
        // Tick 事件已在 Designer 里绑定,这里只设定周期并启动
        timer1.Interval = 100;
        timer1.Start();

        RefreshUiState();
        AppendLog($"系统就绪:模拟卡已加载,{_card.AxisCount} 轴。请先【连接】再【使能】。");
    }

    // ———— 连接区 ————

    private void Connect()
    {
        // v1 坑:txtIp 里带了个前导空格,连接永远失败还看不出来 —— Trim + 判空在门口挡掉
        var ip = txtIp.Text.Trim();
        var r = _card.Connect(ip);
        if (r != MotionResult.Ok) { Fail(r, "连接"); return; }
        AppendLog($"已连接模拟卡 {ip}(共 {_card.AxisCount} 轴)");
        RefreshUiState();
    }

    private void DisconnectCard()
    {
        var r = _card.Disconnect();
        if (r != MotionResult.Ok) { Fail(r, "断开"); return; }
        AppendLog("已断开连接,所有运动已停止");
        RefreshUiState();
    }

    /// <summary>急停:唯一一个红色按钮,点击立即执行、不做任何确认弹窗 —— 急停就该一按就停。</summary>
    private void EmergencyStop()
    {
        _card.StopAll();
        AppendLog("!! 急停触发:全部轴已停止,位置冻结 !!", "ERROR");
        RefreshUiState();
    }

    private void ClearAlarms()
    {
        // 两个轴都清一遍;无报警的轴清了也无副作用(幂等)
        _card.ClearAlarm(0);
        _card.ClearAlarm(1);
        AppendLog("已清除全部报警(v1 的 btnClearAlarm 根本没绑 Click 事件 —— 纯摆设,这里是修复)");
        RefreshUiState();
    }

    // ———— 每轴操作 ————

    private void SetAxisEnabled(int axis, bool enable)
    {
        var r = _card.SetAxisEnable(axis, enable);
        if (r != MotionResult.Ok) { Fail(r, $"轴{axis + 1} {(enable ? "使能" : "失能")}"); return; }
        AppendLog($"轴{axis + 1} {(enable ? "已使能" : "已下使能")}");
        RefreshUiState();
    }

    private void StartJog(int axis, bool forward)
    {
        var speed = SpeedOf(_txtSpeed[axis]);
        var r = _card.JogAxis(axis, speed, forward);
        if (r != MotionResult.Ok) { Fail(r, $"轴{axis + 1} 点动"); return; }
        AppendLog($"轴{axis + 1} 点动 {(forward ? "正转 ▲" : "反转 ▼")} @ {speed:F0} mm/s");
    }

    private void StopJog(int axis)
    {
        _card.StopJog(axis);   // 没连卡时也只是返回错误码,不值得刷屏,不打日志
    }

    private void MoveAbs(int axis)
    {
        // 目标位置解析失败要就地报出来,v1 是"按了没反应"式沉默失败
        if (!double.TryParse(_txtAbs[axis].Text.Trim(), out var target))
        {
            AppendLog($"✗ 轴{axis + 1} 目标位置不是数字:{_txtAbs[axis].Text}", "WARN");
            return;
        }
        var speed = SpeedOf(_txtSpeed[axis]);
        var r = _card.MoveAbsolute(axis, target, speed);
        if (r != MotionResult.Ok) { Fail(r, $"轴{axis + 1} 绝对定位"); return; }
        AppendLog($"轴{axis + 1} 绝对定位 → {target:F2} mm @ {speed:F0} mm/s");
    }

    private void Home(int axis)
    {
        var r = _card.HomeAxis(axis);
        if (r != MotionResult.Ok) { Fail(r, $"轴{axis + 1} 回零"); return; }
        AppendLog($"轴{axis + 1} 回零启动(固定 {MockMotionCard.HomeSpeed:F0} mm/s)…");
    }

    // ———— 卡事件 → 界面(全部先切回 UI 线程) ————

    private void OnPositionChanged(object? sender, PositionChangedEventArgs e)
    {
        // 事件来自后台仿真线程。WinForms 铁律:非 UI 线程碰控件就抛 InvalidOperationException
        if (InvokeRequired) { BeginInvoke(() => OnPositionChanged(sender, e)); return; }
        _txtPos[e.Axis].Text = e.Position.ToString("F3");
    }

    private void OnAlarmChanged(object? sender, AlarmChangedEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(() => OnAlarmChanged(sender, e)); return; }
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        if (e.IsActive)
            txtAlarm.AppendText($"[{stamp}] 轴{e.Axis + 1} 报警:{e.Message}\r\n");
        else
            txtAlarm.AppendText($"[{stamp}] 轴{e.Axis + 1} 报警已清除\r\n");
        RefreshUiState();
    }

    private void OnEmergencyStopped(object? sender, EventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(() => OnEmergencyStopped(sender, e)); return; }
        AppendLog("卡上报:EmergencyStop 已生效", "ERROR");
    }

    // ———— 定时器:状态轮询 + 运动完成边沿检测 ————

    private void Timer1_Tick(object? sender, EventArgs e)
    {
        for (var i = 0; i < 2; i++)
        {
            var moving = _card.IsMoving(i);
            // 上一帧在动、这一帧停了 = 运动刚完成(边沿检测,只报一次)
            if (_wasMoving[i] && !moving)
                AppendLog($"轴{i + 1} 运动完成,停在 {_card.GetAxisPosition(i):F3} mm");
            _wasMoving[i] = moving;
        }

        RefreshUiState();
    }

    // ———— 界面状态集中刷新 ————

    /// <summary>
    /// 所有按钮的 Enabled 状态只在这里计算。
    /// 规则一眼可读:连接区看 IsConnected;每轴操作区 = 已连接 &amp;&amp; 已使能 &amp;&amp; 无报警;
    /// 急停永远可用(急停按钮被禁掉本身就是事故)。
    /// v1 的教训:按钮状态散落在各 handler 里各改各的 → btnMoveAbs1 禁了两次、btnMoveAbs2 忘了禁。
    /// </summary>
    private void RefreshUiState()
    {
        if (IsDisposed) return;

        var connected = _card.IsConnected;
        btnConnect.Enabled = !connected;
        btnDisconnect.Enabled = connected;
        txtIp.Enabled = !connected;
        // 连接指示灯:绿 = 已连接,灰 = 未连接。颜色只用来表达状态,装饰色一概不用
        lblConnectStatus.BackColor = connected ? Color.MediumSeaGreen : Color.DarkGray;

        for (var i = 0; i < 2; i++)
        {
            var operable = connected
                           && _card.IsAxisEnabled(i)
                           && string.IsNullOrEmpty(_card.GetAlarmMessage(i));
            _btnEnable[i].Enabled = connected;          // 使能/失能只要求"卡在线"
            _btnDisable[i].Enabled = connected;
            _btnJogForward[i].Enabled = operable;       // 运动类操作才要求"已使能 + 无报警"
            _btnJogBackward[i].Enabled = operable;
            _btnMoveAbs[i].Enabled = operable;
            _btnHome[i].Enabled = operable;
        }
    }

    // ———— 小工具 ————

    /// <summary>
    /// 解析速度输入:非法(空/非数字/越界)就回退默认 50 并把文本框纠正过来 —— 永远给调用方一个可用值。
    /// 上位机处理手工输入的黄金法则:防呆 + 自愈,而不是抛异常崩给用户看。
    /// </summary>
    private static double SpeedOf(TextBox box)
    {
        if (double.TryParse(box.Text.Trim(), out var v) && v > 0 && v <= 5000) return v;
        box.Text = "50";
        return 50;
    }

    /// <summary>返回码 → 人话。真卡 SDK 给你的只有 int,把它翻译成操作员能看懂的句子是上位机的本职。</summary>
    private void Fail(MotionResult r, string what)
    {
        var msg = r switch
        {
            MotionResult.NotConnected   => "卡未连接",
            MotionResult.AxisIndexError => "轴号越界",
            MotionResult.ParamError     => "参数不合法(速度/目标位置超软限位)",
            MotionResult.AxisDisabled   => "轴未使能",
            MotionResult.AlarmActive    => "轴有报警,请先清报警",
            _ => r.ToString(),
        };
        AppendLog($"✗ {what}失败:{msg}(错误码 {(int)r})", "WARN");
    }

    /// <summary>
    /// 界面日志:黑底等宽字,一行一条;同时经 LogHelper 落盘。
    /// 线程安全:后台事件线程也会调它,InvokeRequired 判断后 BeginInvoke 投递回 UI 线程。
    /// </summary>
    private void AppendLog(string message, string level = "INFO")
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => AppendLog(message, level)); return; }
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n");
        LogHelper.Log(message, level);
    }

    /// <summary>关窗前断开卡:取消所有后台运动任务,进程才能干净退出,不会留僵尸线程占用日志文件。</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _card.Disconnect();
        base.OnFormClosing(e);
    }
}
```

🗺️ **读码地图 —— MainForm 怎么走读**:
1. **构造函数只有四件事**:InitializeComponent(长相)→ 控件收数组 → 订阅事件(按钮的 Click/MouseDown + 卡的三个事件 + 定时器)→ RefreshUiState + 开场日志。别的都不该出现在构造里;
2. **`var axis = i;` 那一行是全文最便宜的保险**:不复制,所有 lambda 捕获的是同一个 i,循环结束后 = 2,两个轴的按钮全去动"轴 3"(不存在)→ 报轴号越错。这是 C# 闭包捕获的经典坑,v1 的"轴 2 按钮日志打轴 1"(坑⑨)本质就是同一类复制粘贴错位;
3. **三个事件处理器一个模式**:`if (InvokeRequired) { BeginInvoke(自己); return; }` —— 先问"我在 UI 线程吗",不在就把自己重新投递回 UI 线程再执行真正的逻辑(见 [📖 WinForms 跨线程](kp:winforms-invoke))。BeginInvoke 是"投递后立刻返回",不阻塞后台仿真线程;
4. **Timer1_Tick 的边沿检测**:`_wasMoving[i] && !moving` = 上一帧在动、这一帧停了 → 报"运动完成"。状态翻转只发生一次,日志就只打一次 —— 比"查询到静止就打日志"聪明在**记住了历史**;
5. **RefreshUiState 是按钮状态的唯一真源**:任何状态变化(连接/使能/报警/急停)最后都调它,规则集中在一处,改规则改一处。v1 的坑⑦(有的按钮禁两次、有的忘禁)在结构上不可能再发生;
6. **SpeedOf 的"防呆 + 自愈"**:非法输入不弹窗、不崩溃,回退默认值并**把文本框纠正过来** —— 操作员下次看到的就是合法值。工业软件对手工输入的态度:永远给调用方一个能用的值。

### 步骤 4:界面布局 MainForm.Designer.cs

**设计思路一句**:Designer 文件只描述"长相"(控件、位置、颜色),事件订阅一行不放(全在 MainForm.cs 构造里)—— 布局与行为彻底分家,抄布局不会抄错行为。

```csharp
// 📂 文件:src/MotionControl/UI/MainForm.Designer.cs
namespace MotionControlProject.UI;

/// <summary>
/// 控件布局(Designer 风格:只描述"长相",事件订阅全在 MainForm.cs 构造函数里)。
///
/// 布局思路(UI 整容篇):
/// - 顶栏 = 连接控制 + 急停(危险动作专属红色,全窗体唯一的彩色按钮);
/// - 主体三栏:轴1 | 轴2 | 报警/日志 —— 两轴 GroupBox 内部布局完全一致,对照着抄第二遍即可;
/// - 报警框淡黄底深红字(警示色),日志框黑底浅绿等宽字(终端风,一眼区分"信息"和"告警");
/// - 颜色只表达状态与危险等级,不做任何装饰 —— 工业界面的铁律。
/// </summary>
partial class MainForm
{
    /// <summary>必需的设计器变量。</summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>清理所有正在使用的资源。</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows 窗体设计器生成的代码

    /// <summary>设计器支持所需的方法 —— 不要修改。</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        panelTop = new Panel();
        gbConnect = new GroupBox();
        lblConnectStatus = new Panel();
        btnDisconnect = new Button();
        btnConnect = new Button();
        txtIp = new TextBox();
        lblIp = new Label();
        btnEstop = new Button();
        tableLayoutPanel1 = new TableLayoutPanel();
        gbAxis1 = new GroupBox();
        lblSoftLimit1 = new Label();
        btnHome1 = new Button();
        btnMoveAbs1 = new Button();
        txtAbs1 = new TextBox();
        lblTarget1 = new Label();
        txtSpeed1 = new TextBox();
        lblSpeed1 = new Label();
        txtPos1 = new TextBox();
        lblPos1 = new Label();
        btnJog1Backward = new Button();
        btnJog1Forward = new Button();
        lblJog1 = new Label();
        btnDisable1 = new Button();
        btnEnable1 = new Button();
        lblEnable1 = new Label();
        gbAxis2 = new GroupBox();
        lblSoftLimit2 = new Label();
        btnHome2 = new Button();
        btnMoveAbs2 = new Button();
        txtAbs2 = new TextBox();
        lblTarget2 = new Label();
        txtSpeed2 = new TextBox();
        lblSpeed2 = new Label();
        txtPos2 = new TextBox();
        lblPos2 = new Label();
        btnJog2Backward = new Button();
        btnJog2Forward = new Button();
        lblJog2 = new Label();
        btnDisable2 = new Button();
        btnEnable2 = new Button();
        lblEnable2 = new Label();
        tableLayoutPanel2 = new TableLayoutPanel();
        gbAlarm = new GroupBox();
        btnClearAlarm = new Button();
        txtAlarm = new RichTextBox();
        gbLog = new GroupBox();
        txtLog = new RichTextBox();
        timer1 = new System.Windows.Forms.Timer(components);
        panelTop.SuspendLayout();
        gbConnect.SuspendLayout();
        tableLayoutPanel1.SuspendLayout();
        gbAxis1.SuspendLayout();
        gbAxis2.SuspendLayout();
        tableLayoutPanel2.SuspendLayout();
        gbAlarm.SuspendLayout();
        gbLog.SuspendLayout();
        SuspendLayout();
        //
        // panelTop —— 顶栏:左边连接控制,右边急停
        //
        panelTop.Controls.Add(gbConnect);
        panelTop.Controls.Add(btnEstop);
        panelTop.Dock = DockStyle.Top;
        panelTop.Location = new Point(0, 0);
        panelTop.Name = "panelTop";
        panelTop.Size = new Size(1200, 80);
        panelTop.TabIndex = 0;
        //
        // gbConnect —— 连接控制分组
        //
        gbConnect.Controls.Add(lblIp);
        gbConnect.Controls.Add(txtIp);
        gbConnect.Controls.Add(btnConnect);
        gbConnect.Controls.Add(btnDisconnect);
        gbConnect.Controls.Add(lblConnectStatus);
        gbConnect.Location = new Point(12, 8);
        gbConnect.Name = "gbConnect";
        gbConnect.Size = new Size(500, 64);
        gbConnect.TabIndex = 0;
        gbConnect.TabStop = false;
        gbConnect.Text = "连接控制";
        //
        // lblIp
        //
        lblIp.AutoSize = true;
        lblIp.Location = new Point(16, 34);
        lblIp.Name = "lblIp";
        lblIp.Size = new Size(65, 17);
        lblIp.TabIndex = 0;
        lblIp.Text = "IP 地址:";
        //
        // txtIp —— 默认值不带空格(v1 里这里藏过一个前导空格)
        //
        txtIp.Font = new Font("Consolas", 10.5F);
        txtIp.Location = new Point(87, 30);
        txtIp.Name = "txtIp";
        txtIp.Size = new Size(140, 25);
        txtIp.TabIndex = 1;
        txtIp.Text = "127.0.0.1";
        //
        // btnConnect
        //
        btnConnect.Location = new Point(242, 28);
        btnConnect.Name = "btnConnect";
        btnConnect.Size = new Size(95, 30);
        btnConnect.TabIndex = 2;
        btnConnect.Text = "连接";
        btnConnect.UseVisualStyleBackColor = true;
        //
        // btnDisconnect
        //
        btnDisconnect.Location = new Point(352, 28);
        btnDisconnect.Name = "btnDisconnect";
        btnDisconnect.Size = new Size(95, 30);
        btnDisconnect.TabIndex = 3;
        btnDisconnect.Text = "断开";
        btnDisconnect.UseVisualStyleBackColor = true;
        //
        // lblConnectStatus —— 连接指示灯(小方片):绿=已连接,灰=未连接,代码里改颜色
        //
        lblConnectStatus.BackColor = Color.DarkGray;
        lblConnectStatus.BorderStyle = BorderStyle.FixedSingle;
        lblConnectStatus.Location = new Point(462, 33);
        lblConnectStatus.Name = "lblConnectStatus";
        lblConnectStatus.Size = new Size(18, 18);
        lblConnectStatus.TabIndex = 4;
        //
        // btnEstop —— 急停:全窗体唯一红色按钮。Anchor 右侧,窗口缩放也贴边
        //
        btnEstop.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnEstop.BackColor = Color.FromArgb(214, 64, 64);
        btnEstop.FlatAppearance.BorderSize = 0;
        btnEstop.FlatStyle = FlatStyle.Flat;
        btnEstop.Font = new Font("微软雅黑", 12F, FontStyle.Bold);
        btnEstop.ForeColor = Color.White;
        btnEstop.Location = new Point(1050, 14);
        btnEstop.Name = "btnEstop";
        btnEstop.Size = new Size(136, 50);
        btnEstop.TabIndex = 1;
        btnEstop.Text = "急停 STOP";
        btnEstop.UseVisualStyleBackColor = false;
        //
        // tableLayoutPanel1 —— 主体三栏:轴1 | 轴2 | 报警+日志
        //
        tableLayoutPanel1.ColumnCount = 3;
        tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
        tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
        tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
        tableLayoutPanel1.Controls.Add(gbAxis1, 0, 0);
        tableLayoutPanel1.Controls.Add(gbAxis2, 1, 0);
        tableLayoutPanel1.Controls.Add(tableLayoutPanel2, 2, 0);
        tableLayoutPanel1.Dock = DockStyle.Fill;
        tableLayoutPanel1.Location = new Point(0, 80);
        tableLayoutPanel1.Name = "tableLayoutPanel1";
        tableLayoutPanel1.Padding = new Padding(10, 8, 10, 10);
        tableLayoutPanel1.RowCount = 1;
        tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        tableLayoutPanel1.Size = new Size(1200, 700);
        tableLayoutPanel1.TabIndex = 1;
        //
        // gbAxis1 —— 轴 1 全部操作。内部纵排:使能 → 点动 → 位置 → 速度 → 目标 → 定位/回零
        //
        gbAxis1.Controls.Add(lblEnable1);
        gbAxis1.Controls.Add(btnEnable1);
        gbAxis1.Controls.Add(btnDisable1);
        gbAxis1.Controls.Add(lblJog1);
        gbAxis1.Controls.Add(btnJog1Forward);
        gbAxis1.Controls.Add(btnJog1Backward);
        gbAxis1.Controls.Add(lblPos1);
        gbAxis1.Controls.Add(txtPos1);
        gbAxis1.Controls.Add(lblSpeed1);
        gbAxis1.Controls.Add(txtSpeed1);
        gbAxis1.Controls.Add(lblTarget1);
        gbAxis1.Controls.Add(txtAbs1);
        gbAxis1.Controls.Add(btnMoveAbs1);
        gbAxis1.Controls.Add(btnHome1);
        gbAxis1.Controls.Add(lblSoftLimit1);
        gbAxis1.Dock = DockStyle.Fill;
        gbAxis1.Location = new Point(13, 11);
        gbAxis1.Name = "gbAxis1";
        gbAxis1.Size = new Size(399, 677);
        gbAxis1.TabIndex = 0;
        gbAxis1.TabStop = false;
        gbAxis1.Text = "轴 1(X 轴)";
        //
        // lblEnable1 —— 小节标题(加粗,视觉分区)
        //
        lblEnable1.AutoSize = true;
        lblEnable1.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
        lblEnable1.Location = new Point(20, 38);
        lblEnable1.Name = "lblEnable1";
        lblEnable1.Size = new Size(62, 17);
        lblEnable1.TabIndex = 0;
        lblEnable1.Text = "使能控制";
        //
        // btnEnable1
        //
        btnEnable1.Location = new Point(20, 66);
        btnEnable1.Name = "btnEnable1";
        btnEnable1.Size = new Size(110, 38);
        btnEnable1.TabIndex = 1;
        btnEnable1.Text = "使能";
        btnEnable1.UseVisualStyleBackColor = true;
        //
        // btnDisable1
        //
        btnDisable1.Location = new Point(145, 66);
        btnDisable1.Name = "btnDisable1";
        btnDisable1.Size = new Size(110, 38);
        btnDisable1.TabIndex = 2;
        btnDisable1.Text = "失能";
        btnDisable1.UseVisualStyleBackColor = true;
        //
        // lblJog1
        //
        lblJog1.AutoSize = true;
        lblJog1.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
        lblJog1.Location = new Point(20, 126);
        lblJog1.Name = "lblJog1";
        lblJog1.Size = new Size(120, 17);
        lblJog1.TabIndex = 3;
        lblJog1.Text = "点动(按住不放)";
        //
        // btnJog1Forward —— 点动不设彩色:危险等级低于急停,红色只留给急停
        //
        btnJog1Forward.Location = new Point(20, 154);
        btnJog1Forward.Name = "btnJog1Forward";
        btnJog1Forward.Size = new Size(170, 56);
        btnJog1Forward.TabIndex = 4;
        btnJog1Forward.Text = "▲ 正转";
        btnJog1Forward.UseVisualStyleBackColor = true;
        //
        // btnJog1Backward
        //
        btnJog1Backward.Location = new Point(205, 154);
        btnJog1Backward.Name = "btnJog1Backward";
        btnJog1Backward.Size = new Size(170, 56);
        btnJog1Backward.TabIndex = 5;
        btnJog1Backward.Text = "▼ 反转";
        btnJog1Backward.UseVisualStyleBackColor = true;
        //
        // lblPos1
        //
        lblPos1.AutoSize = true;
        lblPos1.Location = new Point(20, 232);
        lblPos1.Name = "lblPos1";
        lblPos1.Size = new Size(111, 17);
        lblPos1.TabIndex = 6;
        lblPos1.Text = "当前位置 (mm)";
        //
        // txtPos1 —— 只显示不输入:ReadOnly + 等宽字体,数字跳动不抖版式
        //
        txtPos1.BackColor = Color.White;
        txtPos1.Font = new Font("Consolas", 14.25F);
        txtPos1.Location = new Point(20, 256);
        txtPos1.Name = "txtPos1";
        txtPos1.ReadOnly = true;
        txtPos1.Size = new Size(190, 30);
        txtPos1.TabIndex = 7;
        txtPos1.Text = "0.000";
        txtPos1.TextAlign = HorizontalAlignment.Center;
        //
        // lblSpeed1
        //
        lblSpeed1.AutoSize = true;
        lblSpeed1.Location = new Point(20, 304);
        lblSpeed1.Name = "lblSpeed1";
        lblSpeed1.Size = new Size(103, 17);
        lblSpeed1.TabIndex = 8;
        lblSpeed1.Text = "速度 (mm/s)";
        //
        // txtSpeed1 —— v1 的速度写死 50;v2 可输入,非法输入由 SpeedOf() 兜底回 50
        //
        txtSpeed1.Font = new Font("Consolas", 12F);
        txtSpeed1.Location = new Point(20, 328);
        txtSpeed1.Name = "txtSpeed1";
        txtSpeed1.Size = new Size(120, 29);
        txtSpeed1.TabIndex = 9;
        txtSpeed1.Text = "50";
        //
        // lblTarget1
        //
        lblTarget1.AutoSize = true;
        lblTarget1.Location = new Point(20, 376);
        lblTarget1.Name = "lblTarget1";
        lblTarget1.Size = new Size(127, 17);
        lblTarget1.TabIndex = 10;
        lblTarget1.Text = "目标位置 (mm)";
        //
        // txtAbs1
        //
        txtAbs1.Font = new Font("Consolas", 12F);
        txtAbs1.Location = new Point(20, 400);
        txtAbs1.Name = "txtAbs1";
        txtAbs1.Size = new Size(120, 29);
        txtAbs1.TabIndex = 11;
        txtAbs1.Text = "100";
        //
        // btnMoveAbs1
        //
        btnMoveAbs1.Location = new Point(20, 450);
        btnMoveAbs1.Name = "btnMoveAbs1";
        btnMoveAbs1.Size = new Size(170, 48);
        btnMoveAbs1.TabIndex = 12;
        btnMoveAbs1.Text = "绝对定位";
        btnMoveAbs1.UseVisualStyleBackColor = true;
        //
        // btnHome1
        //
        btnHome1.Location = new Point(205, 450);
        btnHome1.Name = "btnHome1";
        btnHome1.Size = new Size(170, 48);
        btnHome1.TabIndex = 13;
        btnHome1.Text = "回零 ⌂";
        btnHome1.UseVisualStyleBackColor = true;
        //
        // lblSoftLimit1 —— 灰字提示:把"隐藏规则"写在界面上,操作员不用翻文档
        //
        lblSoftLimit1.AutoSize = true;
        lblSoftLimit1.ForeColor = Color.Gray;
        lblSoftLimit1.Location = new Point(20, 516);
        lblSoftLimit1.Name = "lblSoftLimit1";
        lblSoftLimit1.Size = new Size(311, 17);
        lblSoftLimit1.TabIndex = 14;
        lblSoftLimit1.Text = "软限位 ±1000 mm · 流程:连接 → 使能 → 运动";
        //
        // gbAxis2 —— 与 gbAxis1 布局完全一致,仅控件名后缀不同
        //
        gbAxis2.Controls.Add(lblEnable2);
        gbAxis2.Controls.Add(btnEnable2);
        gbAxis2.Controls.Add(btnDisable2);
        gbAxis2.Controls.Add(lblJog2);
        gbAxis2.Controls.Add(btnJog2Forward);
        gbAxis2.Controls.Add(btnJog2Backward);
        gbAxis2.Controls.Add(lblPos2);
        gbAxis2.Controls.Add(txtPos2);
        gbAxis2.Controls.Add(lblSpeed2);
        gbAxis2.Controls.Add(txtSpeed2);
        gbAxis2.Controls.Add(lblTarget2);
        gbAxis2.Controls.Add(txtAbs2);
        gbAxis2.Controls.Add(btnMoveAbs2);
        gbAxis2.Controls.Add(btnHome2);
        gbAxis2.Controls.Add(lblSoftLimit2);
        gbAxis2.Dock = DockStyle.Fill;
        gbAxis2.Location = new Point(418, 11);
        gbAxis2.Name = "gbAxis2";
        gbAxis2.Size = new Size(399, 677);
        gbAxis2.TabIndex = 1;
        gbAxis2.TabStop = false;
        gbAxis2.Text = "轴 2(Y 轴)";
        //
        // lblEnable2
        //
        lblEnable2.AutoSize = true;
        lblEnable2.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
        lblEnable2.Location = new Point(20, 38);
        lblEnable2.Name = "lblEnable2";
        lblEnable2.Size = new Size(62, 17);
        lblEnable2.TabIndex = 0;
        lblEnable2.Text = "使能控制";
        //
        // btnEnable2
        //
        btnEnable2.Location = new Point(20, 66);
        btnEnable2.Name = "btnEnable2";
        btnEnable2.Size = new Size(110, 38);
        btnEnable2.TabIndex = 1;
        btnEnable2.Text = "使能";
        btnEnable2.UseVisualStyleBackColor = true;
        //
        // btnDisable2
        //
        btnDisable2.Location = new Point(145, 66);
        btnDisable2.Name = "btnDisable2";
        btnDisable2.Size = new Size(110, 38);
        btnDisable2.TabIndex = 2;
        btnDisable2.Text = "失能";
        btnDisable2.UseVisualStyleBackColor = true;
        //
        // lblJog2
        //
        lblJog2.AutoSize = true;
        lblJog2.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
        lblJog2.Location = new Point(20, 126);
        lblJog2.Name = "lblJog2";
        lblJog2.Size = new Size(120, 17);
        lblJog2.TabIndex = 3;
        lblJog2.Text = "点动(按住不放)";
        //
        // btnJog2Forward
        //
        btnJog2Forward.Location = new Point(20, 154);
        btnJog2Forward.Name = "btnJog2Forward";
        btnJog2Forward.Size = new Size(170, 56);
        btnJog2Forward.TabIndex = 4;
        btnJog2Forward.Text = "▲ 正转";
        btnJog2Forward.UseVisualStyleBackColor = true;
        //
        // btnJog2Backward
        //
        btnJog2Backward.Location = new Point(205, 154);
        btnJog2Backward.Name = "btnJog2Backward";
        btnJog2Backward.Size = new Size(170, 56);
        btnJog2Backward.TabIndex = 5;
        btnJog2Backward.Text = "▼ 反转";
        btnJog2Backward.UseVisualStyleBackColor = true;
        //
        // lblPos2
        //
        lblPos2.AutoSize = true;
        lblPos2.Location = new Point(20, 232);
        lblPos2.Name = "lblPos2";
        lblPos2.Size = new Size(111, 17);
        lblPos2.TabIndex = 6;
        lblPos2.Text = "当前位置 (mm)";
        //
        // txtPos2
        //
        txtPos2.BackColor = Color.White;
        txtPos2.Font = new Font("Consolas", 14.25F);
        txtPos2.Location = new Point(20, 256);
        txtPos2.Name = "txtPos2";
        txtPos2.ReadOnly = true;
        txtPos2.Size = new Size(190, 30);
        txtPos2.TabIndex = 7;
        txtPos2.Text = "0.000";
        txtPos2.TextAlign = HorizontalAlignment.Center;
        //
        // lblSpeed2
        //
        lblSpeed2.AutoSize = true;
        lblSpeed2.Location = new Point(20, 304);
        lblSpeed2.Name = "lblSpeed2";
        lblSpeed2.Size = new Size(103, 17);
        lblSpeed2.TabIndex = 8;
        lblSpeed2.Text = "速度 (mm/s)";
        //
        // txtSpeed2
        //
        txtSpeed2.Font = new Font("Consolas", 12F);
        txtSpeed2.Location = new Point(20, 328);
        txtSpeed2.Name = "txtSpeed2";
        txtSpeed2.Size = new Size(120, 29);
        txtSpeed2.TabIndex = 9;
        txtSpeed2.Text = "50";
        //
        // lblTarget2
        //
        lblTarget2.AutoSize = true;
        lblTarget2.Location = new Point(20, 376);
        lblTarget2.Name = "lblTarget2";
        lblTarget2.Size = new Size(127, 17);
        lblTarget2.TabIndex = 10;
        lblTarget2.Text = "目标位置 (mm)";
        //
        // txtAbs2
        //
        txtAbs2.Font = new Font("Consolas", 12F);
        txtAbs2.Location = new Point(20, 400);
        txtAbs2.Name = "txtAbs2";
        txtAbs2.Size = new Size(120, 29);
        txtAbs2.TabIndex = 11;
        txtAbs2.Text = "100";
        //
        // btnMoveAbs2
        //
        btnMoveAbs2.Location = new Point(20, 450);
        btnMoveAbs2.Name = "btnMoveAbs2";
        btnMoveAbs2.Size = new Size(170, 48);
        btnMoveAbs2.TabIndex = 12;
        btnMoveAbs2.Text = "绝对定位";
        btnMoveAbs2.UseVisualStyleBackColor = true;
        //
        // btnHome2
        //
        btnHome2.Location = new Point(205, 450);
        btnHome2.Name = "btnHome2";
        btnHome2.Size = new Size(170, 48);
        btnHome2.TabIndex = 13;
        btnHome2.Text = "回零 ⌂";
        btnHome2.UseVisualStyleBackColor = true;
        //
        // lblSoftLimit2
        //
        lblSoftLimit2.AutoSize = true;
        lblSoftLimit2.ForeColor = Color.Gray;
        lblSoftLimit2.Location = new Point(20, 516);
        lblSoftLimit2.Name = "lblSoftLimit2";
        lblSoftLimit2.Size = new Size(311, 17);
        lblSoftLimit2.TabIndex = 14;
        lblSoftLimit2.Text = "软限位 ±1000 mm · 流程:连接 → 使能 → 运动";
        //
        // tableLayoutPanel2 —— 第三栏上下切:报警 52% / 日志 48%
        //
        tableLayoutPanel2.ColumnCount = 1;
        tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tableLayoutPanel2.Controls.Add(gbAlarm, 0, 0);
        tableLayoutPanel2.Controls.Add(gbLog, 0, 1);
        tableLayoutPanel2.Dock = DockStyle.Fill;
        tableLayoutPanel2.Location = new Point(823, 11);
        tableLayoutPanel2.Name = "tableLayoutPanel2";
        tableLayoutPanel2.RowCount = 2;
        tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 52F));
        tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 48F));
        tableLayoutPanel2.Size = new Size(374, 677);
        tableLayoutPanel2.TabIndex = 2;
        //
        // gbAlarm
        //
        gbAlarm.Controls.Add(txtAlarm);
        gbAlarm.Controls.Add(btnClearAlarm);
        gbAlarm.Dock = DockStyle.Fill;
        gbAlarm.Location = new Point(3, 3);
        gbAlarm.Name = "gbAlarm";
        gbAlarm.Size = new Size(368, 346);
        gbAlarm.TabIndex = 0;
        gbAlarm.TabStop = false;
        gbAlarm.Text = "报警信息";
        //
        // txtAlarm —— 淡黄底 + 深红字:一眼锁定告警(与日志的黑绿风彻底区分)
        //
        txtAlarm.BackColor = SystemColors.Info;
        txtAlarm.DetectUrls = false;
        txtAlarm.Font = new Font("Consolas", 9.75F);
        txtAlarm.ForeColor = Color.Firebrick;
        txtAlarm.Location = new Point(16, 40);
        txtAlarm.Name = "txtAlarm";
        txtAlarm.ReadOnly = true;
        txtAlarm.Size = new Size(336, 250);
        txtAlarm.TabIndex = 0;
        txtAlarm.Text = "";
        //
        // btnClearAlarm —— v1 的清报警按钮没绑事件,是摆设;v2 真正工作
        //
        btnClearAlarm.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnClearAlarm.Location = new Point(16, 296);
        btnClearAlarm.Name = "btnClearAlarm";
        btnClearAlarm.Size = new Size(180, 38);
        btnClearAlarm.TabIndex = 1;
        btnClearAlarm.Text = "清除全部报警";
        btnClearAlarm.UseVisualStyleBackColor = true;
        //
        // gbLog
        //
        gbLog.Controls.Add(txtLog);
        gbLog.Dock = DockStyle.Fill;
        gbLog.Location = new Point(3, 355);
        gbLog.Name = "gbLog";
        gbLog.Size = new Size(368, 319);
        gbLog.TabIndex = 1;
        gbLog.TabStop = false;
        gbLog.Text = "运行日志";
        //
        // txtLog —— 黑底浅绿等宽字,终端风;所有动作留痕,同时落盘 logs\
        //
        txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        txtLog.BackColor = Color.Black;
        txtLog.DetectUrls = false;
        txtLog.Font = new Font("Consolas", 9.75F);
        txtLog.ForeColor = Color.LightGreen;
        txtLog.Location = new Point(16, 40);
        txtLog.Name = "txtLog";
        txtLog.ReadOnly = true;
        txtLog.ScrollBars = RichTextBoxScrollBars.Vertical;
        txtLog.Size = new Size(336, 263);
        txtLog.TabIndex = 0;
        txtLog.Text = "";
        //
        // timer1 —— 100ms 界面轮询:按钮状态刷新 + 运动完成边沿检测(Interval 在 MainForm.cs 里设)
        //
        timer1.Tick += Timer1_Tick;
        //
        // MainForm
        //
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1200, 780);
        Controls.Add(tableLayoutPanel1);
        Controls.Add(panelTop);
        Font = new Font("微软雅黑", 9.75F);
        MinimumSize = new Size(1216, 819);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "运动控制平台 · 模拟版 v2(接口化 + 多轴并发 + 软限位 + 急停 + 回零)";
        panelTop.ResumeLayout(false);
        gbConnect.ResumeLayout(false);
        gbConnect.PerformLayout();
        tableLayoutPanel1.ResumeLayout(false);
        tableLayoutPanel1.PerformLayout();
        gbAxis1.ResumeLayout(false);
        gbAxis1.PerformLayout();
        gbAxis2.ResumeLayout(false);
        gbAxis2.PerformLayout();
        tableLayoutPanel2.ResumeLayout(false);
        tableLayoutPanel2.PerformLayout();
        gbAlarm.ResumeLayout(false);
        gbLog.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion

    private Panel panelTop;
    private GroupBox gbConnect;
    private Label lblIp;
    private TextBox txtIp;
    private Button btnConnect;
    private Button btnDisconnect;
    private Panel lblConnectStatus;
    private Button btnEstop;
    private TableLayoutPanel tableLayoutPanel1;
    private GroupBox gbAxis1;
    private Label lblEnable1;
    private Button btnEnable1;
    private Button btnDisable1;
    private Label lblJog1;
    private Button btnJog1Forward;
    private Button btnJog1Backward;
    private Label lblPos1;
    private TextBox txtPos1;
    private Label lblSpeed1;
    private TextBox txtSpeed1;
    private Label lblTarget1;
    private TextBox txtAbs1;
    private Button btnMoveAbs1;
    private Button btnHome1;
    private Label lblSoftLimit1;
    private GroupBox gbAxis2;
    private Label lblEnable2;
    private Button btnEnable2;
    private Button btnDisable2;
    private Label lblJog2;
    private Button btnJog2Forward;
    private Button btnJog2Backward;
    private Label lblPos2;
    private TextBox txtPos2;
    private Label lblSpeed2;
    private TextBox txtSpeed2;
    private Label lblTarget2;
    private TextBox txtAbs2;
    private Button btnMoveAbs2;
    private Button btnHome2;
    private Label lblSoftLimit2;
    private TableLayoutPanel tableLayoutPanel2;
    private GroupBox gbAlarm;
    private RichTextBox txtAlarm;
    private Button btnClearAlarm;
    private GroupBox gbLog;
    private RichTextBox txtLog;
    private System.Windows.Forms.Timer timer1;
}
```

💡 **布局文件里的五个门道**(抄之前先看懂,抄起来才不慌):

- **Dock 与 Anchor 分工**:`Dock = Top/Fill` 负责"占满某条边或剩余空间"(顶栏、三栏布局);`Anchor = Right` 负责"钉住某条边"(急停贴右上、txtLog 四边锚定随窗口拉伸)。一句口诀:**结构用 Dock,贴边用 Anchor**;
- **TableLayoutPanel 按百分比切格**:三栏 34/34/32,窗口拉大每一栏按比例长 —— 数字坐标只管 GroupBox **内部**的小控件,大结构全部交给百分比,这是"窗口怎么拉都不乱"的关键;
- **等宽字体只给数字和日志**:Consolas 的 0 和 O、1 和 l 一眼可分,位置读数跳动时列宽不抖 —— 工业界面凡是数字都用等宽字;
- **颜色语义铁律**:红色只给急停(全窗体唯一彩色按钮);报警框淡黄底深红字;日志黑底浅绿。**颜色只表达状态和危险等级,永远不做装饰** —— 车间里谁有空欣赏你的渐变;
- **两轴 GroupBox 内部布局完全一致**:坐标一模一样、只差控件名后缀 —— 对照轴 1 抄轴 2,抄完把 1 全换成 2。这种"复制级一致"不是偷懒,是把"加第三轴 = 再抄一遍改后缀"变成机械操作(配合 MainForm 里的控件数组,真正加轴只改两处)。

---

## ✅ 验证(沙盒实测输出 + 手动验收)

```bash
dotnet build
```
```
已成功生成。
    0 个警告
    0 个错误
```
```bash
dotnet test
```
```
已通过! - 失败:     0，通过:    12，已跳过:     0，总计:    12，持续时间: 4 s - MotionControl.Tests.dll (net8.0)
```

### 手动验收清单(F8 跑起来,逐条过)

| # | 操作 | 预期 |
|---|---|---|
| 1 | 启动程序 | 日志区"系统就绪…请先【连接】再【使能】";连接可点、断开灰、两轴按钮全灰 |
| 2 | 点【连接】 | 指示灯变绿;IP 框锁定;两轴"使能/失能"变为可点 |
| 3 | 两轴分别【使能】 | 点动/定位/回零变为可点 |
| 4 | 按住轴 1【正转】 | 位置框数字连续跳动;松开立即停 |
| 5 | **同时按住轴 1 正转 + 轴 2 反转** | **两轴同时在动,互不打断**(v1 头号 bug 的肉眼验证) |
| 6 | 轴 1 目标 100,点【绝对定位】 | 到位后日志出现一次"运动完成,停在 100.000 mm" |
| 7 | 点【回零 ⌂】 | 回到 0.000,同样只报一次完成 |
| 8 | 速度框输 `abc` 再点定位 | 文本框自动变回 50,程序照常运行(自愈,不弹窗不崩) |
| 9 | 运动中点【急停 STOP】 | 位置立刻冻结;日志红字"急停触发";急停按钮任何时候都可点 |
| 10 | 速度改 3000,按住轴 1【正转】不放 | 约 3 秒后位置顶在 1000.000;报警框出现"触发正软限位";轴 1 运动按钮全灰 |
| 11 | 点【清除全部报警】 | 报警框记"报警已清除";轴 1 按钮恢复 |
| 12 | 打开 `bin\Debug\net8.0-windows\logs\` | 有 `motion_今天日期.txt`,内容与界面日志一致 |
| 13 | 点【断开】再关窗 | 指示灯变灰;进程干净退出(任务管理器里没有残留 MotionControl) |

---

## ✅ 验收清单(对着需求单逐条勾)

- [ ] FR-U01 三区布局完成,急停是唯一红色按钮
- [ ] FR-U02 控件数组 + 一段循环订阅(记住 `var axis = i;` 那行注释为什么存在)
- [ ] FR-U03 RefreshUiState 集中计算,手动验收 1/2/3/10 的按钮状态全对
- [ ] FR-U04 三个事件处理器都是"InvokeRequired → BeginInvoke → 真逻辑"一个模式
- [ ] FR-U05 速度自愈(验收 8)与目标位置防呆(日志 WARN,不崩)
- [ ] FR-U06 报警/日志视觉区分 + 落盘(验收 12)
- [ ] FR-U07 运动完成只报一次(验收 6/7)
- [ ] FR-U08 关窗断开,进程无残留(验收 13)
- [ ] 手动验收清单 13 条全过

---

## 🎤 面试怎么讲这一篇

> **一句话开场**:"界面层我做了一次彻底的结构化:两轴控件收数组循环订阅、按钮状态集中一处计算、卡事件统一跨线程投递 —— 换真卡或加轴,界面改动是常数级的。"

**追问弹药库**:
- **"多轴控件怎么避免复制粘贴?"** —— 控件数组 + 下标循环,一段代码管所有轴;顺手能讲闭包捕获坑:`for` 里的 `i` 是共享变量,lambda 里必须用 `var axis = i;` 拷贝;
- **"按钮状态怎么管理?"** —— 集中在 RefreshUiState 单一真源:连接看 IsConnected、运动类看"连接 + 使能 + 无报警"、急停永远可用。v1 的教训是散落在各 handler 里改,出现"禁两次/漏禁"的 bug;
- **"跨线程访问控件为什么会炸?怎么解决?"** —— WinForms 控件不是线程安全的,靠"创建控件的线程 + 消息泵"串行访问;非 UI 线程直接碰就抛 InvalidOperationException。解法:InvokeRequired 判断 + BeginInvoke 把调用投递回 UI 线程的消息队列(前端类比:只有主线程能碰 DOM,Worker 的结果要 postMessage 回来);
- **"运动完成怎么检测?"** —— 100ms 定时器查 IsMoving + 边沿检测(上一帧动、这一帧停 = 刚完成)。真卡 SDK 常常只有状态位没有完成事件,这招到处能用;
- **"操作员输错怎么办?"** —— 防呆 + 自愈:非法速度回退默认值并纠正文本框;目标非法就地日志。工业软件的哲学是把人当"最不可靠的输入源"来设计。

下一篇:[MC4 · UI 冒烟验收 —— 让测试替你把界面全流程跑一遍](项目实践_MotionControl_MC4_UI冒烟验收.md)
