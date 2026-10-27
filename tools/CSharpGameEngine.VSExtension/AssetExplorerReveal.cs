using System;
using System.Diagnostics;
using System.IO;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// Assets の右クリック「エクスプローラーで表示」（Windows のエクスプローラーを開く）。ファイルがあればそれを選択して開き、
/// なければ親フォルダ、選択がなければ現在のフォルダを、選択せずに開く。引数の組み立ては純粋関数で、起動だけ差し替えられる。
/// </summary>
internal static class AssetExplorerReveal {
    /// <summary>explorer.exe を起動する。テストで差し替える。引数は <see cref="Resolve(string?,string?)"/> の結果。</summary>
    internal static Action<string>Launch=static Arguments=>{ using var _=Process.Start(new ProcessStartInfo("explorer.exe",Arguments){ UseShellExecute=true }); };
    /// <summary>ファイルを選択して開く引数。</summary>
    internal static string SelectArguments(string FilePath)=>"/select,\""+Normalize(FilePath)+"\"";
    /// <summary>フォルダを選択せずに開く引数。</summary>
    internal static string FolderArguments(string Folder)=>"\""+Normalize(Folder)+"\"";
    internal static string Normalize(string PathValue)=>Path.GetFullPath(PathValue.Replace('/','\\'));
    internal static string?Resolve(string?FilePath,string?Folder)=>Resolve(FilePath,Folder,File.Exists,Directory.Exists);
    /// <summary>
    /// 開く引数を決める。<paramref name="FilePath"/> があればそれを選択する（なければ存在する親フォルダを選択せずに開く）。
    /// 空なら <paramref name="Folder"/> を開く。開けるものがなければ null。
    /// </summary>
    internal static string?Resolve(string?FilePath,string?Folder,Func<string,bool>FileExists,Func<string,bool>DirectoryExists){
        if(!string.IsNullOrEmpty(FilePath)){
            var Full=Normalize(FilePath!);
            if(FileExists(Full)){ return SelectArguments(Full); }
            var Parent=Path.GetDirectoryName(Full);
            return !string.IsNullOrEmpty(Parent)&&DirectoryExists(Parent)?FolderArguments(Parent):null;
        }
        if(string.IsNullOrEmpty(Folder)){ return null; }
        var FolderFull=Normalize(Folder!);
        return DirectoryExists(FolderFull)?FolderArguments(FolderFull):null;
    }
}
