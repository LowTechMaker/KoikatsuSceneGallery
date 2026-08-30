using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

internal static class FriendFolderLayout
{
    public const string PersonalFolderName = "#Personal";
    public const string SelfFolderName = "#Self";
    public const string ScenesFolderName = "Scenes";
    public const string CharactersFolderName = "Characters";
    public const string CoordinatesFolderName = "Coordinates";
    public const string OldVersionFolderName = "#old_version";
    public const string AlternativesFolderName = "#alternatives";

    public static string GetScenesFolder(string friendFolder) =>
        Path.Combine(friendFolder, ScenesFolderName);

    public static string GetCharactersFolder(string friendFolder) =>
        Path.Combine(friendFolder, CharactersFolderName);

    public static string GetCoordinatesFolder(string friendFolder) =>
        Path.Combine(friendFolder, CoordinatesFolderName);

    public static string CreatePersonalFolder(string configuredRoot, string friendName)
    {
        var existingFolder = FindExistingPersonalFolderPath(
            configuredRoot,
            friendName);
        if (existingFolder is not null)
            return existingFolder;

        var friendFolder = GetPersonalFolderPath(configuredRoot, friendName);
        Directory.CreateDirectory(friendFolder);
        return friendFolder;
    }

    public static string GetPersonalFolderPath(
        string configuredRoot,
        string friendName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredRoot);
        var safeName = SanitizeFolderSegment(friendName, nameof(friendName));
        if (safeName.Equals(SelfFolderName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The folder name is reserved for the self profile.",
                nameof(friendName));
        }

        return Path.Combine(
            Path.GetFullPath(configuredRoot),
            PersonalFolderName,
            safeName);
    }

    public static string? FindExistingPersonalFolderPath(
        string configuredRoot,
        string friendName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredRoot);
        var safeName = SanitizeFolderSegment(friendName, nameof(friendName));
        if (safeName.Equals(SelfFolderName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The folder name is reserved for the self profile.",
                nameof(friendName));
        }

        return FindExistingPersonalChild(configuredRoot, safeName);
    }

    public static string GetSelfFolderPath(string configuredRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredRoot);
        return Path.Combine(
            Path.GetFullPath(configuredRoot),
            PersonalFolderName,
            SelfFolderName);
    }

    public static string CreateSelfFolder(string configuredRoot)
    {
        var existingFolder = FindExistingSelfFolderPath(configuredRoot);
        if (existingFolder is not null)
            return existingFolder;

        var selfFolder = GetSelfFolderPath(configuredRoot);
        Directory.CreateDirectory(selfFolder);
        return selfFolder;
    }

    public static string? FindExistingSelfFolderPath(string configuredRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredRoot);
        return FindExistingPersonalChild(configuredRoot, SelfFolderName);
    }

    public static bool IsSelfFolderPath(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return false;

        try
        {
            var folder = new DirectoryInfo(Path.GetFullPath(folderPath));
            return folder.Name.Equals(
                    SelfFolderName,
                    StringComparison.OrdinalIgnoreCase)
                && folder.Parent?.Name.Equals(
                    PersonalFolderName,
                    StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (Exception ex) when (
            ex is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }

    public static void EnsureCharacterVariantFolders(
        string friendCharacterFolder,
        Action<string>? directoryCreated = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(friendCharacterFolder);
        var root = Path.GetFullPath(friendCharacterFolder);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);

        EnsureDirectory(
            Path.Combine(root, OldVersionFolderName),
            directoryCreated);
        EnsureDirectory(
            Path.Combine(root, AlternativesFolderName),
            directoryCreated);
    }

    public static bool HasCharacterVariantFolders(
        string friendCharacterFolder)
    {
        if (string.IsNullOrWhiteSpace(friendCharacterFolder))
            return false;

        try
        {
            var root = Path.GetFullPath(friendCharacterFolder);
            return Directory.Exists(Path.Combine(root, OldVersionFolderName))
                && Directory.Exists(Path.Combine(root, AlternativesFolderName));
        }
        catch (Exception ex) when (
            ex is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }

    public static CharacterVariantKind ClassifyCharacterPath(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var friendFolder = TryGetFriendCharacterFolder(filePath);
        if (friendFolder is null)
            return CharacterVariantKind.Current;

        for (var directory = new FileInfo(filePath).Directory;
             directory is not null
             && !directory.FullName.Equals(
                 friendFolder,
                 StringComparison.OrdinalIgnoreCase);
             directory = directory.Parent)
        {
            if (directory.Parent is null
                || !directory.Parent.FullName.Equals(
                    friendFolder,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            if (directory.Name.Equals(
                    OldVersionFolderName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return CharacterVariantKind.OldVersion;
            }

            if (directory.Name.Equals(
                    AlternativesFolderName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return CharacterVariantKind.Alternative;
            }
        }

        return CharacterVariantKind.Current;
    }

    public static string? TryGetFriendCharacterFolder(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string? personalFolder = null;

        for (var directory = new FileInfo(filePath).Directory;
             directory is not null;
             directory = directory.Parent)
        {
            if (directory.Parent?.Name.Equals(
                    PersonalFolderName,
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                personalFolder = directory.FullName;
            }
        }

        if (personalFolder is null)
            return null;

        // The legacy layout nested the cards in a "Characters" folder directly
        // inside the friend's #Personal folder. Only that nesting counts — an
        // ordinary library root that merely happens to be named "Characters"
        // must not turn every card below it into a friend card.
        var legacyFolder = GetCharactersFolder(personalFolder);
        return IsWithin(Path.GetFullPath(filePath), legacyFolder)
            ? legacyFolder
            : personalFolder;
    }

    public static string BuildCharacterVersionKey(
        string? friendFolderPath,
        string characterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterName);
        return string.IsNullOrWhiteSpace(friendFolderPath)
            ? $"name:{characterName}"
            : $"friend:{Path.GetFullPath(friendFolderPath)}|name:{characterName}";
    }

    public static IReadOnlyList<string> CollapseNestedRoots(
        IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var candidates = roots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.Length)
            .ToList();
        var result = new List<string>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (!result.Any(root => IsWithin(candidate, root)))
                result.Add(candidate);
        }

        return result;
    }

    public static bool IsWithin(string filePath, string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(directoryPath))
            return false;

        try
        {
            var relative = Path.GetRelativePath(directoryPath, filePath);
            return relative != ".."
                && !relative.StartsWith(
                    $"..{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal)
                && !Path.IsPathRooted(relative);
        }
        catch (Exception ex) when (
            ex is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }

    public static bool AreSamePath(string firstPath, string secondPath)
    {
        if (string.IsNullOrWhiteSpace(firstPath)
            || string.IsNullOrWhiteSpace(secondPath))
        {
            return false;
        }

        try
        {
            var first = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(firstPath));
            var second = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(secondPath));
            return string.Equals(
                first,
                second,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (
            ex is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }

    private static string SanitizeFolderSegment(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var safeName = PathSanitizer.SanitizeFolderName(value).TrimEnd('.');
        if (string.IsNullOrWhiteSpace(safeName))
        {
            throw new ArgumentException(
                "The value must contain a valid folder name.",
                parameterName);
        }

        return safeName;
    }

    private static string? FindExistingPersonalChild(
        string configuredRoot,
        string childFolderName)
    {
        var root = Path.GetFullPath(configuredRoot);
        var personalFolder = FindChildDirectory(root, PersonalFolderName);
        return personalFolder is null
            ? null
            : FindChildDirectory(personalFolder, childFolderName);
    }

    private static string? FindChildDirectory(
        string parentPath,
        string childFolderName)
    {
        if (!Directory.Exists(parentPath))
            return null;

        return Directory.EnumerateDirectories(
                parentPath,
                "*",
                SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path =>
                Path.GetFileName(
                        Path.TrimEndingDirectorySeparator(path))
                    .Equals(
                        childFolderName,
                        StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureDirectory(
        string path,
        Action<string>? directoryCreated)
    {
        if (Directory.Exists(path))
            return;

        Directory.CreateDirectory(path);
        directoryCreated?.Invoke(path);
    }
}
