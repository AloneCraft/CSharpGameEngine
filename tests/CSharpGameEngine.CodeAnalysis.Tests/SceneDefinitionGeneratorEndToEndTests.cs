using System.Reflection;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// SceneDefinitionGeneratorが出力したコード(SceneNodeSymbol実装+CreateRuntime)を実際にコンパイル・
/// Emit・ロードし、実World上でEntity/Parent/Component構造が正しく構築されることを検証する。
/// CreateRuntimeはRootのEntityしか返さないため、MainCamera/PlayerはEntityQueryで(それぞれ固有の
/// Camera/PlayerTagを持つArchetypeとして)見つけ出して検証する。
/// </summary>
public sealed class SceneDefinitionGeneratorEndToEndTests
{
    [Fact]
    public void CreateRuntime_BuildsEntityGraph_WithParentAndComponentsFromDefine()
    {
        string source = SceneDefinitionGeneratorTestHelper.CommonComponents + """


            [SceneDefinition]
            public static partial class MainScene
            {
                private static partial SceneNodeSymbol Root { get; }

                private static partial SceneNodeSymbol MainCamera { get; }

                private static partial SceneNodeSymbol Player { get; }

                public static void Define(SceneBuilder Scene)
                {
                    Scene.Create(Root, Root =>
                    {
                        Root.Create(MainCamera, MainCamera =>
                        {
                            MainCamera.Set(new Position { X = 0f, Y = 2f, Z = -10f });
                            MainCamera.Set(new Rotation { X = 0f, Y = 0f, Z = 0f, W = 1f });
                            MainCamera.Set(new Scale { X = 1f, Y = 1f, Z = 1f });
                            MainCamera.Set(new Camera { FieldOfView = 60f });
                        });

                        Root.Create(Player, Player =>
                        {
                            Player.Set(new Position { X = 1f, Y = 0f, Z = 3f });
                            Player.Set(new Rotation { X = 0f, Y = 0f, Z = 0f, W = 1f });
                            Player.Set(new Scale { X = 1f, Y = 1f, Z = 1f });
                            Player.Set(new PlayerTag());
                            Player.Set(new Health { Value = 100f });
                        });
                    });
                }
            }

            public static class TestEntry
            {
                public static string RunScenario()
                {
                    World world = new World();

                    Entity root = MainScene.CreateRuntime(world);

                    if (!world.IsAlive(root)) return "FAIL:RootNotAlive";
                    if (!world.Has<SceneNodeTag>(root)) return "FAIL:RootMissingSceneNodeTag";
                    if (world.Has<Parent>(root)) return "FAIL:RootShouldNotHaveParent";

                    EntityQuery cameraQuery = EntityQuery.Create(all: [ComponentType<Camera>.Id], none: [], access: [ComponentType<Camera>.Id]);
                    QueryMatches cameraMatches = cameraQuery.Update(world);
                    if (cameraMatches.Count != 1) return "FAIL:CameraArchetypeCount";
                    Archetype cameraArchetype = cameraMatches.ArchetypeAt(0);
                    if (cameraArchetype.Count != 1) return "FAIL:CameraEntityCount";
                    Entity mainCamera = cameraArchetype.Entities[0];

                    if (!world.Has<Parent>(mainCamera)) return "FAIL:MainCameraMissingParent";
                    if (world.GetRef<Parent>(mainCamera).Value != root) return "FAIL:MainCameraParentMismatch";
                    if (world.GetRef<Position>(mainCamera).Z != -10f) return "FAIL:MainCameraPosition";
                    if (world.GetRef<Camera>(mainCamera).FieldOfView != 60f) return "FAIL:MainCameraFieldOfView";

                    EntityQuery playerQuery = EntityQuery.Create(all: [ComponentType<PlayerTag>.Id], none: [], access: [ComponentType<PlayerTag>.Id]);
                    QueryMatches playerMatches = playerQuery.Update(world);
                    if (playerMatches.Count != 1) return "FAIL:PlayerArchetypeCount";
                    Archetype playerArchetype = playerMatches.ArchetypeAt(0);
                    if (playerArchetype.Count != 1) return "FAIL:PlayerEntityCount";
                    Entity player = playerArchetype.Entities[0];

                    if (!world.Has<Parent>(player)) return "FAIL:PlayerMissingParent";
                    if (world.GetRef<Parent>(player).Value != root) return "FAIL:PlayerParentMismatch";
                    if (world.GetRef<Position>(player).X != 1f) return "FAIL:PlayerPosition";
                    if (world.GetRef<Health>(player).Value != 100f) return "FAIL:PlayerHealth";

                    return "OK";
                }
            }
            """;

        Assembly assembly = SceneDefinitionGeneratorTestHelper.CompileAndLoad(source);
        object? result = SceneDefinitionGeneratorTestHelper.Invoke(assembly, "Sample.TestEntry", "RunScenario");

        Assert.Equal("OK", result);
    }

    [Fact]
    public void CreateRuntime_EmptyNode_GetsSceneNodeTag_AndSingleSetComponentIsApplied()
    {
        string source = SceneDefinitionGeneratorTestHelper.CommonComponents + """


            [SceneDefinition]
            public static partial class MinimalScene
            {
                private static partial SceneNodeSymbol Root { get; }

                private static partial SceneNodeSymbol Child { get; }

                public static void Define(SceneBuilder Scene)
                {
                    Scene.Create(Root, Root =>
                    {
                        Root.Create(Child, Child =>
                        {
                            Child.Set(new Health { Value = 42f });
                        });
                    });
                }
            }

            public static class TestEntry
            {
                public static string RunScenario()
                {
                    World world = new World();

                    Entity root = MinimalScene.CreateRuntime(world);

                    if (!world.Has<SceneNodeTag>(root)) return "FAIL:RootMissingSceneNodeTag";

                    EntityQuery childQuery = EntityQuery.Create(all: [ComponentType<Health>.Id, ComponentType<Parent>.Id], none: [], access: [ComponentType<Health>.Id, ComponentType<Parent>.Id]);
                    QueryMatches childMatches = childQuery.Update(world);
                    if (childMatches.Count != 1) return "FAIL:ChildArchetypeCount";
                    Archetype childArchetype = childMatches.ArchetypeAt(0);
                    if (childArchetype.Count != 1) return "FAIL:ChildEntityCount";
                    Entity child = childArchetype.Entities[0];

                    if (world.GetRef<Parent>(child).Value != root) return "FAIL:ChildParentMismatch";
                    if (world.GetRef<Health>(child).Value != 42f) return "FAIL:ChildHealth";

                    return "OK";
                }
            }
            """;

        Assembly assembly = SceneDefinitionGeneratorTestHelper.CompileAndLoad(source);
        object? result = SceneDefinitionGeneratorTestHelper.Invoke(assembly, "Sample.TestEntry", "RunScenario");

        Assert.Equal("OK", result);
    }
}
