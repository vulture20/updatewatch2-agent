using UpdateWatch2.Agent.Certificates;

namespace UpdateWatch2.Agent.Tests.Certificates;

public class AgentCertificateStateTests
{
    [Fact]
    public async Task WaitUntilReadyAsync_completes_immediately_once_already_marked_ready()
    {
        var state = new AgentCertificateState();
        state.MarkReady();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await state.WaitUntilReadyAsync(cts.Token);

        Assert.True(state.IsReady);
    }

    [Fact]
    public async Task WaitUntilReadyAsync_blocks_until_MarkReady_is_called()
    {
        var state = new AgentCertificateState();
        var waitTask = state.WaitUntilReadyAsync();

        Assert.False(waitTask.IsCompleted);

        state.MarkReady();

        await waitTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(state.IsReady);
    }

    [Fact]
    public async Task WaitUntilReadyAsync_throws_when_cancelled_before_MarkReady_without_affecting_other_waiters()
    {
        var state = new AgentCertificateState();
        using var cts = new CancellationTokenSource();
        var cancelledWait = state.WaitUntilReadyAsync(cts.Token);
        var uncancelledWait = state.WaitUntilReadyAsync();

        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelledWait);

        // The other waiter must not have been faulted by the first
        // caller's cancellation — each call races its own delay task, not
        // a shared one (see AgentCertificateState's remarks).
        Assert.False(uncancelledWait.IsCompleted);
        state.MarkReady();
        await uncancelledWait.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
