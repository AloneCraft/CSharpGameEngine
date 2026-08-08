using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Unity;

namespace UserGameProject.Systems;

/// <summary>
/// Drives the render <see cref="Camera"/> from the player's eye position + <see cref="CameraRig"/>
/// each frame. <see cref="Camera"/> lives as a component directly on the player entity (there is
/// only ever one) rather than a separate singleton entity, so this stays a single-archetype,
/// single-entity SoA loop instead of needing a cross-entity join the new model does not support.
/// First-person: Camera.Target doubles as the eye position itself (see
/// <see cref="Camera.CreateFirstPerson"/>).
/// </summary>
[GameSystem]
public static class CameraSystem
{
    public const float EyeHeight = 1.6f;

    [Update]
    public static void SyncCamera(ref Camera camera, in Position position, in CameraRig cameraRig, in PlayerTag _)
    {
        // EyeHeight is added along RUF Up (ADR-0028), matching Camera/PlayerControlSystem's shared
        // RUF-space convention rather than assuming which raw component is "up".
        RufVector positionRuf = new(position.X, position.Y, position.Z);
        RufVector eyeRuf = positionRuf + (RufVector.Up * EyeHeight);
        camera.Target = (Vector3)Spatial<UnityCoordinateConvention>.ToXyz(eyeRuf);
        camera.Yaw = cameraRig.Yaw;
        camera.Pitch = cameraRig.Pitch;
    }
}
