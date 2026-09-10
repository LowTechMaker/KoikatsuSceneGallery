namespace KoikatsuSceneGallery.Services;

internal sealed record ShutdownParticipant(Func<Task> Stop, Action DisposeResources);

// Start/WaitAsync are called serially on the owner thread. Stop must synchronously
// reject new work before returning its drain task. Resource disposal runs off the UI
// thread only after that participant's drain succeeds, even if the waiter timed out.
internal sealed class ShutdownCoordinator(
    IReadOnlyList<ShutdownParticipant> participants, Action<Exception> reportError)
{
    private Task? _completion;

    internal Task Start() => _completion ??= Task.WhenAll(participants.Select(StopAndDisposeAsync));

    internal async Task<bool> WaitAsync(TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        var completion = Start();
        try
        {
            await completion.WaitAsync(deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested) { return false; }
    }

    private async Task StopAndDisposeAsync(ShutdownParticipant participant)
    {
        try
        {
            await participant.Stop().ConfigureAwait(false);
            await Task.Run(participant.DisposeResources).ConfigureAwait(false);
        }
        catch (Exception ex) { reportError(ex); }
    }
}
