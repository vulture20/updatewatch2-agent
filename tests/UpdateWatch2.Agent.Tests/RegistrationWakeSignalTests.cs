using UpdateWatch2.Agent.Certificates;

namespace UpdateWatch2.Agent.Tests;

public class RegistrationWakeSignalTests
{
    [Fact]
    public async Task WaitForWakeOrTimeoutAsync_returns_almost_immediately_when_signaled_before_the_timeout()
    {
        var signal = new RegistrationWakeSignal();
        signal.RequestImmediateCheck();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await signal.WaitForWakeOrTimeoutAsync(TimeSpan.FromSeconds(30));
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Expected an immediate return, took {sw.Elapsed}.");
    }

    [Fact]
    public async Task WaitForWakeOrTimeoutAsync_returns_once_the_timeout_elapses_with_no_signal()
    {
        var signal = new RegistrationWakeSignal();

        await signal.WaitForWakeOrTimeoutAsync(TimeSpan.FromMilliseconds(50));

        // No assertion beyond "this returned at all" — the point is it
        // doesn't hang forever waiting for a signal that never comes.
    }

    [Fact]
    public async Task RequestImmediateCheck_before_anyone_is_waiting_is_not_lost()
    {
        var signal = new RegistrationWakeSignal();

        signal.RequestImmediateCheck();

        // The next wait should pick up the pre-charged signal rather than
        // waiting out its own timeout — proven by racing it against a short
        // timeout task of the test's own.
        var waitTask = signal.WaitForWakeOrTimeoutAsync(TimeSpan.FromSeconds(30));
        var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(waitTask, completed);
    }

    [Fact]
    public void RequestImmediateCheck_called_repeatedly_with_no_waiter_does_not_throw()
    {
        var signal = new RegistrationWakeSignal();

        signal.RequestImmediateCheck();
        signal.RequestImmediateCheck();
        signal.RequestImmediateCheck();

        // No SemaphoreFullException — collapsing several pending requests
        // into a single remembered wake is exactly the intended behavior.
    }
}
