using CSharpGameEngine.Navigation.Cook;
using static CSharpGameEngine.Navigation.Tests.TestProject;
namespace CSharpGameEngine.Navigation.Tests;
/// <summary>ADR-0079/M0106: シーン JSON から、領域と静的な衝突形状を取り出す。物理と同じ元(StaticMesh と StaticMeshCollider、RigidBody の無い Collider)から読む。</summary>
public sealed class SceneGeometryTests {
    private static (float MinR,float MaxR,float MinU,float MaxU,float MinF,float MaxF)TriangleBounds(IEnumerable<NavTriangle>Triangles){
        var Points=Triangles.SelectMany(static T=>new[]{ T.A,T.B,T.C }).ToArray();
        return (Points.Min(static P=>Geometry.F(P.R)),Points.Max(static P=>Geometry.F(P.R)),Points.Min(static P=>Geometry.F(P.U)),Points.Max(static P=>Geometry.F(P.U)),Points.Min(static P=>Geometry.F(P.F)),Points.Max(static P=>Geometry.F(P.F)));
    }
    private static SceneGeometry Load(TestProject Project,string Scene){
        Assert.True(SceneGeometry.TryLoad(Scene,Project.Root,out var Geometry,out var Error),Error);
        return Geometry;
    }
    [Fact]
    public void ABoxCollider_IsPlacedByItsParentsAndItsOwnTransform(){
        using var Project=new TestProject();
        // 親(位置 10、拡縮 2)の子の箱(局所の位置 (1, 0, 0)、半分の大きさ (0.5, 0.5, 1)、90 度回す)。
        var Child=Node("Box",[Transform(R:1f,YawTurns:0.25f),BoxCollider(0.5f,0.5f,1f)]);
        var Root=Node("Root",[Transform(R:10f,ScaleR:2f,ScaleU:2f,ScaleF:2f),Bounds()],Child);
        var Scene=Project.WriteScene("Level.json","Level",Root);
        var Geometry_=Load(Project,Scene);
        Assert.Equal(12,Geometry_.Triangles.Count);
        var (MinR,MaxR,MinU,MaxU,MinF,MaxF)=TriangleBounds(Geometry_.Triangles);
        // 子の位置 (1,0,0) は親の拡縮で (2,0,0)、親の位置で (12,0,0)。半分の大きさは 90 度回って R が 1、F が 0.5、親の拡縮で R が 2、F が 1。
        Assert.Equal(12f-2f,MinR,0.01f);
        Assert.Equal(12f+2f,MaxR,0.01f);
        Assert.Equal(-1f,MinU,0.01f);
        Assert.Equal(1f,MaxU,0.01f);
        Assert.Equal(-1f,MinF,0.01f);
        Assert.Equal(1f,MaxF,0.01f);
    }
    [Fact]
    public void ABoxColliderTriangles_FaceOutwards_EvenUnderAMirroringScale(){
        using var Project=new TestProject();
        var Normal=Node("A",[Transform(),BoxCollider(1f,1f,1f)]);
        var Mirror=Node("B",[Transform(ScaleR:-1f),BoxCollider(1f,1f,1f)]);
        var Scene=Project.WriteScene("Level.json","Level",Node("Root",[Bounds()],Normal,Mirror));
        var Triangles=Load(Project,Scene).Triangles;
        Assert.Equal(24,Triangles.Count);
        foreach(var Triangle in Triangles){
            var Cross=LogicalVector3.Cross(Triangle.B-Triangle.A,Triangle.C-Triangle.A);
            var Center=(Triangle.A+Triangle.B+Triangle.C)/LogicalScalar.FromInt64(3);
            Assert.True(LogicalVector3.Dot(Cross,Center)>LogicalScalar.Zero,"A box triangle must face away from the box's center.");
        }
    }
    [Fact]
    public void AStaticMeshWithACollider_IsReadThroughItsGlbAndPlaced(){
        using var Project=new TestProject();
        var Asset=Project.CopyBox();
        var Floor=Node("Floor",[Transform(U:-0.5f,ScaleR:10f,ScaleU:1f,ScaleF:10f),StaticMesh(Asset)]);
        var Scene=Project.WriteScene("Level.json","Level",Node("Root",[Bounds()],Floor));
        var Geometry_=Load(Project,Scene);
        var (MinR,MaxR,MinU,MaxU,MinF,MaxF)=TriangleBounds(Geometry_.Triangles);
        Assert.Equal(-5f,MinR,0.05f);
        Assert.Equal(5f,MaxR,0.05f);
        Assert.Equal(-1f,MinU,0.05f);
        Assert.Equal(0f,MaxU,0.05f);
        Assert.Equal(-5f,MinF,0.05f);
        Assert.Equal(5f,MaxF,0.05f);
    }
    [Fact]
    public void DynamicBodies_Triggers_AndShapesItCannotRead_AddNoGeometry(){
        using var Project=new TestProject();
        var Body=Node("Crate",[Transform(),BoxCollider(1f,1f,1f),Component("CSharpGameEngine.Runtime.Physics.RigidBody")]);
        var Trigger=Node("Zone",[Transform(),BoxCollider(1f,1f,1f,",\"IsTrigger\": true")]);
        var Sphere=Node("Ball",[Transform(),Component("CSharpGameEngine.Runtime.Physics.Collider","{ \"Shape\": \"Sphere\", \"Radius\": 1 }")]);
        var Scene=Project.WriteScene("Level.json","Level",Node("Root",[Bounds()],Body,Trigger,Sphere));
        var Geometry_=Load(Project,Scene);
        Assert.Empty(Geometry_.Triangles);
        Assert.Contains(Geometry_.Messages,static Message=>Message.Contains("warning NAV008")&&Message.Contains("Sphere"));
    }
    [Fact]
    public void OtherStartupScenesAddTheirCollision_ButScenesSpawnedLaterDoNot(){
        using var Project=new TestProject();
        Project.WriteScene("Walls.json","Walls",Node("Walls",[Transform(),BoxCollider(1f,1f,1f)]),StartupOrder:1);
        Project.WriteScene("Effect.json","Effect",Node("Effect",[Transform(),BoxCollider(1f,1f,1f)]),StartupOrder:null);
        var Scene=Project.WriteScene("Volume.json","Volume",Node("Volume",[Bounds()]),StartupOrder:2);
        Assert.Equal(12,Load(Project,Scene).Triangles.Count);
    }
    [Fact]
    public void BoundsOfTheOtherScenes_AreNotVolumesOfThisBake(){
        using var Project=new TestProject();
        Project.WriteScene("Other.json","Other",Node("Volume",[Bounds("Other")]),StartupOrder:1);
        var Scene=Project.WriteScene("Volume.json","Volume",Node("Volume",[Bounds("Mine")]),StartupOrder:2);
        Assert.Equal("Mine",Assert.Single(Load(Project,Scene).Volumes).Id);
    }
    [Fact]
    public void AVolumeIsTheAxisAlignedBoxOfItsPlacement_AndTakesItsAgentFromTheMembers(){
        using var Project=new TestProject();
        var Root=Node("Volume",[Transform(R:2f,U:1f,F:-3f,ScaleR:2f),Bounds("Level",3f,2f,4f,",\"AgentRadius\": 0.5, \"CellSize\": 0.25, \"AgentMaxSlope\": 0.0833333333")]);
        var Volume=Assert.Single(Load(Project,Project.WriteScene("Volume.json","Volume",Root)).Volumes);
        Assert.Equal("Level",Volume.Id);
        // 半分の大きさ (3,2,4) の R だけ 2 倍: R は 2 ± 6、U は 1 ± 2、F は -3 ± 4。
        Assert.Equal(-4f,Geometry.F(Volume.Min.R),0.001f);
        Assert.Equal(8f,Geometry.F(Volume.Max.R),0.001f);
        Assert.Equal(-1f,Geometry.F(Volume.Min.U),0.001f);
        Assert.Equal(3f,Geometry.F(Volume.Max.U),0.001f);
        Assert.Equal(-7f,Geometry.F(Volume.Min.F),0.001f);
        Assert.Equal(1f,Geometry.F(Volume.Max.F),0.001f);
        Assert.Equal(0.5f,Geometry.F(Volume.Settings.AgentRadius),0.001f);
        Assert.Equal(0.25f,Geometry.F(Volume.Settings.CellSize),0.001f);
        Assert.Equal(30f,Geometry.F(Volume.Settings.AgentMaxSlopeDegrees),0.01f);
        // 書かなかった値は UE の TopDown の既定値。
        Assert.Equal(NavigationDefaults.AgentHeight,Volume.Settings.AgentHeight);
        Assert.Equal(NavigationDefaults.AgentMaxClimb,Volume.Settings.AgentMaxClimb);
        Assert.Equal(NavigationDefaults.CellHeight,Volume.Settings.CellHeight);
    }
    [Fact]
    public void TheSlopeKeyFollowsTheSchemaVersion(){
        // 版 3: 単位語なしのキーに回転数。版 2: C# の名前のキーに回転数。版 1: C# の名前のキーに度。
        using var Project=new TestProject();
        LogicalScalar Slope(int Version,string Key,string Value){
            var Root=Node("Volume",[Bounds("Level",Extra:$", \"{Key}\": {Value}")]);
            return Assert.Single(Load(Project,Project.WriteScene("V"+Version+".json","V"+Version,Root,SchemaVersion:Version)).Volumes).Settings.AgentMaxSlopeDegrees;
        }
        Assert.Equal(36f,Geometry.F(Slope(3,"AgentMaxSlope","0.1")),0.01f);
        Assert.Equal(36f,Geometry.F(Slope(2,"AgentMaxSlopeDegrees","0.1")),0.01f);
        Assert.Equal(36f,Geometry.F(Slope(1,"AgentMaxSlopeDegrees","36")),0.01f);
    }
    [Fact]
    public void BrokenInput_IsRefusedWithACodedMessage(){
        using var Project=new TestProject();
        var Broken=Path.Combine(Project.Root,"Scenes","Broken.json");
        File.WriteAllText(Broken,"{ not json");
        Assert.False(SceneGeometry.TryLoad(Broken,Project.Root,out _,out var Syntax));
        Assert.Contains("error NAV001",Syntax);
        var Other=Path.Combine(Project.Root,"Scenes","Other.json");
        File.WriteAllText(Other,"{ \"className\": \"X\" }");
        Assert.False(SceneGeometry.TryLoad(Other,Project.Root,out _,out var NoRoot));
        Assert.Contains("error NAV001",NoRoot);
        var BadTransform=Project.WriteScene("Bad.json","Bad",Node("Root",[Component("CSharpGameEngine.Entities.Transform","{ \"Scale\": { \"R\": 0, \"U\": 1, \"F\": 1 } }"),Bounds()]));
        Assert.False(SceneGeometry.TryLoad(BadTransform,Project.Root,out _,out var Scale));
        Assert.Contains("error NAV004",Scale);
        var BadId=Project.WriteScene("BadId.json","BadId",Node("Root",[Bounds("../escape")]));
        Assert.False(SceneGeometry.TryLoad(BadId,Project.Root,out _,out var Id));
        Assert.Contains("error NAV005",Id);
        var BadHalf=Project.WriteScene("BadHalf.json","BadHalf",Node("Root",[Bounds("Level",0f,1f,1f)]));
        Assert.False(SceneGeometry.TryLoad(BadHalf,Project.Root,out _,out var Half));
        Assert.Contains("error NAV005",Half);
    }
    [Fact]
    public void AMissingMeshFile_IsAnErrorNamingIt(){
        using var Project=new TestProject();
        var Scene=Project.WriteScene("Level.json","Level",Node("Root",[Bounds()],Node("Wall",[Transform(),StaticMesh("Assets/Meshes/Gone.glb")])));
        Assert.False(SceneGeometry.TryLoad(Scene,Project.Root,out _,out var Error));
        Assert.Contains("error NAV003",Error);
        Assert.Contains("Assets/Meshes/Gone.glb",Error);
    }
    [Fact]
    public void ACorruptMeshFile_IsAnErrorNamingItsNode(){
        using var Project=new TestProject();
        var Path_=System.IO.Path.Combine(Project.Root,"Assets","Meshes","Bad.glb");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);
        File.WriteAllText(Path_,"not a glb");
        var Scene=Project.WriteScene("Level.json","Level",Node("Root",[Bounds()],Node("Wall",[Transform(),StaticMesh("Assets/Meshes/Bad.glb")])));
        Assert.False(SceneGeometry.TryLoad(Scene,Project.Root,out _,out var Error));
        Assert.Contains("error NAV006",Error);
        Assert.Contains("'Wall'",Error);
    }
}
