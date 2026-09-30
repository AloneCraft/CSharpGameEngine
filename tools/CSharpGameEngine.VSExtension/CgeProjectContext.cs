using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// The game project the editor works on (ADR-0065), replacing the repository-bound RepoLocator. A
/// game project is a csproj that imports <c>CSharpGameEngine.Game.props</c> (SourceMode, inside the
/// engine repository) or references the <c>CSharpGameEngine.Sdk</c> package (PackageMode, anywhere).
/// <list type="bullet">
/// <item><see cref="ProjectDirectory"/> anchors Scene asset paths (<c>Assets/...</c>) and the build output.</item>
/// <item><see cref="EngineRoot"/> is the engine repository in SourceMode and null in PackageMode.</item>
/// <item><see cref="WorkspaceRoot"/> is where authoring bridges run: the engine repository when there is
/// one (its tools can be built from source), otherwise the project directory.</item>
/// </list>
/// Bridges and hosts ship inside the VSIX; repository build outputs are only a development fallback.
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

    /// <summary>The open solution, set by the package's solution events; null when none is open.</summary>
    internal static string?SolutionPath { get; set; }

    internal string ProjectFilePath { get; }
    internal string ProjectDirectory { get; }
    internal string?EngineRoot { get; }
    internal string AssemblyName { get; }
    internal string WorkspaceRoot=>this.EngineRoot??this.ProjectDirectory;
    internal string AssetsDirectory=>Path.Combine(this.ProjectDirectory,"Assets");

    /// <summary>The game assembly the project builds for <paramref name="Configuration"/> (Game.props fixes the framework and runtime).</summary>
    internal string OutputAssemblyPath(string Configuration)=>
        Path.Combine(this.ProjectDirectory,"bin",Configuration,TargetFramework,RuntimeIdentifier,this.AssemblyName+".dll");

    /// <summary>A project-relative path such as a Scene's <c>Assets/...</c> as a full path.</summary>
    internal string ResolveProjectPath(string RelativePath)=>Path.GetFullPath(Path.Combine(this.ProjectDirectory,RelativePath));

    /// <summary>
    /// Resolves the game project for <paramref name="Hint"/> (a file or directory inside it), then for
    /// the open solution, then for the repository this extension was built from.
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

    /// <summary>The nearest game project at or above <paramref name="Path"/>.</summary>
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
    /// The game project under <paramref name="Root"/> (up to four levels deep). With several, the one
    /// named UserEcsProject wins (the repository's development game), then the shallowest path.
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

    /// <summary>A context for <paramref name="ProjectFilePath"/> when it is a game project; otherwise null.</summary>
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
