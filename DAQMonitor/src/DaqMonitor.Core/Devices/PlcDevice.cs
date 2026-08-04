using DaqMonitor.Core.Models;
using System.Collections.Generic;
using System.Threading;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// PLC 设备（M3 落地）：实现 <see cref="IDevice"/>，直连西门子 S7 读 DB 块。
///
/// 和 ModbusDevice 同一套路：上层只认 IDevice，换设备 UI 零改动。
///
/// 两种运行模式：
/// - <b>模拟模式</b>（默认 simulate=true）：后台轮询产生随机值，<b>零硬件</b>即可跑通——你目前没 PLC 也能学、能测。
/// - <b>真实模式</b>：用 S7.Net（<c>dotnet add package S7NetPlus</c>）直连 PLC。下方给出真实写法，
///   因本机无 PLC 且为保持工程零依赖可编译，真实路径以注释保留，激活模拟路径。
///
/// 重点（M3 反复强调）：PLC 的 IsConnected 不可全信，真正"通不通"要看读回来的 LastErrorCode / 读值是否合理。
/// </summary>
public sealed class PlcDevice : DeviceBase
{
    /// <summary>点位 → PLC 地址映射，如 "DB1.DBW0"（数据块1、字0）。</summary>
    public sealed record PlcMap(int PointId, string DbAddress);

    private readonly bool _simulate;
    private readonly List<PlcMap> _maps;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly Random _rnd = new();
    private readonly Dictionary<int, double> _last = new();

    public PlcDevice(int id, string name, IEnumerable<PlcMap> maps, bool simulate = true)
        : base(id, name)
    {
        _simulate = simulate;
        _maps = maps.ToList();
    }

    public override void Connect()
    {
        State = DeviceState.Connecting;
        Start();
        State = DeviceState.Online;
    }

    public override void Disconnect()
    {
        Stop();
        State = DeviceState.Offline;
    }

    private void Start()
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
                    Tick();
                    await Task.Delay(500, token);
                }
            }
            catch (OperationCanceledException) { /* 正常退出 */ }
        }, token);
    }

    private void Stop()
    {
        _cts?.Cancel();
        try { _loop?.Wait(500); } catch { /* 忽略 */ }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    private void Tick()
    {
        if (_simulate)
        {
            foreach (var m in _maps)
            {
                double v = Math.Round(20 + _rnd.NextDouble() * 70, 2);
                _last[m.PointId] = v;
                RaiseData(m.PointId, v);
            }
            return;
        }

        // —— 真实 S7.Net 写法（需 dotnet add package S7NetPlus，并填 PLC 的 IP）——
        // using S7;
        // var plc = new Plc(CpuType.S71200, "192.168.0.1", 0, 1);
        // plc.Open();
        // try
        // {
        //     foreach (var m in _maps)
        //     {
        //         // 读 DB 块指定地址；addr 的编码参考 M3：DB*10000 + 偏移
        //         var raw = (short)plc.Read(m.DbAddress);
        //         if (plc.LastErrorCode != 0) continue;     // IsConnected 不可全信，看错误码
        //         double v = raw;
        //         _last[m.PointId] = v;
        //         RaiseData(m.PointId, v);
        //     }
        // }
        // finally { plc.Close(); }
    }

    public override double Read(int addr)
        => _last.TryGetValue(addr, out var v) ? v : double.NaN;

    public override void Write(int addr, double value)
    {
        if (_simulate) return;
        // 真实模式：plc.Write(m.DbAddress, (short)value);
    }
}
