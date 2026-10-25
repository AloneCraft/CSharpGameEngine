using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// ADR-0078/M0113: the Scene view's Fracture preview reads what to cut from the scene's Destructible and its box Collider (the same values the build's Cook
/// module reads), runs the physics host with those numbers, and draws the bonds and the fragments' outlines where the node is.
/// </summary>
public sealed class PrefractureDebugServiceTests {
    private const string Components="""
        namespace CSharpGameEngine.Runtime.Physics {
            public enum ColliderShape { Box, Sphere }
            public struct LogicalVector3 { public LogicalVector3(float R,float U,float F){} }
            public struct Collider {
                public ColliderShape Shape;
                public LogicalVector3 Center;
                public LogicalVector3 HalfExtents;
            }
        }
        namespace CSharpGameEngine.Destruction {
            public struct Destructible {
                public string FractureAsset;
                public int FragmentCount;
                public int Seed;
                public int ClusterCount;
                public float Density;
                public float BondStrength;
                public string SourceMesh;
            }
        }
        """;

    private static SceneNode Root(string Members,string Collider="new Collider { Shape=ColliderShape.Box,HalfExtents=new LogicalVector3(0.5f,1f,2f),Center=new LogicalVector3(0f,1f,0f) }"){
        string Scene=$$"""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Physics;
            using CSharpGameEngine.Destruction;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[
                    {{Collider}},
                    new Destructible { {{Members}} },
                ]};
            }
            """;
        var Model=SceneAnalysisTestHelper.Analyze([Components,Scene]);
        Assert.NotNull(Model.Root);
        return Model.Root!;
    }

    [Fact]
    public void FindFractureAsset_UsesTheDestructionPluginsComponent(){
        var Node=Root("""FractureAsset="Crate" """);
        Assert.Equal("Crate",PrefractureDebugService.FindFractureAsset(Node));
        Assert.Contains(Node.Components,static Component=>Component.TypeFullyQualifiedName=="global::CSharpGameEngine.Destruction.Destructible");
    }

    [Fact]
    public void ThePreviewCutsTheColliderBoxWithTheNodesCountSeedAndClusters(){
        var Node=Root("""FractureAsset="Crate",FragmentCount=12,Seed=-7,ClusterCount=3,Density=2400f,BondStrength=500f""");
        Assert.True(PrefractureDebugService.TryReadPreviewRequest(Node,out var Request,out var Problem),Problem);
        Assert.Equal(new FracturePreviewRequest("Crate",new Vector3(0.5f,1f,2f),new Vector3(0f,1f,0f),12,-7,3,2400f,500f),Request);
        string[]Arguments=PrefractureDebugService.PreviewArguments(Request,"out.json");
        Assert.Equal(["preview-prefracture","0.5","1","2","12","-7","3","out.json","0","1","0","2400","500"],Arguments);
    }

    [Fact]
    public void LeftOutMembersHaveTheComponentsDefaults(){
        var Node=Root("""FractureAsset="Crate" """);
        Assert.True(PrefractureDebugService.TryReadPreviewRequest(Node,out var Request,out var Problem),Problem);
        Assert.Equal(PrefractureDebugService.DefaultFragmentCount,Request.FragmentCount);
        Assert.Equal(PrefractureDebugService.DefaultSeed,Request.Seed);
        Assert.Equal(PrefractureDebugService.DefaultClusterCount,Request.ClusterCount);
        Assert.Equal(PrefractureDebugService.DefaultDensity,Request.Density);
        Assert.Equal(PrefractureDebugService.DefaultBondStrength,Request.BondStrength);
    }

    [Fact]
    public void TheDefaultsAreThePluginsDefaults(){
        var Text=File.ReadAllText(Path.Combine(RepositoryRoot(),"plugins","CSharpGameEngine.Destruction","Runtime","DestructionDefaults.cs"));
        int Constant(string Name)=>int.Parse(Regex.Match(Text,$@"public const int {Name}=(-?\d+);").Groups[1].Value,CultureInfo.InvariantCulture);
        Assert.Equal(PrefractureDebugService.DefaultFragmentCount,Constant("FragmentCount"));
        Assert.Equal(PrefractureDebugService.DefaultSeed,Constant("Seed"));
        Assert.Equal(PrefractureDebugService.DefaultClusterCount,Constant("ClusterCount"));
        Assert.Equal((int)PrefractureDebugService.DefaultDensity,Constant("Density"));
        Assert.Equal((int)PrefractureDebugService.DefaultBondStrength,Constant("BondStrength"));
    }

    [Theory]
    [InlineData("FragmentCount=1","2 to 256")]
    [InlineData("FragmentCount=0","2 to 256")]
    [InlineData("FragmentCount=257","2 to 256")]
    [InlineData("Density=0f","positive")]
    public void ACountOrNumberTheCookRefusesIsReportedInsteadOfPreviewed(string Members,string Reason){
        var Node=Root($"""FractureAsset="Crate",{Members}""");
        Assert.False(PrefractureDebugService.TryReadPreviewRequest(Node,out _,out var Problem));
        Assert.Contains(Reason,Problem);
    }

    [Fact]
    public void ASphereColliderIsNotCut(){
        var Node=Root("""FractureAsset="Crate" ""","new Collider { Shape=ColliderShape.Sphere }");
        Assert.False(PrefractureDebugService.TryReadPreviewRequest(Node,out _,out var Problem));
        Assert.Contains("Sphere",Problem);
    }

    [Fact]
    public void TheOverlayHasTheBondsAndTheOutlinesPlacedByTheNode(){
        PrefractureDebugService.PrefractureInspectionDocument Document=new(){
            SourceHash="hash",
            Fragments=[
                new(){ Index=0,R=-0.25f,Edges=[0f,0f,0f,1f,0f,0f] },
                new(){ Index=1,R=0.25f,Edges=[] },
            ],
            Bonds=[new(){ First=0,Second=1,Strength=8f,Area=0.5f }],
        };
        Assert.True(PrefractureDebugService.TryValidate(Document,out var Error),Error);
        List<ScenePhysicsFractureBond>Lines=[];
        Assert.True(PrefractureDebugService.AppendLines(Lines,Document,Matrix4x4.CreateTranslation(10f,0f,0f)));
        Assert.Equal(2,Lines.Count);
        Assert.Equal(new ScenePhysicsFractureBond(new Vector3(9.75f,0f,0f),new Vector3(10.25f,0f,0f),8f),Lines[0]);
        Assert.Equal(new ScenePhysicsFractureBond(new Vector3(10f,0f,0f),new Vector3(11f,0f,0f),0f,Outline:true),Lines[1]);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-1)]
    public void ABrokenOutlineIsRejected(int Case){
        PrefractureDebugService.PrefractureInspectionDocument Document=new(){
            SourceHash="hash",
            Fragments=[new(){ Index=0,Edges=Case>0?[0f,0f,0f,1f,0f]:[0f,0f,0f,1f,float.NaN,0f] },new(){ Index=1 }],
        };
        Assert.False(PrefractureDebugService.TryValidate(Document,out var Error));
        Assert.Equal("fragment outline is invalid",Error);
    }

    [Fact]
    public void TheCookedAssetOfASourceMeshIsTheNewestUnderTheBuildsAssets(){
        var Project=Path.Combine(Path.GetTempPath(),"cge-fracture-preview-"+Guid.NewGuid().ToString("N"));
        try {
            var Old=Path.Combine(Project,"bin","Debug","Assets","Destruction","Rock.cgefracture");
            var New=Path.Combine(Project,"bin","Release","Assets","Destruction","Rock.cgefracture");
            var Elsewhere=Path.Combine(Project,"bin","Release","Other","Rock.cgefracture");
            foreach(var File in new[]{ Old,New,Elsewhere }){
                Directory.CreateDirectory(Path.GetDirectoryName(File)!);
                System.IO.File.WriteAllBytes(File,[1]);
            }
            System.IO.File.SetLastWriteTimeUtc(Old,new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc));
            System.IO.File.SetLastWriteTimeUtc(New,new DateTime(2026,2,1,0,0,0,DateTimeKind.Utc));
            System.IO.File.SetLastWriteTimeUtc(Elsewhere,new DateTime(2026,3,1,0,0,0,DateTimeKind.Utc));
            Assert.Equal(New,PrefractureDebugService.FindCookedAsset(Project,"Rock"));
            Assert.Null(PrefractureDebugService.FindCookedAsset(Project,"Missing"));
        }
        finally {
            if(Directory.Exists(Project)){ Directory.Delete(Project,true); }
        }
    }

    private static string RepositoryRoot(){
        var Directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ Directory=Directory.Parent; }
        return Directory?.FullName??throw new InvalidOperationException("The repository root was not found.");
    }
}
