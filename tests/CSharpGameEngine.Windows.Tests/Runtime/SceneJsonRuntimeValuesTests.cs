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
    public struct Shelves {
        public HashSet<int> Tags;
        public SortedSet<int> Sorted;
        public System.Collections.ObjectModel.Collection<int> Plain;
        public System.Collections.ObjectModel.ReadOnlyCollection<int> Fixed;
        public ImmutableList<int> Frozen;
        public IReadOnlyList<int> View;
        public Queue<int> Waiting;
    }
    public struct Measures {
        public decimal Price;
        public double Ratio;
        public float Speed;
        public object Boxed;
        public IComparable Comparable;
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

    [Theory]
    [InlineData(typeof(Transform),"""{ "Position": { "R": 1e30, "U": 0, "F": 0 } }""")]
    [InlineData(typeof(CSharpGameEngine.Runtime.Lighting.PointLight),"""{ "Color": { "R": -1, "G": 0, "B": 0 } }""")]
    public void ValuesTheTypeItselfRejects_DoNotFit(Type Type,string Json){
        // A position beyond the Q64.64 range or a negative light colour is rejected by the type's own conversion or
        // constructor. Like any other mismatch it must surface as InvalidOperationException, which the runtime view
        // skips (keeping the last good value), and not as the reflection wrapper.
        var Error=Assert.Throws<InvalidOperationException>(()=>SceneJsonRuntimeValues.Component(Type,SceneJsonReader.Parse(Json),Type.Name));
        Assert.Contains("does not fit",Error.Message,StringComparison.Ordinal);
    }

    [Fact]
    public void Camera_ReadsTheDepthMappingAndIsRadialWhenAbsent(){
        // M0100: radial is the default, so a scene without a DepthMapping member renders radial; standard is written out.
        var Radial=(CSharpGameEngine.Runtime.Windows.Camera)SceneJsonRuntimeValues.Component(typeof(CSharpGameEngine.Runtime.Windows.Camera),
            SceneJsonReader.Parse("""{ "DepthMapping": "Radial", "RadialTransitionMeters": 2, "NearZ": 0.001, "FarZ": 1e18 }"""),"Camera");
        Assert.Equal(CSharpGameEngine.Runtime.Windows.CameraDepthMapping.Radial,Radial.DepthMapping);
        Assert.Equal(2f,Radial.RadialTransitionMeters);
        Assert.Equal(1e18f,Radial.FarZ);
        var Existing=(CSharpGameEngine.Runtime.Windows.Camera)SceneJsonRuntimeValues.Component(typeof(CSharpGameEngine.Runtime.Windows.Camera),
            SceneJsonReader.Parse("""{ "FarZ": 500 }"""),"Camera");
        Assert.Equal(CSharpGameEngine.Runtime.Windows.CameraDepthMapping.Radial,Existing.DepthMapping);
        Assert.Equal(1f,Existing.RadialTransitionMeters);
        var Standard=(CSharpGameEngine.Runtime.Windows.Camera)SceneJsonRuntimeValues.Component(typeof(CSharpGameEngine.Runtime.Windows.Camera),
            SceneJsonReader.Parse("""{ "DepthMapping": "Standard" }"""),"Camera");
        Assert.Equal(CSharpGameEngine.Runtime.Windows.CameraDepthMapping.Standard,Standard.DepthMapping);
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

    /// <summary>
    /// A number reads as the generator's literal of it does: a decimal takes an exponent (<c>1.5e3m</c> compiles), and a
    /// number beyond its member's type is a build error (SCENE203), so it does not fit here rather than becoming infinity.
    /// </summary>
    [Fact]
    public void Numbers_ReadAsTheGeneratedLiteralsDo(){
        var Value=(Measures)SceneJsonRuntimeValues.Component(typeof(Measures),
            SceneJsonReader.Parse("""{ "Price": 1.5e3, "Ratio": 2E-3, "Speed": 1e-2 }"""),"Measures");
        Assert.Equal((1.5e3m,2E-3d,1e-2f),(Value.Price,Value.Ratio,Value.Speed));
        // An object or interface member takes the generated float literal boxed.
        var Boxed=(Measures)SceneJsonRuntimeValues.Component(typeof(Measures),SceneJsonReader.Parse("""{ "Boxed": 2.5, "Comparable": 3 }"""),"Measures");
        Assert.Equal((object)2.5f,Boxed.Boxed);
        Assert.Equal((IComparable)3f,Boxed.Comparable);
        foreach(var Json in new[]{"""{ "Speed": 1e39 }""","""{ "Ratio": 1e400 }""","""{ "Price": 1e29 }""","""{ "Boxed": 1e39 }"""}){
            Assert.Throws<InvalidOperationException>(()=>SceneJsonRuntimeValues.Component(typeof(Measures),SceneJsonReader.Parse(Json),"Measures"));
        }
    }

    /// <summary>
    /// A JSON array fills each collection the generated collection expression creates. Only IList types were filled, so a
    /// HashSet member did not fit, and a ReadOnlyCollection or ImmutableList member threw MissingMethodException past the
    /// runtime view, which then ended. A Queue, which the generated code cannot create (SCENE203), does not fit.
    /// </summary>
    [Fact]
    public void Collections_FillAsTheGeneratedCollectionExpressionDoes(){
        var Value=(Shelves)SceneJsonRuntimeValues.Component(typeof(Shelves),SceneJsonReader.Parse(
            """{ "Tags": [3, 1], "Sorted": [5, 2], "Plain": [7], "Fixed": [8, 9], "Frozen": [4], "View": [6] }"""),"Shelves");
        Assert.Equal(new HashSet<int>{1,3},Value.Tags);
        Assert.Equal([2,5],Value.Sorted);
        Assert.Equal([7],Value.Plain);
        Assert.Equal([8,9],Value.Fixed);
        Assert.Equal([4],Value.Frozen);
        Assert.Equal([6],Value.View);
        Assert.Throws<InvalidOperationException>(()=>SceneJsonRuntimeValues.Component(typeof(Shelves),SceneJsonReader.Parse("""{ "Waiting": [1] }"""),"Shelves"));
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
