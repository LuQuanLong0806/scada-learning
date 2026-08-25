// 📂 文件:src/MotionControl/Common/LogHelper.cs
namespace MotionControlProject.Common;

/// <summary>
/// 文件日志 —— 上位机的"黑匣子"。
/// 断网车间里出了问题,现场人员能给你的往往只有这个日志文件,所以关键动作必须落盘。
///
/// v1 坑:直接 File.AppendAllText 没加锁 —— 多线程同时写文件时,两行日志会穿插成乱码。
/// v2:所有写入包在 lock 里串行化,一行永远是一行。
/// </summary>
public static class LogHelper
{
    /// <summary>写文件互斥锁:静态类全局唯一,任何线程的写入排队通过。</summary>
    private static readonly object Gate = new();

    /// <summary>日志目录:exe 旁的 logs\,按天分文件,方便按日期回溯故障。</summary>
    private static string LogDir => Path.Combine(AppContext.BaseDirectory, "logs");

    /// <summary>
    /// 写一条日志并落盘。
    /// level 用 "INFO"/"WARN"/"ERROR"(对齐 -5 左对齐,日志列才整齐)。
    /// </summary>
    public static void Log(string message, string level = "INFO")
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level,-5}] {message}";
        lock (Gate)   // 没这把锁,后台线程和 UI 线程同时写就是乱码
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(
                Path.Combine(LogDir, $"motion_{DateTime.Now:yyyyMMdd}.txt"),
                line + Environment.NewLine);
        }
    }
}
