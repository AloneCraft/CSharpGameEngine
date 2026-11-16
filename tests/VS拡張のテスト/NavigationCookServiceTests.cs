using System;
using System.IO;
using System.Linq;
using System.Threading;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>ADR-0079/M0106: VS のナビメッシュの Cook(ビルドと同じ <c>CgeCookNavigation</c> を <c>dotnet msbuild</c> に動かす)。process は差し替えて、引数・出力の読み取り・失敗の扱いを確かめる。</summary>
public sealed class NavigationCookServiceTests:IDisposable {
    private readonly string Root=Path.Combine(Path.GetTempPath(),"cge-nav-vsix-"+Guid.NewGuid().ToString("N"));
    public NavigationCookServiceTests(){
        Directory.CreateDirectory(Path.Combine(this.Root,"Game","Scenes"));
        File.WriteAllText(Path.Combine(this.Root,"Game","Game.csproj"),"<Project />");
    }
    public void Dispose(){
        if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,recursive:true); }
    }
    private string Scene(string Text){
        var Path_=Path.Combine(this.Root,"Game","Scenes","Level.json");
        File.WriteAllText(Path_,Text);
        return Path_;
    }
    private static string Items(params string[]Paths)=>
        "{\n  \"Items\": {\n    \"CgeNavigationOutput\": [\n"+string.Join(",\n",Paths.Select(static Path_=>"      { \"Identity\": \""+Path_.Replace("\\","\\\\")+"\", \"FullPath\": \""+Path_.Replace("\\","\\\\")+"\" }"))+"\n    ]\n  }\n}\n";
    [Fact]
    public void ASceneWithABoundsComponent_HasANavigationVolume(){
        Assert.True(NavigationCookService.HasNavigationVolume(this.Scene("{ \"type\": \"CSharpGameEngine.Navigation.NavMeshBounds\" }")));
        Assert.False(NavigationCookService.HasNavigationVolume(this.Scene("{ \"type\": \"CSharpGameEngine.Entities.Transform\" }")));
        Assert.False(NavigationCookService.HasNavigationVolume(Path.Combine(this.Root,"missing.json")));
    }
    [Fact]
    public void TheProjectFile_IsTheOnlyOneInTheNearestFolderAboveTheScene(){
        var Scene=this.Scene("{}");
        Assert.Equal(Path.Combine(this.Root,"Game","Game.csproj"),NavigationCookService.FindProjectFile(Scene),StringComparer.OrdinalIgnoreCase);
        File.WriteAllText(Path.Combine(this.Root,"Game","Second.csproj"),"<Project />");
        Assert.Null(NavigationCookService.FindProjectFile(Scene));
    }
    [Fact]
    public void TheArguments_RunTheBuildsCookTargetAndAskForItsOutputs(){
        Assert.Equal(
            "msbuild \"C:\\Games\\Top Down\\Game.csproj\" -t:CgeCookNavigation -nologo -v:minimal -getItem:CgeNavigationOutput",
            NavigationCookService.BuildArguments("C:\\Games\\Top Down\\Game.csproj"));
    }
    [Fact]
    public void ACook_RunsInTheProjectFolderAndListsTheFilesItWrote(){
        var Scene=this.Scene("{ \"type\": \"CSharpGameEngine.Navigation.NavMeshBounds\" }");
        string?Arguments=null;
        string?WorkingDirectory=null;
        var Written=Path.Combine(this.Root,"Game","obj","cge-navigation","Assets","Navigation","Level.cgenav");
        var Result=NavigationCookService.Cook(Scene,CancellationToken.None,(Command,Directory,_)=>{
            Arguments=Command;
            WorkingDirectory=Directory;
            return (0,"Level.json: NavMesh 'Level': 12 polygons, 10 portals, 64x64 cells.\n"+Items(Written));
        });
        Assert.True(Result.Succeeded,Result.Message);
        Assert.Equal(Written,Assert.Single(Result.OutputPaths));
        Assert.Contains("-t:CgeCookNavigation",Arguments);
        Assert.Equal(Path.Combine(this.Root,"Game"),WorkingDirectory,StringComparer.OrdinalIgnoreCase);
    }
    [Fact]
    public void ABuildError_IsAFailureWithItsDiagnosticLines(){
        var Scene=this.Scene("{}");
        var Result=NavigationCookService.Cook(Scene,CancellationToken.None,(_,_,_)=>(1,
            "Level.json : error NAV011: The navigation mesh 'Level' could not be built: too large.\nLevel.json : warning NAV008: a Sphere collider adds no geometry.\n"));
        Assert.False(Result.Succeeded);
        Assert.Contains("NAV011",Result.Message);
        Assert.Equal(2,Result.Diagnostics.Count);
        Assert.Empty(Result.OutputPaths);
    }
    [Fact]
    public void AWarningAlone_DoesNotFailAnExitCodeZeroCook(){
        var Result=NavigationCookService.ParseOutput(0,"Level.json: warning NAV012: no walkable surface.\n"+Items());
        Assert.True(Result.Succeeded);
        Assert.Single(Result.Diagnostics);
        Assert.Empty(Result.OutputPaths);
    }
    [Fact]
    public void ANonZeroExitWithoutAnErrorLine_StillFailsWithTheExitCode(){
        var Result=NavigationCookService.ParseOutput(7,"something went wrong\n");
        Assert.False(Result.Succeeded);
        Assert.Contains("7",Result.Message);
    }
    [Fact]
    public void ASceneWithoutAProject_IsAFailureBeforeAnythingRuns(){
        var Orphan=Path.Combine(Path.GetTempPath(),"cge-nav-orphan-"+Guid.NewGuid().ToString("N"),"Level.json");
        var Ran=false;
        var Result=NavigationCookService.Cook(Orphan,CancellationToken.None,(_,_,_)=>{ Ran=true;return (0,string.Empty); });
        Assert.False(Result.Succeeded);
        Assert.False(Ran);
    }
}
