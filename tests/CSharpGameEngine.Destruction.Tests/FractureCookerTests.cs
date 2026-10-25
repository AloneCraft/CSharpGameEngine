using System.Diagnostics;
using CSharpGameEngine.Cooking;
using static CSharpGameEngine.Destruction.Tests.TestProject;
namespace CSharpGameEngine.Destruction.Tests;
/// <summary>
/// ADR-0078/M0113: the Destruction plugin's Cook module cuts a scene's destructibles into <c>Assets/Destruction/&lt;name&gt;.cgefracture</c>: the box of the
/// node's collider (or a closed convex SourceMesh) with its count, seed and clusters, the same bytes every time, and a build-readable error for each
/// destructible it cannot cut.
/// </summary>
public sealed class FractureCookerTests {
    private static CookResult Cook(TestProject Project,string Scene){
        var Output=Path.Combine(Project.Root,"out");
        Directory.CreateDirectory(Output);
        return new FractureCooker().Cook(new CookRequest(Scene,Output,Project.Root));
    }
    private static string Crate(TestProject Project,string Extra="")=>
        Project.WriteScene("Level.json","Level",Node("Level",[],Node("Crate",[Transform(U:0.5f),BoxCollider(0.5f,0.5f,0.5f),Destructible("Crate",Extra)])));
    private static PrefractureAsset Read(TestProject Project,string Name){
        var Path=System.IO.Path.Combine(Project.Root,"out","Assets","Destruction",Name+".cgefracture");
        Assert.True(File.Exists(Path),Path);
        Assert.True(PrefractureArtifact.TryRead(File.ReadAllBytes(Path),out var Asset,out var Error),Error);
        return Asset!;
    }
    [Fact]
    public void ABoxDestructibleIsCutIntoItsFragmentsAndClusters(){
        using var Project=new TestProject();
        var Result=Cook(Project,Crate(Project,""","FragmentCount": 12, "Seed": 3, "ClusterCount": 4"""));
        Assert.True(Result.Succeeded,string.Join("\n",Result.Messages));
        Assert.Equal(["Assets/Destruction/Crate.cgefracture"],Result.Outputs);
        var Asset=Read(Project,"Crate");
        Assert.Contains(Result.Messages,Message=>Message.StartsWith($"Level.json: Fracture 'Crate': 12 fragments, {Asset.ClusterCount} clusters",StringComparison.Ordinal));
        Assert.Equal(12,Asset.Fragments.Count);
        Assert.InRange(Asset.ClusterCount,4,6);
        Assert.Equal(3,Asset.Seed);
        // The source is the collider in the node's own frame: a 1 m box about the origin, 500 kg/m³.
        var Mass=Asset.Fragments.Aggregate(LogicalScalar.Zero,static(Total,Fragment)=>Total+Fragment.Mass);
        Assert.InRange(Mass.TryToSingle(out var Kilograms)?Kilograms:0f,499.9f,500.1f);
        Assert.Equal(LogicalScalar.FromInt64(DestructionDefaults.BondStrength),Asset.BondStrengthPerArea);
    }
    [Fact]
    public void TheSameSceneCooksToTheSameBytesAndAnotherSeedToOthers(){
        using var Project=new TestProject();
        var Scene=Crate(Project,""","FragmentCount": 10, "Seed": 9""");
        Assert.True(Cook(Project,Scene).Succeeded);
        var First=File.ReadAllBytes(Path.Combine(Project.Root,"out","Assets","Destruction","Crate.cgefracture"));
        Assert.True(Cook(Project,Scene).Succeeded);
        Assert.Equal(First,File.ReadAllBytes(Path.Combine(Project.Root,"out","Assets","Destruction","Crate.cgefracture")));
        Assert.True(Cook(Project,Crate(Project,""","FragmentCount": 10, "Seed": 10""")).Succeeded);
        Assert.NotEqual(First,File.ReadAllBytes(Path.Combine(Project.Root,"out","Assets","Destruction","Crate.cgefracture")));
    }
    [Fact]
    public void TwoNodesOfOneNameShareOneAssetWhenTheirCutsAgree(){
        using var Project=new TestProject();
        var Scene=Project.WriteScene("Level.json","Level",Node("Level",[],
            Node("A",[Transform(),BoxCollider(0.5f,0.5f,0.5f),Destructible("Crate",""","Seed": 1""")]),
            Node("B",[Transform(R:3f),BoxCollider(0.5f,0.5f,0.5f),Destructible("Crate",""","Seed": 1""")])));
        var Result=Cook(Project,Scene);
        Assert.True(Result.Succeeded,string.Join("\n",Result.Messages));
        Assert.Single(Result.Outputs);
    }
    [Fact]
    public void TwoNodesOfOneNameWithDifferentCutsAreAnError(){
        using var Project=new TestProject();
        var Scene=Project.WriteScene("Level.json","Level",Node("Level",[],
            Node("A",[Transform(),BoxCollider(0.5f,0.5f,0.5f),Destructible("Crate",""","Seed": 1""")]),
            Node("B",[Transform(),BoxCollider(0.5f,0.5f,0.5f),Destructible("Crate",""","Seed": 2""")])));
        var Result=Cook(Project,Scene);
        Assert.False(Result.Succeeded);
        Assert.StartsWith("Level.json: error DST010",Result.Messages[0]);
    }
    [Theory]
    [InlineData(""" "FractureAsset": "" ""","DST001")]
    [InlineData(""" "FractureAsset": "../outside" ""","DST001")]
    [InlineData(""" "FractureAsset": "Crate", "FragmentCount": 0 ""","DST003")]
    [InlineData(""" "FractureAsset": "Crate", "FragmentCount": 1 ""","DST003")]
    [InlineData(""" "FractureAsset": "Crate", "FragmentCount": 257 ""","DST003")]
    [InlineData(""" "FractureAsset": "Crate", "Density": 0 ""","DST003")]
    [InlineData(""" "FractureAsset": "Crate", "Density": "NaN" ""","DST011")]
    [InlineData(""" "FractureAsset": "Crate", "BondStrength": -1 ""","DST003")]
    [InlineData(""" "FractureAsset": "Crate", "FragmentCount": 2.5 ""","DST011")]
    [InlineData(""" "FractureAsset": "Crate", "SourceMesh": "Assets/Missing.glb" ""","DST007")]
    public void ADestructibleTheCookCannotCutIsABuildError(string Members,string Code){
        using var Project=new TestProject();
        var Scene=Project.WriteScene("Level.json","Level",Node("Level",[],Node("Crate",[Transform(),BoxCollider(0.5f,0.5f,0.5f),Component(DestructionDefaults.DestructibleType,"{"+Members+"}")])));
        var Result=Cook(Project,Scene);
        Assert.False(Result.Succeeded);
        Assert.StartsWith($"Level.json: error {Code}",Result.Messages[0]);
    }
    [Fact]
    public void AScaledNodeASphereAFlatBoxAndNoColliderAreBuildErrors(){
        using var Project=new TestProject();
        void Expect(string Root,string Code){
            var Result=Cook(Project,Project.WriteScene("Level.json","Level",Root));
            Assert.False(Result.Succeeded);
            Assert.StartsWith($"Level.json: error {Code}",Result.Messages[0]);
        }
        Expect(Node("Crate",[Transform(Scale:2f),BoxCollider(0.5f,0.5f,0.5f),Destructible("Crate")]),"DST004");
        Expect(Node("Crate",[Transform(),Component("CSharpGameEngine.Runtime.Physics.Collider","""{ "Shape": "Sphere", "Radius": 1 }"""),Destructible("Crate")]),"DST006");
        Expect(Node("Crate",[Transform(),BoxCollider(0.5f,0f,0.5f),Destructible("Crate")]),"DST006");
        Expect(Node("Crate",[Transform(),Destructible("Crate")]),"DST006");
        Expect(Node("Level",[Transform()]),"DST009");
    }
    [Fact]
    public void AClosedConvexSourceMeshIsCutInsteadOfTheCollider(){
        using var Project=new TestProject();
        var Mesh=Project.CopyBox();
        var Scene=Project.WriteScene("Level.json","Level",Node("Rock",[Transform(),Destructible("Rock",$""","SourceMesh": "{Mesh}", "FragmentCount": 6""")]));
        var Result=Cook(Project,Scene);
        Assert.True(Result.Succeeded,string.Join("\n",Result.Messages));
        Assert.Equal(6,Read(Project,"Rock").Fragments.Count);
        Assert.Contains(Result.Messages,static Message=>Message.Contains("mesh Assets/Meshes/Box.glb",StringComparison.Ordinal));
    }
    [Fact]
    public void TheCookCliCutsInItsOwnProcessAndVerifiesTheBytes(){
        using var Project=new TestProject();
        var Scene=Crate(Project,""","FragmentCount": 8""");
        var Output=Path.Combine(Project.Root,"cli");
        var (ExitCode,Standard,Error)=RunCli("cook",Module,Scene,Output,"--project",Project.Root,"--verify");
        Assert.True(ExitCode==0,Error);
        Assert.Contains("Crate.cgefracture",Standard);
        Assert.True(File.Exists(Path.Combine(Output,"Assets","Destruction","Crate.cgefracture")));
        var (FailCode,_,FailError)=RunCli("cook",Module,Project.WriteScene("Empty.json","Empty",Node("Level",[Transform()])),Path.Combine(Project.Root,"fail"),"--project",Project.Root);
        Assert.Equal(1,FailCode);
        Assert.Matches(@"^Empty\.json: error DST009: .+",FailError.Trim());
    }
    private static string Configuration=>new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)).Parent!.Name;
    private static string Module=>Path.Combine(RepositoryRoot,"plugins","CSharpGameEngine.Destruction","Cook","bin",Configuration,"net10.0","CSharpGameEngine.Destruction.Cook.dll");
    private static (int ExitCode,string Output,string Error)RunCli(params string[]Arguments){
        var Start=new ProcessStartInfo("dotnet"){ RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false };
        Start.ArgumentList.Add(Path.Combine(RepositoryRoot,"tools","CSharpGameEngine.Cook.Cli","bin",Configuration,"net10.0","CSharpGameEngine.Cook.Cli.dll"));
        foreach(var Argument in Arguments){ Start.ArgumentList.Add(Argument); }
        using var Process=System.Diagnostics.Process.Start(Start)!;
        var Output=Process.StandardOutput.ReadToEndAsync();
        var Error=Process.StandardError.ReadToEndAsync();
        Assert.True(Process.WaitForExit(120_000),"The Cook CLI did not finish.");
        return (Process.ExitCode,Output.Result,Error.Result);
    }
}
