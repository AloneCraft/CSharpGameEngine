using System.Diagnostics;
using CSharpGameEngine.Cooking;
using CSharpGameEngine.Navigation.Cook;
using static CSharpGameEngine.Navigation.Tests.TestProject;
namespace CSharpGameEngine.Navigation.Tests;
/// <summary>
/// ADR-0079/M0106: Cook モジュールとしてのナビメッシュ。シーンから <c>Assets/Navigation/&lt;Id&gt;.cgenav</c> を決定的に書き、
/// 失敗は <c>ファイル: error NAVnnn: 内容</c> で返す。CLI(別のプロセス)でも <c>--verify</c> が通る。
/// </summary>
public sealed class NavMeshCookerTests {
    /// <summary>平らな床の箱と、床の上の箱 1 つ、それを囲む領域のシーン。</summary>
    private static string LevelScene(TestProject Project,string Id="Level"){
        var Floor=Node("Floor",[Transform(U:-0.5f),BoxCollider(8f,0.5f,8f)]);
        var Crate=Node("Wall",[Transform(U:1f),BoxCollider(1f,1f,1f)]);
        var Volume=Node("Volume",[Transform(),Bounds(Id,6f,4f,6f)]);
        return Project.WriteScene("Level.json","Level",Node("Level",[],Floor,Crate,Volume));
    }
    private static CookResult Cook(TestProject Project,string Scene,string OutputName="out"){
        var Output=Path.Combine(Project.Root,OutputName);
        Directory.CreateDirectory(Output);
        return new NavMeshCooker().Cook(new CookRequest(Scene,Output,Project.Root));
    }
    [Fact]
    public void ACookWritesTheMeshForEachId_AndItReadsBack(){
        using var Project=new TestProject();
        var Result=Cook(Project,LevelScene(Project));
        Assert.True(Result.Succeeded,string.Join(" ",Result.Messages));
        Assert.Equal(["Assets/Navigation/Level.cgenav"],Result.Outputs);
        var Bytes=File.ReadAllBytes(Path.Combine(Project.Root,"out","Assets","Navigation","Level.cgenav"));
        Assert.True(NavMesh.TryRead(Bytes,out var Mesh,out var Error),Error);
        Assert.NotEmpty(Mesh!.Polygons);
        // 床の上の箱(1 m 角ではなく 2 m 角)の真ん中は歩けず、そのまわりは歩ける。
        Assert.False(Geometry.Covered(Mesh,0f,0f));
        Assert.True(Geometry.Covered(Mesh,3f,3f));
        Assert.Contains(Result.Messages,static Message=>Message.Contains("NavMesh 'Level'"));
    }
    [Fact]
    public void TheSameSceneCooksToTheSameBytes_AnyNumberOfTimes(){
        using var Project=new TestProject();
        var Scene=LevelScene(Project);
        Cook(Project,Scene,"a");
        Cook(Project,Scene,"b");
        Assert.Equal(File.ReadAllBytes(Path.Combine(Project.Root,"a","Assets","Navigation","Level.cgenav")),File.ReadAllBytes(Path.Combine(Project.Root,"b","Assets","Navigation","Level.cgenav")));
    }
    [Fact]
    public void VolumesWithTheSameId_MakeOneMeshOverTheirUnion_AndVolumesWithOtherIdsMakeOtherFiles(){
        using var Project=new TestProject();
        var Floor=Node("Floor",[Transform(U:-0.5f),BoxCollider(20f,0.5f,20f)]);
        var Left=Node("Left",[Transform(R:-4f),Bounds("One",3f,4f,3f)]);
        var Right=Node("Right",[Transform(R:4f),Bounds("One",3f,4f,3f)]);
        var Far=Node("Far",[Transform(F:12f),Bounds("Two",3f,4f,3f)]);
        var Scene=Project.WriteScene("Level.json","Level",Node("Level",[],Floor,Left,Right,Far));
        var Result=Cook(Project,Scene);
        Assert.True(Result.Succeeded,string.Join(" ",Result.Messages));
        Assert.Equal(["Assets/Navigation/One.cgenav","Assets/Navigation/Two.cgenav"],Result.Outputs);
        Assert.True(NavMesh.TryRead(File.ReadAllBytes(Path.Combine(Project.Root,"out","Assets","Navigation","One.cgenav")),out var One,out _));
        // 2 つの領域(R -7〜-1 と 1〜7)の和は R -7〜7(隙間の R -1〜1 も、同じ床なので格子の中)。
        Assert.True(Geometry.F(One!.CellSize)*One.GridWidth>=13.9f);
    }
    [Fact]
    public void VolumesWithTheSameIdThatDisagreeOnTheAgent_AreAnError(){
        using var Project=new TestProject();
        var Scene=Project.WriteScene("Level.json","Level",Node("Level",[],Node("A",[Bounds("One")]),Node("B",[Bounds("One",Extra:",\"AgentRadius\": 0.6")])));
        var Result=Cook(Project,Scene);
        Assert.False(Result.Succeeded);
        Assert.Contains("error NAV010",Assert.Single(Result.Messages));
        Assert.Contains("Level.json",Result.Messages[0]);
    }
    [Fact]
    public void ASceneWithoutAVolume_IsAnErrorNotAnEmptyOutput(){
        using var Project=new TestProject();
        var Scene=Project.WriteScene("Level.json","Level",Node("Level",[Transform()]));
        var Result=Cook(Project,Scene);
        Assert.False(Result.Succeeded);
        Assert.Contains("error NAV009",Result.Messages[0]);
    }
    [Fact]
    public void AVolumeWithNoCollisionInIt_WritesAnEmptyMeshAndWarns(){
        using var Project=new TestProject();
        var Scene=Project.WriteScene("Level.json","Level",Node("Level",[],Node("Volume",[Bounds("Void")])));
        var Result=Cook(Project,Scene);
        Assert.True(Result.Succeeded);
        Assert.Contains(Result.Messages,static Message=>Message.Contains("warning NAV012"));
        Assert.True(NavMesh.TryRead(File.ReadAllBytes(Path.Combine(Project.Root,"out","Assets","Navigation","Void.cgenav")),out var Mesh,out _));
        Assert.Empty(Mesh!.Polygons);
    }
    [Fact]
    public void AVolumeTooLargeForItsCells_IsAnErrorWithTheSceneName(){
        using var Project=new TestProject();
        var Scene=Project.WriteScene("Level.json","Level",Node("Level",[],Node("Volume",[Bounds("Huge",5000f,5f,5000f)])));
        var Result=Cook(Project,Scene);
        Assert.False(Result.Succeeded);
        Assert.StartsWith("Level.json: error NAV011",Result.Messages[0]);
        Assert.Contains("too large",Result.Messages[0]);
    }
    private static string CookCli(){
        // テストは bin/<構成>/net10.0 で動く。CLI は同じ構成でビルドされている(プロジェクト参照)。
        return Path.Combine(RepositoryRoot,"tools","CSharpGameEngine.Cook.Cli","bin",Configuration,"net10.0","CSharpGameEngine.Cook.Cli.dll");
    }
    private static string Configuration=>new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)).Parent!.Name;
    private static (int ExitCode,string Output,string Error)RunCli(params string[]Arguments){
        var Start=new ProcessStartInfo("dotnet"){ RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false };
        Start.ArgumentList.Add(CookCli());
        foreach(var Argument in Arguments){ Start.ArgumentList.Add(Argument); }
        using var Process=System.Diagnostics.Process.Start(Start)!;
        var Output=Process.StandardOutput.ReadToEndAsync();
        var Error=Process.StandardError.ReadToEndAsync();
        Assert.True(Process.WaitForExit(120_000),"The Cook CLI did not finish.");
        return (Process.ExitCode,Output.Result,Error.Result);
    }
    /// <summary>モジュールは自分の出力フォルダ(.deps.json と依存の DLL が隣にある)から読ませる。</summary>
    private static string Module=>Path.Combine(RepositoryRoot,"plugins","CSharpGameEngine.Navigation","Cook","bin",Configuration,"net10.0","CSharpGameEngine.Navigation.Cook.dll");
    [Fact]
    public void TheCookCli_BakesInItsOwnProcess_AndVerifiesDeterminism(){
        using var Project=new TestProject();
        var Scene=LevelScene(Project);
        var Output=Path.Combine(Project.Root,"cli");
        var (ExitCode,Standard,Error)=RunCli("cook",Module,Scene,Output,"--project",Project.Root,"--verify");
        Assert.True(ExitCode==0,Error);
        Assert.Contains("Level.cgenav",Standard);
        Assert.Contains("NavMesh 'Level'",Error);
        Assert.True(File.Exists(Path.Combine(Output,"Assets","Navigation","Level.cgenav")));
        var (ListExit,Listed,_)=RunCli("list",Module);
        Assert.Equal(0,ListExit);
        Assert.Contains("NavMesh\t.json",Listed);
    }
    [Fact]
    public void TheCookCli_PrintsAFailureAsABuildReadableDiagnosticAndExitsWithOne(){
        using var Project=new TestProject();
        var Scene=Project.WriteScene("Level.json","Level",Node("Level",[Transform()]));
        var (ExitCode,_,Error)=RunCli("cook",Module,Scene,Path.Combine(Project.Root,"cli"),"--project",Project.Root);
        Assert.Equal(1,ExitCode);
        // MSBuild の Exec が読む形: 行頭から「場所: error コード: 内容」。
        Assert.Matches(@"^Level\.json: error NAV009: .+",Error.Trim());
    }
}
