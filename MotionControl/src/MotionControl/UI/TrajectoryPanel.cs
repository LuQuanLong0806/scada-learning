// 📂 文件:src/MotionControl/UI/TrajectoryPanel.cs
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
