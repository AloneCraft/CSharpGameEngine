using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// Scene DSLのノード形状が、SystemのUpdate/Spawn形状と<b>同一のArchetype空間</b>へ入ることの検証
/// (ADR-0040 I節)。
///
/// <para>
/// 合流点は<see cref="GameSystemIndexAssigner.Assign"/>の<c>AdditionalComponentSets</c>ただ1つで、
/// Scene由来の集合は<b>実Systemの後に</b>登録される。この順序により、Sceneを足しても既存のArchetype番号が
/// 動かない(ADR-0038が確立した保証の継承)。
/// </para>
/// </summary>
public sealed class SceneArchetypeIntegrationTests {
    private const string Components="""
        namespace Sample;

        public struct Transform { public float X; }
        public struct Light { public float Intensity; }
        public struct Mesh { public int Id; }
        """;

    private const string Systems="""
        using CSharpGameEngine.Entities;
        using Sample;
        namespace Game;
        internal struct MoveSystem : ISystem {
            public bool Update(ref Transform transform, ref State state) => true;
        }
        internal struct LightSystem : ISystem {
            public bool Update(ref Transform transform, ref Light light, ref State state) => true;
        }
        """;

    private static string TypesText(params string[]ExtraSources)=>
        GameSystemGeneratorTestHelper.GeneratedText(
            GameSystemGeneratorTestHelper.RunGenerator([.. GameSystemGeneratorTestHelper.SceneDslSources,.. ExtraSources]),
            "GameSystem.Types.g.cs");

    [Fact]
    public void SceneShapeMatchingASystemShape_SharesTheSameArchetype(){
        var WithoutScene=TypesText(Components,Systems);
        var WithScene=TypesText(Components,Systems,"""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new Transform { X=1f }] };
            }
            """);

        // {Transform} は MoveSystem が既に宣言しているので、Archetype は増えない。
        Assert.Equal(CountArchetypes(WithoutScene),CountArchetypes(WithScene));
    }

    [Fact]
    public void AddingASceneDoesNotRenumberSystemDerivedArchetypes(){
        var WithoutScene=TypesText(Components,Systems);
        var WithScene=TypesText(Components,Systems,"""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() {
                    Components=[new Mesh { Id=1 }],
                    Children=[new Node { Components=[new Light()] }],
                };
            }
            """);

        // Scene由来の集合は実Systemの後に登録されるため、System由来のArchetype番号は動かない。
        Assert.Contains("public sealed class Archetype0 : IArchetype",WithoutScene);
        Assert.Contains("public sealed class Archetype0 : IArchetype",WithScene);
        Assert.True(CountArchetypes(WithScene)>CountArchetypes(WithoutScene));
    }

    [Fact]
    public void SceneOnlyShapesProduceNewArchetypes(){
        var Text=TypesText(Components,Systems,"""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() {
                    Components=[new Transform()],
                    Children=[new Node { Components=[new Mesh { Id=2 }] }],
                };
            }
            """);

        // {Mesh, Parent} はどのSystemも宣言していないScene固有の構成。
        Assert.Contains("global::CSharpGameEngine.Entities.Parent",Text);
        Assert.Contains("global::Sample.Mesh",Text);
    }

    [Fact]
    public void Sysgen026_ReportsWhenComponentTypesExceedTheMaskWidth(){
        // マスクが long なので65個目から静かに壊れる。破綻する前に止まることを固定する。
        var TooManyComponents=string.Join("\n",Enumerable.Range(0,GameSystemIndexAssigner.MaxComponentTypes+1)
            .Select(static Index=> $"public struct C{Index} {{ public int Value; }}"));
        var TooManySystems=string.Join("\n",Enumerable.Range(0,GameSystemIndexAssigner.MaxComponentTypes+1)
            .Select(static Index=> $"internal struct S{Index} : ISystem {{ public bool Update(ref C{Index} c, ref State state) => true; }}"));

        GeneratorDriverRunResult Result=GameSystemGeneratorTestHelper.RunGenerator(
            "namespace Sample;\n"+TooManyComponents,
            "using CSharpGameEngine.Entities;\nusing Sample;\nnamespace Game;\n"+TooManySystems);

        Assert.Contains(Result.Diagnostics,static D=>D.Id=="SYSGEN026");
    }

    [Fact]
    public void ExactlyMaxComponentTypes_IsAccepted(){
        var Components64=string.Join("\n",Enumerable.Range(0,GameSystemIndexAssigner.MaxComponentTypes)
            .Select(static Index=> $"public struct C{Index} {{ public int Value; }}"));
        var Systems64=string.Join("\n",Enumerable.Range(0,GameSystemIndexAssigner.MaxComponentTypes)
            .Select(static Index=> $"internal struct S{Index} : ISystem {{ public bool Update(ref C{Index} c, ref State state) => true; }}"));

        GeneratorDriverRunResult Result=GameSystemGeneratorTestHelper.RunGenerator(
            "namespace Sample;\n"+Components64,
            "using CSharpGameEngine.Entities;\nusing Sample;\nnamespace Game;\n"+Systems64);

        Assert.DoesNotContain(Result.Diagnostics,static D=>D.Id=="SYSGEN026");
    }

    private static int CountArchetypes(string TypesText){
        var Count=0;
        var Index=0;

        while((Index=TypesText.IndexOf(" : IArchetype",Index,StringComparison.Ordinal))>=0){
            Count++;
            Index+=" : IArchetype".Length;
        }

        return Count;
    }
}
