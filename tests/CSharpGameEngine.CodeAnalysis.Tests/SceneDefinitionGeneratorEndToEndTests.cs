using System.Reflection;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// 生成された<c>SceneNodeSymbol</c>実装+<c>CreateRuntime</c>を実際にコンパイル・Emit・ロードし、
/// 実World上でEntity/Parent/Component構造が正しく構築されることを検証する。
/// ADR-0033で<c>CreateRuntime</c>は<c>ref World</c>を取るようになり、内容の確認はQueryではなく
/// 生成された<c>WorldSnapshot.Capture</c>(VSIXプレビューブリッジと同じ経路)で行う。
/// </summary>
public sealed class SceneDefinitionGeneratorEndToEndTests
{
    /// <summary>スナップショットから、指定した名前のComponentを持つ最初のエントリを探すヘルパー(テスト対象ソースへ埋め込む)。</summary>
    private const string SnapshotHelpers = """

        public static class SnapshotQuery
        {
            public static ArchetypeSnapshotEntry? FindByComponent(ArchetypeSnapshotEntry[] entries, string componentTypeName)
            {
                foreach (ArchetypeSnapshotEntry entry in entries)
                {
                    foreach (object value in entry.ComponentValues)
                    {
                        if (value.GetType().Name == componentTypeName) return entry;
                    }
                }

                return null;
            }

            public static T Component<T>(ArchetypeSnapshotEntry entry)
            {
                foreach (object value in entry.ComponentValues)
                {
                    if (value is T typed) return typed;
                }

                throw new InvalidOperationException("component not found: " + typeof(T).Name);
            }
        }
        """;

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

                    Entity root = MainScene.CreateRuntime(ref world);

                    ArchetypeSnapshotEntry[] entries = WorldSnapshot.Capture(world);
                    if (entries.Length != 3) return "FAIL:EntityCount=" + entries.Length;

                    ArchetypeSnapshotEntry? rootEntry = SnapshotQuery.FindByComponent(entries, "SceneNodeTag");
                    if (rootEntry is null) return "FAIL:RootMissingSceneNodeTag";
                    if (rootEntry.Value.EntityId != root.Id) return "FAIL:RootIdMismatch";
                    if (rootEntry.Value.HasParent) return "FAIL:RootShouldNotHaveParent";

                    ArchetypeSnapshotEntry? cameraEntry = SnapshotQuery.FindByComponent(entries, "Camera");
                    if (cameraEntry is null) return "FAIL:CameraMissing";
                    if (!cameraEntry.Value.HasParent) return "FAIL:MainCameraMissingParent";
                    if (cameraEntry.Value.ParentEntityId != root.Id) return "FAIL:MainCameraParentMismatch";
                    if (SnapshotQuery.Component<Position>(cameraEntry.Value).Z != -10f) return "FAIL:MainCameraPosition";
                    if (SnapshotQuery.Component<Camera>(cameraEntry.Value).FieldOfView != 60f) return "FAIL:MainCameraFieldOfView";

                    ArchetypeSnapshotEntry? playerEntry = SnapshotQuery.FindByComponent(entries, "PlayerTag");
                    if (playerEntry is null) return "FAIL:PlayerMissing";
                    if (playerEntry.Value.ParentEntityId != root.Id) return "FAIL:PlayerParentMismatch";
                    if (SnapshotQuery.Component<Position>(playerEntry.Value).X != 1f) return "FAIL:PlayerPosition";
                    if (SnapshotQuery.Component<Health>(playerEntry.Value).Value != 100f) return "FAIL:PlayerHealth";

                    return "OK";
                }
            }
            """ + SnapshotHelpers;

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

                    Entity root = MinimalScene.CreateRuntime(ref world);

                    ArchetypeSnapshotEntry[] entries = WorldSnapshot.Capture(world);
                    if (entries.Length != 2) return "FAIL:EntityCount=" + entries.Length;

                    ArchetypeSnapshotEntry? rootEntry = SnapshotQuery.FindByComponent(entries, "SceneNodeTag");
                    if (rootEntry is null) return "FAIL:RootMissingSceneNodeTag";
                    if (rootEntry.Value.EntityId != root.Id) return "FAIL:RootIdMismatch";

                    ArchetypeSnapshotEntry? childEntry = SnapshotQuery.FindByComponent(entries, "Health");
                    if (childEntry is null) return "FAIL:ChildMissing";
                    if (childEntry.Value.ParentEntityId != root.Id) return "FAIL:ChildParentMismatch";
                    if (SnapshotQuery.Component<Health>(childEntry.Value).Value != 42f) return "FAIL:ChildHealth";

                    return "OK";
                }
            }
            """ + SnapshotHelpers;

        Assembly assembly = SceneDefinitionGeneratorTestHelper.CompileAndLoad(source);
        object? result = SceneDefinitionGeneratorTestHelper.Invoke(assembly, "Sample.TestEntry", "RunScenario");

        Assert.Equal("OK", result);
    }
}
