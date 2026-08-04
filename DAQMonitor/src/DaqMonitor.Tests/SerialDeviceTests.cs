using DaqMonitor.Core.Acquisition;
using DaqMonitor.Core.Devices;
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Protocol;
using DaqMonitor.Core.Store;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// 验证“加一种设备 = 只写一个小类、UI/采集层零改动”：
/// 用 LoopbackSerialChannel（内存回环，零硬件）喂字节，断言 SerialDevice 的协议解析与整条链路成立。
/// 这里不碰任何真实串口，所以 CI / 没硬件的机器也能跑绿。
/// </summary>
public class SerialDeviceTests
{
    [Fact]
    public void Parses_SingleFrame_AndRaisesData()
    {
        var ch = new LoopbackSerialChannel();
        var dev = new SerialDevice(1, "SER", ch);
        var got = new List<(int, double)>();
        dev.DataReceived += (_, e) => got.Add((e.PointId, e.Value));

        dev.Connect();
        ch.Write(FrameParser.Build(1, 123.5));
        Thread.Sleep(200);                      // 等回环异步回调
        dev.Disconnect();

        Assert.Contains(got, x => x.Item1 == 1 && Math.Abs(x.Item2 - 123.5) < 1e-6);
    }

    [Fact]
    public void Handles_粘包_TwoFramesInOneChunk()
    {
        var ch = new LoopbackSerialChannel();
        var dev = new SerialDevice(1, "SER", ch);
        var got = new List<int>();
        dev.DataReceived += (_, e) => got.Add(e.PointId);

        dev.Connect();
        var both = FrameParser.Build(1, 10).Concat(FrameParser.Build(2, 20)).ToArray();
        ch.Write(both);                         // 两帧粘在一起一次到达
        Thread.Sleep(200);
        dev.Disconnect();

        Assert.Contains(1, got);
        Assert.Contains(2, got);
    }

    [Fact]
    public void Handles_半包_SplitAcrossChunks()
    {
        var ch = new LoopbackSerialChannel();
        var dev = new SerialDevice(1, "SER", ch);
        var got = new List<int>();
        dev.DataReceived += (_, e) => got.Add(e.PointId);

        dev.Connect();
        var frame = FrameParser.Build(3, 30);
        ch.Write(frame.AsSpan(0, frame.Length / 2).ToArray());   // 先来半包
        Thread.Sleep(50);
        ch.Write(frame.AsSpan(frame.Length / 2).ToArray());      // 补齐剩余
        Thread.Sleep(200);
        dev.Disconnect();

        Assert.Contains(3, got);                  // 半包必须能拼回完整帧
    }

    [Fact]
    public void Drops_Frame_WithBadCrc()
    {
        var ch = new LoopbackSerialChannel();
        var dev = new SerialDevice(1, "SER", ch);
        var got = new List<int>();
        dev.DataReceived += (_, e) => got.Add(e.PointId);

        dev.Connect();
        var frame = FrameParser.Build(5, 50);
        frame[^1] ^= 0xFF;                        // 故意破坏 CRC
        ch.Write(frame);
        Thread.Sleep(200);
        dev.Disconnect();

        Assert.DoesNotContain(5, got);            // 坏帧被丢弃，不污染业务
    }

    [Fact]
    public async Task SerialDevice_ThroughPipeline_ProducesPoints_InStore()
    {
        // 最强证明：SerialDevice 直接挂到统一采集管道 + 存储，整条链路不碰 UI、不碰真实硬件
        var ch = new LoopbackSerialChannel();
        var dev = new SerialDevice(1, "SER", ch);
        using var pipeline = new AcquisitionPipeline(TimeSpan.FromMilliseconds(50));
        var store = new PointStore();

        pipeline.Register(dev);
        dev.Connect();
        ch.Write(FrameParser.Build(1, 123.5));

        var done = new TaskCompletionSource<bool>();
        pipeline.BatchReady += (_, b) =>
        {
            foreach (var p in b) store.AddOrUpdate(p);
            if (b.Count > 0) done.TrySetResult(true);
        };

        await Task.WhenAny(done.Task, Task.Delay(2000));
        dev.Disconnect();

        Assert.True(store.GetAll().Any(p => p.Id == 1 && Math.Abs(p.Value - 123.5) < 1e-6),
            "SerialDevice 经管道写入存储失败——‘换设备 UI 零改动’未成立");
    }

    [Fact]
    public void RawLog_Fires_OnSendAndReceive()   // 验证 M15 联调“调试开关”
    {
        var ch = new LoopbackSerialChannel();
        var dev = new SerialDevice(1, "SER", ch);
        var logs = new List<string>();
        dev.RawLog = m => logs.Add(m);

        dev.Connect();
        dev.Write(9, 1.0);                     // 触发 TX 日志（下发命令帧）
        ch.Write(FrameParser.Build(1, 1.0));   // 触发 RX 日志（设备回数据）
        Thread.Sleep(200);
        dev.Disconnect();

        Assert.Contains(logs, l => l.StartsWith("TX "));
        Assert.Contains(logs, l => l.StartsWith("RX "));
    }
}
