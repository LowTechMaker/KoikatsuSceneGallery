using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;

namespace KoikatsuSceneGallery.IntegrationTests;

public sealed class GalleryMetadataRefreshTests
{
    [Fact]
    public async Task RealDispatcherTicksCompletesAndDisposesWithoutOpeningWindow()
    {
        var controller = DispatcherActivation.Run(DispatcherQueueController.CreateOnDedicatedThread);
        var queue = controller.DispatcherQueue;
        GalleryMetadataRefresh? refresh = null;
        var ticked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkedLifecycle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        try
        {
            Assert.True(queue.TryEnqueue(() =>
            {
                try
                {
                    refresh = new(queue, () =>
                    {
                        count++;
                        refresh!.Stop();
                        ticked.TrySetResult();
                    });
                    refresh.Start();
                    refresh.Start(); // Restart must not register the callback twice.
                }
                catch (Exception error) { ticked.TrySetException(error); }
            }));
            await ticked.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(queue.TryEnqueue(() =>
            {
                try
                {
                    Assert.Equal(1, count);
                    refresh!.Complete();
                    Assert.Equal(2, count);
                    refresh.Dispose();
                    refresh.Dispose();
                    refresh.Start();
                    refresh.Complete();
                    Assert.Equal(2, count);
                    checkedLifecycle.SetResult();
                }
                catch (Exception error) { checkedLifecycle.TrySetException(error); }
            }));
            await checkedLifecycle.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            queue.TryEnqueue(() => refresh?.Dispose());
            await controller.ShutdownQueueAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }
}
