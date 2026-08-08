using System.Numerics;
using CSharpGameEngine.Entities;

namespace UserGameProject.Systems;

/// <summary>
/// Demonstrates the "movable" mobility archetype: a continuous spin, recomputed fresh from
/// elapsed time each frame (not accumulated frame-to-frame) so there is no drift and no need to
/// remember each prop's original position. There is no gameplay reason for the motion beyond
/// visibly proving Static vs Movable props are independently spawnable and updatable.
/// </summary>
[GameSystem]
public static class MovingPropSystem
{
    private const float SpinSpeedRadiansPerSecond = 0.8f;

    [Update]
    public static void Spin(ref Rotation rotation, in MovableTag _, [Context] in FrameContext frame)
    {
        Quaternion spin = Quaternion.CreateFromAxisAngle(Vector3.UnitY, frame.ElapsedSeconds * SpinSpeedRadiansPerSecond);
        rotation = new Rotation { X = spin.X, Y = spin.Y, Z = spin.Z, W = spin.W };
    }
}
