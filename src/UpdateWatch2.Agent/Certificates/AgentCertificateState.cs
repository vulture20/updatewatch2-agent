namespace UpdateWatch2.Agent.Certificates;

public class AgentCertificateState : IAgentCertificateState
{
    private readonly TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsReady => _readyTcs.Task.IsCompleted;

    public void MarkReady() => _readyTcs.TrySetResult();

    public async Task WaitUntilReadyAsync(CancellationToken ct = default)
    {
        if (_readyTcs.Task.IsCompleted)
        {
            return;
        }

        // Deliberately not registering a cancellation callback on the
        // shared TaskCompletionSource itself — that would let one caller's
        // cancellation fault every other concurrent waiter's task too.
        // Racing against a per-call delay task keeps cancellation local to
        // this one call.
        var cancellationTask = Task.Delay(Timeout.Infinite, ct);
        var completed = await Task.WhenAny(_readyTcs.Task, cancellationTask);
        if (completed == cancellationTask)
        {
            ct.ThrowIfCancellationRequested();
        }
    }
}
