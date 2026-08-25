// 📂 文件:src/MotionControl/Device/IMotionCard.cs
namespace MotionControlProject.Device;

/// <summary>
/// 运动指令返回码 —— 对齐真实板卡 SDK 的习惯:0 = 成功,负数 = 各种失败原因。
/// 真卡 SDK(Googol/雷赛/正运动…)几乎都返回 int 错误码,这里用枚举把"魔数"变成可读名字,
/// 但保留负数值,练习者将来看到真卡返回 -1 就知道该怎么对应。
/// </summary>
public enum MotionResult
{
    /// <summary>指令成功</summary>
    Ok = 0,
    /// <summary>卡未连接(网线没插 / Connect 没调 / 已断开)</summary>
    NotConnected = -1,
    /// <summary>轴号越界(只有 2 轴的卡,传了 axis=5)</summary>
    AxisIndexError = -2,
    /// <summary>参数非法:空 IP、速度 ≤ 0、目标位置超出软限位…</summary>
    ParamError = -3,
    /// <summary>轴未使能(伺服没上使能就让它动,真机上电机纹丝不动)</summary>
    AxisDisabled = -4,
    /// <summary>轴处于报警状态,必须先清报警才能再动</summary>
    AlarmActive = -5,
}

/// <summary>位置变化事件参数:哪根轴、现在走到哪(mm)。</summary>
public class PositionChangedEventArgs : EventArgs
{
    public int Axis { get; }
    public double Position { get; }

    public PositionChangedEventArgs(int axis, double position)
    {
        Axis = axis;
        Position = position;
    }
}

/// <summary>报警事件参数:哪根轴、报什么、是"发生报警"还是"报警清除"。</summary>
public class AlarmChangedEventArgs : EventArgs
{
    public int Axis { get; }
    public string Message { get; }
    /// <summary>true = 报警发生;false = 报警被清除。</summary>
    public bool IsActive { get; }

    public AlarmChangedEventArgs(int axis, string message, bool isActive)
    {
        Axis = axis;
        Message = message;
        IsActive = isActive;
    }
}

/// <summary>
/// 运动控制卡抽象 —— 整个工程的"插座"。
/// 上位机行业现实:你开发时手上往往没有真卡(卡在客户产线上),
/// 所以把"卡能做什么"抽象成接口,模拟卡和真卡各做一个实现,上层代码完全复用。
/// 这与采集项目的 IDevice 是同一个思路:面向接口编程,设备可替换。
/// </summary>
public interface IMotionCard
{
    // ———— 状态查询(属性) ————

    /// <summary>卡是否已连接。</summary>
    bool IsConnected { get; }

    /// <summary>卡控制几根轴。</summary>
    int AxisCount { get; }

    // ———— 事件:卡主动向上层"汇报" ————

    /// <summary>任一轴位置变化时触发(模拟卡每个仿真节拍发一次;真卡可由轮询线程发)。</summary>
    event EventHandler<PositionChangedEventArgs>? PositionChanged;

    /// <summary>报警发生 / 报警清除时触发。</summary>
    event EventHandler<AlarmChangedEventArgs>? AlarmChanged;

    /// <summary>急停(StopAll)生效时触发一次。</summary>
    event EventHandler? EmergencyStopped;

    // ———— 连接管理 ————

    /// <summary>连接卡。ipAddress 为空或全空格返回 ParamError(真卡会做 ping/握手)。</summary>
    MotionResult Connect(string ipAddress);

    /// <summary>断开连接,并取消所有进行中的运动。</summary>
    MotionResult Disconnect();

    // ———— 轴状态 ————

    /// <summary>使能 / 下使能某轴。使能 = 伺服上电锁轴,未使能一切运动指令都会被拒绝。</summary>
    MotionResult SetAxisEnable(int axis, bool enable);

    /// <summary>某轴是否已使能。</summary>
    bool IsAxisEnabled(int axis);

    /// <summary>某轴是否正在运动(点动 / 定位 / 回零都算)。</summary>
    bool IsMoving(int axis);

    /// <summary>读某轴当前位置(mm)。注意:读位置永远允许,连没使能都能读。</summary>
    double GetAxisPosition(int axis);

    /// <summary>读某轴当前报警信息,空字符串 = 无报警。</summary>
    string GetAlarmMessage(int axis);

    // ———— 运动指令 ————

    /// <summary>
    /// 点动(JOG):按住按钮朝一个方向一直走,松手停。
    /// speed 单位 mm/s;forward = true 正转 / false 反转。
    /// </summary>
    MotionResult JogAxis(int axis, double speed, bool forward);

    /// <summary>停止某轴点动(松手时调用)。</summary>
    MotionResult StopJog(int axis);

    /// <summary>
    /// 绝对定位:走到"绝对坐标" position(mm),速度 speed(mm/s)。
    /// 若该轴正在运动,新指令会打断旧运动(真卡的常规语义:后到的指令赢)。
    /// </summary>
    MotionResult MoveAbsolute(int axis, double position, double speed);

    /// <summary>回零(回原点):走到机械零点位置 0。速度固定 100mm/s(简化的"回零速度")。</summary>
    MotionResult HomeAxis(int axis);

    /// <summary>急停:所有轴立即停止,位置就地冻结。触发 EmergencyStopped 事件。</summary>
    MotionResult StopAll();

    /// <summary>清除某轴报警。清完报警轴还要重新确认使能状态才能运动(与真卡一致)。</summary>
    MotionResult ClearAlarm(int axis);

    // ———— 可选进阶:两轴直线插补 ————

    /// <summary>
    /// 多轴直线插补:各轴同时启动、等比推进、同时到位,走出一条空间直线。
    /// 例:X 从 0→50,Y 从 0→30,任意时刻 X:Y 恒等于 5:3。
    /// </summary>
    MotionResult MoveLinear(int[] axes, double[] targets, double speed);

    // ———— 模拟卡专用(真卡没有) ————

    /// <summary>人为注入一条报警 —— 用来在没真故障的情况下测试报警链路。</summary>
    void SimulateAlarm(int axis, string message);
}
