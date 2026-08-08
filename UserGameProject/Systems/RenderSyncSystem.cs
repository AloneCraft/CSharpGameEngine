using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;

namespace UserGameProject.Systems;

/// <summary>
/// Pushes ECS transform/camera/light/fog state into <see cref="StageRenderer"/> once per frame -
/// the only system that consumes <see cref="RenderObjectRef"/> indices or writes to the renderer.
/// <see cref="SyncProp"/> requires exactly Position+Rotation+Scale+RenderObjectRef, which both the
/// static and movable stage-prop archetypes satisfy (MovableStageProp is a strict superset), so one
/// method's Query naturally covers both - the old AoS-era code needed two near-identical overloads
/// only because each hand-declared Archetype needed its own exact-shape collection.
/// </summary>
[GameSystem]
public static class RenderSyncSystem
{
    [Update]
    public static void SyncProp(in Position position, in Rotation rotation, in Scale scale, in RenderObjectRef renderObjectRef, [Context] StageRenderer renderer)
    {
        renderer.SetObjectTransform(renderObjectRef.Index, ComposeTransform(position, rotation, scale));
    }

    [Update]
    public static void SyncCameraToRenderer(in Camera camera, in PlayerTag _, [Context] StageRenderer renderer)
    {
        renderer.SetCamera(camera);
    }

    /// <summary>Applies the single sun singleton, if any (0 matching entities = "not configured, leave the renderer's current/default value alone" - the same behavior the old code got from an explicit Count check, here it falls out of the Query matching zero rows).</summary>
    [Update]
    public static void SyncSunLight(in DirectionalLightData light, [Context] StageRenderer renderer)
    {
        renderer.SetDirectionalLight(
            new Vector3(light.DirectionTowardLightX, light.DirectionTowardLightY, light.DirectionTowardLightZ),
            new Vector3(light.ColorR, light.ColorG, light.ColorB),
            light.Intensity);
    }

    [Update]
    public static void SyncAmbientLight(in AmbientLightData ambient, [Context] StageRenderer renderer)
    {
        renderer.SetAmbientLight(new Vector3(ambient.ColorR, ambient.ColorG, ambient.ColorB));
    }

    [Update]
    public static void SyncFog(in FogData fog, [Context] StageRenderer renderer)
    {
        renderer.SetFog(new FogParams(fog.Density, fog.HeightFalloff, new Vector3(fog.ColorR, fog.ColorG, fog.ColorB)));
    }

    private static Matrix4x4 ComposeTransform(in Position position, in Rotation rotation, in Scale scale)
    {
        Quaternion rotationQuaternion = new(rotation.X, rotation.Y, rotation.Z, rotation.W);
        return Matrix4x4.CreateScale(scale.X, scale.Y, scale.Z)
            * Matrix4x4.CreateFromQuaternion(rotationQuaternion)
            * Matrix4x4.CreateTranslation(position.X, position.Y, position.Z);
    }
}
