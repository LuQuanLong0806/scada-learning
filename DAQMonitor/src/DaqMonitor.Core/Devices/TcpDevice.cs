using DaqMonitor.Core.Models;
using DaqMonitor.Core.Protocol;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;

namespace DaqMonitor.Core.Devices;

/// <summary>
/// TCP 设备（M11 落地）：实现 <see cref="IDevice"/>，面向长连接 + 长度前缀帧。
///
/// 设计要点（与 ModbusDevice / SerialDevice 一致的接口语义）：
///   - 上层只认 IDevice，换成它零改动。
///   - 帧格式由 <see cref="TcpFrameParser"/> 处理（AA55 + 小端长度 + payload + CRC16）。
///   - 后台 ReadAsync 循环 + 滚动缓冲做粘包/半包拼帧。
///   - 心跳：每 10 秒发心跳包；30 秒没收到任何对端数据判定掉线 → 自动重连。
///   - 自动重连：SocketException 后按 1s/2s/4s/8s/16s 指数退避重试，直到成功或 Dispose。
///
/// 模拟模式（simulate=true）：不建 socket，后台周期产生随机值，零硬件即可跑通链路（与 ModbusDevice 同套路）。
///
/// 真实模式 payload 解析示例：约定 payload 第一字节是功能码，
///   0x01 = 多点数据上报（后续每 5B 一组：[pointId(2)][double(4) 简化版]）。
///   真实工程应抽出到独立 PayloadCodec 类，这里只示意解析点。
/// </summary>
public sealed class TcpDevice : DeviceBase, IDisposable
{
    /// <summary>点位映射：地址 → PointId（解析后用 PointId 抬事件）。</summary>
    public sealed class TcpMap(int addr, int pointId) { public int Addr { get; } = addr; public int PointId { get; } = pointId; }

    private const int HeartbeatIntervalMs = 10_000;
    private const int OfflineTimeoutMs = 30_000;
    private static readonly int[] BackoffMs = { 1000, 2000, 4000, 8000, 16000 };

    private readonly bool _simulate;
    private readonly string _host;
    private readonly int _port;
    private readonly List<TcpMap> _maps;
    private readonly ConcurrentDictionary<int, double> _last = new();

    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly byte[] _rx = new byte[4096];

    public TcpDevice(int id, string name, string host, int port, IEnumerable<TcpMap>? maps = null, bool simulate = false)
        : base(id, name)
    {
        _simulate = simulate;
        _host = host;
        _port = port;
        _maps = maps?.ToList() ?? new();
    }

    public override void Connect()
    {
        if (State == DeviceState.Online) return;
        State = DeviceState.Connecting;
        Start();
        if (_simulate) State = DeviceState.Online; // 真实模式由 RealLoop 在 ConnectOnce 成功后标 Online
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
        _loop = Task.Run(() => _simulate ? SimulateLoop(token) : RealLoop(token), token);
    }

    private void Stop()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _loop?.Wait(1000); } catch { /* ignore */ }
        CloseSocket();
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    // ===== 模拟模式：与 ModbusDevice.SimulateTick 同套路，零硬件跑通链路 =====
    private async Task SimulateLoop(CancellationToken ct)
    {
        var rnd = new Random();
        while (!ct.IsCancellationRequested)
        {
            foreach (var m in _maps)
            {
                double v = Math.Round(20 + rnd.NextDouble() * 70, 2);
                _last[m.PointId] = v;
                RaiseData(m.PointId, v);
            }
            try { await Task.Delay(500, ct); } catch (OperationCanceledException) { break; }
        }
    }

    // ===== 真实模式：长连接 + ReadAsync 拼帧 + 心跳 + 自动重连 =====
    private async Task RealLoop(CancellationToken ct)
    {
        int backoffIdx = 0;
        // 外层循环：断了重连，直到成功连上或被取消
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!ConnectOnce())
                {
                    await DelayBackoff(ct, backoffIdx);
                    backoffIdx = Math.Min(backoffIdx + 1, BackoffMs.Length - 1);
                    continue;
                }
                backoffIdx = 0;
                State = DeviceState.Online;

                // 心跳用 Task.Delay 并行触发；read 循环内同步判断静默时长
                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _ = HeartbeatLoop(heartbeatCts.Token);

                await ReceiveLoop(ct);   // 正常退出=对端关闭或掉线
                heartbeatCts.Cancel();
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { /* 走重连 */ }
            catch (IOException) { /* 走重连 */ }
            finally
            {
                CloseSocket();
                if (State != DeviceState.Offline) State = DeviceState.Connecting;
            }

            if (ct.IsCancellationRequested) break;
            await DelayBackoff(ct, backoffIdx);
            backoffIdx = Math.Min(backoffIdx + 1, BackoffMs.Length - 1);
        }
    }

    private bool ConnectOnce()
    {
        try
        {
            CloseSocket();
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                ReceiveTimeout = OfflineTimeoutMs,
                SendTimeout = 5000
            };
            _socket.Connect(_host, _port);
            return true;
        }
        catch (SocketException) { return false; }
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        // 滚动缓冲区：堆字节 + TryParse 滑窗。生产可用 ArrayPool 进一步优化分配。
        var buffer = new List<byte>(4096);
        var seg = new byte[4096];
        DateTime lastRecv = DateTime.UtcNow;

        while (!ct.IsCancellationRequested && _socket?.Connected == true)
        {
            int n;
            try { n = await _socket.ReceiveAsync(new ArraySegment<byte>(seg), SocketFlags.None, ct); }
            catch (SocketException) { return; }
            if (n == 0) return; // 对端关闭
            lastRecv = DateTime.UtcNow;

            buffer.AddRange(seg.AsSpan(0, n));
            DrainBuffer(buffer);

            if ((DateTime.UtcNow - lastRecv).TotalMilliseconds > OfflineTimeoutMs) return; // 心跳超时
        }
    }

    private void DrainBuffer(List<byte> buffer)
    {
        while (buffer.Count > 0)
        {
            var arr = buffer.ToArray();
            if (!TcpFrameParser.TryParse(arr, out var payload, out int frameLen, out bool needResync))
            {
                if (needResync) buffer.RemoveAt(0);   // 头不对齐：丢 1 字节重同步
                break;                                // 数据不够：等下次 Receive
            }
            HandlePayload(payload);
            buffer.RemoveRange(0, frameLen);
        }
    }

    /// <summary>
    /// 业务侧 payload 解析：约定第一字节是功能码。
    /// 0x01：多点上报，[0x01][N][N×(addr:2, val:4 float LE)]；
    /// 0x02：心跳响应，忽略。
    /// 真实工程可把 codec 抽成单独策略类注入，这里只演示一例。
    /// </summary>
    private void HandlePayload(byte[] payload)
    {
        if (payload.Length == 0) return;
        switch (payload[0])
        {
            case 0x01: ParseMultiPoints(payload); break;
            case 0x02: /* 心跳响应，无操作 */ break;
        }
    }

    private void ParseMultiPoints(byte[] payload)
    {
        if (payload.Length < 2) return;
        int n = payload[1];
        for (int i = 0; i < n; i++)
        {
            int off = 2 + i * 6;
            if (off + 6 > payload.Length) break;
            int pointId = payload[off] | (payload[off + 1] << 8);
            double value = BitConverter.ToSingle(payload, off + 2);
            _last[pointId] = value;
            RaiseData(pointId, Math.Round(value, 3));
        }
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        // 心跳 payload = [0x02]，由 BuildFrame 现算 CRC
        var frame = TcpFrameParser.BuildFrame(new byte[] { 0x02 });
        while (!ct.IsCancellationRequested && _socket?.Connected == true)
        {
            try
            {
                await _socket!.SendAsync(new ArraySegment<byte>(frame), SocketFlags.None, ct);
            }
            catch (SocketException) { return; }
            catch (OperationCanceledException) { return; }
            try { await Task.Delay(HeartbeatIntervalMs, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private static async Task DelayBackoff(CancellationToken ct, int idx)
    {
        int ms = BackoffMs[Math.Min(idx, BackoffMs.Length - 1)];
        try { await Task.Delay(ms, ct); } catch (OperationCanceledException) { }
    }

    private void CloseSocket()
    {
        try { _socket?.Shutdown(SocketShutdown.Both); } catch { /* ignore */ }
        try { _socket?.Close(); } catch { /* ignore */ }
        _socket = null;
    }

    /// <summary>读最近一次缓存值（与 ModbusDevice.Read 同语义）。</summary>
    public override double Read(int addr)
    {
        var map = _maps.FirstOrDefault(m => m.Addr == addr);
        int pid = map?.PointId ?? addr;
        return _last.TryGetValue(pid, out var v) ? v : double.NaN;
    }

    /// <summary>
    /// 写值：把 [0x03][addr:2][value:4] 打成一帧下发。
    /// </summary>
    public override void Write(int addr, double value)
    {
        if (_simulate) return;
        if (_socket?.Connected != true) return;
        var payload = new byte[7];
        payload[0] = 0x03;
        payload[1] = (byte)(addr & 0xFF);
        payload[2] = (byte)((addr >> 8) & 0xFF);
        BitConverter.TryWriteBytes(payload.AsSpan(3), (float)value);
        var frame = TcpFrameParser.BuildFrame(payload);
        try { _socket.Send(frame); } catch (SocketException) { /* 重连循环会接手 */ }
    }

    public void Dispose()
    {
        Stop();
        _last.Clear();
    }
}
