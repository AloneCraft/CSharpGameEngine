using System;
using System.IO;
using CSharpGameEngine.VSExtension;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>ADR-0065：エディターは、エンジンリポジトリを前提とせずにゲームプロジェクトを見つける。</summary>
public sealed class CgeProjectContextTests:IDisposable {
    private readonly string Root=Path.Combine(Path.GetTempPath(),"cge-context-"+Guid.NewGuid().ToString("N"));

    public CgeProjectContextTests(){
        Directory.CreateDirectory(this.Root);
    }

    public void Dispose(){
        try { Directory.Delete(this.Root,true); } catch(IOException){ }
    }

    private string Write(string RelativePath,string Text){
        var FullPath=Path.Combine(this.Root,RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(FullPath)!);
        File.WriteAllText(FullPath,Text);
        return FullPath;
    }

    [Fact]
    public void PackageModeProject_HasNoEngineRootAndRunsBridgesFromItsOwnDirectory(){
        var Project=this.Write(@"MyGame\MyGame.csproj","<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"CSharpGameEngine.Sdk\" Version=\"0.1.0\" /></ItemGroup></Project>");
        var Context=CgeProjectContext.TryCreate(Project);
        Assert.NotNull(Context);
        Assert.Null(Context!.EngineRoot);
        Assert.Equal(Path.GetDirectoryName(Project),Context.WorkspaceRoot);
        Assert.Equal("MyGame",Context.AssemblyName);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(Project)!,"bin","Debug",CgeProjectContext.TargetFramework,"win-x64","MyGame.dll"),Context.OutputAssemblyPath("Debug"));
        Assert.Equal(Path.Combine(Path.GetDirectoryName(Project)!,"Assets","Stage","Level.glb"),Context.ResolveProjectPath("Assets/Stage/Level.glb"));
    }

    [Fact]
    public void SourceModeProject_FindsTheEngineRootThroughItsGamePropsImport(){
        this.Write(@"Engine\build\CSharpGameEngine.Game.props","<Project />");
        var Project=this.Write(@"Engine\templates\Game\Game.csproj","<Project Sdk=\"Microsoft.NET.Sdk\"><Import Project=\"..\\..\\build\\CSharpGameEngine.Game.props\" /><PropertyGroup><AssemblyName>CgeGame</AssemblyName></PropertyGroup></Project>");
        var Context=CgeProjectContext.TryCreate(Project)!;
        Assert.Equal(Path.Combine(this.Root,"Engine"),Context.EngineRoot);
        Assert.Equal(Context.EngineRoot,Context.WorkspaceRoot);
        Assert.Equal("CgeGame",Context.AssemblyName);
    }

    [Fact]
    public void OrdinaryProjects_AreNotGameProjects(){
        var Project=this.Write(@"Tool\Tool.csproj","<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"CSharpGameEngine.SdkTools\" Version=\"1.0.0\" /></ItemGroup></Project>");
        Assert.Null(CgeProjectContext.TryCreate(Project));
    }

    [Fact]
    public void ContainingDirectory_WalksUpFromASceneFile(){
        var Project=this.Write(@"MyGame\MyGame.csproj","<Project><ItemGroup><PackageReference Include=\"CSharpGameEngine.Sdk\" Version=\"0.1.0\" /></ItemGroup></Project>");
        var Scene=this.Write(@"MyGame\Scenes\Level.cs","class Level {}");
        Assert.Equal(Project,CgeProjectContext.FromContainingDirectory(Scene)!.ProjectFilePath);
        Assert.Null(CgeProjectContext.FromContainingDirectory(Path.GetTempPath()));
    }

    [Fact]
    public void SearchRoot_PrefersUserEcsProjectThenTheShallowestGameProject(){
        const string Sdk="<Project><ItemGroup><PackageReference Include=\"CSharpGameEngine.Sdk\" Version=\"0.1.0\" /></ItemGroup></Project>";
        this.Write(@"a\b\Deep.csproj",Sdk);
        var Shallow=this.Write(@"z\Shallow.csproj",Sdk);
        this.Write(@"bin\Debug\Ignored.csproj",Sdk);
        Assert.Equal(Shallow,CgeProjectContext.FromSearchRoot(this.Root)!.ProjectFilePath);
        var UserEcs=this.Write(@"x\y\UserEcsProject.csproj",Sdk);
        Assert.Equal(UserEcs,CgeProjectContext.FromSearchRoot(this.Root)!.ProjectFilePath);
    }
}
