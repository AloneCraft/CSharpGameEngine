using System.Text.Json;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Physics;
using Xunit.Abstractions;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0112: Vehicle テンプレートのレベルの地面。UE の Landscape(起伏のある 126 m 四方)と、その上の Landscape Spline に沿った縁石つきのトラックを、
/// 静的な三角形メッシュとして取り込んだ(平らな板 1 枚で代えていた)。ここでは取り込んだ glb をゲームの <c>StaticMeshCollider</c> と同じ三角形で物理に置き、
/// UE の衝突で測った高さ(<c>tools/UnrealExport/probe_vehicle_landscape_heights.py</c>・<c>probe_vehicle_surfaces.py</c> の値。UE 座標の cm)と照合し、車が走れることを確かめる。
/// 値の出典: 起伏と標本は UE の Landscape へのライン トレース。トラックの路面の高さ 7.744 cm は SM_Track_10M の路面(0.0481 m)× スプラインの縮尺 1.6076。
/// </summary>
public sealed class VehicleLevelTests(ITestOutputHelper Output) {
    private static float F(LogicalScalar Value)=>Value.TryToSingle(out var Result)?Result:float.NaN;
    private static string RepositoryRoot(){
        var Root=AppContext.BaseDirectory;
        while(Root is not null&&!File.Exists(Path.Combine(Root,"CSharpGameEngine.slnx"))){ Root=Path.GetDirectoryName(Root); }
        return Root??throw new InvalidOperationException("CSharpGameEngine.slnx was not found above the test binaries.");
    }
    private static string Template(params string[]Parts)=>Path.Combine([RepositoryRoot(),"Templates","CSharpGameEngine.Templates.Vehicle",.. Parts]);
    private static string Mesh(string Name)=>Template("Assets","Stage","Lvl_VehicleBasic","Meshes",Name);
    /// <summary>レベルの 2 つの面(シーンの <c>Landscape</c> と <c>Track</c> と同じ材質)だけを置いた物理世界の上の、UE の PlayerStart(R 44.5, U 1.02, F 0、半回転)の車。</summary>
    private static ChaosVehicleTrack TheLevel(LogicalVector3?Start=null,LogicalQuaternion?Rotation=null){
        var Car=new ChaosVehicleTrack(Floor:false,Start:Start??new LogicalVector3(LogicalScalar.FromSingle(44.5f),LogicalScalar.FromSingle(1.02f),0),
            Rotation:Rotation??LogicalQuaternion.FromEulerRuf(new LogicalVector3(0,LogicalMath.Pi,0)));
        Car.AddGltf(Mesh("Landscape_Terrain.glb"),LogicalScalar.One,LogicalScalar.FromSingle(0.4f));
        Car.AddGltf(Mesh("Track_Road.glb"),LogicalScalar.One,LogicalScalar.Zero);
        return Car;
    }
    /// <summary>UE 座標(cm)の (X, Y) の真上から真下へ撃った光線が、レベルのどこに当たるか(m)。当たらなければ NaN。エンジンの座標は R = X、F = −Y。</summary>
    private static float HeightAt(ChaosVehicleTrack Level,float UeXCentimetres,float UeYCentimetres){
        var Hits=new List<PhysicsRayHit>();
        Assert.Equal(PhysicsQueryStatus.Success,Level.World.Raycast(new PhysicsRay(
            new LogicalVector3(LogicalScalar.FromSingle(UeXCentimetres/100f),50,LogicalScalar.FromSingle(-UeYCentimetres/100f)),-LogicalVector3.UnitU,100),PhysicsLayerMask.All,Hits).Status);
        return Hits.Count==0?float.NaN:Hits.Max(static Hit=>F(Hit.Point.U));
    }
    /// <summary>UE の Landscape への衝突の光線が、起伏の上で返した高さ(UE の cm)。メッシュの対角線が UE と違えば、格子の間の点で数 cm〜1 m ずれる。</summary>
    public static TheoryData<float,float,float>UeLandscapeSamples=>new() {
        { 243.382f,677.547f,281.761f },
        { 59.754f,656.718f,280.175f },
        { -1118.003f,-1511.627f,164.235f },
        { 2113.488f,247.043f,163.779f },
        { -886.441f,2239.103f,163.403f },
        { -295.657f,3031.22f,34.239f },
        { -2873.889f,1432.321f,33.971f },
        { -128.516f,3025.096f,30.648f },
        { -5400f,-2000f,0f },
    };
    [Theory]
    [MemberData(nameof(UeLandscapeSamples))]
    public void TheTerrainIsAsHighAsUesLandscapeAtItsOwnTracedPoints(float X,float Y,float UeHeight){
        var Level=TheLevel(Start:new LogicalVector3(0,50,0));
        var Height=HeightAt(Level,X,Y);
        Assert.False(float.IsNaN(Height),$"nothing under ({X}, {Y})");
        // UE の三角形の分け方(各格子の (x, y)-(x+1, y+1))と同じなので、格子の間の点も数 mm で一致する。
        Assert.InRange(Height*100f,UeHeight-0.5f,UeHeight+0.5f);
    }
    [Fact]
    public void TheHillRisesAlmostThreeMetresAndTheArenaAroundItIsFlat(){
        var Level=TheLevel(Start:new LogicalVector3(0,50,0));
        Assert.InRange(HeightAt(Level,243f,677f),2.7f,2.9f);
        foreach(var (X,Y) in new[]{(-6000f,-6000f),(6000f,6000f),(-6000f,6000f),(6000f,-6000f),(0f,-5500f)}){
            Assert.InRange(HeightAt(Level,X,Y),-0.0001f,0.0001f);
        }
    }
    [Fact]
    public void TheTrackRoadRingIsClosedAndItsSurfaceSitsOnTheGroundAtThePavementHeight(){
        var Level=TheLevel(Start:new LogicalVector3(0,50,0));
        // 半径 45 m の輪の路面。UE の光線が測った高さは 7.744 cm(SM_Track_10M の路面 4.817 cm × 縮尺 1.6076)。継ぎ目を避けた角度で一周する。
        for(var Degrees=3;Degrees<360;Degrees+=11){
            var Radians=Degrees*MathF.PI/180f;
            var Height=HeightAt(Level,4500f*MathF.Cos(Radians),4500f*MathF.Sin(Radians));
            Assert.InRange(Height*100f,7.7f,7.8f);
        }
    }
    [Fact]
    public void TheCurbsAreDrawnButNotCollidedWithAndRiseAboveThePavementAtTheRingsEdges(){
        // 縁石は路面から立ち上がる(SM_Track_10M の縁石の頂 0.123 m × 縮尺 1.6076 = 0.198 m)。縁石は描くだけで衝突は持たない: エンジンの箱と三角形メッシュの接触は
        // 1 tick に 1 つの面の向きしか使わないので、縁石の斜面と平らな地面にまたがった車体が地面の支えを失って沈んだ(縁石と路面を 1 つにしても、別にしても同じ)。
        // 縁石の頂の高さ・輪の半径(45 m)からの距離を glb の頂点で確かめ、シーンで衝突が付いていないことも確かめる。
        var Result=CSharpGameEngine.Import.Gltf.GltfImporter.ImportScene(Mesh("Track_Curbs.glb"));
        Assert.True(Result.Success,string.Join("; ",Result.Diagnostics.Select(static Item=>Item.Message)));
        var Points=Result.Scene!.Meshes.SelectMany(static Item=>Item.Positions).ToArray();
        Assert.InRange(Points.Max(static Point=>Point.Y),0.19f,0.21f);
        var Distances=Points.Select(static Point=>MathF.Sqrt((Point.X*Point.X)+(Point.Z*Point.Z))).ToArray();
        Assert.InRange(Distances.Min(),36.5f,38.5f);
        Assert.InRange(Distances.Max(),51.5f,54.5f);
        using var Scene=JsonDocument.Parse(File.ReadAllText(Template("Scenes","Lvl_VehicleBasic.json")));
        var Curbs=Scene.RootElement.GetProperty("root").GetProperty("children").EnumerateArray().First(static Child=>Child.GetProperty("name").GetString()=="TrackCurbs");
        Assert.DoesNotContain(Curbs.GetProperty("components").EnumerateArray(),static Item=>Item.GetProperty("type").GetString()!.EndsWith(".StaticMeshCollider",StringComparison.Ordinal));
    }
    [Fact]
    public void TheCarDroppedAtUesPlayerStartSettlesOnItsFourWheelsOnTheTrack(){
        var Car=TheLevel();
        Car.Run(240);
        Output.WriteLine(Car.Describe());
        Assert.Equal(4,Car.State.WheelsOnGround);
        // 路面は 7.7 cm。静止の重心の高さは平らな床の 0.5〜0.65 m(RestsOnItsSuspensions...)にその分を足した値。
        Assert.InRange(Car.Y,0.57f,0.73f);
        Assert.InRange(Car.UpDot,0.999f,1.0001f);
    }
    [Theory]
    [InlineData(60f,1f,0f,0.5f,0.65f)]
    [InlineData(3f,4f,-6f,2.5f,3.5f)]
    [InlineData(45f,1f,-20f,0.5f,0.75f)]
    [InlineData(48f,1f,-20f,0.5f,0.75f)]
    [InlineData(51f,1f,-20f,0.5f,0.75f)]
    [InlineData(54f,1f,-20f,0.5f,0.75f)]
    public void ACarDroppedOnTheBareTerrainRestsOnItsWheels(float R,float U,float F,float MinimumHeight,float MaximumHeight){
        var Car=TheLevel(new LogicalVector3(LogicalScalar.FromSingle(R),LogicalScalar.FromSingle(U),LogicalScalar.FromSingle(F)));
        Car.Run(300);
        Output.WriteLine(Car.Describe());
        Assert.True(Car.Y>MinimumHeight&&Car.Y<MaximumHeight,Car.Describe());
        Assert.Equal(4,Car.State.WheelsOnGround);
    }
    [Fact]
    public void ACarHoldingTheThrottleFromThePlayerStartLeavesTheRoadOverItsEdgeAndKeepsDrivingOnTheTerrain(){
        // PlayerStart は輪の路面の上(半径 44.5 m)で、車は輪の接線(−F)へ走る。約 28 m で路面の外側の端(半径 51.3 m)を越えて地形へ出る(輪の外は壁まで平ら)。
        var Car=TheLevel();
        Car.Run(60);
        for(var Second=0;Second<4;Second++){
            Car.Run(60,Throttle:1f);
            Output.WriteLine(Car.Describe());
            Assert.True(Car.Y>0.4f,$"the car fell through the level: {Car.Describe()}");
        }
        Assert.True(Car.Z<-35f,Car.Describe());
        Assert.InRange(Car.Y,0.45f,0.75f);
        Assert.True(Car.KilometersPerHour>60f,Car.Describe());
    }
    [Fact]
    public void TheSceneUsesTheLandscapeAndTheTrackInsteadOfAFlatSlab(){
        using var Scene=JsonDocument.Parse(File.ReadAllText(Template("Scenes","Lvl_VehicleBasic.json")));
        var Children=Scene.RootElement.GetProperty("root").GetProperty("children").EnumerateArray().ToDictionary(static Child=>Child.GetProperty("name").GetString()!,static Child=>Child);
        Assert.DoesNotContain("Ground",Children.Keys);
        Assert.Contains("TrackCurbs",Children.Keys);
        foreach(var (Name,Restitution) in new[]{("Landscape",0.4f),("Track",0f)}){
            var Components=Children[Name].GetProperty("components").EnumerateArray().ToList();
            var Path=Components.First(static Item=>Item.GetProperty("type").GetString()!.EndsWith(".StaticMesh",StringComparison.Ordinal)).GetProperty("members").GetProperty("AssetPath").GetString()!;
            Assert.True(File.Exists(Template(Path.Split('/'))),Path);
            var Material=Components.First(static Item=>Item.GetProperty("type").GetString()!.EndsWith(".StaticMeshCollider",StringComparison.Ordinal)).GetProperty("members").GetProperty("Material");
            // UE の物理マテリアル: Landscape の Ground 層は Slippery(摩擦 1、反発 0.4)、トラックは NonSlippery(摩擦 1、反発 0)。
            Assert.Equal(1f,Material.GetProperty("Friction").GetSingle());
            Assert.Equal(Restitution,Material.GetProperty("Restitution").GetSingle());
        }
    }
}
