using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>Visual Studio側が読む、Component icon 属性の安定した完全修飾名。</summary>
internal static class SceneComponentIconMetadata {
    internal const string AttributeFullyQualifiedName="global::CSharpGameEngine.Entities.SceneComponentIconAttribute";

    /// <summary>
    /// Returns the value declared by the component itself.  The result is deliberately not trusted
    /// as a file path until <see cref="ProjectIconPath.TryResolve"/> has checked it against the
    /// current project root.
    /// </summary>
    internal static string?TryGetDeclaredProjectRelativePath(INamedTypeSymbol Type){
        foreach(AttributeData Attribute in Type.GetAttributes()){
            if(!string.Equals(
                Attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                AttributeFullyQualifiedName,StringComparison.Ordinal)){
                continue;
            }

            if(Attribute.ConstructorArguments.Length==1
                &&Attribute.ConstructorArguments[0].Value is string Path){
                return Path;
            }
        }

        return null;
    }
}

/// <summary>
/// A reusable icon description.  Hierarchy rows and C# editor glyphs can both render this model
/// without knowing whether the image came from the Visual Studio catalog or a project asset.
/// <see cref="HasReservedLockOverlay"/> is intentionally false today; keeping it here reserves the
/// consistent overlay slot for a later authoring-lock feature.
/// </summary>
public sealed class SceneVisualIcon {
    private SceneVisualIcon(ImageMoniker Moniker,string?ProjectRelativePath,string?AbsolutePath){
        this.Moniker=Moniker;
        this.ProjectRelativePath=ProjectRelativePath;
        this.AbsolutePath=AbsolutePath;
    }

    public ImageMoniker Moniker { get; }

    public string? ProjectRelativePath { get; }

    public string? AbsolutePath { get; }

    public bool IsProjectAsset=>this.AbsolutePath is not null;

    public bool HasReservedLockOverlay=>false;

    /// <summary>Accessible tooltip text shared by hierarchy rows and editor glyphs.</summary>
    public string ToolTip=>this.ProjectRelativePath??"Component";

    internal static SceneVisualIcon ComponentDefault()=>new(KnownMonikers.Component,null,null);

    internal static SceneVisualIcon ProjectAsset(string ProjectRelativePath,string AbsolutePath)=>
        new(KnownMonikers.Component,ProjectRelativePath,AbsolutePath);
}

/// <summary>
/// Validation boundary for the attribute and picker.  Attribute values are project-relative only;
/// no rooted path, parent traversal, or non-image extension can reach the image loader.
/// </summary>
internal static class ProjectIconPath {
    internal static bool TryNormalize(string? Value,out string Normalized){
        Normalized=string.Empty;
        if(string.IsNullOrWhiteSpace(Value)){
            return false;
        }

        string Candidate=Value!.Trim().Replace('\\','/');
        if(Path.IsPathRooted(Candidate)||Candidate.IndexOf(':')>=0){
            return false;
        }

        string[]Segments=Candidate.Split(new[]{'/'},StringSplitOptions.RemoveEmptyEntries);
        if(Segments.Length==0
            ||Segments.Any(static Segment=>string.Equals(Segment,".",StringComparison.Ordinal)
                ||string.Equals(Segment,"..",StringComparison.Ordinal)
                ||Segment.IndexOfAny(Path.GetInvalidFileNameChars())>=0)){
            return false;
        }

        string Extension=Path.GetExtension(Segments[Segments.Length-1]);
        if(!string.Equals(Extension,".svg",StringComparison.OrdinalIgnoreCase)
            &&!string.Equals(Extension,".png",StringComparison.OrdinalIgnoreCase)){
            return false;
        }

        Normalized=string.Join("/",Segments);
        return true;
    }

    internal static bool TryResolve(string ProjectDirectory,string? Value,out string Normalized,out string AbsolutePath){
        Normalized=string.Empty;
        AbsolutePath=string.Empty;
        if(string.IsNullOrWhiteSpace(ProjectDirectory)||!TryNormalize(Value,out Normalized)){
            return false;
        }

        string Root;
        try {
            Root=Path.GetFullPath(ProjectDirectory)
                .TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)
                +Path.DirectorySeparatorChar;
            AbsolutePath=Path.GetFullPath(Path.Combine(Root,Normalized.Replace('/',Path.DirectorySeparatorChar)));
        } catch(ArgumentException){
            return false;
        } catch(NotSupportedException){
            return false;
        } catch(PathTooLongException){
            return false;
        }

        if(!AbsolutePath.StartsWith(Root,StringComparison.OrdinalIgnoreCase)||!File.Exists(AbsolutePath)){
            AbsolutePath=string.Empty;
            return false;
        }

        return true;
    }

    internal static bool TryCreateRelative(string ProjectDirectory,string AbsolutePath,out string Normalized){
        Normalized=string.Empty;
        if(string.IsNullOrWhiteSpace(ProjectDirectory)||string.IsNullOrWhiteSpace(AbsolutePath)){
            return false;
        }

        string Root;
        string FullPath;
        try {
            Root=Path.GetFullPath(ProjectDirectory)
                .TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)
                +Path.DirectorySeparatorChar;
            FullPath=Path.GetFullPath(AbsolutePath);
        } catch(ArgumentException){
            return false;
        } catch(NotSupportedException){
            return false;
        } catch(PathTooLongException){
            return false;
        }

        if(!FullPath.StartsWith(Root,StringComparison.OrdinalIgnoreCase)){
            return false;
        }

        return TryNormalize(FullPath.Substring(Root.Length).Replace(Path.DirectorySeparatorChar,'/'),out Normalized);
    }
}

/// <summary>One selectable SVG/PNG asset in the current Roslyn project.</summary>
internal sealed class ProjectIconAsset {
    internal ProjectIconAsset(string ProjectRelativePath,string AbsolutePath){
        this.ProjectRelativePath=ProjectRelativePath;
        this.AbsolutePath=AbsolutePath;
    }

    internal string ProjectRelativePath { get; }

    internal string AbsolutePath { get; }

    internal string DisplayName=>this.ProjectRelativePath;
}

/// <summary>
/// Lists image files beneath an SDK-style project's physical root.  Project files include unmatched
/// files as <c>None</c> by default; build/intermediate roots are excluded so generated or arbitrary
/// machine-local output can never be selected.  Every result passes <see cref="ProjectIconPath"/>
/// before it reaches the UI.
/// </summary>
internal static class ProjectIconAssetCatalog {
    private static readonly HashSet<string>ExcludedDirectories=new(StringComparer.OrdinalIgnoreCase){
        ".git",".vs","bin","obj","packages",
    };

    internal static IReadOnlyList<ProjectIconAsset>Enumerate(string?ProjectDirectory){
        if(string.IsNullOrWhiteSpace(ProjectDirectory)||!Directory.Exists(ProjectDirectory)){
            return [];
        }

        List<ProjectIconAsset>Assets=[];
        try {
            EnumerateDirectory(Path.GetFullPath(ProjectDirectory),Path.GetFullPath(ProjectDirectory),Assets);
        } catch(UnauthorizedAccessException){
            // A project may contain an inaccessible directory.  It is not an image candidate and
            // must not prevent the rest of the project from being selectable.
        } catch(IOException){
            // File changes while the picker is opening are benign; a later opening will refresh.
        }

        return Assets
            .OrderBy(static Asset=>Asset.ProjectRelativePath,StringComparer.OrdinalIgnoreCase)
            .ThenBy(static Asset=>Asset.ProjectRelativePath,StringComparer.Ordinal)
            .ToArray();
    }

    private static void EnumerateDirectory(string Root,string DirectoryPath,List<ProjectIconAsset>Assets){
        foreach(string ChildDirectory in Directory.EnumerateDirectories(DirectoryPath)){
            string Name=Path.GetFileName(ChildDirectory);
            if(ExcludedDirectories.Contains(Name)){
                continue;
            }

            EnumerateDirectory(Root,ChildDirectory,Assets);
        }

        foreach(string FilePath in Directory.EnumerateFiles(DirectoryPath)){
            if(!ProjectIconPath.TryCreateRelative(Root,FilePath,out string RelativePath)){
                continue;
            }

            Assets.Add(new ProjectIconAsset(RelativePath,FilePath));
        }
    }
}

/// <summary>Builds a validated visual description for a component, falling back without throwing.</summary>
internal static class SceneComponentIconResolver {
    internal static SceneVisualIcon Resolve(INamedTypeSymbol Type,string?ProjectDirectory){
        // Referenced assemblies and metadata do not have an editable project declaration in this
        // workspace. Even if they happen to contain an attribute whose text matches a local image
        // path, never let that turn into a project-authored visual: they retain the stable Visual
        // Studio Component moniker and have no authoring UI.
        if(!Type.Locations.Any(static Location=>Location.IsInSource)){
            return SceneVisualIcon.ComponentDefault();
        }

        string?DeclaredPath=SceneComponentIconMetadata.TryGetDeclaredProjectRelativePath(Type);
        return ProjectDirectory is not null
            &&ProjectIconPath.TryResolve(ProjectDirectory,DeclaredPath,out string RelativePath,out string AbsolutePath)
            ?SceneVisualIcon.ProjectAsset(RelativePath,AbsolutePath)
            :SceneVisualIcon.ComponentDefault();
    }
}
