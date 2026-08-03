using System.Numerics;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>
/// UE5 ThirdPerson-style spring-arm orbit camera: yaw/pitch around a target point, at a fixed
/// distance. Pure System.Numerics logic with no GPU/TerraFX dependency, so it is unit-testable
/// without a device (mirrors the "pure-logic types stay separate for testability" precedent
/// already used for <see cref="ViewerRunOptions"/>/<see cref="TriangleRunOptions"/>, ADR-0003 §5).
/// Yaw is measured around +Y from +Z toward +X; pitch tilts toward +Y.
/// </summary>
public struct Camera
{
    public const float MinPitchRadians = -80f * MathF.PI / 180f;
    public const float MaxPitchRadians = 40f * MathF.PI / 180f;

    public Vector3 Target;
    public float Yaw;
    public float Pitch;
    public float Distance;
    public float FovYRadians;
    public float NearZ;
    public float FarZ;

    public static Camera CreateDefault(Vector3 target)
    {
        return new Camera
        {
            Target = target,
            Yaw = 0f,
            Pitch = -15f * MathF.PI / 180f,
            Distance = 4.5f,
            FovYRadians = MathF.PI / 4f,
            NearZ = 0.1f,
            FarZ = 1000f,
        };
    }

    /// <summary>Full 3D forward direction (includes pitch), from the eye toward the target.</summary>
    public readonly Vector3 GetForward()
    {
        float cosPitch = MathF.Cos(Pitch);
        return Vector3.Normalize(new Vector3(
            cosPitch * MathF.Sin(Yaw),
            MathF.Sin(Pitch),
            cosPitch * MathF.Cos(Yaw)));
    }

    /// <summary>Forward direction projected onto the XZ plane (pitch removed) - the basis for WASD movement.</summary>
    public readonly Vector3 GetFlatForward()
    {
        return Vector3.Normalize(new Vector3(MathF.Sin(Yaw), 0f, MathF.Cos(Yaw)));
    }

    /// <summary>
    /// Right direction on the XZ plane. Sign is provisional until verified interactively (see
    /// ADR-0004 §6 precedent: handedness/sign conventions for this engine are only confirmed
    /// empirically at runtime, not derived on paper) - flip if strafing feels reversed.
    /// </summary>
    public readonly Vector3 GetFlatRight()
    {
        return Vector3.Normalize(Vector3.Cross(Vector3.UnitY, GetFlatForward()));
    }

    public readonly Vector3 GetEyePosition()
    {
        return Target - (GetForward() * Distance);
    }

    public readonly Matrix4x4 GetView()
    {
        return Matrix4x4.CreateLookAt(GetEyePosition(), Target, Vector3.UnitY);
    }

    public readonly Matrix4x4 GetProjection(float aspectRatio)
    {
        return Matrix4x4.CreatePerspectiveFieldOfView(FovYRadians, aspectRatio, NearZ, FarZ);
    }

    /// <summary>Applies raw mouse-delta input to yaw/pitch, clamping pitch to avoid gimbal flip.</summary>
    public void ApplyMouseDelta(float deltaX, float deltaY, float yawSensitivity, float pitchSensitivity)
    {
        Yaw += deltaX * yawSensitivity;
        Pitch = Math.Clamp(Pitch - (deltaY * pitchSensitivity), MinPitchRadians, MaxPitchRadians);
    }
}
