using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// エディターが操作するゲームプロジェクト (ADR-0065)。リポジトリに依存する RepoLocator を置き換える。ゲームプロジェクトは、<c>CSharpGameEngine.Game.props</c> をインポートする csproj（エンジンリポジトリ内の SourceMode）または <c>CSharpGameEngine.Sdk</c> パッケージを参照する csproj（場所を問わない PackageMode）である。
/// <list type="bullet">
/// <item><see cref="ProjectDirectory"/> は Scene アセットパス (<c>Assets/...</c>) とビルド出力の基準。</item>
/// <item><see cref="EngineRoot"/> は SourceMode ではエンジンリポジトリ、PackageMode では null。</item>
/// <item><see cref="WorkspaceRoot"/> は作成ブリッジを実行する場所。エンジンリポジトリがある場合はそこ（ツールをソースからビルド可能）、なければプロジェクトディレクトリ。</item>
/// </list>
/// ブリッジとホストは VSIX に同梱する。リポジトリのビルド出力は開発時の代替手段に限る。
/// </summary>
internal sealed class CgeProjectContext {
    internal const string SdkPackageId="CSharpGameEngine.Sdk";
    internal const string GamePropsFileName="CSharpGameEngine.Game.props";
    internal const string TargetFramework="net10.0-windows10.0.19041.0";
    internal const string RuntimeIdentifier="win-x64";
    private static readonly string[]IgnoredDirectories=["bin","obj",".git",".vs","node_modules",".artifacts"];
    private static readonly Regex GamePropsImport=new(@"<Import\s+Project\s*=\s*""(?<Path>[^""]*CSharpGameEngine\.Game\.props)""",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    private static readonly Regex SdkReference=new(@"<PackageReference\s+Include\s*=\s*""CSharpGameEngine\.Sdk""",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    private static readonly Regex AssemblyNameElement=new(@"<AssemblyName>\s*(?<Name>[^<\s]+)\s*</AssemblyName>",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);

    private CgeProjectContext(string ProjectFilePath,string?EngineRoot,string AssemblyName){
        this.ProjectFilePath=ProjectFilePath;
        this.ProjectDirectory=Path.GetDirectoryName(ProjectFilePath)!;
        this.EngineRoot=EngineRoot;
        this.AssemblyName=AssemblyName;
    }

    /// <summary>パッケージのソリューションイベントで設定される、開いているソリューション。開いていない場合は null。</summary>
    internal static string?SolutionPath { get; set; }

    internal string ProjectFilePath { get; }
    internal string ProjectDirectory { get; }
    internal string?EngineRoot { get; }
    internal string AssemblyName { get; }
    internal string WorkspaceRoot=>this.EngineRoot??this.ProjectDirectory;
    internal string AssetsDirectory=>Path.Combine(this.ProjectDirectory,"Assets");

    /// <summary><paramref name="Configuration"/> に対してプロジェクトがビルドするゲームアセンブリ (Game.props がフレームワークとランタイムを固定)。</summary>
    internal string OutputAssemblyPath(string Configuration)=>
        Path.Combine(this.ProjectDirectory,"bin",Configuration,TargetFramework,RuntimeIdentifier,this.AssemblyName+".dll");

    /// <summary>Scene の <c>Assets/...</c> など、プロジェクト相対パスをフルパスにしたもの。</summary>
    internal string ResolveProjectPath(string RelativePath)=>Path.GetFullPath(Path.Combine(this.ProjectDirectory,RelativePath));

    /// <summary>
    /// まず <paramref name="Hint"/>（プロジェクト内のファイルまたはディレクトリ）に対応するゲームプロジェクト、次に開いているソリューションのプロジェクト、最後にこの拡張機能をビルドしたリポジトリ内のプロジェクトを解決する。
    /// </summary>
    internal static CgeProjectContext?Resolve(string?Hint=null){
        if(!string.IsNullOrWhiteSpace(Hint)&&FromContainingDirectory(Hint!) is{ }Containing){
            return Containing;
        }
        var Solution=SolutionPath;
        if(!string.IsNullOrWhiteSpace(Solution)&&FromSearchRoot(Path.GetDirectoryName(Path.GetFullPath(Solution))!) is{ }FromSolution){
            return FromSolution;
        }
        var Repository=RepoLocator.ResolveRepoRoot();
        return Repository is null?null:FromSearchRoot(Repository);
    }

    /// <summary><paramref name="Path"/> と同じか、その上位にある最も近いゲームプロジェクト。</summary>
    internal static CgeProjectContext?FromContainingDirectory(string Path){
        var Directory=System.IO.Directory.Exists(Path)?Path:System.IO.Path.GetDirectoryName(Path);
        while(!string.IsNullOrEmpty(Directory)){
            foreach(var Project in System.IO.Directory.EnumerateFiles(Directory,"*.csproj").OrderBy(static Item=>Item,StringComparer.OrdinalIgnoreCase)){
                if(TryCreate(Project) is{ }Context){
                    return Context;
                }
            }
            Directory=System.IO.Path.GetDirectoryName(Directory);
        }
        return null;
    }

    /// <summary>
    /// <paramref name="Root"/> 以下（最大 4 階層）のゲームプロジェクト。複数ある場合、まず UserEcsProject（リポジトリの開発用ゲーム）を優先し、次にパスが最も浅いものを選ぶ。
    /// </summary>
    internal static CgeProjectContext?FromSearchRoot(string Root){
        List<CgeProjectContext>Found=[];
        Collect(Root,0,Found);
        return Found
            .OrderBy(static Context=>string.Equals(Context.AssemblyName,"UserEcsProject",StringComparison.OrdinalIgnoreCase)?0:1)
            .ThenBy(static Context=>Context.ProjectFilePath.Count(static Character=>Character==Path.DirectorySeparatorChar))
            .ThenBy(static Context=>Context.ProjectFilePath,StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>
    /// 開いているソリューション内（またはソリューションがなければこの拡張機能をビルドしたリポジトリ内）のすべてのゲームプロジェクト。<see cref="FromSearchRoot"/> と同じ方法で探し、検索を実行したフォルダーとともに返す。
    /// </summary>
    internal static IReadOnlyList<CgeProjectContext>FindAll(out string?Root){
        var Solution=SolutionPath;
        Root=!string.IsNullOrWhiteSpace(Solution)?Path.GetDirectoryName(Path.GetFullPath(Solution)):RepoLocator.ResolveRepoRoot();
        List<CgeProjectContext>Found=[];
        if(Root is not null){ Collect(Root,0,Found); }
        return Found;
    }

    private static void Collect(string Directory,int Depth,List<CgeProjectContext>Found){
        if(Depth>4||!System.IO.Directory.Exists(Directory)){
            return;
        }
        foreach(var Project in System.IO.Directory.EnumerateFiles(Directory,"*.csproj")){
            if(TryCreate(Project) is{ }Context){
                Found.Add(Context);
            }
        }
        foreach(var Child in System.IO.Directory.EnumerateDirectories(Directory)){
            if(!IgnoredDirectories.Contains(Path.GetFileName(Child),StringComparer.OrdinalIgnoreCase)){
                Collect(Child,Depth+1,Found);
            }
        }
    }

    /// <summary><paramref name="ProjectFilePath"/> がゲームプロジェクトの場合はそのコンテキスト。それ以外は null。</summary>
    internal static CgeProjectContext?TryCreate(string ProjectFilePath){
        string Text;
        try {
            Text=File.ReadAllText(ProjectFilePath);
        }
        catch(Exception Ex) when(Ex is IOException or UnauthorizedAccessException){
            return null;
        }
        var FullPath=Path.GetFullPath(ProjectFilePath);
        var AssemblyName=AssemblyNameElement.Match(Text) is{ Success:true }Name?Name.Groups["Name"].Value:Path.GetFileNameWithoutExtension(FullPath);
        var Import=GamePropsImport.Match(Text);
        if(Import.Success){
            var PropsPath=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(FullPath)!,Import.Groups["Path"].Value.Replace("$(MSBuildThisFileDirectory)",string.Empty)));
            var BuildDirectory=Path.GetDirectoryName(PropsPath);
            var EngineRoot=BuildDirectory is null?null:Path.GetDirectoryName(BuildDirectory);
            return new CgeProjectContext(FullPath,EngineRoot is not null&&File.Exists(PropsPath)?EngineRoot:null,AssemblyName);
        }
        return SdkReference.IsMatch(Text)?new CgeProjectContext(FullPath,null,AssemblyName):null;
    }
}
