using CSharpGameEngine.Entities;

namespace SceneAuthoringDemo;

/// <summary>
/// MainScene.Define(コード上のScene定義)からSceneDefinitionGeneratorが生成したCreateRuntimeを実際に
/// 実行し、Entity階層(Root→MainCamera/Player)・Parentの自動付与・各Componentの初期値が、Define本体
/// に書いたとおりに実World上へ反映されていることを検証する。CreateRuntimeはRootのEntityしか返さない
/// ため、MainCamera/PlayerはそれぞれCamera/PlayerTagを持つ唯一のArchetypeとしてEntityQueryで見つける。
/// </summary>
public static class DemoScenario
{
    private const float Epsilon = 0.0001f;

    public static string Run()
    {
        World world = new World();

        Entity root = MainScene.CreateRuntime(world);

        if (!world.IsAlive(root))
        {
            return "FAIL:Root is not alive";
        }

        if (!world.Has<SceneNodeTag>(root))
        {
            return "FAIL:Root should carry the implicit SceneNodeTag (it has no explicit Set calls)";
        }

        if (world.Has<Parent>(root))
        {
            return "FAIL:Root must not have a Parent";
        }

        EntityQuery cameraQuery = EntityQuery.Create(all: [ComponentType<Camera>.Id], none: [], access: [ComponentType<Camera>.Id]);
        QueryMatches cameraMatches = cameraQuery.Update(world);

        if (cameraMatches.Count != 1 || cameraMatches.ArchetypeAt(0).Count != 1)
        {
            return "FAIL:Expected exactly one entity with a Camera component";
        }

        Entity mainCamera = cameraMatches.ArchetypeAt(0).Entities[0];

        if (!world.Has<Parent>(mainCamera) || world.GetRef<Parent>(mainCamera).Value != root)
        {
            return "FAIL:MainCamera.Parent should be Root (auto-attached from Define's nesting)";
        }

        Position mainCameraPosition = world.GetRef<Position>(mainCamera);

        if (!NearlyEqual(mainCameraPosition.X, 0f) || !NearlyEqual(mainCameraPosition.Y, 2f) || !NearlyEqual(mainCameraPosition.Z, -10f))
        {
            return "FAIL:MainCamera.Position does not match Define";
        }

        if (!NearlyEqual(world.GetRef<Camera>(mainCamera).FieldOfView, 60f))
        {
            return "FAIL:MainCamera.Camera.FieldOfView does not match Define";
        }

        EntityQuery playerQuery = EntityQuery.Create(all: [ComponentType<PlayerTag>.Id], none: [], access: [ComponentType<PlayerTag>.Id]);
        QueryMatches playerMatches = playerQuery.Update(world);

        if (playerMatches.Count != 1 || playerMatches.ArchetypeAt(0).Count != 1)
        {
            return "FAIL:Expected exactly one entity with a PlayerTag component";
        }

        Entity player = playerMatches.ArchetypeAt(0).Entities[0];

        if (!world.Has<Parent>(player) || world.GetRef<Parent>(player).Value != root)
        {
            return "FAIL:Player.Parent should be Root (auto-attached from Define's nesting)";
        }

        if (!NearlyEqual(world.GetRef<Health>(player).Value, 100f))
        {
            return "FAIL:Player.Health does not match Define";
        }

        return "OK";
    }

    private static bool NearlyEqual(float actual, float expected)
    {
        return MathF.Abs(actual - expected) < Epsilon;
    }
}
