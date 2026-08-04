using System.Numerics;
using CSharpGameEngine.Graphics.D3D12;

namespace UserGameProject.Systems;

/// <summary>Drives the render <see cref="Camera"/> from the player's eye position + <see cref="CameraRig"/> each frame. First-person: Camera.Target doubles as the eye position itself (see <see cref="Camera.CreateFirstPerson"/>).</summary>
public static class CameraSystem
{
    public const float EyeHeight = 1.6f;

    public static void Update(Player player, ref Camera camera)
    {
        camera.Target = new Vector3(player.Position.X, player.Position.Y + EyeHeight, player.Position.Z);
        camera.Yaw = player.CameraRig.Yaw;
        camera.Pitch = player.CameraRig.Pitch;
    }
}
