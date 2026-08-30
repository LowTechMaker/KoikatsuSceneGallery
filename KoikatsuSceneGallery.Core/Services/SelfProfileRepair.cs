using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

internal static class SelfProfileRepair
{
    public static bool Repair(
        SelfProfile profile,
        string defaultName,
        Action<string, Exception>? reportInvalidPath = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultName);

        var changed = false;
        if (profile.Id == Guid.Empty)
        {
            profile.Id = Guid.NewGuid();
            changed = true;
        }

        var repairedName = profile.Name?.Trim() ?? string.Empty;
        if (repairedName.Length == 0)
            repairedName = defaultName.Trim();
        if (!string.Equals(profile.Name, repairedName, StringComparison.Ordinal))
        {
            profile.Name = repairedName;
            changed = true;
        }

        changed |= RepairOptionalPath(
            profile.SceneFolderPath,
            value => profile.SceneFolderPath = value,
            reportInvalidPath);
        changed |= RepairOptionalPath(
            profile.CharacterFolderPath,
            value => profile.CharacterFolderPath = value,
            reportInvalidPath);
        changed |= RepairOptionalPath(
            profile.CoordinateFolderPath,
            value => profile.CoordinateFolderPath = value,
            reportInvalidPath);
        changed |= RepairOptionalPath(
            profile.AvatarPath,
            value => profile.AvatarPath = value,
            reportInvalidPath);

        var originalCardPaths = profile.CardPaths ?? [];
        var repairedCardPaths = originalCardPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizePath(path, reportInvalidPath))
            .Where(path => path is not null)
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (profile.CardPaths is null
            || !originalCardPaths.SequenceEqual(
                repairedCardPaths,
                StringComparer.OrdinalIgnoreCase))
        {
            profile.CardPaths = repairedCardPaths;
            changed = true;
        }

        return changed;
    }

    private static bool RepairOptionalPath(
        string? original,
        Action<string?> assign,
        Action<string, Exception>? reportInvalidPath)
    {
        var repaired = NormalizePath(original, reportInvalidPath);
        if (string.Equals(original, repaired, StringComparison.Ordinal))
            return false;

        assign(repaired);
        return true;
    }

    private static string? NormalizePath(
        string? path,
        Action<string, Exception>? reportInvalidPath)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (
            ex is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            reportInvalidPath?.Invoke(path, ex);
            return null;
        }
    }
}
