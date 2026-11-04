using System.ComponentModel;
using System.Runtime.InteropServices;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

public enum ImportConflictKind
{
    /// <summary>The destination holds a different card under the same name.</summary>
    NameClash,
    /// <summary>The library already holds this card, in another folder than the destination.</summary>
    InLibraryElsewhere,
}

public enum ImportConflictChoice
{
    Skip,
    /// <summary>Import under a free name beside the existing file.</summary>
    Rename,
    /// <summary>Replace the existing file; the old one goes to the recycle bin.</summary>
    Overwrite,
    /// <summary>A hard link at the destination to the library's copy: no second copy on disk.</summary>
    HardLink,
    /// <summary>A symbolic link at the destination to the library's copy.</summary>
    SymbolicLink,
    /// <summary>Import a second copy at the destination.</summary>
    Copy,
}

public static class ImportConflictRules
{
    public static IReadOnlyList<ImportConflictChoice> ChoicesFor(ImportConflictKind kind) => kind switch
    {
        ImportConflictKind.NameClash => [ImportConflictChoice.Rename, ImportConflictChoice.Overwrite, ImportConflictChoice.Skip],
        _ => [ImportConflictChoice.Skip, ImportConflictChoice.HardLink, ImportConflictChoice.SymbolicLink, ImportConflictChoice.Copy],
    };

    /// <summary>The first choice offered, which is also what nobody loses anything by.</summary>
    public static ImportConflictChoice DefaultFor(ImportConflictKind kind) => ChoicesFor(kind)[0];

    /// <summary>
    /// Whether <paramref name="destination"/> exists and holds a different card from
    /// <paramref name="source"/>. The same card with another preview image is not a clash.
    /// </summary>
    public static bool IsNameClash(string source, string destination)
    {
        if (!File.Exists(destination)) return false;
        try
        {
            return !ImportDuplicateDetector.AreFilesIdentical(source, destination)
                   && !CardPayloadHash.AreSameCard(source, destination);
        }
        catch (IOException)
        {
            return true; // unreadable: let the user decide rather than guess it is a duplicate
        }
    }

    /// <summary>A free name beside <paramref name="destination"/>: "card_1.png", "card_2.png"…</summary>
    public static string RenamedDestination(string source, string destination, IDictionary<string, string> claimed)
        => LocalDestinationNames.Resolve(source, destination, claimed, File.Exists, (_, _) => false);
}

/// <summary>
/// Moves the file an overwrite replaces out of the way first, so a failed import can put
/// it back. Only once the new file is in place is the old one let go of.
/// </summary>
public sealed class OverwriteBackup
{
    private readonly List<(string Original, string Backup)> _moved = [];
    private readonly string _directory;

    public OverwriteBackup(string directory) => _directory = directory;

    public IReadOnlyList<string> BackupPaths => _moved.Select(m => m.Backup).ToList();

    public void Move(string path)
    {
        Directory.CreateDirectory(_directory);
        var backup = Path.Combine(_directory, $"{_moved.Count:D4}_{Path.GetFileName(path)}");
        File.Move(path, backup);
        _moved.Add((path, backup));
    }

    /// <summary>Puts every moved file back where it was; one that cannot be is reported, not thrown.</summary>
    public IReadOnlyList<string> Restore()
    {
        var failed = new List<string>();
        foreach (var (original, backup) in _moved)
        {
            try
            {
                if (!File.Exists(original)) File.Move(backup, original);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(original);
            }
        }
        return failed;
    }
}

public static class FileLinks
{
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkNative(string fileName, string existingFileName, IntPtr securityAttributes);

    /// <summary>
    /// Both paths must be on one NTFS volume. Throws <see cref="IOException"/> with the
    /// system's reason otherwise.
    /// </summary>
    public static void CreateHardLink(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        if (!CreateHardLinkNative(link, target, IntPtr.Zero))
            throw new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
    }

    /// <summary>
    /// Windows allows this only with Developer Mode on or with administrator rights;
    /// the <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> says so.
    /// </summary>
    public static void CreateSymbolicLink(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        File.CreateSymbolicLink(link, target);
    }
}
