using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KoikatsuSceneGallery.Helpers;
using Microsoft.Win32.SafeHandles;

namespace KoikatsuSceneGallery.Services;

internal enum LibraryCardMoveOutcome { Moved, AlreadyInPlace, Collision, DuplicateAtDestination, SourceMissing, Failed }

/// <param name="FinalPath">Where the card is now: the destination after a move, otherwise the source
/// (or, for <see cref="LibraryCardMoveOutcome.DuplicateAtDestination"/>, the identical file found there).</param>
internal sealed record LibraryCardMoveResult(LibraryCardMoveOutcome Outcome, string FinalPath, string? Error);

/// <summary>Test seams for <see cref="LibraryCardMover"/>. Production code passes none.</summary>
internal sealed class LibraryCardMoverHooks
{
    /// <summary>Overrides the volume check, so tests can drive the copy path on one disk.</summary>
    public Func<string, string, bool>? IsSameVolume { get; set; }
    /// <summary>Copy path only: after the temp copy is written, before it is verified.</summary>
    public Action<string>? AfterCopy { get; set; }
    /// <summary>Just before the file is put in place: the last point where nothing has changed.</summary>
    public Action? BeforeCommit { get; set; }
    /// <summary>Copy path only: after the copy is in place, before the source is deleted.</summary>
    public Action? BeforeSourceDelete { get; set; }
}

/// <summary>
/// Moves one card file inside the library, refusing anything that could lose
/// or overwrite a card.
/// </summary>
/// <remarks>
/// The guards, each of which leaves every file exactly as it was:
/// <list type="bullet">
/// <item>source and destination the same file, however spelled (case, <c>..</c>,
/// a hard link) → <see cref="LibraryCardMoveOutcome.AlreadyInPlace"/>;</item>
/// <item>something at the destination → <see cref="LibraryCardMoveOutcome.Collision"/>
/// or, with identical bytes, <see cref="LibraryCardMoveOutcome.DuplicateAtDestination"/>:
/// never overwritten, never renamed, and whether the source should go is the
/// caller's decision, not this class's;</item>
/// <item>every move and rename is without overwrite, so a file that appears at
/// the destination mid-move is still never replaced;</item>
/// <item>across volumes the card is copied to a temp name beside the
/// destination, checked by length and SHA-256, renamed into place, and only
/// then is the source deleted; if the source cannot be deleted or changed
/// meanwhile, the copy is removed again: one card, one place;</item>
/// <item>folders it created are removed again on failure, if still empty;</item>
/// <item>cancellation is honoured only up to the commit, and undoes what was done.</item>
/// </list>
/// Nothing here creates links: a link would turn a later delete of one name
/// into the loss of the card.
/// </remarks>
internal sealed class LibraryCardMover
{
    private readonly LibraryCardMoverHooks _hooks;

    public LibraryCardMover() : this(null) { }

    internal LibraryCardMover(LibraryCardMoverHooks? hooks) => _hooks = hooks ?? new LibraryCardMoverHooks();

    public Task<LibraryCardMoveResult> MoveAsync(string sourcePath, string destinationPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // File work is synchronous; keep it off the caller's (often the UI) thread.
        return Task.Run(() => Move(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), ct), ct);
    }

    /// <summary>Whether two full paths are on the same volume, by their roots.</summary>
    /// <remarks>
    /// A folder mounted inside another volume fools this; <see cref="File.Move(string, string, bool)"/>
    /// then copies and deletes by itself, still without overwriting.
    /// </remarks>
    internal static bool AreOnSameVolume(string a, string b)
        => string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), PathComparison.Comparison);

    private LibraryCardMoveResult Move(string source, string destination, CancellationToken ct)
    {
        if (string.Equals(source, destination, PathComparison.Comparison))
            return new(LibraryCardMoveOutcome.AlreadyInPlace, source, null);
        if (!File.Exists(source))
            return new(LibraryCardMoveOutcome.SourceMissing, source, null);

        if (Directory.Exists(destination))
            return new(LibraryCardMoveOutcome.Collision, source, null);
        if (File.Exists(destination))
        {
            try
            {
                if (AreSameFile(source, destination))
                    return new(LibraryCardMoveOutcome.AlreadyInPlace, source, null);
                return HaveSameBytes(source, destination, ct)
                    ? new(LibraryCardMoveOutcome.DuplicateAtDestination, destination, null)
                    : new(LibraryCardMoveOutcome.Collision, source, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new(LibraryCardMoveOutcome.Failed, source, ex.Message);
            }
        }

        var created = new List<string>();
        string? temp = null;
        try
        {
            var directory = Path.GetDirectoryName(destination)!;
            // Deepest first, which is also the order to remove them in.
            for (var d = directory; d is not null && !Directory.Exists(d); d = Path.GetDirectoryName(d))
                created.Add(d);
            Directory.CreateDirectory(directory);

            var sameVolume = _hooks.IsSameVolume?.Invoke(source, destination) ?? AreOnSameVolume(source, destination);
            if (sameVolume)
            {
                _hooks.BeforeCommit?.Invoke();
                ct.ThrowIfCancellationRequested();
                File.Move(source, destination, overwrite: false);
                return new(LibraryCardMoveOutcome.Moved, destination, null);
            }

            var before = new FileInfo(source);
            var (length, written) = (before.Length, before.LastWriteTimeUtc);
            temp = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.moving");
            File.Copy(source, temp, overwrite: false);
            _hooks.AfterCopy?.Invoke(temp);
            // HaveSameBytes checks the token before reading.
            if (new FileInfo(temp).Length != length || !HaveSameBytes(source, temp, ct))
                return Undo(new(LibraryCardMoveOutcome.Failed, source, "The copy did not match the original."));

            _hooks.BeforeCommit?.Invoke();
            ct.ThrowIfCancellationRequested();
            File.Move(temp, destination, overwrite: false);
            temp = null;

            // Past the commit: from here a failure takes the copy away again
            // rather than leave the card in two places.
            try
            {
                _hooks.BeforeSourceDelete?.Invoke();
                var now = new FileInfo(source);
                if (now.Length != length || now.LastWriteTimeUtc != written)
                    throw new IOException("The card changed while it was being copied.");
                File.Delete(source);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryDelete(destination);
                return Undo(new(LibraryCardMoveOutcome.Failed, source, ex.Message));
            }
            return new(LibraryCardMoveOutcome.Moved, destination, null);
        }
        catch (OperationCanceledException)
        {
            Undo(null);
            throw;
        }
        catch (IOException) when (File.Exists(destination) && File.Exists(source))
        {
            // Something appeared at the destination after the checks above.
            // This class never writes the destination before the commit, so
            // whatever is there belongs to someone else.
            return Undo(new(LibraryCardMoveOutcome.Collision, source, null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Undo(new(LibraryCardMoveOutcome.Failed, source, ex.Message));
        }

        LibraryCardMoveResult Undo(LibraryCardMoveResult? result)
        {
            if (temp is not null) TryDelete(temp);
            foreach (var directory in created)
            {
                try
                {
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                        Directory.Delete(directory);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Best effort; a folder with anything in it is never removed.
                }
            }
            return result!;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool HaveSameBytes(string a, string b, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        using var streamA = File.OpenRead(a);
        using var streamB = File.OpenRead(b);
        return SHA256.HashData(streamA).AsSpan().SequenceEqual(SHA256.HashData(streamB));
    }

    /// <summary>
    /// Whether two existing paths name the same file: a hard link, a junction
    /// in the path or a short name all spell one file differently.
    /// </summary>
    private static bool AreSameFile(string a, string b)
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var handleA = File.OpenHandle(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var handleB = File.OpenHandle(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Identity(handleA) == Identity(handleB);
    }

    private static (uint Volume, uint High, uint Low) Identity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info))
            throw new IOException("Could not read the file's identity.", new Win32Exception(Marshal.GetLastWin32Error()));
        return (info.VolumeSerialNumber, info.FileIndexHigh, info.FileIndexLow);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);
}
