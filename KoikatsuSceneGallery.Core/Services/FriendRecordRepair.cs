using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

internal static class FriendRecordRepair
{
    public static bool Repair(
        FriendRecord friend,
        ISet<Guid> usedIds,
        Action<string, Exception>? reportInvalidPath = null)
    {
        ArgumentNullException.ThrowIfNull(friend);
        ArgumentNullException.ThrowIfNull(usedIds);

        var changed = false;
        if (friend.Id == Guid.Empty || !usedIds.Add(friend.Id))
        {
            do
            {
                friend.Id = Guid.NewGuid();
            }
            while (!usedIds.Add(friend.Id));

            changed = true;
        }

        var repairedName = friend.Name?.Trim() ?? string.Empty;
        if (repairedName.Length == 0)
            repairedName = friend.Id.ToString("N")[..8];
        if (!string.Equals(friend.Name, repairedName, StringComparison.Ordinal))
        {
            friend.Name = repairedName;
            changed = true;
        }

        if (friend.FolderPath is not null)
        {
            var legacyFolder = NormalizePath(friend.FolderPath, reportInvalidPath);
            if (legacyFolder is not null)
            {
                friend.SceneFolderPath ??=
                    FriendFolderLayout.GetScenesFolder(legacyFolder);
                friend.CharacterFolderPath ??=
                    FriendFolderLayout.GetCharactersFolder(legacyFolder);
                friend.CoordinateFolderPath ??=
                    FriendFolderLayout.GetCoordinatesFolder(legacyFolder);
            }

            friend.FolderPath = null;
            changed = true;
        }

        changed |= RepairOptionalPath(
            friend.SceneFolderPath,
            value => friend.SceneFolderPath = value,
            reportInvalidPath);
        changed |= RepairOptionalPath(
            friend.CharacterFolderPath,
            value => friend.CharacterFolderPath = value,
            reportInvalidPath);
        changed |= RepairOptionalPath(
            friend.CoordinateFolderPath,
            value => friend.CoordinateFolderPath = value,
            reportInvalidPath);
        changed |= RepairOptionalPath(
            friend.AvatarPath,
            value => friend.AvatarPath = value,
            reportInvalidPath);

        var originalCardPaths = friend.CardPaths ?? [];
        var repairedCardPaths = originalCardPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizePath(path, reportInvalidPath))
            .Where(path => path is not null)
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (friend.CardPaths is null
            || !originalCardPaths.SequenceEqual(
                repairedCardPaths,
                StringComparer.OrdinalIgnoreCase))
        {
            friend.CardPaths = repairedCardPaths;
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
