// 📂 文件:src/MotionControl/UI/MainForm.Designer.cs
namespace MotionControlProject.UI;

/// <summary>
/// 控件布局(Designer 风格:只描述"长相",事件订阅全在 MainForm.cs 构造函数里)。
///
/// 布局思路(UI 整容篇):
/// - 顶栏 = 连接控制 + 急停(危险动作专属红色,全窗体唯一的彩色按钮);
/// - 主体四栏:轴1 | 轴2 | 轨迹图 | 报警/日志 —— 两轴 GroupBox 内部布局完全一致,对照着抄第二遍即可;
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
        btnLinear = new Button();
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
        gbTraj = new GroupBox();
        trajPanel = new TrajectoryPanel();
        btnClearTrail = new Button();
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
        gbTraj.SuspendLayout();
        SuspendLayout();
        //
        // panelTop —— 顶栏:左边连接控制,右边急停
        //
        panelTop.Controls.Add(gbConnect);
        panelTop.Controls.Add(btnEstop);
        panelTop.Dock = DockStyle.Top;
        panelTop.Location = new Point(0, 0);
        panelTop.Name = "panelTop";
        panelTop.Size = new Size(1520, 80);
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
        btnEstop.Location = new Point(1370, 14);
        btnEstop.Name = "btnEstop";
        btnEstop.Size = new Size(136, 50);
        btnEstop.TabIndex = 1;
        btnEstop.Text = "急停 STOP";
        btnEstop.UseVisualStyleBackColor = false;
        //
        // tableLayoutPanel1 —— 主体四栏:轴1 | 轴2 | 轨迹图 | 报警+日志
        //
        tableLayoutPanel1.ColumnCount = 4;
        tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26F));
        tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26F));
        tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));
        tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));
        tableLayoutPanel1.Controls.Add(gbAxis1, 0, 0);
        tableLayoutPanel1.Controls.Add(gbAxis2, 1, 0);
        tableLayoutPanel1.Controls.Add(gbTraj, 2, 0);
        tableLayoutPanel1.Controls.Add(tableLayoutPanel2, 3, 0);
        tableLayoutPanel1.Dock = DockStyle.Fill;
        tableLayoutPanel1.Location = new Point(0, 80);
        tableLayoutPanel1.Name = "tableLayoutPanel1";
        tableLayoutPanel1.Padding = new Padding(10, 8, 10, 10);
        tableLayoutPanel1.RowCount = 1;
        tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        tableLayoutPanel1.Size = new Size(1520, 700);
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
        gbAxis1.Controls.Add(btnLinear);
        gbAxis1.Dock = DockStyle.Fill;
        gbAxis1.Location = new Point(13, 11);
        gbAxis1.Name = "gbAxis1";
        gbAxis1.Size = new Size(386, 677);
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
        // btnLinear —— 两轴直线插补演示:X/Y 同起同停、等比推进(放在轴 1 框里,但驱动两根轴)
        //
        btnLinear.Location = new Point(20, 548);
        btnLinear.Name = "btnLinear";
        btnLinear.Size = new Size(355, 44);
        btnLinear.TabIndex = 15;
        btnLinear.Text = "⇗ 两轴插补演示 → X 200 · Y 120";
        btnLinear.UseVisualStyleBackColor = true;
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
        gbAxis2.Location = new Point(405, 11);
        gbAxis2.Name = "gbAxis2";
        gbAxis2.Size = new Size(386, 677);
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
        // gbTraj —— X-Y 轨迹图(MC6):把两轴位置画成平面运动轨迹
        //
        gbTraj.Controls.Add(btnClearTrail);
        gbTraj.Controls.Add(trajPanel);
        gbTraj.Dock = DockStyle.Fill;
        gbTraj.Location = new Point(797, 11);
        gbTraj.Name = "gbTraj";
        gbTraj.Size = new Size(352, 677);
        gbTraj.TabIndex = 2;
        gbTraj.TabStop = false;
        gbTraj.Text = "轨迹图 · X-Y(轴1 = X,轴2 = Y)";
        //
        // trajPanel —— 自定义控件(见 TrajectoryPanel.cs):坐标轴 + 网格 + 软限位边框 + 轨迹线 + 当前点
        //
        trajPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        trajPanel.Location = new Point(16, 40);
        trajPanel.Name = "trajPanel";
        trajPanel.Size = new Size(320, 560);
        trajPanel.TabIndex = 0;
        //
        // btnClearTrail —— 清空轨迹(只清画面,不动轴)
        //
        btnClearTrail.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnClearTrail.Location = new Point(16, 608);
        btnClearTrail.Name = "btnClearTrail";
        btnClearTrail.Size = new Size(180, 38);
        btnClearTrail.TabIndex = 1;
        btnClearTrail.Text = "清空轨迹";
        btnClearTrail.UseVisualStyleBackColor = true;
        //
        // tableLayoutPanel2 —— 第四栏上下切:报警 52% / 日志 48%
        //
        tableLayoutPanel2.ColumnCount = 1;
        tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tableLayoutPanel2.Controls.Add(gbAlarm, 0, 0);
        tableLayoutPanel2.Controls.Add(gbLog, 0, 1);
        tableLayoutPanel2.Dock = DockStyle.Fill;
        tableLayoutPanel2.Location = new Point(1155, 11);
        tableLayoutPanel2.Name = "tableLayoutPanel2";
        tableLayoutPanel2.RowCount = 2;
        tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 52F));
        tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 48F));
        tableLayoutPanel2.Size = new Size(352, 677);
        tableLayoutPanel2.TabIndex = 3;
        //
        // gbAlarm
        //
        gbAlarm.Controls.Add(txtAlarm);
        gbAlarm.Controls.Add(btnClearAlarm);
        gbAlarm.Dock = DockStyle.Fill;
        gbAlarm.Location = new Point(3, 3);
        gbAlarm.Name = "gbAlarm";
        gbAlarm.Size = new Size(346, 346);
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
        txtAlarm.Size = new Size(314, 250);
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
        gbLog.Size = new Size(346, 319);
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
        txtLog.Size = new Size(314, 263);
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
        ClientSize = new Size(1520, 780);
        Controls.Add(tableLayoutPanel1);
        Controls.Add(panelTop);
        Font = new Font("微软雅黑", 9.75F);
        MinimumSize = new Size(1536, 819);
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
        gbTraj.ResumeLayout(false);
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
    private GroupBox gbTraj;
    private TrajectoryPanel trajPanel;
    private Button btnClearTrail;
    private TableLayoutPanel tableLayoutPanel2;
    private GroupBox gbAlarm;
    private RichTextBox txtAlarm;
    private Button btnClearAlarm;
    private GroupBox gbLog;
    private RichTextBox txtLog;
    private System.Windows.Forms.Timer timer1;
    private Button btnLinear;
}
