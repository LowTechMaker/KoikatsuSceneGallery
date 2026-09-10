using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using KoikatsuSceneGallery.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Data;
using Windows.Foundation;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>Use WinUI's viewport-driven incremental loading, always mutating on the UI thread.</summary>
internal sealed class DiscoveryCollection(DispatcherQueue dispatcher, Func<bool> canLoad, Func<int, int> append)
    : ObservableCollection<DiscoveryItem>, ISupportIncrementalLoading
{
    private int _generation;
    public bool HasMoreItems => canLoad();
    public void Invalidate() => _generation++;

    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
    {
        int generation = _generation;
        return AsyncInfo.Run(async token =>
        {
            var completion = new TaskCompletionSource<LoadMoreItemsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!dispatcher.TryEnqueue(() =>
            {
                try
                {
                    uint loaded = !token.IsCancellationRequested && generation == _generation && HasMoreItems
                        ? (uint)append((int)Math.Clamp(count, 1u, 64u)) : 0;
                    completion.TrySetResult(new LoadMoreItemsResult { Count = loaded });
                }
                catch (Exception ex) { completion.TrySetException(ex); }
            })) completion.TrySetResult(new LoadMoreItemsResult());
            return await completion.Task.WaitAsync(token);
        });
    }
}
