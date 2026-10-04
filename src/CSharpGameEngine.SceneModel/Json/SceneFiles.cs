using System;

namespace CSharpGameEngine.SceneAnalysis.Json;

/// <summary>
/// Which files are scenes (ADR-0070, revised 2026-10-01 at the user's request): every <c>*.json</c>
/// under the project's <c>Scenes</c> folder, at any depth. The build includes exactly these
/// (<c>Scenes\**\*.json</c>); the generator decides with the build's ProjectDir and the Visual Studio
/// extension with the nearest project folder, both through this one rule. A scene file needs no
/// special extension, and JSON outside <c>Scenes</c> (appsettings.json, assets) is never a scene.
/// </summary>
internal static class SceneFiles {
    internal const string FolderName="Scenes";
    internal const string Extension=".json";

    internal static bool IsScene(string?FilePath,string?ProjectDirectory){
        if(string.IsNullOrEmpty(FilePath)||string.IsNullOrEmpty(ProjectDirectory)
            ||!FilePath!.EndsWith(Extension,StringComparison.OrdinalIgnoreCase)){
            return false;
        }
        string File=Normalize(FilePath);
        string Folder=Normalize(ProjectDirectory!).TrimEnd('/')+"/"+FolderName+"/";
        return File.Length>Folder.Length+Extension.Length-1
            &&File.StartsWith(Folder,StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string Path)=>Path.Replace('\\','/');
}
