using System.Collections;

namespace KoikatsuSceneGallery.Helpers;

internal sealed class ShuffleQueueComparer(Dictionary<object, int> orderMap) : IComparer
{
    public int Compare(object? x, object? y)
    {
        int ix = x is not null && orderMap.TryGetValue(x, out var a) ? a : int.MaxValue;
        int iy = y is not null && orderMap.TryGetValue(y, out var b) ? b : int.MaxValue;
        return ix.CompareTo(iy);
    }
}
