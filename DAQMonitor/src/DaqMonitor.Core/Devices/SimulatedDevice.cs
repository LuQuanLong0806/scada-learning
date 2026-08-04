using DaqMonitor.Core.Models;
using System.Threading;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// 模拟设备：实现 IDevice，用后台线程周期性产生随机点位。
/// 没有真实硬件也能跑通整个采集链路——本地演示 / 单元测试 / 面试 demo 都用它。
///
/// 关键点：它和真实串口设备、PLC 设备一样，都只暴露 IDevice 接口。
/// 所以把 SimulatedDevice 换成 SerialDevice / PlcDevice（M1 / M3 落地）时，
/// 上层（采集管道、UI、报警引擎）完全无感——这就是面向接口 + 依赖注入带来的“可插拔”。
/// </summary>
public class SimulatedDevice : DeviceBase
{
    private readonly int[] _pointIds;
    private readonly Random _rnd = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public SimulatedDevice(int id, string name, params int[] pointIds)
        : base(id, name)
        => _pointIds = pointIds.Length > 0 ? pointIds : new[] { 1 };

    public override void Connect()
    {
        State = DeviceState.Connecting;
        Thread.Sleep(50); // 模拟握手耗时
        State = DeviceState.Online;
    }

    public override void Disconnect()
    {
        Stop();
        State = DeviceState.Offline;
    }

    public override double Read(int addr) => Math.Round(_rnd.NextDouble() * 100, 2);

    public override void Write(int addr, double value) { /* 模拟设备只读，忽略写 */ }

    /// <summary>
    /// 开始模拟采集：每隔 interval 给每个点位发一个随机值。
    /// 约 10% 概率冲到 95~120 区间，从而越过 100 的报警阈值，方便看到报警效果。
    /// </summary>
    public void Start(TimeSpan interval)
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    foreach (var pid in _pointIds)
                    {
                        var v = _rnd.NextDouble() < 0.1
                            ? 95 + _rnd.NextDouble() * 25   // 可能越界，触发报警
                            : 20 + _rnd.NextDouble() * 70;  // 正常区间
                        RaiseData(pid, Math.Round(v, 2));
                    }
                    await Task.Delay(interval, token);
                }
            }
            catch (OperationCanceledException) { /* 正常退出 */ }
        }, token);
    }

    /// <summary>停止模拟采集并释放后台任务。</summary>
    public void Stop()
    {
        _cts?.Cancel();
        try { _loop?.Wait(500); } catch { /* 忽略 */ }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }
}
