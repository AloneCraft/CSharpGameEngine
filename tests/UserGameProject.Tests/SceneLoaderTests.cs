using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;

namespace UserGameProject.Tests;

/// <summary>End-to-end: a synthetic GLB (built with SharpGLTF.Toolkit, since a committed fixture can't easily be hand-edited per test) + a hand-written scene_meta.json, combined exactly the way tools/UnrealExport's real output is.</summary>
public class SceneLoaderTests
{
    private static string BuildGlbWithOneCubeAndOneLight(string cubeName)
    {
        var mesh = new MeshBuilder<VertexPosition, VertexTexture1, VertexEmpty>(cubeName);
        var primitive = mesh.UsePrimitive(new MaterialBuilder("mat").WithMetallicRoughness());
        primitive.AddTriangle(
            (new VertexPosition(0, 0, 0), new VertexTexture1(new Vector2(0, 0))),
            (new VertexPosition(1, 0, 0), new VertexTexture1(new Vector2(1, 0))),
            (new VertexPosition(0, 1, 0), new VertexTexture1(new Vector2(0, 1))));

        var sceneBuilder = new SceneBuilder();
        var cubeNode = new NodeBuilder(cubeName) { LocalTransform = new AffineTransform(Matrix4x4.CreateTranslation(2, 0, 3)) };
        sceneBuilder.AddRigidMesh(mesh, cubeNode);

        var light = new LightBuilder.Directional { Color = new Vector3(1, 1, 1), Intensity = 4 };
        var lightNode = new NodeBuilder("Sun") { LocalTransform = new AffineTransform(Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f)) };
        sceneBuilder.AddLight(light, lightNode);

        string path = Path.Combine(Path.GetTempPath(), $"scene-loader-test-{Guid.NewGuid():N}.glb");
        sceneBuilder.ToGltf2().SaveGLB(path);
        return path;
    }

    private static string WriteSceneMeta(string cubeName, string mobility)
    {
        string json = $$"""
            {
              "ambient": { "colorRgb": [0.2, 0.3, 0.4], "intensity": 2.0 },
              "fog": { "density": 0.05, "heightFalloff": 0.1, "colorRgb": [0.5, 0.5, 0.5] },
              "playerStart": { "translation": [1.0, 2.0, 3.0], "yawRadians": 1.5 },
              "mobilityOverrides": { "{{cubeName}}": "{{mobility}}" }
            }
            """;

        string path = Path.Combine(Path.GetTempPath(), $"scene-meta-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Load_CombinesGlbNodesWithSceneMetaMobilityAmbientFogAndPlayerStart()
    {
        string glbPath = BuildGlbWithOneCubeAndOneLight("TestCube");
        string metaPath = WriteSceneMeta("TestCube", "movable");
        try
        {
            LoadedScene scene = SceneLoader.Load(glbPath, metaPath);

            LoadedSceneSpawn spawn = Assert.Single(scene.Spawns);
            Assert.Equal(PropMobility.Movable, spawn.Mobility);
            Assert.Equal(new Vector3(2, 0, 3), spawn.Position);

            Assert.NotNull(scene.Sun);
            Assert.Equal(4f, scene.Sun!.Value.Intensity, precision: 3);
            Assert.Equal(1f, scene.Sun.Value.ColorR, precision: 3);

            Assert.NotNull(scene.Ambient);
            // Intensity is folded into color: 0.2*2.0=0.4, 0.3*2.0=0.6, 0.4*2.0=0.8.
            Assert.Equal(0.4f, scene.Ambient!.Value.ColorR, precision: 3);
            Assert.Equal(0.8f, scene.Ambient.Value.ColorB, precision: 3);

            Assert.NotNull(scene.Fog);
            Assert.Equal(0.05f, scene.Fog!.Value.Density, precision: 3);

            Assert.Equal(new Vector3(1, 2, 3), scene.PlayerStartPosition);
            Assert.Equal(1.5f, scene.PlayerStartYawRadians, precision: 3);

            Assert.Single(scene.Meshes);
            Assert.Single(scene.Materials);
            Assert.Single(scene.Objects);
        }
        finally
        {
            File.Delete(glbPath);
            File.Delete(metaPath);
        }
    }

    [Fact]
    public void Load_ActorNotInMobilityOverrides_DefaultsToStatic()
    {
        string glbPath = BuildGlbWithOneCubeAndOneLight("UnlistedCube");
        string metaPath = WriteSceneMeta("SomeOtherActorEntirely", "movable");
        try
        {
            LoadedScene scene = SceneLoader.Load(glbPath, metaPath);

            LoadedSceneSpawn spawn = Assert.Single(scene.Spawns);
            Assert.Equal(PropMobility.Static, spawn.Mobility);
        }
        finally
        {
            File.Delete(glbPath);
            File.Delete(metaPath);
        }
    }
}
