namespace DaqMonitor.Core.Resilience;

/// <summary>
/// 生产级重试：指数退避 + 随机抖动。无需 Polly，手撸即可（A 类必补项）。
/// 用途：串口 / Modbus / PLC / 网络 通信偶发失败，不应直接抛给用户，应重试。
/// 面试常问"通信断了怎么办"——答案就是：重试 + 退避 + 超时 + 重连，而不是裸 try-catch。
/// </summary>
public static class Retry
{
    /// <summary>无返回值的重试。maxRetries=3 表示最多试 4 次（首试 + 3 次重试）。</summary>
    public static async Task ExecuteAsync(Func<Task> action, int maxRetries = 3, int baseDelayMs = 200, CancellationToken ct = default)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception) when (attempt < maxRetries && !ct.IsCancellationRequested)
            {
                attempt++;
                var delay = (int)(baseDelayMs * Math.Pow(2, attempt - 1)) + Random.Shared.Next(0, baseDelayMs);
                await Task.Delay(delay, ct);
            }
        }
    }

    /// <summary>带返回值的重试。</summary>
    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> action, int maxRetries = 3, int baseDelayMs = 200, CancellationToken ct = default)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                return await action();
            }
            catch (Exception) when (attempt < maxRetries && !ct.IsCancellationRequested)
            {
                attempt++;
                var delay = (int)(baseDelayMs * Math.Pow(2, attempt - 1)) + Random.Shared.Next(0, baseDelayMs);
                await Task.Delay(delay, ct);
            }
        }
    }
}
