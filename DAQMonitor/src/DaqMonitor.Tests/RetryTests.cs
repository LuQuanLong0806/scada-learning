using DaqMonitor.Core.Resilience;
using Xunit;

namespace DaqMonitor.Tests;

public class RetryTests
{
    [Fact]
    public async Task ExecuteAsync_RetriesThenSucceeds()
    {
        int calls = 0;
        await Retry.ExecuteAsync(async () =>
        {
            calls++;
            if (calls < 3) throw new InvalidOperationException("transient");
            await Task.Yield();
        }, maxRetries: 5);

        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsAfterExhaustingRetries()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Retry.ExecuteAsync(async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException();
            }, maxRetries: 2));
    }
}
