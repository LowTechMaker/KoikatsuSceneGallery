using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KoikatsuSceneGallery.IntegrationTests;

// A synchronous thread-local activation scope: never hold it across an await.
// VSTest lacks the application's embedded WinRT manifest. No machine registration occurs.
internal static class DispatcherActivation
{
    public static T Run<T>(Func<T> action)
    {
        var context = new ActivationContext
        {
            Size = (uint)Marshal.SizeOf<ActivationContext>(),
            Flags = 4, // ACTCTX_FLAG_ASSEMBLY_DIRECTORY_VALID
            Source = Path.Combine(AppContext.BaseDirectory, "DispatcherQueue.manifest"),
            AssemblyDirectory = AppContext.BaseDirectory
        };
        var handle = CreateActCtxW(ref context);
        if (handle == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!ActivateActCtx(handle, out var cookie)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { return action(); }
            finally
            {
                if (!DeactivateActCtx(0, cookie)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally { ReleaseActCtx(handle); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ActivationContext
    {
        public uint Size;
        public uint Flags;
        public string Source;
        public ushort Architecture;
        public ushort Language;
        public string AssemblyDirectory;
        public IntPtr ResourceName;
        public IntPtr ApplicationName;
        public IntPtr Module;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateActCtxW(ref ActivationContext context);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ActivateActCtx(IntPtr handle, out UIntPtr cookie);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeactivateActCtx(uint flags, UIntPtr cookie);
    [DllImport("kernel32.dll")]
    private static extern void ReleaseActCtx(IntPtr handle);
}
