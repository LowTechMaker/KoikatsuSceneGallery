using System.Runtime.InteropServices;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Sends a file to the recycle bin.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="File.Delete"/>. This is the first thing in the
/// app that destroys a card the user collected, and the whole point of the
/// affordance is cleaning up something that turned out to be redundant — a
/// judgement they may want back. The recycle bin is that undo.
/// </remarks>
internal static partial class RecycleBin
{
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperationW(ref ShFileOpStruct operation);

    /// <summary>
    /// Moves <paramref name="path"/> to the recycle bin. False when the file is
    /// gone, in use, or the shell refused; the caller reports that rather than
    /// pretending the card was removed.
    /// </summary>
    public static bool TryDelete(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        // The shell reads pFrom as a list, so it needs a second terminator on
        // top of the one the marshaller adds.
        var operation = new ShFileOpStruct
        {
            wFunc = FO_DELETE,
            pFrom = Path.GetFullPath(path) + '\0',
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };

        try
        {
            return SHFileOperationW(ref operation) == 0
                && !operation.fAnyOperationsAborted
                && !File.Exists(path);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
