# MC6 · 轨迹可视化(可选加餐:X-Y 轨迹图,亲眼看见运动)

> **系列导航**:[MC1 骨架与模拟卡](项目实践_MotionControl_MC1_工程骨架与模拟卡.md) → [MC2 卡的行为测试](项目实践_MotionControl_MC2_卡的行为测试.md) → [MC3 WinForms 主界面](项目实践_MotionControl_MC3_WinForms主界面.md) → [MC4 UI 冒烟验收](项目实践_MotionControl_MC4_UI冒烟验收.md) → [MC5 两轴直线插补(可选)](项目实践_MotionControl_MC5_两轴直线插补.md) → **MC6 轨迹可视化(可选)**
> **定位**:数字位置框读得出**数**,看不出**形**。本篇写一个自定义绘图控件 TrajectoryPanel:轴 1 = X、轴 2 = Y,把两轴位置画成平面上的运动轨迹 —— 坐标轴、网格、软限位边框一应俱全,轨迹一条绿线、当前位置一个红点。点动画出走过的路、插补画出**笔直的斜线**(MC5 的数学亲眼可见)、急停红点原地冻住。顺路学会 WinForms 自绘控件的三条铁律:数据绘制分离、双缓冲、坐标映射。
> **前置**:MC5(插补按钮可用,14/14 全绿)。
> **预计开发时长**:跟敲 0.5 天。**先只看「📋 需求单」自己想,卡住再看「🛠️ 参考实现」。**

---

## 🎯 本篇交付物

1. 自定义控件 `TrajectoryPanel.cs`(约 130 行):坐标轴 + 250mm 网格 + 软限位边框 + 轨迹折线 + 当前点;
2. 界面从三栏变四栏:轴1 | 轴2 | **轨迹图** | 报警/日志,外加【清空轨迹】按钮;
3. `dotnet build` 0 错 0 警,14/14 测试照旧全绿(轨迹只读位置,不改任何行为);
4. 视觉验收:插补画出直线、急停红点冻结、回零画出归零线。

---

## 📋 需求单(视觉工程师视角 —— 先自己想怎么做)

| 编号 | 需求 | 验收标准 |
|---|---|---|
| FR-V01 | 坐标系:X/Y 十字轴过原点,原点标注 (0,0) | 屏幕上方 = 机械 Y 正方向(X 不翻、Y 要翻) |
| FR-V02 | 网格 + 软限位边框 | 每 250mm 一条浅灰线;±1000 边框银灰色,一眼看出行程边界 |
| FR-V03 | 轨迹折线 + 当前点 | 绿线画走过的路,末端红点 = 当前位置;静止时不追加重复点 |
| FR-V04 | mm→像素**等比例** | 比例取面板短边算,1mm 在 X/Y 方向等长,圆画出来不会变椭圆 |
| FR-V05 | 双缓冲防闪烁 | 快速点动时轨迹刷新不闪眼 |
| FR-V06 | 两轴位置**同一时刻一起采样** | 采样放定时器(100ms),不放逐轴事件 —— 坐标才是同一时刻的快照 |
| FR-V07 | 轨迹点数有上限 | 到 4000 点丢最老的(滚动),无限点动也不吃内存 |
| FR-V08 | 【清空轨迹】按钮 | 只清画面,不动轴、不断运动 |

**先自己想**:
① WinForms 控件上"画画"写在哪?写在按钮事件里行不行?(提示:窗口被别的窗口挡一下再露出,画的东西谁负责恢复?)
② 面板宽 330、高 560,毫米范围 ±1000 —— scale 这个换算系数,为什么不存成字段、每帧重算?
③ 轨迹点从哪来?`PositionChanged` 事件每个轴各自来报,订阅它画轨迹会有什么坑?(提示:X 报到 50.0 的那一瞬间,Y 报到了吗?)
④ 每个节拍重画整个面板,为什么眼睛看着会闪?前端的 canvas 有个"离屏"技巧,WinForms 对应物是什么?
⑤ 屏幕 Y 轴向下、机械 Y 轴向上 —— 换算公式哪一项要取反?

---

## 📚 本篇知识点(不懂再点回去学)

- [📖 WinForms 跨线程访问控件](kp:winforms-invoke) —— 为什么采样放在 UI 线程的定时器里就天生安全
- [📖 定时/定量批量刷新](kp:batching) —— 事件逐个刷界面 vs 按周期合并,工业系统标配思路

---

## 🛠️ 参考实现(三步:新控件 → 布局 → 接线)

### 步骤 1:自定义控件 TrajectoryPanel.cs(新建文件)

**设计思路一句**:控件自己管"数据(轨迹点)"和"渲染(OnPaint)" —— Sample() 只存点 + 标脏,所有画图只发生在 OnPaint,像前端"改 state → 触发 re-render"。

```csharp
// 📂 文件:src/MotionControl/UI/TrajectoryPanel.cs(新建)
using System.Drawing.Drawing2D;

namespace MotionControlProject.UI;

/// <summary>
/// X-Y 轨迹面板(自定义控件):轴 1 = X、轴 2 = Y,把两根轴的位置画成平面上的运动轨迹。
///
/// 自定义绘制控件的三条铁律:
/// 1. 数据与绘制分离:Sample() 只存点 + Invalidate() 标脏,所有画图只发生在 OnPaint 里
///    —— 相当于前端"改 state → 触发 re-render",绝不在数据更新时直接拿 Graphics 画;
/// 2. DoubleBuffered = true:先画进内存位图再整帧贴屏,否则每帧重画都闪 —— 离屏 canvas 同理;
/// 3. mm→像素的换算不存字段,OnPaint 里每帧现算:窗口会被拉伸,存下来的比例必然过期。
/// </summary>
public class TrajectoryPanel : Panel
{
    /// <summary>轨迹点序列(毫米坐标,机坐标系)。[^1] 永远是最新位置。</summary>
    private readonly List<PointF> _trail = new();

    /// <summary>软限位(±mm):画边界框 + 定显示范围 —— 轨迹图的可视范围跟着卡的行程走。</summary>
    public double SoftLimit { get; set; } = 1000;

    /// <summary>轨迹点数上限:到顶丢最老的(滚动日志的思路),无限长的点动也不会把内存吃穿。</summary>
    private const int MaxPoints = 4000;

    public TrajectoryPanel()
    {
        DoubleBuffered = true;   // 防闪烁
        ResizeRedraw = true;     // 拉伸窗口时整块重画,不留残影
        BackColor = Color.White;
    }

    /// <summary>
    /// 采样一个点(毫米)。位置没变就不记 —— 静止时定时器照常调,但轨迹不灌重复点。
    /// 谁来调:主窗体定时器(100ms),两轴位置同一时刻一起取,坐标才是一致的快照。
    /// </summary>
    public void Sample(double x, double y)
    {
        var p = new PointF((float)x, (float)y);
        if (_trail.Count > 0 && _trail[^1] == p) return;
        _trail.Add(p);
        if (_trail.Count > MaxPoints) _trail.RemoveAt(0);
        Invalidate();   // 只标记"画面过期",真正的画发生在下一次 OnPaint
    }

    /// <summary>清空轨迹(界面"清空轨迹"按钮调用)。</summary>
    public void ClearTrail()
    {
        _trail.Clear();
        Invalidate();
    }

    // OnPaint 是自绘控件的"渲染函数":所有线条、文字只在这里画。
    // 系统触发它的时机:Invalidate 之后的消息循环、窗口遮挡后露出、拉伸尺寸(ResizeRedraw)
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // —— 坐标映射:mm → 像素 ——
        // 比例取短边算、留 8px 边距:画出来是面板中央一个"正方形工作区",1mm 在 X/Y 方向等长,
        // 轨迹不变形(比例若按长宽各算各的,圆会变椭圆、直线会变斜线,这是绘图映射最经典的坑)
        var scale = (Math.Min(Width, Height) - 16f) / (float)(SoftLimit * 2);
        float Px(double mm) => Width / 2f + (float)(mm * scale);    // 机械 X 正向 = 屏幕右,方向一致不翻
        float Py(double mm) => Height / 2f - (float)(mm * scale);   // 屏幕 Y 轴向下、机械 Y 轴向上 → 取反

        // 1. 网格:每 SoftLimit/4(=250mm)一条浅灰线,方便对着轨迹读大概位置
        using (var gridPen = new Pen(Color.Gainsboro, 1f))
        {
            var step = SoftLimit / 4;
            for (var mm = -SoftLimit; mm <= SoftLimit + 0.5; mm += step)
            {
                g.DrawLine(gridPen, Px(mm), Py(-SoftLimit), Px(mm), Py(SoftLimit));   // 竖线
                g.DrawLine(gridPen, Px(-SoftLimit), Py(mm), Px(SoftLimit), Py(mm));   // 横线
            }
        }

        // 2. 工作区边框 = 软限位:轴永远出不了这个方框,行程边界一眼可见
        using (var borderPen = new Pen(Color.Silver, 1.5f))
            g.DrawRectangle(borderPen,
                Px(-SoftLimit), Py(SoftLimit),
                Px(SoftLimit) - Px(-SoftLimit), Py(-SoftLimit) - Py(SoftLimit));

        // 3. 坐标轴:过原点的 X/Y 十字线 —— 读轨迹的参照系
        using (var axisPen = new Pen(Color.DimGray, 1.2f))
        {
            g.DrawLine(axisPen, Px(-SoftLimit), Py(0), Px(SoftLimit), Py(0));   // X 轴
            g.DrawLine(axisPen, Px(0), Py(SoftLimit), Px(0), Py(-SoftLimit));   // Y 轴
        }
        using (var font = new Font("Consolas", 9f))
        using (var brush = new SolidBrush(Color.DimGray))
        {
            g.DrawString("X+", font, brush, Px(SoftLimit) - 26, Py(0) + 4);
            g.DrawString("Y+", font, brush, Px(0) + 6, Py(SoftLimit) + 2);
            g.DrawString("(0,0)", font, brush, Px(0) + 6, Py(0) + 4);
        }

        // 4. 轨迹折线:点动画出走过的路,插补画出一条直线,急停后线停在原地
        if (_trail.Count > 1)
        {
            var pts = new PointF[_trail.Count];
            for (var i = 0; i < _trail.Count; i++)
                pts[i] = new PointF(Px(_trail[i].X), Py(_trail[i].Y));
            using var trailPen = new Pen(Color.MediumSeaGreen, 2f);
            g.DrawLines(trailPen, pts);
        }

        // 5. 当前位置:轨迹末端一个红点(与急停同款红)—— 没动过时它就坐在原点上
        if (_trail.Count > 0)
        {
            var last = _trail[^1];
            using var dotBrush = new SolidBrush(Color.FromArgb(214, 64, 64));
            g.FillEllipse(dotBrush, Px(last.X) - 5, Py(last.Y) - 5, 10, 10);
        }
    }
}
```

💡 **OnPaint 五层画的顺序就是"后画的压在前画的上面"**:网格(底)→ 边框 → 坐标轴 → 轨迹线 → 当前点(顶)。层次想反了(比如轨迹画在网格下面),线就被网格盖住 —— 绘图永远是从底到顶一层层叠。

### 步骤 2:布局改造(MainForm.Designer.cs,八处小改)

窗体从 1200 加宽到 1520,第三栏位置让给轨迹图,报警/日志挪到第四栏。

**2a.** 头部注释:三栏改四栏
```csharp
/// - 主体四栏:轴1 | 轴2 | 轨迹图 | 报警/日志 —— 两轴 GroupBox 内部布局完全一致,对照着抄第二遍即可;
```

**2b.** `btnEstop = new Button();` 之后(即 `btnLinear = new Button();` 之后)加三行创建:
```csharp
        gbTraj = new GroupBox();
        trajPanel = new TrajectoryPanel();
        btnClearTrail = new Button();
```

**2c.** `gbLog.SuspendLayout();` 之后加:
```csharp
        gbTraj.SuspendLayout();
```

**2d.** `tableLayoutPanel1` 整块替换(3 列 34/34/32 → 4 列 26/26/24/24,gbTraj 占第 3 列):
```csharp
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
```

**2e.** `lblSoftLimit2` 配置块之后、`// tableLayoutPanel2` 注释之前,插入轨迹图三件套:
```csharp
        //
        // gbTraj —— X-Y 轨迹图(MC6):把两轴位置画成平面运动轨迹
        //
        gbTraj.Controls.Add(btnClearTrail);
        gbTraj.Controls.Add(trajPanel);
        gbTraj.Dock = DockStyle.Fill;
        gbTraj.Location = new Point(823, 11);
        gbTraj.Name = "gbTraj";
        gbTraj.Size = new Size(374, 677);
        gbTraj.TabIndex = 2;
        gbTraj.TabStop = false;
        gbTraj.Text = "轨迹图 · X-Y(轴1 = X,轴2 = Y)";
        //
        // trajPanel —— 自定义控件(见 TrajectoryPanel.cs):坐标轴 + 网格 + 软限位边框 + 轨迹线 + 当前点
        //
        trajPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        trajPanel.Location = new Point(16, 40);
        trajPanel.Name = "trajPanel";
        trajPanel.Size = new Size(330, 560);
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
```

**2f.** `tableLayoutPanel2` 的注释和 TabIndex(第三栏 → 第四栏):
```csharp
        // tableLayoutPanel2 —— 第四栏上下切:报警 52% / 日志 48%
```
```csharp
        tableLayoutPanel2.TabIndex = 3;
```

**2g.** 窗体加宽(1200 → 1520):
```csharp
        ClientSize = new Size(1520, 780);
```
```csharp
        MinimumSize = new Size(1536, 819);
```

**2h.** `gbLog.ResumeLayout(false);` 之后加:
```csharp
        gbTraj.ResumeLayout(false);
```

**2i.** 字段区 `private Button btnLinear;` 之后加:
```csharp
    private GroupBox gbTraj;
    private TrajectoryPanel trajPanel;
    private Button btnClearTrail;
```

### 步骤 3:接线(MainForm.cs 两处)

**3a.** 构造函数里,`btnLinear.Click += …` 之后加:
```csharp
        // 轨迹图(MC6):清空轨迹只清画面,不动轴
        btnClearTrail.Click += (s, e) => { trajPanel.ClearTrail(); AppendLog("轨迹已清空"); };
```

**3b.** `Timer1_Tick` 里,for 循环的 `}` 之后、`RefreshUiState();` 之前加:
```csharp
        // 轨迹采样:每帧把两轴位置一起记一个 (X, Y) 点。
        // 为什么在定时器里采、而不是订阅 PositionChanged 事件?事件是"每根轴各自来报",
        // X 到达时 Y 可能还停在上个节拍 → 轨迹会画出锯齿;定时器同一时刻取两轴,坐标才是
        // 一致的快照 —— 采集项目里"按周期采样"对付"事件乱序"是同一个思路。
        trajPanel.Sample(_card.GetAxisPosition(0), _card.GetAxisPosition(1));
```

💡 **为什么采样放定时器而不是 PositionChanged 事件 —— 本篇最重要的设计决策**:插补时两轴各自发事件,轴 1 报"X=50.0"那一刻,轴 2 可能还停在上个节拍的 Y 值 —— 逐事件记点,轨迹会画出一个个小锯齿,直线变台阶。定时器 100ms 把两轴位置**一次性一起取**,拿到的是同一时刻的快照,轨迹才干净。代价是采样率从 10Hz(节拍)降到 10Hz 定时器 —— 对可视化足够;数据记录才需要更高频率(那是采集项目 R 系列的领域)。**事件适合"发生了什么",采样适合"现在怎么样"** —— 这句话值得记进面试弹药库。

---

## ✅ 验证(沙盒实测输出 + 视觉验收)

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
已通过! - 失败:     0，通过:    14，已跳过:     0，总计:    14，持续时间: 9 s - MotionControl.Tests.dll (net8.0)
```
(MC4 的冒烟测试此时也顺带覆盖了轨迹面板:定时器 Tick → Sample → Invalidate → DoEvents 分发重绘,整条链路全程在线程模型下真跑。)

### 视觉验收清单(F8 跑起来,逐条过)

| # | 操作 | 预期 |
|---|---|---|
| 1 | 启动程序 | 第三栏是轨迹图:坐标轴 + 网格 + 银色软限位边框,原点一个红点 |
| 2 | 按住轴 1【正转】 | 红点沿 X 正方向移动,身后拖出一条水平绿线;松手线停 |
| 3 | 双轴同时点动(轴1 正 + 轴2 正) | 红点走斜线,轨迹是平滑斜线不是锯齿台阶 |
| 4 | 点【⇗ 两轴插补演示】 | **一条笔直的斜线**(X:Y 恒 5:3)—— MC5 的等比推进亲眼可见 |
| 5 | 插补运动中点【急停】 | 红点在轨迹末端当场冻住,不多走一步 |
| 6 | 点【回零 ⌂】再点另一轴回零 | 轨迹画回原点的线,红点最终回到 (0,0) |
| 7 | 速度 3000 按住轴 1 正转撞软限位 | 红点顶在边框边缘停下(X=+1000 位置),不穿框 |
| 8 | 点【清空轨迹】 | 绿线消失、红点留在当前位置;轴不受任何影响 |
| 9 | 拉伸窗口 | 轨迹图随之变大,网格/边框/轨迹等比例缩放,不变形不残影 |

---

## ✅ 验收清单(对着需求单逐条勾)

- [ ] FR-V01 坐标轴过原点,Y 方向正确(上 = 正)
- [ ] FR-V02 网格 250mm + 软限位边框可见
- [ ] FR-V03 轨迹线 + 当前点;静止不灌重复点
- [ ] FR-V04 等比例:圆轨迹不会变椭圆(拉伸窗口看视觉验收 9)
- [ ] FR-V05 快速点动不闪(双缓冲)
- [ ] FR-V06 采样在定时器,双轴同拍快照(视觉验收 3/4 无锯齿)
- [ ] FR-V07 MaxPoints = 4000 滚动上限
- [ ] FR-V08 清空按钮只清画面
- [ ] 视觉验收 9 条全过

---

## 🎤 面试怎么讲这一篇

> **一句话开场**:"轨迹可视化我写了个自绘控件:数据与绘制分离(Sample 存点 + Invalidate 标脏,OnPaint 统一渲染)、双缓冲防闪、mm 到像素等比例映射 —— 顺带用定时器采样解决了多轴事件乱序画锯齿的问题。"

**追问弹药库**:
- **"自绘控件为什么不直接在事件里画?"** —— 绘图必须响应系统的重绘消息(遮挡露出、拉伸、Invalidate),只有把画图放进 OnPaint,任何"需要重画"的时机系统都会来调它;在别处画的,一次遮挡就没了。这是"声明式渲染"和"命令式画一笔"的区别 —— 前端同学可以类比:改 state 触发 re-render,而不是绕过框架直接操作 DOM;
- **"闪烁怎么解决?"** —— 双缓冲 DoubleBuffered:每帧先画进内存位图,再整帧贴屏。没有它,网格、边框、轨迹逐笔画到屏幕上,眼睛看得到中间态,就是闪。离屏 canvas / requestAnimationFrame 合成同理;
- **"坐标映射有什么坑?"** —— 两个:① 屏幕 Y 向下、机械 Y 向上,Y 必须取反,忘了就是"轴往上走轨迹往下跑";② 比例必须 X/Y 共用一个(按短边算),各算各的圆变椭圆。另外窗口会拉伸,比例每帧现算,不能存字段;
- **"轨迹数据从事件来还是轮询来?"** —— 轮询(定时器 100ms 两轴一起采)。事件是每根轴各自报,插补时两轴上报有半个节拍的错位,逐事件记点会画出锯齿;定时器一次取两轴,快照才一致。原则:**事件适合感知"发生了什么",采样适合回答"现在怎么样"**;
- **"轨迹无限长怎么办?"** —— 点数上限 4000,到顶丢最老的,滚动窗口 —— 和日志滚动的思路一样,内存占用有界。

[← 回系列导航](项目实践_MotionControl_MC1_工程骨架与模拟卡.md) · 恭喜,MC 系列六篇全部完成 —— 两个项目(DaqMonitor + MotionControl)在手,14K 的底气就是它们加上你能把每一行讲清楚。
