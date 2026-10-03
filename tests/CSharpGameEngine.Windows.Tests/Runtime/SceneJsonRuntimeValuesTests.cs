using System.Collections.Immutable;
using System.Reflection;
using CSharpGameEngine.EditorPreview.Host;
using CSharpGameEngine.Entities;
using CSharpGameEngine.SceneAnalysis.Json;
using EcsWorld=CSharpGameEngine.Entities.World;
namespace CSharpGameEngine.Windows.Tests.Runtime;
/// <summary>
/// ADR-0070 decision 6: the runtime view reads the editor's scene JSON by reflection with the
/// generator's rules. For real scenes, every component it makes equals the one the generated code
/// put into the world from the same JSON.
/// </summary>
public sealed class SceneJsonRuntimeValuesTests {
    public enum Shade { Dark,Bright }
    public struct Pair(float X,float Y) {
        public float X=X;
        public float Y=Y;
    }
    public struct Sample() {
        public int Count=7;
        public byte Small;
        public bool Enabled;
        public string?Label;
        public Shade Mode;
        public Pair Offset;
        public LogicalScalar Scalar;
        public int?Maybe;
        public int[]Array=[];
        public List<float>List=[];
        public ImmutableArray<Shade>Shades=[];
    }
    public struct Aim {
        [CSharpGameEngine.Contracts.Angle(CSharpGameEngine.Contracts.AngleUnit.Degrees)] public float YawDegrees;
        [CSharpGameEngine.Contracts.Angle(CSharpGameEngine.Contracts.AngleUnit.Radians)] public float FovRadians;
        [CSharpGameEngine.Contracts.Angle(CSharpGameEngine.Contracts.AngleUnit.Degrees)] public float PitchDegrees;
        public float Pitch;
    }
    public readonly struct Cone([CSharpGameEngine.Contracts.Angle(CSharpGameEngine.Contracts.AngleUnit.Degrees)]float OuterDegrees) {
        public float OuterDegrees { get; }=OuterDegrees;
    }

    [Theory]
    [InlineData("PrimitiveShowcaseBoxScene")]
    [InlineData("EngineShowcaseScene")]
    [InlineData("PhysicsProjectileScene")]
    public void EveryComponent_EqualsWhatTheGeneratedSceneCreated(string SceneName){
        var Json=SceneJsonReader.Parse(File.ReadAllText(Path.Combine(VsixRepositoryRoot(),"UserEcsProject","Scenes",SceneName+".json")));
        var World=new EcsWorld();
        var Instance=(SceneInstance)typeof(EcsWorld).GetMethod(nameof(EcsWorld.InstantiateScene))!
            .MakeGenericMethod(typeof(EcsWorld).Assembly.GetType("UserEcsProject.Scenes."+SceneName,throwOnError:true)!)
            .Invoke(World,null)!;
        var Entities=World.GetSceneEntities(Instance);
        var Nodes=PreOrder(Json.Get("root")!).ToArray();
        Assert.Equal(Nodes.Length,Entities.Length);
        var Compared=0;
        for(var Index=0;Index<Nodes.Length;Index++){
            foreach(var Component in Nodes[Index].Get("components")!.Items){
                var Type=ResolveType(Component.Get("type")!.Text!);
                var Made=SceneJsonRuntimeValues.Component(Type,Component.Get("members")!,Type.Name,SceneJsonRuntimeValues.UsesTurns(Json));
                var Getter=typeof(EcsWorld).GetMethods().Single(Method=>Method.Name==nameof(EcsWorld.TryGetComponent)
                    &&Method.GetParameters() is [_,{ }Out]&&Out.ParameterType.GetElementType()==Type);
                object?[]Arguments=[Entities[Index],null];
                Assert.True((bool)Getter.Invoke(World,Arguments)!);
                Assert.Equal(Arguments[1],Made);
                Compared++;
            }
        }
        Assert.True(Compared>0);
    }

    [Fact]
    public void Camera_ReadsTheRadialDepthMappingAndKeepsStandardWhenAbsent(){
        // M0099: scenes written before radial depth have no DepthMapping member and stay standard.
        var Radial=(CSharpGameEngine.Runtime.Windows.Camera)SceneJsonRuntimeValues.Component(typeof(CSharpGameEngine.Runtime.Windows.Camera),
            SceneJsonReader.Parse("""{ "DepthMapping": "Radial", "RadialTransitionMeters": 2, "NearZ": 0.001, "FarZ": 1e18 }"""),"Camera");
        Assert.Equal(CSharpGameEngine.Runtime.Windows.CameraDepthMapping.Radial,Radial.DepthMapping);
        Assert.Equal(2f,Radial.RadialTransitionMeters);
        Assert.Equal(1e18f,Radial.FarZ);
        var Existing=(CSharpGameEngine.Runtime.Windows.Camera)SceneJsonRuntimeValues.Component(typeof(CSharpGameEngine.Runtime.Windows.Camera),
            SceneJsonReader.Parse("""{ "FarZ": 500 }"""),"Camera");
        Assert.Equal(CSharpGameEngine.Runtime.Windows.CameraDepthMapping.Standard,Existing.DepthMapping);
        Assert.Equal(1f,Existing.RadialTransitionMeters);
    }

    [Fact]
    public void Values_FollowTheGeneratorsRules(){
        var Json=SceneJsonReader.Parse("""
            { "Small": 200, "Enabled": true, "Label": "Lamp", "Mode": "Bright", "Offset": { "X": 1.5, "Y": -2 },
              "Scalar": 0.25, "Maybe": null, "Array": [1, 2], "List": [0.5], "Shades": ["Dark", "Bright"] }
            """);

        var Value=(Sample)SceneJsonRuntimeValues.Component(typeof(Sample),Json,"Sample");

        Assert.Equal(7,Value.Count);
        Assert.Equal((byte)200,Value.Small);
        Assert.True(Value.Enabled);
        Assert.Equal("Lamp",Value.Label);
        Assert.Equal(Shade.Bright,Value.Mode);
        Assert.Equal((1.5f,-2f),(Value.Offset.X,Value.Offset.Y));
        Assert.Equal((LogicalScalar)0.25f,Value.Scalar);
        Assert.Null(Value.Maybe);
        Assert.Equal([1,2],Value.Array);
        Assert.Equal([0.5f],Value.List);
        Assert.Equal(new[]{Shade.Dark,Shade.Bright},Value.Shades.ToArray());
        Assert.Throws<InvalidOperationException>(()=>SceneJsonRuntimeValues.Component(typeof(Sample),SceneJsonReader.Parse("""{ "Mode": "Dim" }"""),"Sample"));
        Assert.Throws<InvalidOperationException>(()=>SceneJsonRuntimeValues.Component(typeof(Sample),SceneJsonReader.Parse("""{ "Small": 300 }"""),"Sample"));
        Assert.Throws<InvalidOperationException>(()=>SceneJsonRuntimeValues.Component(typeof(Sample),SceneJsonReader.Parse("""{ "Missing": 1 }"""),"Sample"));
    }

    /// <summary>ADR-0074 version 3: an angle's key may leave out the C# unit word, as the generator reads it.</summary>
    [Fact]
    public void AngleKeys_ReadWithoutTheUnitWordAndByTheCSharpName(){
        var Unitless=(Aim)SceneJsonRuntimeValues.Component(typeof(Aim),
            SceneJsonReader.Parse("""{ "Yaw": 0.25, "Fov": 0.25, "PitchDegrees": 0.125, "Pitch": 7 }"""),"Aim",Turns:true);
        Assert.Equal((90f,1.5707964f,45f,7f),(Unitless.YawDegrees,Unitless.FovRadians,Unitless.PitchDegrees,Unitless.Pitch));
        var Named=(Aim)SceneJsonRuntimeValues.Component(typeof(Aim),
            SceneJsonReader.Parse("""{ "YawDegrees": 0.25, "FovRadians": 0.25 }"""),"Aim",Turns:true);
        Assert.Equal((90f,1.5707964f),(Named.YawDegrees,Named.FovRadians));
        var Cone=(Cone)SceneJsonRuntimeValues.Component(typeof(Cone),SceneJsonReader.Parse("""{ "Outer": 0.25 }"""),"Cone",Turns:true);
        Assert.Equal(90f,Cone.OuterDegrees);
    }

    private static IEnumerable<SceneJsonValue>PreOrder(SceneJsonValue Node){
        yield return Node;
        foreach(var Child in Node.Get("children")!.Items){
            foreach(var Descendant in PreOrder(Child)){ yield return Descendant; }
        }
    }

    private static Type ResolveType(string FullName)=>
        AppDomain.CurrentDomain.GetAssemblies().Select(Assembly=>Assembly.GetType(FullName,throwOnError:false)).FirstOrDefault(static Type=>Type is not null)
        ??throw new InvalidOperationException($"Type {FullName} was not found.");

    private static string VsixRepositoryRoot(){
        var Directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ Directory=Directory.Parent; }
        return Directory?.FullName??throw new DirectoryNotFoundException();
    }
}
