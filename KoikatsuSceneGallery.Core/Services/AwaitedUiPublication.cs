namespace KoikatsuSceneGallery.Services;

internal static class AwaitedUiPublication
{
    // For synchronous background producers only; never block the destination UI thread.
    // Timeout/caller cancellation invalidates callbacks that have not begun publishing.
    public static void InvokeBlocking(Func<Action, bool> enqueue, Action publish,
        TimeSpan timeout, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var task = InvokeAsync(enqueue, () => { publish(); return true; }, lifetime.Token);
        // A timeout can race a publication failure; observe faults even if the waiter left.
        _ = task.ContinueWith(failed => { _ = failed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        try { task.WaitAsync(timeout, token).GetAwaiter().GetResult(); }
        finally { lifetime.Cancel(); }
    }

    public static async Task<T> InvokeAsync<T>(Func<Action, bool> enqueue, Func<T> publish, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        if (!enqueue(() =>
        {
            if (completion.Task.IsCompleted || token.IsCancellationRequested) return;
            try { completion.TrySetResult(publish()); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }))
            completion.TrySetException(new InvalidOperationException("The UI dispatcher rejected publication."));
        return await completion.Task.ConfigureAwait(false);
    }
}
