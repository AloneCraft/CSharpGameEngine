using System;
using System.IO;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class PhysicsSceneBakeServiceTests {
    [Fact]
    public void DependencyHash_ChangesForProductSourceButIgnoresDerivedBakeOutput(){
        string Root=CreateRepository();
        try {
            string Project=Path.Combine(Root,"UserEcsProject");
            string Initial=PhysicsSceneBakeService.ComputeDependencyHash(Root,Project);
            string BakeDirectory=Path.Combine(Project,"Assets","Physics","Bakes");
            Directory.CreateDirectory(BakeDirectory);
            File.WriteAllText(Path.Combine(BakeDirectory,"derived.cgephyscache"),"derived");
            Assert.Equal(Initial,PhysicsSceneBakeService.ComputeDependencyHash(Root,Project));

            File.AppendAllText(Path.Combine(Project,"Scenes.cs"),"// semantic source change");
            Assert.NotEqual(Initial,PhysicsSceneBakeService.ComputeDependencyHash(Root,Project));
        }
        finally { Directory.Delete(Root,recursive:true); }
    }

    [Fact]
    public void CreateRequests_UsesProjectLocalStableCollisionSafeOutput(){
        string Root=CreateRepository();
        try {
            string SceneFile=Path.Combine(Root,"UserEcsProject","Scenes.cs");
            ProjectSceneCatalogEntry First=new(new SceneKey("project","First.Space.Scene"),"Scene",SceneFile,0);
            ProjectSceneCatalogEntry Second=new(new SceneKey("project","Second.Space.Scene"),"Scene",SceneFile,1);

            IReadOnlyList<PhysicsSceneBakeRequest> Requests=PhysicsSceneBakeService.CreateRequests(Root,[First,Second],12);

            Assert.Equal(2,Requests.Count);
            Assert.Equal("First.Space.Scene",Requests[0].SceneName);
            Assert.Equal(12,Requests[0].TickCount);
            Assert.Equal(Requests[0].DependencyHash,Requests[1].DependencyHash);
            Assert.All(Requests,Request=>Assert.Equal(
                Path.Combine(Root,"UserEcsProject","UserEcsProject.csproj"),Request.ProjectFile,StringComparer.OrdinalIgnoreCase));
            Assert.NotEqual(Requests[0].OutputPath,Requests[1].OutputPath);
            Assert.All(Requests,Request=>Assert.StartsWith(
                Path.Combine(Root,"UserEcsProject","Assets","Physics","Bakes"),Request.OutputPath,StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(Root,recursive:true); }
    }

    [Fact]
    public void CreateRequests_HashesTheScenesOwnProjectNotUserEcsProject(){
        // M0096 G: the Bake runs the scene's own game build, so its identity is that project's sources, whichever
        // project it is.
        string Root=CreateRepository();
        try {
            string OtherProject=Path.Combine(Root,"Games","Other");
            Directory.CreateDirectory(Path.Combine(OtherProject,"Scenes"));
            File.WriteAllText(Path.Combine(OtherProject,"Other.csproj"),"<Project />");
            string SceneFile=Path.Combine(OtherProject,"Scenes","Level.json");
            File.WriteAllText(SceneFile,"{}");
            ProjectSceneCatalogEntry Entry=new(new SceneKey("other","Other.Scenes.Level"),"Level",SceneFile,0);
            string Initial=PhysicsSceneBakeService.CreateRequests(Root,[Entry],12)[0].DependencyHash;

            File.AppendAllText(Path.Combine(Root,"UserEcsProject","Scenes.cs"),"// another game");
            Assert.Equal(Initial,PhysicsSceneBakeService.CreateRequests(Root,[Entry],12)[0].DependencyHash);

            File.WriteAllText(Path.Combine(OtherProject,"Level.cs"),"internal class Level {}");
            Assert.NotEqual(Initial,PhysicsSceneBakeService.CreateRequests(Root,[Entry],12)[0].DependencyHash);
        }
        finally { Directory.Delete(Root,recursive:true); }
    }

    [Fact]
    public void HostHash_CoversTheEngineButNoGame(){
        // M0096 G: the host loads the game it is given, so a game's change must not rebuild the host.
        string Root=CreateRepository();
        try {
            string Initial=PhysicsSceneBakeService.ComputeHostHash(Root);

            File.AppendAllText(Path.Combine(Root,"UserEcsProject","Scenes.cs"),"// game change");
            Assert.Equal(Initial,PhysicsSceneBakeService.ComputeHostHash(Root));

            File.AppendAllText(Path.Combine(Root,"src","Runtime.cs"),"// engine change");
            Assert.NotEqual(Initial,PhysicsSceneBakeService.ComputeHostHash(Root));
        }
        finally { Directory.Delete(Root,recursive:true); }
    }

    [Fact]
    public void Bake_BuildsEachGameOnceAndBakesWithItsBuild(){
        string Root=CreateRepository();
        try {
            string OtherProject=Path.Combine(Root,"Games","Other");
            Directory.CreateDirectory(OtherProject);
            File.WriteAllText(Path.Combine(OtherProject,"Other.csproj"),"<Project />");
            string OtherScene=Path.Combine(OtherProject,"Level.json");
            File.WriteAllText(OtherScene,"{}");
            ProjectSceneCatalogEntry[]Entries=[
                ..CreateEntries(Root),
                new ProjectSceneCatalogEntry(new SceneKey("other","Other.Level"),"Level",OtherScene,2),
            ];
            List<string>Built=[];
            List<(string Game,string Scene)>Baked=[];

            PhysicsSceneBakeBatchResult Result=PhysicsSceneBakeService.Bake(
                Root,Entries,Guid.NewGuid(),ReportProgress:null,CancellationToken.None,
                PrepareHost:static(_,_,_)=>"host",
                PrepareGame:(_,Request,_)=>{
                    Built.Add(Path.GetFileName(Request.ProjectFile));
                    return Path.GetFileNameWithoutExtension(Request.ProjectFile)+".dll";
                },
                BakeRequest:(_,Game,Request,_)=>{
                    Baked.Add((Game,Request.SceneName));
                    Directory.CreateDirectory(Path.GetDirectoryName(Request.OutputPath)!);
                    File.WriteAllText(Request.OutputPath,Request.SceneName);
                });

            Assert.True(Result.Succeeded,Result.Message);
            Assert.Equal(["UserEcsProject.csproj","Other.csproj"],Built);
            Assert.Equal([
                ("UserEcsProject.dll","First.Space.Scene"),
                ("UserEcsProject.dll","Second.Space.Scene"),
                ("Other.dll","Other.Level"),
            ],Baked);
        }
        finally { Directory.Delete(Root,recursive:true); }
    }

    [Fact]
    public void ExactHostPath_UsesRidAwareSdkArtifactsLayout(){
        string Root=Path.GetFullPath("repo-root");

        string Result=PhysicsSceneBakeService.GetExactHostExecutablePath(Root,"ABC123");

        Assert.Equal(Path.Combine(Root,".artifacts","PhysicsBakeHost","ABC123","build","bin",
            "CSharpGameEngine.Physics.Cli","debug_win-x64","CSharpGameEngine.Physics.Cli.exe"),Result);
    }

    [Fact]
    public void Bake_ReportsCorrelatedMonotonicProgressAndCompletesAfterEveryOutput(){
        string Root=CreateRepository();
        try {
            ProjectSceneCatalogEntry[]Entries=CreateEntries(Root);
            Guid RequestId=Guid.NewGuid();
            List<AuthoringHostProgress>Progress=[];

            PhysicsSceneBakeBatchResult Result=PhysicsSceneBakeService.Bake(
                Root,Entries,RequestId,Progress.Add,CancellationToken.None,
                PrepareHost:static(_,_,_)=>"host",
                PrepareGame:static(_,_,_)=>"game.dll",
                BakeRequest:static(_,_,Request,_)=>{
                    Directory.CreateDirectory(Path.GetDirectoryName(Request.OutputPath)!);
                    File.WriteAllText(Request.OutputPath,Request.SceneName);
                });

            Assert.True(Result.Succeeded,Result.Message);
            Assert.Equal(2,Result.CompletedCount);
            Assert.Equal(AuthoringOperationUiState.Succeeded,Result.Operation.State);
            Assert.Equal(1f,Result.Operation.Fraction);
            Assert.NotEmpty(Progress);
            Assert.All(Progress,Item=>Assert.Equal(RequestId,Item.RequestId));
            Assert.True(Progress.Zip(Progress.Skip(1),(Left,Right)=>
                Right.Sequence>Left.Sequence&&Right.Fraction>=Left.Fraction).All(static Value=>Value));
            Assert.All(Result.OutputPaths,FilePath=>Assert.True(File.Exists(FilePath)));
        }
        finally { Directory.Delete(Root,recursive:true); }
    }

    [Fact]
    public void Bake_LaterFailurePreservesCompletedOutputsAndTypedDiagnostic(){
        string Root=CreateRepository();
        try {
            ProjectSceneCatalogEntry[]Entries=CreateEntries(Root);
            int Invocation=0;

            PhysicsSceneBakeBatchResult Result=PhysicsSceneBakeService.Bake(
                Root,Entries,Guid.NewGuid(),ReportProgress:null,CancellationToken.None,
                PrepareHost:static(_,_,_)=>"host",
                PrepareGame:static(_,_,_)=>"game.dll",
                BakeRequest:(_,_,Request,_)=>{
                    if(++Invocation==2){ throw new InvalidOperationException("second failed"); }
                    Directory.CreateDirectory(Path.GetDirectoryName(Request.OutputPath)!);
                    File.WriteAllText(Request.OutputPath,Request.SceneName);
                });

            Assert.False(Result.Succeeded);
            Assert.Equal(1,Result.CompletedCount);
            Assert.Single(Result.OutputPaths);
            Assert.Equal(AuthoringOperationUiState.Failed,Result.Operation.State);
            Assert.Equal("BAKE_FAILED",Assert.Single(Result.Operation.Diagnostics).Code);
        }
        finally { Directory.Delete(Root,recursive:true); }
    }

    [Fact]
    public void Bake_PreservesHostDiagnosticEnvelope(){
        string Root=CreateRepository();
        try {
            ProjectSceneCatalogEntry[]Entries=CreateEntries(Root);
            PhysicsSceneBakeBatchResult Result=PhysicsSceneBakeService.Bake(
                Root,Entries,Guid.NewGuid(),ReportProgress:null,CancellationToken.None,
                PrepareHost:static(_,_,_)=>"host",
                PrepareGame:static(_,_,_)=>"game.dll",
                BakeRequest:static(_,_,_,_)=>throw new AuthoringHostOperationException(
                    "physics",new AuthoringHostResult {
                        RequestId=Guid.NewGuid(),State=AuthoringHostTerminalState.Failed,
                        ExitCode=7,Output="invalid collider",
                        Diagnostics=[new AuthoringHostDiagnostic {
                            Severity=AuthoringHostDiagnosticSeverity.Error,
                            Code="PHYSICS_COLLIDER_INVALID",Message="invalid collider",
                        }],
                    }));

            Assert.False(Result.Succeeded);
            Assert.Equal("PHYSICS_COLLIDER_INVALID",
                Assert.Single(Result.Operation.Diagnostics).Code);
            Assert.Equal("invalid collider",Result.Message);
        }
        finally { Directory.Delete(Root,recursive:true); }
    }

    private static ProjectSceneCatalogEntry[]CreateEntries(string Root){
        string SceneFile=Path.Combine(Root,"UserEcsProject","Scenes.cs");
        return [
            new ProjectSceneCatalogEntry(
                new SceneKey("project","First.Space.Scene"),"Scene",SceneFile,0),
            new ProjectSceneCatalogEntry(
                new SceneKey("project","Second.Space.Scene"),"Scene",SceneFile,1),
        ];
    }

    private static string CreateRepository(){
        string Root=Path.Combine(Path.GetTempPath(),"cge-physics-bake-tests-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Root,"src"));
        Directory.CreateDirectory(Path.Combine(Root,"UserEcsProject","Assets"));
        File.WriteAllText(Path.Combine(Root,"CSharpGameEngine.slnx"),"<Solution />");
        File.WriteAllText(Path.Combine(Root,"Directory.Build.props"),"<Project />");
        File.WriteAllText(Path.Combine(Root,"src","Runtime.cs"),"internal class Runtime {}");
        File.WriteAllText(Path.Combine(Root,"UserEcsProject","UserEcsProject.csproj"),"<Project />");
        File.WriteAllText(Path.Combine(Root,"UserEcsProject","Scenes.cs"),"internal class Scene {}");
        File.WriteAllText(Path.Combine(Root,"UserEcsProject","Assets","shape.bin"),"asset");
        return Root;
    }
}
