using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// The user's own G / R-18 / R-18G rating of scene cards, read and written the
/// same way from every place that offers it: the detail page, the card menu,
/// and the inspector. A rating is keyed by the card's payload hash, so reading
/// it means reading the file; that runs off the UI thread.
/// </summary>
internal static class CardRatingActions
{
    /// <summary>The labels for ratings 0, 1 and 2.</summary>
    public static readonly string[] Labels = ["G", "R-18", "R-18G"];

    private static UserCardRatingStore Store => App.Services.GetRequiredService<UserCardRatingStore>();

    private static string[] Roots() => [.. App.Services.GetRequiredService<SettingsViewModel>().FolderPaths];

    /// <summary>
    /// The card's payload hash and its rating, or a null hash when the file
    /// could not be read (no rating can be saved for it then).
    /// </summary>
    public static async Task<(string? Hash, int? Rating)> LoadAsync(SceneCard card)
    {
        var store = Store;
        var roots = Roots();
        var hash = await Task.Run(() =>
        {
            store.EnsureLoaded(roots);
            return CardPayloadHash.TryCompute(card.FilePath);
        });
        if (hash is null) return (null, null);
        return (hash, await store.ResolveAsync(card.FilePath, hash));
    }

    /// <summary>Saves one card's rating, or clears it with null. Shows a toast when it fails.</summary>
    /// <returns>The rating now stored for the card.</returns>
    public static async Task<int?> SaveAsync(SceneCard card, string hash, int? rating)
    {
        var store = Store;
        var ok = rating is { } value
            ? await store.SetAsync(card.FilePath, hash, value)
            : await store.ClearAsync(card.FilePath, hash);
        if (!ok) AppToast.Show(UiText.Get("Rating_SaveFailed"), InfoBarSeverity.Error, key: "rating-save");
        return ok ? rating : store.TryGetByHash(hash);
    }

    /// <summary>
    /// Sets (or clears) the rating on several cards, from a menu or a batch
    /// action, and says how it went in one toast.
    /// </summary>
    public static async Task RateManyAsync(IReadOnlyList<SceneCard> cards, int? rating)
    {
        var store = Store;
        var roots = Roots();
        var hashed = await Task.Run(() =>
        {
            store.EnsureLoaded(roots);
            return cards.Select(card => (card, hash: CardPayloadHash.TryCompute(card.FilePath))).ToArray();
        });

        var failed = 0;
        foreach (var (card, hash) in hashed)
        {
            var ok = hash is not null && (rating is { } value
                ? await store.SetAsync(card.FilePath, hash, value)
                : await store.ClearAsync(card.FilePath, hash));
            if (!ok) failed++;
        }

        if (failed > 0)
        {
            AppToast.Show(UiText.Get("Rating_SaveFailed"), InfoBarSeverity.Error, key: "rating-save");
            return;
        }

        var label = rating is { } chosen ? Labels[chosen] : UiText.Get("CardMenu_RateClear");
        var message = cards.Count == 1
            ? UiText.Format("CardMenu_RatedOne", label)
            : UiText.Format("CardMenu_RatedMany", cards.Count, label);
        // With the platform as the rating source the badge on the tile does not
        // change, which reads as "it did not save" unless something says why.
        if (!App.Services.GetRequiredService<SettingsViewModel>().UseUserRatings)
            message += " " + UiText.Get("CardMenu_RatedPlatformHint");
        AppToast.Show(message, InfoBarSeverity.Success, key: "rating-save");
    }
}
