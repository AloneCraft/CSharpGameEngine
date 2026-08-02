using System;
using System.Runtime.InteropServices;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Resolves well-known shell folders not exposed by <see cref="Environment.SpecialFolder"/> on
/// .NET Framework (notably Downloads). Used by <see cref="FabDownloadWatcher"/> to find the OS's
/// actual default downloads folder rather than guessing <c>%USERPROFILE%\Downloads</c> (which is
/// wrong when the user has relocated it).
/// </summary>
internal static class KnownFolders
{
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr pszPath);

    public static string GetDownloadsFolder()
    {
        int hr = SHGetKnownFolderPath(DownloadsFolderId, 0, IntPtr.Zero, out IntPtr pathPtr);
        if (hr != 0)
        {
            throw new InvalidOperationException($"SHGetKnownFolderPath(FOLDERID_Downloads) failed (0x{hr:X8}).");
        }

        try
        {
            return Marshal.PtrToStringUni(pathPtr)
                ?? throw new InvalidOperationException("SHGetKnownFolderPath returned a null path.");
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPtr);
        }
    }
}
