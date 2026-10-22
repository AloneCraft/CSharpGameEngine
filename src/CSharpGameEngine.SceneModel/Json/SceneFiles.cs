using System;
using System.Collections.Generic;

namespace CSharpGameEngine.SceneAnalysis.Json;

/// <summary>
/// Which files are scenes (ADR-0070, revised 2026-10-01 at the user's request): every <c>*.json</c>
/// under the project's <c>Scenes</c> folder, at any depth. The build includes exactly these
/// (<c>Scenes\**\*.json</c>); the generator decides with the build's ProjectDir and the Visual Studio
/// extension with the nearest project folder, both through this one rule. A scene file needs no
/// special extension, and JSON outside <c>Scenes</c> (appsettings.json, assets) is never a scene.
/// A plugin's <c>Scenes</c> folder holds scenes too (ADR-0079): a project plugin's
/// (<c>Plugins\&lt;Name&gt;\Scenes</c>) by this rule alone, and an engine or package plugin's through the plugin roots the build passes.
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
            // Plugins/<Name>/Scenes/...: exactly one folder between Plugins and Scenes.
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
/// Paths the build passes to the generator (ADR-0079). MSBuild properties reach the generator through a generated .editorconfig whose
/// reader cuts a value at <c>#</c> or <c>;</c> (a comment), so a game under <c>C:\C#\</c> lost its ProjectDir and built without its scenes
/// and Assets catalog. The build writes <c>#</c> as <c>?h</c> and <c>;</c> as <c>?s</c> (<c>?</c> cannot occur in a Windows path).
/// </summary>
internal static class GeneratorPath {
    internal static string Encode(string Path)=>Path.Replace("#","?h").Replace(";","?s");
    internal static string Decode(string Value)=>Value.Replace("?s",";").Replace("?h","#");
}

/// <summary>
/// An enabled plugin's name and folder (ADR-0079). The build passes them to the generator as the <c>CgePluginRoots</c> property,
/// <c>Name=Folder</c> pairs separated by <c>|</c>. Not <c>;</c>: the property reaches the generator through an .editorconfig, whose
/// reader drops everything from a <c>;</c> or <c>#</c> on as a comment (the first plugin package check saw an empty value).
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
