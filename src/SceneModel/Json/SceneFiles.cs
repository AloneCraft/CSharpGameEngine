using System;
using System.Collections.Generic;

namespace CSharpGameEngine.SceneAnalysis.Json;

/// <summary>
/// どのファイルを Scene として扱うか（ADR-0070、ユーザー要望により2026-10-01改訂）。プロジェクトの <c>Scenes</c> フォルダー以下、深さを問わず、すべての
/// <c>*.json</c> を対象とする。ビルドはこのファイルだけを含める（<c>Scenes\**\*.json</c>）。ソースジェネレーターはビルドの ProjectDir、Visual
/// Studio 拡張機能は最も近いプロジェクトフォルダーを使うが、どちらもこの単一の規則で判断する。Scene ファイルに専用の拡張子は不要。<c>Scenes</c> の外にある JSON（
/// appsettings.json、アセットなど）は Scene として扱わない。プラグイン内の <c>Scenes</c> フォルダーも対象（ADR-0079）。プロジェクトプラグインでは、この規則だ
/// けで <c>Plugins\&lt;Name&gt;\Scenes</c> を対象にする。エンジンまたはパッケージのプラグインでは、ビルドから渡されるプラグインルートを通じて対象にする。
/// </summary>
internal static class SceneFiles {
    internal const string FolderName="Scenes";
    internal const string Extension=".json";
    internal const string PluginsFolderName="Plugins";

    internal static bool IsScene(string?FilePath,string?ProjectDirectory,IReadOnlyList<PluginRoot>?PluginRoots=null){
        if(string.IsNullOrEmpty(FilePath)||!FilePath!.EndsWith(Extension,StringComparison.OrdinalIgnoreCase)){
            return false;
        }
        string File=Normalize(FilePath);
        if(!string.IsNullOrEmpty(ProjectDirectory)){
            string Project=Normalize(ProjectDirectory!).TrimEnd('/')+"/";
            if(IsUnder(File,Project+FolderName+"/")){ return true; }
            // Plugins/<Name>/Scenes/...：Plugins と Scenes の間にはフォルダーが1つだけある。
            string Plugins=Project+PluginsFolderName+"/";
            if(File.StartsWith(Plugins,StringComparison.OrdinalIgnoreCase)){
                var NameEnd=File.IndexOf('/',Plugins.Length);
                if(NameEnd>Plugins.Length&&IsUnder(File,File.Substring(0,NameEnd+1)+FolderName+"/")){ return true; }
            }
        }
        if(PluginRoots is not null){
            foreach(var Root in PluginRoots){
                if(IsUnder(File,Normalize(Root.Directory).TrimEnd('/')+"/"+FolderName+"/")){ return true; }
            }
        }
        return false;
    }

    private static bool IsUnder(string File,string Folder)=>
        File.Length>Folder.Length+Extension.Length-1&&File.StartsWith(Folder,StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string Path)=>Path.Replace('\\','/');
}

/// <summary>
/// ビルドがジェネレーターに渡すパス（ADR-0079）。MSBuild のプロパティは、生成される .editorconfig を経由してジェネレーターに渡す。.editorconfig の読み込み側は
/// コメントとして <c>#</c> または <c>;</c> 以降を切り捨てるため、<c>C:\C#\</c> の下にあるゲームでは ProjectDir が欠落し、Scene と Assets
/// カタログなしでビルドされていた。ビルドでは <c>#</c> を <c>?h</c>、<c>;</c> を <c>?s</c> として書き出す（Windows パスに <c>?</c> は使えない）。
/// </summary>
internal static class GeneratorPath {
    internal static string Encode(string Path)=>Path.Replace("#","?h").Replace(";","?s");
    internal static string Decode(string Value)=>Value.Replace("?s",";").Replace("?h","#");
}

/// <summary>
/// 有効なプラグインの名前とフォルダー（ADR-0079）。ビルドはこれらを <c>CgePluginRoots</c> プロパティとしてジェネレーターへ渡す。形式は <c>Name=Folder</c>
///  の組を <c>|</c> で区切る。<c>;</c> は使わない。.editorconfig 経由でジェネレーターに届くプロパティは、読み込み側が <c>;</c> または <c>#</c> 以降を
/// コメントとして削除するため（最初のプラグインパッケージ検査では空の値になった）。
/// </summary>
internal readonly struct PluginRoot(string Name,string Directory) {
    internal const char Separator='|';
    internal readonly string Name=Name;
    internal readonly string Directory=Directory;

    internal static IReadOnlyList<PluginRoot>Parse(string?Value){
        List<PluginRoot>Roots=[];
        if(string.IsNullOrEmpty(Value)){ return Roots; }
        foreach(var Entry in Value!.Split(Separator)){
            var Separator=Entry.IndexOf('=');
            if(Separator<=0||Separator==Entry.Length-1){ continue; }
            Roots.Add(new PluginRoot(Entry.Substring(0,Separator).Trim(),GeneratorPath.Decode(Entry.Substring(Separator+1).Trim())));
        }
        return Roots;
    }
}
