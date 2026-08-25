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

        // 两轴插补演示按钮(驱动两根轴,不属于任何单轴数组,单独订阅)
        btnLinear.Click += (s, e) => MoveLinearDemo();

        // 轨迹图(MC6):清空轨迹只清画面,不动轴
        btnClearTrail.Click += (s, e) => { trajPanel.ClearTrail(); AppendLog("轨迹已清空"); };

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

    /// <summary>
    /// 两轴直线插补演示:X→200、Y→120 同起同停、等比推进。
    /// 按下后盯着两个位置框看 —— 任意时刻 X:Y 恒等于 200:120(≈5:3),这就是"插补走直线"的直观含义。
    /// 速度取轴 1 的速度框,语义 = 走得最远的轴(这里是 X)的速度。
    /// </summary>
    private void MoveLinearDemo()
    {
        var speed = SpeedOf(_txtSpeed[0]);
        var r = _card.MoveLinear(new[] { 0, 1 }, new[] { 200.0, 120.0 }, speed);
        if (r != MotionResult.Ok) { Fail(r, "两轴插补"); return; }
        AppendLog($"两轴插补 → X 200 · Y 120 @ {speed:F0} mm/s(同起同停,等比推进)");
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

        // 轨迹采样:每帧把两轴位置一起记一个 (X, Y) 点。
        // 为什么在定时器里采、而不是订阅 PositionChanged 事件?事件是"每根轴各自来报",
        // X 到达时 Y 可能还停在上个节拍 → 轨迹会画出锯齿;定时器同一时刻取两轴,坐标才是
        // 一致的快照 —— 采集项目里"按周期采样"对付"事件乱序"是同一个思路。
        trajPanel.Sample(_card.GetAxisPosition(0), _card.GetAxisPosition(1));

        RefreshUiState();
    }

    // ———— 界面状态集中刷新 ————

    /// <summary>
    /// 所有按钮的 Enabled 状态只在这里计算。
    /// 规则一眼可读:连接区看 IsConnected;每轴操作区 = 已连接 && 已使能 && 无报警;
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

        // 插补按钮要求两轴同时可用 —— 插补是"绑腿跑",任何一轴不满足,整条指令都会被卡拒绝
        btnLinear.Enabled = connected
                            && _card.IsAxisEnabled(0) && _card.IsAxisEnabled(1)
                            && string.IsNullOrEmpty(_card.GetAlarmMessage(0))
                            && string.IsNullOrEmpty(_card.GetAlarmMessage(1));
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
