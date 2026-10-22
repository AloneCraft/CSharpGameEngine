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
    private static int Occurrences(string Text,string Value)=>(Text.Length-Text.Replace(Value,string.Empty).Length)/Value.Length;
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

        var Result=GameSystemGeneratorTestHelper.RunGenerator(
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

        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              "namespace Sample;\n"+Components64,
                                                              "using CSharpGameEngine.Entities;\nusing Sample;\nnamespace Game;\n"+Systems64);

        Assert.DoesNotContain(Result.Diagnostics,static D=>D.Id=="SYSGEN026");
    }

    [Fact]
    public void Exactly64ComponentTypes_KeepTheSingleMaskPath(){
        var Components64=string.Join("\n",Enumerable.Range(0,64).Select(static Index=>$"public struct C{Index} {{ public int Value; }}"));
        var Systems64=string.Join("\n",Enumerable.Range(0,64).Select(static Index=>$"internal struct S{Index} : ISystem {{ public bool Update(ref C{Index} c, ref State state) => true; }}"));
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
            "namespace Sample;\n"+Components64,
            "using CSharpGameEngine.Entities;\nusing Sample;\nnamespace Game;\n"+Systems64);
        Assert.DoesNotContain(Result.Diagnostics,static D=>D.Id=="SYSGEN026");
        var Types=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Types.g.cs");
        var Loop=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");
        Assert.DoesNotContain("ComponentEnabledHigh",Types,StringComparison.Ordinal);
        Assert.DoesNotContain(", 0x0000000000000000UL)) continue;",Loop,StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(65)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(200)]
    [InlineData(512)]
    public void WideComponentMasksDoNotAliasHighComponentsOntoLowComponents(int ComponentCount){
        var ComponentSource=string.Join("\n",Enumerable.Range(0,ComponentCount).Select(static Index=>$"public struct C{Index} {{ public int Value; }}"));
        var CombinedParameters=string.Join(", ",Enumerable.Range(0,ComponentCount).Select(static Index=>$"ref C{Index} c{Index}"));
        var HighIndex=ComponentCount-1;
        var SystemSource=$$"""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            internal struct Low : ISystem { public bool Update(ref C0 c, ref State state) => true; }
            internal struct High : ISystem { public bool Update(ref C{{HighIndex}} c, ref State state) => true; }
            internal struct Combined : ISystem { public void Spawn({{CombinedParameters}}) { } }
            """;
        var Result=GameSystemGeneratorTestHelper.RunGenerator("namespace Sample;\n"+ComponentSource,SystemSource);
        Assert.Empty(Result.Diagnostics);
        var Loop=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");
        var Types=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Types.g.cs");
        Assert.Equal(2,Occurrences(Loop,"Systems.Low.Update("));
        Assert.Equal(2,Occurrences(Loop,"Systems.High.Update("));
        Assert.Contains("ComponentEnabledHigh",Types,StringComparison.Ordinal);
        // ADR-0079: one more word per 64 component types after the first 128.
        Assert.Equal(ComponentCount>128,Types.Contains("ComponentEnabledWord2",StringComparison.Ordinal));
        Assert.Equal(ComponentCount>448,Types.Contains("ComponentEnabledWord7",StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(65)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(200)]
    [InlineData(512)]
    public void WideComponentMasksCompileAndExecuteHighComponentQueries(int ComponentCount){
        var ComponentSource=string.Join("\n",Enumerable.Range(0,ComponentCount).Select(static Index=>$"public struct C{Index} {{ public int Value; }}"));
        var CombinedParameters=string.Join(", ",Enumerable.Range(0,ComponentCount).Select(static Index=>$"ref C{Index} c{Index}"));
        var CreateArguments=string.Join(", ",Enumerable.Range(0,ComponentCount).Select(static Index=>$"new C{Index}()"));
        var HighIndex=ComponentCount-1;
        var SystemSource=$$"""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Simulation;
            using Sample;
            namespace Game;
            internal struct Combined : ISystem { public void Spawn({{CombinedParameters}}) { } }
            internal struct High : ISystem {
                public bool Update(ref C{{HighIndex}} c, ref State state) { c.Value++; return true; }
            }
            internal static class TestEntry {
                internal static int Run() {
                    World world = new();
                    Entity entity = world.Create({{CreateArguments}});
                    State state = new(world) { Enabled = true };
                    High high = default;
                    FixedTickScheduler scheduler = new();
                    if (!world.SetComponentEnabled(entity, EcsComponentType.C{{HighIndex}}, false)) return -2;
                    CSharpGameEngine.Generated.GeneratedGameLoop.RunFrame(ref state, ref high, ref scheduler, 1, 60);
                    if (!world.TryGetComponent(entity, out C{{HighIndex}} disabled) || disabled.Value != 0) return -3;
                    world.SetComponentEnabled(entity, EcsComponentType.C{{HighIndex}}, true);
                    ChangeVersion beforeWrite = world.CaptureChangeVersion();
                    CSharpGameEngine.Generated.GeneratedGameLoop.RunFrame(ref state, ref high, ref scheduler, 1, 60);
                    Span<Entity> changed = stackalloc Entity[1];
                    int count = world.CollectChanged(EcsComponentType.C{{HighIndex}}, beforeWrite, changed);
                    return world.TryGetComponent(entity, out C{{HighIndex}} value) ? value.Value * 10 + count : -1;
                }
            }
            """;
        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad("namespace Sample;\n"+ComponentSource,SystemSource);
        var Value=(int)GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","Run")!;
        Assert.Equal(11,Value);
    }

    /// <summary>
    /// M0053: <c>IRuntimeComponent&lt;TAuthored&gt;</c>を実装する型は、作者が<c>TAuthored</c>を宣言した
    /// Nodeへ<c>Parent</c>/<c>SceneNodeTag</c>と同じ仕組みで自動付与される。
    /// <b>Archetype登録(Collect)と生成(EmitNode)が同じ集合を導出しないとビルドが壊れる</b>ので、
    /// 実際に生成されたInstantiateコードに随伴Componentが現れることで両者の一致を確かめる。
    /// </summary>
    [Fact]
    public void RuntimeCompanionComponentIsAttachedAutomatically(){
        var Scene=GameSystemGeneratorTestHelper.GeneratedText(
            GameSystemGeneratorTestHelper.RunGenerator([
                .. GameSystemGeneratorTestHelper.SceneDslSources,
                Components,
                RuntimeCompanion,
                Systems,
                """
                using CSharpGameEngine.Entities;
                using Sample;
                namespace Game;
                public partial class MainScene:IScene {
                    public Node Root=>new() { Components=[new Light { Intensity=1f }] };
                }
                """,
            ]),
            "Game.MainScene.Scene.g.cs");

        // 作者Componentの直後に随伴Componentが並ぶ。
        Assert.Contains("global::Sample.Light",Scene);
        Assert.Contains("new global::Sample.LightRuntime()",Scene);
    }

    /// <summary>随伴Componentは作者Componentを宣言していないNodeへは付かない。</summary>
    [Fact]
    public void RuntimeCompanionComponentIsNotAttachedToUnrelatedNodes(){
        var Scene=GameSystemGeneratorTestHelper.GeneratedText(
            GameSystemGeneratorTestHelper.RunGenerator([
                .. GameSystemGeneratorTestHelper.SceneDslSources,
                Components,
                RuntimeCompanion,
                Systems,
                """
                using CSharpGameEngine.Entities;
                using Sample;
                namespace Game;
                public partial class MainScene:IScene {
                    public Node Root=>new() { Components=[new Transform { X=1f }] };
                }
                """,
            ]),
            "Game.MainScene.Scene.g.cs");

        Assert.DoesNotContain("LightRuntime",Scene);
    }

    /// <summary>
    /// SYSGEN027: 同じ作者Componentを2つの実行時Componentが主張すると、どちらを随伴付与するかが
    /// 非決定になる。曖昧なまま片方を選ぶより明示的に止める。
    /// </summary>
    [Fact]
    public void Sysgen027_ReportsWhenTwoRuntimeComponentsClaimTheSameAuthoredType(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator([
            .. GameSystemGeneratorTestHelper.SceneDslSources,
            Components,
            """
            using CSharpGameEngine.Entities;
            namespace Sample;
            public struct LightRuntime : IRuntimeComponent<Light> { public int Handle; }
            public struct LightRuntimeAgain : IRuntimeComponent<Light> { public int Other; }
            """,
            Systems,
        ]);

        Assert.Contains(Result.Diagnostics,static Diagnostic=>Diagnostic.Id=="SYSGEN027");
    }

    private const string RuntimeCompanion="""
        using CSharpGameEngine.Entities;
        namespace Sample;
        public struct LightRuntime : IRuntimeComponent<Light> { public int Handle; }
        """;

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
