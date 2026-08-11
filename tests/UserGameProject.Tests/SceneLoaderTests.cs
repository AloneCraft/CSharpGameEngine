using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;

namespace UserGameProject.Tests;

/// <summary>End-to-end: a synthetic GLB (built with SharpGLTF.Toolkit, since a committed fixture can't easily be hand-edited per test) + a hand-written scene_meta.json, combined exactly the way tools/UnrealExport's real output is.</summary>
public class SceneLoaderTests {
    private static string BuildGlbWithOneCubeAndOneLight(string CubeName){
        var Mesh=new MeshBuilder<VertexPosition,VertexTexture1,VertexEmpty>(CubeName);
        var Primitive=Mesh.UsePrimitive(new MaterialBuilder("mat").WithMetallicRoughness());
        Primitive.AddTriangle(
            (new VertexPosition(0,0,0),new VertexTexture1(new Vector2(0,0))),
            (new VertexPosition(1,0,0),new VertexTexture1(new Vector2(1,0))),
            (new VertexPosition(0,1,0),new VertexTexture1(new Vector2(0,1))));

        var sceneBuilder=new SceneBuilder();
        var CubeNode=new NodeBuilder(CubeName){ LocalTransform=new AffineTransform(Matrix4x4.CreateTranslation(2,0,3))};
        sceneBuilder.AddRigidMesh(Mesh,CubeNode);

        var Light=new LightBuilder.Directional { Color=new Vector3(1,1,1),Intensity=4 };
        var LightNode=new NodeBuilder("Sun"){ LocalTransform=new AffineTransform(Quaternion.CreateFromAxisAngle(Vector3.UnitX,-MathF.PI/2f))};
        sceneBuilder.AddLight(Light,LightNode);

        var path=Path.Combine(Path.GetTempPath(), $"scene-loader-test-{Guid.NewGuid():N}.glb");
        sceneBuilder.ToGltf2().SaveGLB(path);
        return path;
    }

    private static string WriteSceneMeta(string CubeName,string Mobility){
        var Json= $$"""
            {
              "ambient": { "colorRgb": [0.2, 0.3, 0.4], "intensity": 2.0 },
              "fog": { "density": 0.05, "heightFalloff": 0.1, "colorRgb": [0.5, 0.5, 0.5] },
              "playerStart": { "translation": [1.0, 2.0, 3.0], "yawRadians": 1.5 },
              "mobilityOverrides": { "{{CubeName}}": "{{Mobility}}" }
            }
            """;

        var path=Path.Combine(Path.GetTempPath(), $"scene-meta-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path,Json);
        return path;
    }

    [Fact]
    public void Load_CombinesGlbNodesWithSceneMetaMobilityAmbientFogAndPlayerStart(){
        var GlbPath=BuildGlbWithOneCubeAndOneLight("TestCube");
        var MetaPath=WriteSceneMeta("TestCube","movable");
        try {
            LoadedScene Scene=SceneLoader.Load(GlbPath,MetaPath);

            LoadedSceneSpawn Spawn=Assert.Single(Scene.Spawns);
            Assert.Equal(PropMobility.Movable,Spawn.Mobility);
            // glTF-space (2,0,3) -> engine-space (2,0,-3): GltfImporter now converts every node's
            // WorldTransform from glTF's right-handed space into this engine's fixed
            // UnityCoordinateConvention (left-handed), ADR-0027.
            Assert.Equal(new Vector3(2,0,-3),Spawn.Position);

            Assert.NotNull(Scene.Sun);
            Assert.Equal(4f,Scene.Sun!.Value.Intensity,precision:3);
            Assert.Equal(1f,Scene.Sun.Value.ColorR,precision:3);

            Assert.NotNull(Scene.Ambient);
            // Intensity is folded into color: 0.2*2.0=0.4, 0.3*2.0=0.6, 0.4*2.0=0.8.
            Assert.Equal(0.4f,Scene.Ambient!.Value.ColorR,precision:3);
            Assert.Equal(0.8f,Scene.Ambient.Value.ColorB,precision:3);

            Assert.NotNull(Scene.Fog);
            Assert.Equal(0.05f,Scene.Fog!.Value.Density,precision:3);

            // scene_meta.json's PlayerStart is written in glTF space by export_lvl_firstperson.py's
            // own UE->glTF conversion, so SceneLoader applies the same glTF->engine conversion here
            // as every other node (ADR-0027): position Z negates, and yaw negates too (a pure Y-axis
            // rotation's angle flips sign under the same Z-mirror, since Y is the mirror's fixed axis).
            Assert.Equal(new Vector3(1,2,-3),Scene.PlayerStartPosition);
            Assert.Equal(-1.5f,Scene.PlayerStartYawRadians,precision:3);

            Assert.Single(Scene.Meshes);
            Assert.Single(Scene.Materials);
            Assert.Single(Scene.Objects);
        } finally {
            File.Delete(GlbPath);
            File.Delete(MetaPath);
        }
    }

    [Fact]
    public void Load_ActorNotInMobilityOverrides_DefaultsToStatic(){
        var GlbPath=BuildGlbWithOneCubeAndOneLight("UnlistedCube");
        var MetaPath=WriteSceneMeta("SomeOtherActorEntirely","movable");
        try {
            LoadedScene Scene=SceneLoader.Load(GlbPath,MetaPath);

            LoadedSceneSpawn Spawn=Assert.Single(Scene.Spawns);
            Assert.Equal(PropMobility.Static,Spawn.Mobility);
        } finally {
            File.Delete(GlbPath);
            File.Delete(MetaPath);
        }
    }
}
