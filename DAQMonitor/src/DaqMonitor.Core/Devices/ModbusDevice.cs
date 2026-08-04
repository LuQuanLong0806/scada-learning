using DaqMonitor.Core.Models;
using DaqMonitor.Core.Protocol;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// Modbus 设备（M2 落地）：实现 <see cref="IDevice"/>。
///
/// 设计原则（和 SerialDevice / CanDevice 一致）：上层（采集管道 / UI / 报警）只认 IDevice，
/// 把 SimulatedDevice 换成它，**一行都不用改**。
///
/// 两种运行模式：
/// - <b>模拟模式</b>（simulate=true）：后台轮询产生随机值，<b>零硬件</b>即可跑通整条链路——你目前没串口设备也能学、能测。
/// - <b>真实 RTU 模式</b>：手搓 Modbus RTU 请求帧 → 经 SerialPort 下发 → 收响应 → 用
///   <see cref="ModbusFrameParser"/> 解析 + <see cref="Crc16"/> 校验。<b>完全复用 M2 Day 3 知识点，不依赖第三方库</b>
///   （生产要省事可直接换 NModbus，但手搓版让你真正"懂协议"）。
///
/// 生产提示：TCP 模式只需把 SerialPort 换成 TcpClient 并用 MBAP 头（无 CRC），解析逻辑复用 ModbusFrameParser。
/// </summary>
public sealed class ModbusDevice : DeviceBase
{
    /// <summary>点位 → 寄存器映射：地址 + 数据类型(float 跨 2 寄存器 / word 单寄存器) + 浮点字节序。</summary>
    public sealed record RegisterMap(int PointId, ushort Address, string Type, ModbusFrameParser.ByteOrder Order = ModbusFrameParser.ByteOrder.ABCD);

    private readonly bool _simulate;
    private readonly byte _slave;
    private readonly List<RegisterMap> _maps;
    private readonly string _portName;
    private readonly int _baud;
    private SerialPort? _port;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly Random _rnd = new();
    private readonly Dictionary<int, double> _last = new();

    public ModbusDevice(int id, string name, byte slave, IEnumerable<RegisterMap> maps,
        bool simulate = false, string portName = "COM3", int baud = 9600)
        : base(id, name)
    {
        _simulate = simulate;
        _slave = slave;
        _maps = maps.ToList();
        _portName = portName;
        _baud = baud;
        if (!_simulate) _port = new SerialPort(portName, baud, Parity.None, 8, StopBits.One);
    }

    public override void Connect()
    {
        State = DeviceState.Connecting;
        if (!_simulate) _port!.Open();
        Start();
        State = DeviceState.Online;
    }

    public override void Disconnect()
    {
        Stop();
        if (_port?.IsOpen == true) _port.Close();
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
                    if (_simulate) SimulateTick();
                    else RealTick();
                    await Task.Delay(500, token);   // 500ms 轮询，不阻塞 UI
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

    private void SimulateTick()
    {
        foreach (var m in _maps)
        {
            double v = m.Type == "float"
                ? Math.Round(20 + _rnd.NextDouble() * 70, 2)   // 模拟工程量
                : Math.Round(_rnd.NextDouble() * 100, 2);
            _last[m.PointId] = v;
            RaiseData(m.PointId, v);                            // 推给采集管道 → 最终到 UI
        }
    }

    private void RealTick()
    {
        foreach (var m in _maps)
        {
            bool isFloat = m.Type == "float";
            ushort count = isFloat ? (ushort)2 : (ushort)1;    // float 跨 2 个寄存器
            var req = ModbusFrameParser.BuildReadHoldingRequest(_slave, m.Address, count);
            _port!.Write(req, 0, req.Length);

            // 等响应（简化：轮询 BytesToRead；真实工程要按 3.5 字符静默判断帧边界，见 M2 Day 3）
            int waited = 0;
            int minLen = isFloat ? 9 : 7;                      // 读1寄存器响应=7B；读2=9B
            while (_port.BytesToRead < minLen && waited < 1000) { Thread.Sleep(10); waited += 10; }
            if (_port.BytesToRead == 0) continue;

            var resp = new byte[_port.BytesToRead];
            _port.Read(resp, 0, resp.Length);

            if (ModbusFrameParser.IsExceptionResponse(resp, out var code)) continue;  // 设备拒了
            if (!Crc16.Check(resp)) continue;                                       // CRC 坏帧丢弃
            var regs = ModbusFrameParser.ParseReadRegisters(resp);
            if (regs.Length == 0) continue;

            double value = isFloat
                ? ModbusFrameParser.ToFloatModbus(regs[0], regs[1], m.Order)        // 32 位浮点按字节序拼
                : regs[0];
            _last[m.PointId] = value;
            RaiseData(m.PointId, value);
        }
    }

    public override double Read(int addr)
        => _last.TryGetValue(addr, out var v) ? v : double.NaN;

    public override void Write(int addr, double value)
    {
        if (_simulate) return;   // 模拟设备只读
        // 写单寄存器（功能码 0x06）：[从站][0x06][地址2B][值2B][CRC低前]
        var payload = new List<byte> { _slave, 0x06, (byte)(addr >> 8), (byte)addr,
                                       (byte)((int)value >> 8), (byte)(int)value };
        ushort crc = Crc16.Modbus(payload.ToArray());
        payload.Add((byte)(crc & 0xFF));
        payload.Add((byte)(crc >> 8));
        _port!.Write(payload.ToArray(), 0, payload.Count);
    }
}
