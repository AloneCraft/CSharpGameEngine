using System.Numerics;

namespace UserGameProject.Systems;

/// <summary>
/// Demonstrates the "movable" mobility archetype: a continuous spin, recomputed fresh from
/// elapsed time each frame (not accumulated frame-to-frame) so there is no drift and no need to
/// remember each prop's original position. There is no gameplay reason for the motion beyond
/// visibly proving Static vs Movable props are independently spawnable and updatable.
/// </summary>
public static class MovingPropSystem
{
    private const float SpinSpeedRadiansPerSecond = 0.8f;

    public static void Update(List<MovableStageProp> props, float totalTimeSeconds)
    {
        Quaternion spin = Quaternion.CreateFromAxisAngle(Vector3.UnitY, totalTimeSeconds * SpinSpeedRadiansPerSecond);

        for (int i = 0; i < props.Count; i++)
        {
            props[i].Rotation = new Rotation { X = spin.X, Y = spin.Y, Z = spin.Z, W = spin.W };
        }
    }
}
