using System.Numerics;
using CSharpGameEngine.Graphics.D3D12;

namespace UserGameProject.Systems;

/// <summary>Pushes ECS transform/light/fog state into <see cref="StageRenderer"/> once per frame - the only system that consumes <see cref="RenderObjectRef"/> indices.</summary>
public static class RenderSyncSystem
{
    public static void SyncProps(List<StageProp> props, StageRenderer renderer)
    {
        foreach (StageProp prop in props)
        {
            renderer.SetObjectTransform(prop.RenderObjectRef.Index, ComposeTransform(prop.Position, prop.Rotation, prop.Scale));
        }
    }

    public static void SyncMovableProps(List<MovableStageProp> props, StageRenderer renderer)
    {
        foreach (MovableStageProp prop in props)
        {
            renderer.SetObjectTransform(prop.RenderObjectRef.Index, ComposeTransform(prop.Position, prop.Rotation, prop.Scale));
        }
    }

    /// <summary>Applies the single sun/ambient/fog singleton found, if any (0 entities = "not configured, leave the renderer's current/default value alone").</summary>
    public static void SyncLighting(List<SunLight> suns, List<AmbientLight> ambients, List<FogVolume> fogs, StageRenderer renderer)
    {
        if (suns.Count > 0)
        {
            DirectionalLightData light = suns[0].DirectionalLightData;
            renderer.SetDirectionalLight(
                new Vector3(light.DirectionTowardLightX, light.DirectionTowardLightY, light.DirectionTowardLightZ),
                new Vector3(light.ColorR, light.ColorG, light.ColorB),
                light.Intensity);
        }

        if (ambients.Count > 0)
        {
            AmbientLightData ambient = ambients[0].AmbientLightData;
            renderer.SetAmbientLight(new Vector3(ambient.ColorR, ambient.ColorG, ambient.ColorB));
        }

        if (fogs.Count > 0)
        {
            FogData fog = fogs[0].FogData;
            renderer.SetFog(new FogParams(fog.Density, fog.HeightFalloff, new Vector3(fog.ColorR, fog.ColorG, fog.ColorB)));
        }
    }

    private static Matrix4x4 ComposeTransform(Position position, Rotation rotation, Scale scale)
    {
        Quaternion rotationQuaternion = new(rotation.X, rotation.Y, rotation.Z, rotation.W);
        return Matrix4x4.CreateScale(scale.X, scale.Y, scale.Z)
            * Matrix4x4.CreateFromQuaternion(rotationQuaternion)
            * Matrix4x4.CreateTranslation(position.X, position.Y, position.Z);
    }
}
