using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.CodeAnalysis.Tests;
/// <summary>
/// ADR-0078/M0105: a Scene whose root has a Transform can be created at a position while the game runs. The generator gives it
/// <c>InstantiateDeferredAt</c> and marks it <c>ISpawnableScene</c>, so <c>Spawner.Spawn&lt;TScene&gt;</c> accepts exactly those Scenes. Also the
/// service the Spawner is: a sealed class that is not disposable must compile into the loop.
/// </summary>
public sealed class SceneSpawnGeneratorTests {
    private static readonly string EngineServiceAttribute=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"TestAssets","Core","EngineServiceAttribute.cs"));
    /// <summary>The engine's Transform, vectors and the spawnable-scene contract, reduced to what the generated code touches.</summary>
    private const string Contract="""
        namespace CSharpGameEngine.Numerics
        {
            public struct LogicalVector3 { public float R; public float U; public float F; public LogicalVector3(float r, float u, float f) { R = r; U = u; F = f; } }
            public struct LogicalQuaternion { public float W; }
        }
        namespace CSharpGameEngine.Entities
        {
            // The engine's own types are scene components only when they carry this attribute (SceneTypePredicates.IsAddableComponent).
            public sealed class SceneComponentOrderAttribute : System.Attribute { public SceneComponentOrderAttribute(int order) { } }
            [SceneComponentOrder(0)]
            public struct Transform { public CSharpGameEngine.Numerics.LogicalVector3 Position; public CSharpGameEngine.Numerics.LogicalQuaternion Rotation; }
            public interface ISpawnableScene : ISceneDefinition
            {
                static abstract SceneInstance InstantiateDeferredAt(ref World World, CSharpGameEngine.Numerics.LogicalVector3 Position);
                static abstract SceneInstance InstantiateDeferredAt(ref World World, CSharpGameEngine.Numerics.LogicalVector3 Position, CSharpGameEngine.Numerics.LogicalQuaternion Rotation);
            }
            public static class SceneSpawn
            {
                public static Transform At(Transform Authored, CSharpGameEngine.Numerics.LogicalVector3 Position) { Authored.Position = Position; return Authored; }
                public static Transform At(Transform Authored, CSharpGameEngine.Numerics.LogicalVector3 Position, CSharpGameEngine.Numerics.LogicalQuaternion Rotation) { Authored.Position = Position; Authored.Rotation = Rotation; return Authored; }
            }
        }
        """;
    private const string Scenes="""
        using CSharpGameEngine.Entities;
        using Sample;
        namespace Game;
        public partial class WithTransform:IScene {
            public Node Root=>new() {
                Name="Root",
                Components=[new Transform()],
                Children=[new Node { Name="Child",Components=[new Transform(),new Health { Value=3 }] }],
            };
        }
        public partial class WithoutTransform:IScene {
            public Node Root=>new() { Name="Root",Components=[new Health { Value=1 }] };
        }
        internal struct KeepTransformAlive:ISystem {
            public void Update(ref Transform Transform,ref State State){ }
        }
        """;
    private static string Text(GeneratorDriverRunResult Result,string Scene)=>GameSystemGeneratorTestHelper.GeneratedText(Result,$"Game.{Scene}.Scene.g.cs");
    [Fact]
    public void ASceneWithATransformRootIsSpawnableAndOneWithoutIsNot(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator([GameSystemGeneratorTestHelper.CommonComponents,Contract,Scenes]);
        Assert.Empty(Result.Diagnostics);
        var With=Text(Result,"WithTransform");
        Assert.Contains("partial class WithTransform : global::CSharpGameEngine.Entities.ISceneDefinition, global::CSharpGameEngine.Entities.ISpawnableScene {",With);
        Assert.Contains("public static global::CSharpGameEngine.Entities.SceneInstance InstantiateDeferredAt(ref global::CSharpGameEngine.Entities.World World, global::CSharpGameEngine.Numerics.LogicalVector3 Position) {",With);
        Assert.Contains("public static global::CSharpGameEngine.Entities.SceneInstance InstantiateDeferredAt(ref global::CSharpGameEngine.Entities.World World, global::CSharpGameEngine.Numerics.LogicalVector3 Position, global::CSharpGameEngine.Numerics.LogicalQuaternion Rotation) {",With);
        // Only the root's Transform is replaced; the child keeps what it was authored with.
        Assert.Equal(2,Count(With,"global::CSharpGameEngine.Entities.SceneSpawn.At("));
        var Without=Text(Result,"WithoutTransform");
        Assert.DoesNotContain("ISpawnableScene",Without);
        Assert.DoesNotContain("InstantiateDeferredAt",Without);
    }
    [Fact]
    public void WithoutTheSpawnContractInTheCompilationNothingSpawnableIsEmitted(){
        // A compilation that does not have ISpawnableScene (such as the generator's other tests) gets the scenes it always got.
        var Result=GameSystemGeneratorTestHelper.RunGenerator([GameSystemGeneratorTestHelper.CommonComponents,Contract.Replace("ISpawnableScene : ISceneDefinition","ISomethingElse : ISceneDefinition"),Scenes]);
        Assert.DoesNotContain("InstantiateDeferredAt",Text(Result,"WithTransform"));
    }
    [Fact]
    public void ASpawnedSceneHasItsRootPlacedAndItsChildLeftAlone(){
        const string Driver="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Numerics;
            using Sample;
            namespace Game;
            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();
                    State state = new(world) { Enabled = true };
                    var instance = WithTransform.InstantiateDeferredAt(ref world, new LogicalVector3(1f, 2f, 3f));
                    // Deferred: nothing exists until the structural changes are played back.
                    if (world.GetStatistics().AliveEntityCount != 0) return "FAIL:early";
                    world.PlaybackStructuralChanges(ref state);
                    var entities = world.GetSceneEntities(instance);
                    if (entities.Length != 2) return "FAIL:entities " + entities.Length;
                    if (!world.TryGetComponent(entities[0], out Transform root)) return "FAIL:root";
                    if (root.Position.R != 1f || root.Position.U != 2f || root.Position.F != 3f) return "FAIL:position";
                    if (!world.TryGetComponent(entities[1], out Transform child) || child.Position.R != 0f) return "FAIL:child";
                    if (!world.TryGetComponent(entities[1], out Parent parent) || parent.Value != entities[0]) return "FAIL:parent";
                    var turned = WithTransform.InstantiateDeferredAt(ref world, new LogicalVector3(4f, 0f, 0f), new LogicalQuaternion { W = 0.5f });
                    world.PlaybackStructuralChanges(ref state);
                    if (!world.TryGetComponent(world.GetSceneRoot(turned), out Transform second)) return "FAIL:second";
                    if (second.Position.R != 4f || second.Rotation.W != 0.5f) return "FAIL:rotation";
                    if (world.GetSceneRoot(turned) == world.GetSceneRoot(instance)) return "FAIL:same";
                    return "OK";
                }
            }
            """;
        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(GameSystemGeneratorTestHelper.CommonComponents,Contract,Scenes,Driver);
        Assert.Equal("OK",GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario"));
    }
    [Fact]
    public void ASealedServiceThatIsNotDisposableCompilesIntoTheLoopAndShutdownLeavesItAlone(){
        // The loop disposes the services it made when the game ends; testing a sealed class against IDisposable directly is CS8121.
        const string Service="""
            using CSharpGameEngine.Authoring;
            namespace Game;
            [EngineService]
            public sealed class Plain { public int Seen; }
            """;
        const string System="""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            internal struct Count : ISystem
            {
                public void Update(in Position position, Plain plain) { plain.Seen++; }
            }
            """;
        const string Driver="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Generated;
            using Sample;
            namespace Game;
            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    World world = new();
                    State state = new(world) { Enabled = true };
                    world.Create(new Position { X = 1f });
                    GeneratedSystemSet systems = default;
                    GeneratedGameLoop.RunBakeTick(ref state, ref systems);
                    if (systems.Service0!.Seen != 1) return "FAIL:seen";
                    GeneratedGameLoop.Shutdown(ref state, ref systems);
                    return systems.Service0 is null ? "OK" : "FAIL:kept";
                }
            }
            """;
        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(GameSystemGeneratorTestHelper.CommonComponents,EngineServiceAttribute,Service,System,Driver);
        Assert.Equal("OK",GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario"));
    }
    private static int Count(string Text,string Part){
        var Found=0;
        for(var At=Text.IndexOf(Part,StringComparison.Ordinal);At>=0;At=Text.IndexOf(Part,At+Part.Length,StringComparison.Ordinal)){ Found++; }
        return Found;
    }
}
