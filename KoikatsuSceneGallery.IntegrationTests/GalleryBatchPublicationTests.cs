using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;

namespace KoikatsuSceneGallery.IntegrationTests;

public sealed class GalleryBatchPublicationTests
{
    [Fact]
    public async Task RealDispatcherPublishesOnUiAndRecoversAfterPublisherFailure()
    {
        var controller = DispatcherActivation.Run(DispatcherQueueController.CreateOnDedicatedThread);
        var queue = controller.DispatcherQueue;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var values = new List<int>();
        Task? producer = null;
        try
        {
            producer = Publish(() =>
            {
                Assert.True(queue.HasThreadAccess);
                values.Add(1);
                values.Add(2);
            });
            await producer.WaitAsync(lifetime.Token);
            Assert.Equal(new[] { 1, 2 }, values);

            var failure = new IOException("batch publication failed");
            producer = Publish(() => throw failure);
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(
                () => producer.WaitAsync(lifetime.Token)));

            producer = Publish(() => values.Add(3));
            await producer.WaitAsync(lifetime.Token);
            Assert.Equal(new[] { 1, 2, 3 }, values);
        }
        finally
        {
            lifetime.Cancel();
            try
            {
                if (producer is not null)
                {
                    try { await producer.WaitAsync(TimeSpan.FromSeconds(15)); }
                    catch (Exception) when (producer.IsCompleted) { }
                }
            }
            finally
            {
                await controller.ShutdownQueueAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            }
        }

        Task Publish(Action action) => Task.Run(() => AwaitedUiPublication.InvokeBlocking(
            callback => queue.TryEnqueue(DispatcherQueuePriority.Low, () => callback()),
            action, TimeSpan.FromSeconds(10), lifetime.Token));
    }
}
