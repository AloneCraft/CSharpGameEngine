using System;
using System.Runtime.InteropServices;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// .NET Framework の <see cref="Environment.SpecialFolder"/> では公開されない既知のシェルフォルダー（特に Downloads）を解決します。ユーザーが別の場所に移動した場合は誤ったパスとなる <c>%USERPROFILE%\Downloads</c> を推測せず、OS の実際の既定ダウンロードフォルダーを特定するために <see cref="FabDownloadWatcher"/> が使用します。
/// </summary>
internal static class KnownFolders {
    private static readonly Guid DownloadsFolderId=new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)]Guid Rfid,uint DwFlags,IntPtr HToken,out IntPtr PszPath);

    public static string GetDownloadsFolder(){
        var Hr=SHGetKnownFolderPath(DownloadsFolderId,0,IntPtr.Zero,out var PathPtr);
        if(Hr!=0){
            throw new InvalidOperationException($"SHGetKnownFolderPath(FOLDERID_Downloads) failed (0x{Hr:X8}).");
        }

        try {
            return Marshal.PtrToStringUni(PathPtr)
                ??throw new InvalidOperationException("SHGetKnownFolderPath returned a null path.");
        } finally {
            Marshal.FreeCoTaskMem(PathPtr);
        }
    }
}
