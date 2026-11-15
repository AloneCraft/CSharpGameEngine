using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>Visual Studio側が読む、Component icon 属性の安定した完全修飾名。</summary>
internal static class SceneComponentIconMetadata {
    internal const string AttributeFullyQualifiedName="global::CSharpGameEngine.Entities.SceneComponentIconAttribute";

    /// <summary>
    /// コンポーネント自身が宣言した値を返す。<see cref="ProjectIconPath.TryResolve"/> が現在のプロジェクトルートに対して検査するまでは、結果をファイルパスとして信頼しない。
    /// </summary>
    internal static string?TryGetDeclaredProjectRelativePath(INamedTypeSymbol Type){
        foreach(var Attribute in Type.GetAttributes()){
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
/// 再利用可能なアイコンの説明。Hierarchy の行と C# エディターのグリフは、画像が Visual Studio カタログ由来かプロジェクトアセット由来かを知らずに、このモデルを描画できる。現在 <see cref="HasReservedLockOverlay"/> は意図的に false。ここに保持することで、後の作成ロック機能に向けて一貫したオーバーレイ領域を確保する。
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

    /// <summary>Hierarchy の行とエディターのグリフが共有する、アクセシビリティ対応のツールチップテキスト。</summary>
    public string ToolTip=>this.ProjectRelativePath??VsixText.Get("ComponentIcon_DefaultIcon");

    internal static SceneVisualIcon ComponentDefault()=>new(KnownMonikers.Component,null,null);

    internal static SceneVisualIcon ProjectAsset(string ProjectRelativePath,string AbsolutePath)=>
        new(KnownMonikers.Component,ProjectRelativePath,AbsolutePath);
}

/// <summary>
/// 属性値とピッカーの検証境界。属性値はプロジェクト相対パスに限り、ルート付きパス、親ディレクトリへの移動、画像以外の拡張子は画像ローダーに到達できない。
/// </summary>
internal static class ProjectIconPath {
    internal static bool TryNormalize(string? Value,out string Normalized){
        Normalized=string.Empty;
        if(string.IsNullOrWhiteSpace(Value)){
            return false;
        }

        var Candidate=Value!.Trim().Replace('\\','/');
        if(Path.IsPathRooted(Candidate)||Candidate.IndexOf(':')>=0){
            return false;
        }

        var Segments=Candidate.Split(new[]{'/'},StringSplitOptions.RemoveEmptyEntries);
        if(Segments.Length==0
            ||Segments.Any(static Segment=>string.Equals(Segment,".",StringComparison.Ordinal)
                ||string.Equals(Segment,"..",StringComparison.Ordinal)
                ||Segment.IndexOfAny(Path.GetInvalidFileNameChars())>=0)){
            return false;
        }

        var Extension=Path.GetExtension(Segments[Segments.Length-1]);
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

/// <summary>現在の Roslyn プロジェクトで選択可能な SVG/PNG アセット1件。</summary>
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
/// SDK スタイルのプロジェクトの物理ルート以下にある画像ファイルを列挙します。プロジェクトファイルに含まれないファイルも既定で <c>None</c> として含まれます。生成物や任意のマシンローカル出力が選択されないよう、build/intermediate ルートは除外します。各結果は UI に渡す前に <see cref="ProjectIconPath"/> を通します。
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
            // プロジェクトにアクセスできないディレクトリが含まれている場合があります。そのディレクトリは画像候補ではなく、
            // プロジェクト内のほかのファイルを選択できなくすることもありません。
        } catch(IOException){
            // ピッカーを開いている間のファイル変更は問題ありません。次に開いたときに更新されます。
        }

        return Assets
            .OrderBy(static Asset=>Asset.ProjectRelativePath,StringComparer.OrdinalIgnoreCase)
            .ThenBy(static Asset=>Asset.ProjectRelativePath,StringComparer.Ordinal)
            .ToArray();
    }

    private static void EnumerateDirectory(string Root,string DirectoryPath,List<ProjectIconAsset>Assets){
        foreach(var ChildDirectory in Directory.EnumerateDirectories(DirectoryPath)){
            var Name=Path.GetFileName(ChildDirectory);
            if(ExcludedDirectories.Contains(Name)){
                continue;
            }

            EnumerateDirectory(Root,ChildDirectory,Assets);
        }

        foreach(var FilePath in Directory.EnumerateFiles(DirectoryPath)){
            if(!ProjectIconPath.TryCreateRelative(Root,FilePath,out string RelativePath)){
                continue;
            }

            Assets.Add(new ProjectIconAsset(RelativePath,FilePath));
        }
    }
}

/// <summary>例外をスローせず、フォールバックを使いながらコンポーネントの検証済み視覚表現を構築します。</summary>
internal static class SceneComponentIconResolver {
    internal static SceneVisualIcon Resolve(INamedTypeSymbol Type,string?ProjectDirectory){
        var DeclaredPath=SceneComponentIconMetadata.TryGetDeclaredProjectRelativePath(Type);
        if(DeclaredPath is null){
            return SceneVisualIcon.ComponentDefault();
        }

        string?DeclaringProjectDirectory=SceneComponentProjectLocator.Resolve(Type,ProjectDirectory);
        return DeclaringProjectDirectory is not null
            &&ProjectIconPath.TryResolve(DeclaringProjectDirectory,DeclaredPath,out string RelativePath,out string AbsolutePath)
            ?SceneVisualIcon.ProjectAsset(RelativePath,AbsolutePath)
            :SceneVisualIcon.ComponentDefault();
    }
}

internal static class SceneComponentProjectLocator {
    private static readonly Dictionary<string,string?>Cache=new(StringComparer.OrdinalIgnoreCase);

    internal static string? Resolve(INamedTypeSymbol Type,string?FallbackProjectDirectory){
        var SourceLocation=Type.Locations.FirstOrDefault(static Location=>Location.IsInSource);
        var SourcePath=SourceLocation?.SourceTree?.FilePath;
        if(!string.IsNullOrWhiteSpace(SourcePath)){
            return FindProjectDirectory(SourcePath!);
        }

        if(SourceLocation is not null){
            return FallbackProjectDirectory;
        }

        var AssemblyName=Type.ContainingAssembly?.Name??string.Empty;
        if(AssemblyName.Length==0||FallbackProjectDirectory is null){
            return null;
        }

        string?Root=FindSolutionRoot(FallbackProjectDirectory);
        var CacheKey=AssemblyName+"|"+(Root??FallbackProjectDirectory);
        lock(Cache){
            if(Cache.TryGetValue(CacheKey,out string?Cached)){
                return Cached;
            }
        }

        string?Resolved=Root is null?null:FindProjectByAssemblyName(Root,AssemblyName);
        lock(Cache){
            Cache[CacheKey]=Resolved;
        }

        return Resolved;
    }

    private static string? FindProjectDirectory(string FilePath){
        try {
            var Current=Directory.GetParent(FilePath);
            while(Current is not null){
                if(Current.EnumerateFiles("*.csproj",SearchOption.TopDirectoryOnly).Any()){
                    return Current.FullName;
                }

                Current=Current.Parent;
            }
        } catch(IOException){
        } catch(UnauthorizedAccessException){
        }

        return null;
    }

    private static string? FindSolutionRoot(string ProjectDirectory){
        try {
            var Current=Directory.Exists(ProjectDirectory)
                           ?new DirectoryInfo(ProjectDirectory)
                           :Directory.GetParent(ProjectDirectory);
            while(Current is not null){
                if(Current.EnumerateFiles("*.slnx",SearchOption.TopDirectoryOnly).Any()
                    ||Current.EnumerateFiles("*.sln",SearchOption.TopDirectoryOnly).Any()){
                    return Current.FullName;
                }

                Current=Current.Parent;
            }
        } catch(IOException){
        } catch(UnauthorizedAccessException){
        }

        return null;
    }

    private static string? FindProjectByAssemblyName(string Root,string AssemblyName){
        try {
            foreach(var ProjectFile in Directory.EnumerateFiles(Root,"*.csproj",SearchOption.AllDirectories)){
                if(ProjectFile.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)
                    .Any(static Segment=>string.Equals(Segment,"bin",StringComparison.OrdinalIgnoreCase)
                        ||string.Equals(Segment,"obj",StringComparison.OrdinalIgnoreCase))){
                    continue;
                }

                if(string.Equals(Path.GetFileNameWithoutExtension(ProjectFile),AssemblyName,StringComparison.OrdinalIgnoreCase)){
                    return Path.GetDirectoryName(ProjectFile);
                }

                var ProjectText=File.ReadAllText(ProjectFile);
                var Match=Regex.Match(ProjectText,@"<AssemblyName>\s*(?<Name>[^<]+?)\s*</AssemblyName>",
                                      RegexOptions.IgnoreCase);
                if(Match.Success&&string.Equals(Match.Groups["Name"].Value.Trim(),AssemblyName,StringComparison.OrdinalIgnoreCase)){
                    return Path.GetDirectoryName(ProjectFile);
                }
            }
        } catch(IOException){
        } catch(UnauthorizedAccessException){
        }

        return null;
    }
}
