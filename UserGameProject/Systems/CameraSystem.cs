using System.Numerics;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Unity;

namespace UserGameProject.Systems;

/// <summary>Drives the render <see cref="Camera"/> from the player's eye position + <see cref="CameraRig"/> each frame. First-person: Camera.Target doubles as the eye position itself (see <see cref="Camera.CreateFirstPerson"/>).</summary>
public static class CameraSystem
{
    public const float EyeHeight = 1.6f;

    public static void Update(Player player, ref Camera camera)
    {
        // EyeHeight is added along RUF Up (ADR-0028), matching Camera/PlayerControlSystem's shared
        // RUF-space convention rather than assuming which raw component is "up".
        RufVector positionRuf = new(player.Position.X, player.Position.Y, player.Position.Z);
        RufVector eyeRuf = positionRuf + (RufVector.Up * EyeHeight);
        camera.Target = (Vector3)Spatial<UnityCoordinateConvention>.ToXyz(eyeRuf);
        camera.Yaw = player.CameraRig.Yaw;
        camera.Pitch = player.CameraRig.Pitch;
    }
}
