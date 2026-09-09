namespace UpdateWatch2.Agent.Certificates;

public class RegistrationWakeSignal : IRegistrationWakeSignal
{
    // 0/1 (not counting), so at most one pending wake is ever remembered —
    // several RequestImmediateCheck calls before anyone waits still only
    // wake the next wait once, which is all that's needed (the woken
    // iteration re-checks real state from scratch either way).
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void RequestImmediateCheck()
    {
        // Release() throws SemaphoreFullException if already at the max
        // count — exactly the "already have a pending wake queued" case
        // this is meant to collapse into a no-op, not propagate as an error.
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public Task WaitForWakeOrTimeoutAsync(TimeSpan timeout, CancellationToken ct = default) =>
        _signal.WaitAsync(timeout, ct);
}
