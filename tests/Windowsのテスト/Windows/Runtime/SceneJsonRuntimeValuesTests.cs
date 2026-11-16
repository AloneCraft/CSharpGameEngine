extern alias PreviewHost;
using Xunit;
using CSharpGameEngine.Numerics;
using System.Collections.Immutable;
using System.Reflection;
using PreviewHost::CSharpGameEngine.EditorPreview.Host;
using CSharpGameEngine.Entities;
using PreviewHost::CSharpGameEngine.SceneAnalysis.Json;
using EcsWorld=CSharpGameEngine.Entities.World;
namespace CSharpGameEngine.Windows.Tests.Runtime;
/// <summary>
/// ADR-0070 決定 6：ランタイムビューはジェネレーターの規則で、リフレクションを使ってエディターのシーン JSON を読み込む。実際のシーンでは、生成コードが同じ JSON からワールドに配置したものと、生成されるすべてのコンポーネントが一致する。
/// </summary>
public sealed class SceneJsonRuntimeValuesTests {
    private static object ReadComponent(Type Type,SceneJsonValue Members,string Where)=>SceneJsonRuntimeValues.Component(Type,Members,Where,CSharpGameEngine.Generated.GeneratedEditorPreviewBridge.ParseRuntimeLogicalScalar);
    [Theory]
    [InlineData("1.0000000000000000000542101086242752217003726400434970855712890625")]
    [InlineData("1844674407370955161.25")]
    public void FixedNumbersUseTheGamesGeneratedReaderWithoutFloatNarrowing(string Text){
        var Value=(Sample)ReadComponent(typeof(Sample),SceneJsonReader.Parse("{\"Scalar\":"+Text+"}"),"Sample");
        Assert.Equal(LogicalScalar.ParseInvariant(Text),Value.Scalar);
        Assert.Throws<InvalidOperationException>(()=>SceneJsonRuntimeValues.Component(typeof(Sample),SceneJsonReader.Parse("{\"Scalar\":"+Text+"}"),"Sample"));
        Assert.Throws<InvalidOperationException>(()=>SceneJsonRuntimeValues.Component(typeof(Sample),SceneJsonReader.Parse("{\"Scalar\":"+Text+"}"),"Sample",ParseLogicalScalar:static _=>1f));
    }
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
    public struct ByRef {
        public ByRef(ref int A){ this.A=A*10; }
        public int A;
    }
    public struct Retired {
        [Obsolete("Use the members.",true)] public Retired(int A){ this.A=A*10; }
        public int A;
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
                var Made=ReadComponent(Type,Component.Get("members")!,Type.Name);
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
        // Q64.64 の範囲を超える位置や負のライト色は、型独自の変換または
        // コンストラクターによって拒否される。他の不一致と同じく InvalidOperationException として表面化し、ランタイムビューは
        // リフレクションのラッパーとしてではなく、その例外をスキップする（最後に正常だった値を維持する）。
        var Error=Assert.Throws<InvalidOperationException>(()=>ReadComponent(Type,SceneJsonReader.Parse(Json),Type.Name));
        Assert.Contains("does not fit",Error.Message,StringComparison.Ordinal);
    }

    [Fact]
    public void Camera_ReadsTheNativeCurveAndPreservesItsDefaults(){
        var Radial=(CSharpGameEngine.Runtime.Windows.Camera)ReadComponent(typeof(CSharpGameEngine.Runtime.Windows.Camera),
            SceneJsonReader.Parse("""{ "RadialTransitionMeters": 2, "NearZ": 0.001, "FarZ": 1e18 }"""),"Camera");
        Assert.Equal(2f,Radial.RadialTransitionMeters);
        Assert.Equal(1e18f,Radial.FarZ);
        var Existing=(CSharpGameEngine.Runtime.Windows.Camera)ReadComponent(typeof(CSharpGameEngine.Runtime.Windows.Camera),
            SceneJsonReader.Parse("""{ "FarZ": 500 }"""),"Camera");
        Assert.Equal(1f,Existing.RadialTransitionMeters);
        Assert.Equal(500f,Existing.FarZ);
    }

    [Fact]
    public void Values_FollowTheGeneratorsRules(){
        var Json=SceneJsonReader.Parse("""
            { "Small": 200, "Enabled": true, "Label": "Lamp", "Mode": "Bright", "Offset": { "X": 1.5, "Y": -2 },
              "Scalar": 0.25, "Maybe": null, "Array": [1, 2], "List": [0.5], "Shades": ["Dark", "Bright"] }
            """);

        var Value=(Sample)ReadComponent(typeof(Sample),Json,"Sample");

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
        Assert.Throws<InvalidOperationException>(()=>ReadComponent(typeof(Sample),SceneJsonReader.Parse("""{ "Mode": "Dim" }"""),"Sample"));
        Assert.Throws<InvalidOperationException>(()=>ReadComponent(typeof(Sample),SceneJsonReader.Parse("""{ "Small": 300 }"""),"Sample"));
        Assert.Throws<InvalidOperationException>(()=>ReadComponent(typeof(Sample),SceneJsonReader.Parse("""{ "Missing": 1 }"""),"Sample"));
    }

    /// <summary>
    /// 数値はジェネレーターが出力するリテラルと同様に読み込む。decimal は指数表記を使える（<c>1.5e3m</c> はコンパイル可能）。メンバーの型の範囲を超える数値はビルドエラー (SCENE203) となるため、ここでは無限大になるのではなく不適合として扱う。
    /// </summary>
    [Fact]
    public void Numbers_ReadAsTheGeneratedLiteralsDo(){
        var Value=(Measures)ReadComponent(typeof(Measures),
            SceneJsonReader.Parse("""{ "Price": 1.5e3, "Ratio": 2E-3, "Speed": 1e-2 }"""),"Measures");
        Assert.Equal((1.5e3m,2E-3d,1e-2f),(Value.Price,Value.Ratio,Value.Speed));
        // オブジェクトまたはインターフェイス型のメンバーには、生成された float リテラルをボックス化して設定する。
        var Boxed=(Measures)ReadComponent(typeof(Measures),SceneJsonReader.Parse("""{ "Boxed": 2.5, "Comparable": 3 }"""),"Measures");
        Assert.Equal((object)2.5f,Boxed.Boxed);
        Assert.Equal((IComparable)3f,Boxed.Comparable);
        foreach(var Json in new[]{"""{ "Speed": 1e39 }""","""{ "Ratio": 1e400 }""","""{ "Price": 1e29 }""","""{ "Boxed": 1e39 }"""}){
            Assert.Throws<InvalidOperationException>(()=>ReadComponent(typeof(Measures),SceneJsonReader.Parse(Json),"Measures"));
        }
    }

    /// <summary>
    /// JSON 配列は、生成されたコレクション式が作る各コレクションを埋める。以前は IList 型だけが対象で、HashSet メンバーは使えず、ReadOnlyCollection や ImmutableList のメンバーは MissingMethodException をスローしてランタイムビューを終了させていた。生成コードで作成できない Queue (SCENE203) は対象外となる。
    /// </summary>
    [Fact]
    public void Collections_FillAsTheGeneratedCollectionExpressionDoes(){
        var Value=(Shelves)ReadComponent(typeof(Shelves),SceneJsonReader.Parse(
            """{ "Tags": [3, 1], "Sorted": [5, 2], "Plain": [7], "Fixed": [8, 9], "Frozen": [4], "View": [6] }"""),"Shelves");
        Assert.Equal(new HashSet<int>{1,3},Value.Tags);
        Assert.Equal([2,5],Value.Sorted);
        Assert.Equal([7],Value.Plain);
        Assert.Equal([8,9],Value.Fixed);
        Assert.Equal([4],Value.Frozen);
        Assert.Equal([6],Value.View);
        Assert.Throws<InvalidOperationException>(()=>ReadComponent(typeof(Shelves),SceneJsonReader.Parse("""{ "Waiting": [1] }"""),"Shelves"));
    }

    /// <summary>
    /// ジェネレーターは ref/out パラメーターを持つコンストラクターや、エラーとして obsolete 指定されたコンストラクターを呼び出さず、代わりにメンバーを設定する。ランタイムビューも同様にする。以前はそれらを呼び出しており、値を変更するコンストラクターでは、ビューとビルド済みゲームが異なっていた。
    /// </summary>
    [Fact]
    public void Constructors_TheGeneratorSkips_AreSkipped(){
        Assert.Equal(4,((ByRef)ReadComponent(typeof(ByRef),SceneJsonReader.Parse("""{ "A": 4 }"""),"ByRef")).A);
        Assert.Equal(4,((Retired)ReadComponent(typeof(Retired),SceneJsonReader.Parse("""{ "A": 4 }"""),"Retired")).A);
    }

    /// <summary>
    /// ランタイムビューはドキュメントに記された名前、つまり C# 名 (Outer.Inner) またはメタデータ名 (Outer+Inner) で入れ子のコンポーネント型を見つける。以前はメタデータ名でしか見つからず、そのようなシーンでは再ビルドが必要とされた。
    /// </summary>
    [Theory]
    [InlineData("CSharpGameEngine.Windows.Tests.Runtime.SceneJsonRuntimeValuesTests.Pair")]
    [InlineData("CSharpGameEngine.Windows.Tests.Runtime.SceneJsonRuntimeValuesTests+Pair")]
    public void NestedComponentTypes_AreFoundByEitherName(string Name){
        Assert.Equal(typeof(Pair),SceneJsonRuntimeValues.FindType([typeof(Pair).Assembly],Name));
        Assert.Null(SceneJsonRuntimeValues.FindType([typeof(Pair).Assembly],Name+"Missing"));
    }

    /// <summary>
    /// ADR-0074 バージョン 3：角度は回転数で、キーは C# の単位語を省いた名前。C# の名前の角度キーは、ジェネレーターと同じく読まない（ADR-0098 §5 規則 2）。
    /// </summary>
    [Fact]
    public void AngleKeys_ReadWithoutTheUnitWordAndRejectTheCSharpName(){
        var Unitless=(Aim)ReadComponent(typeof(Aim),
            SceneJsonReader.Parse("""{ "Yaw": 0.25, "Fov": 0.25, "PitchDegrees": 0.125, "Pitch": 7 }"""),"Aim");
        Assert.Equal((90f,1.5707964f,45f,7f),(Unitless.YawDegrees,Unitless.FovRadians,Unitless.PitchDegrees,Unitless.Pitch));
        var Field=Assert.Throws<InvalidOperationException>(()=>ReadComponent(typeof(Aim),SceneJsonReader.Parse("""{ "YawDegrees": 0.25 }"""),"Aim"));
        Assert.Contains("'YawDegrees' is no longer read; write it as 'Yaw'",Field.Message,StringComparison.Ordinal);
        var Cone=(Cone)ReadComponent(typeof(Cone),SceneJsonReader.Parse("""{ "Outer": 0.25 }"""),"Cone");
        Assert.Equal(90f,Cone.OuterDegrees);
        var Parameter=Assert.Throws<InvalidOperationException>(()=>ReadComponent(typeof(Cone),SceneJsonReader.Parse("""{ "OuterDegrees": 0.25 }"""),"Cone"));
        Assert.Contains("'OuterDegrees' is no longer read; write it as 'Outer'",Parameter.Message,StringComparison.Ordinal);
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
