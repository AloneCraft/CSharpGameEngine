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

    /// <summary>Wider than the orbit camera's range (a first-person view has no orbit-collision reason to stay near-level) - see <see cref="CreateFirstPerson"/>.</summary>
    public const float MinPitchRadiansFirstPerson = -85f * MathF.PI / 180f;
    public const float MaxPitchRadiansFirstPerson = 85f * MathF.PI / 180f;

    public Vector3 Target;
    public float Yaw;
    public float Pitch;
    public float Distance;
    public float FovYRadians;
    public float NearZ;
    public float FarZ;

    /// <summary>Pitch clamp range applied by <see cref="ApplyMouseDelta"/>. Set by the <c>Create*</c> factories; <see cref="MinPitchRadians"/>/<see cref="MaxPitchRadians"/> remain the orbit-camera default values callers may still compare against directly.</summary>
    public float PitchClampMinRadians;
    public float PitchClampMaxRadians;

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
            PitchClampMinRadians = MinPitchRadians,
            PitchClampMaxRadians = MaxPitchRadians,
        };
    }

    /// <summary>
    /// A first-person eye camera: <see cref="Distance"/> is zero, so <see cref="Target"/> doubles
    /// as the eye position itself (the caller updates it every frame to the player's eye position,
    /// exactly like the orbit camera's <see cref="Target"/> is already updated to the player's
    /// position each frame). <see cref="GetView"/> looks along <see cref="GetForward"/> from there
    /// rather than toward a distinct target point, since eye and target would otherwise coincide.
    /// </summary>
    public static Camera CreateFirstPerson(Vector3 eyePosition)
    {
        return new Camera
        {
            Target = eyePosition,
            Yaw = 0f,
            Pitch = 0f,
            Distance = 0f,
            FovYRadians = MathF.PI / 4f,
            // Non-reversed-Z perspective (CreatePerspectiveFieldOfView) concentrates almost all
            // depth-buffer precision within the first few units of Near, so Near/Far ratio matters
            // far more than either value alone. The original 0.05/1000 (20000:1) caused visible
            // Z-fighting across whole walls/ceilings in a ~38-unit room-scale level (M0004) - values
            // this close to the eye were never exercised by earlier vertical-slice scenes, where the
            // camera sat right next to the only two objects in view. 0.1/300 (3000:1) keeps a wide
            // safety margin over this room's scale while cutting the ratio by ~7x.
            NearZ = 0.1f,
            FarZ = 300f,
            PitchClampMinRadians = MinPitchRadiansFirstPerson,
            PitchClampMaxRadians = MaxPitchRadiansFirstPerson,
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

    /// <summary>
    /// At Distance > 0 (orbit cameras) this looks from the eye toward <see cref="Target"/>, as
    /// before. At Distance == 0 (see <see cref="CreateFirstPerson"/>) the eye and Target coincide,
    /// so CreateLookAt's from/to points would be identical (an undefined/degenerate view direction)
    /// - instead it looks from the eye along <see cref="GetForward"/>.
    /// </summary>
    public readonly Matrix4x4 GetView()
    {
        Vector3 eye = GetEyePosition();
        Vector3 lookAtPoint = Distance > 1e-4f ? Target : eye + GetForward();
        return Matrix4x4.CreateLookAt(eye, lookAtPoint, Vector3.UnitY);
    }

    public readonly Matrix4x4 GetProjection(float aspectRatio)
    {
        return Matrix4x4.CreatePerspectiveFieldOfView(FovYRadians, aspectRatio, NearZ, FarZ);
    }

    /// <summary>Applies raw mouse-delta input to yaw/pitch, clamping pitch to avoid gimbal flip.</summary>
    public void ApplyMouseDelta(float deltaX, float deltaY, float yawSensitivity, float pitchSensitivity)
    {
        Yaw += deltaX * yawSensitivity;
        Pitch = Math.Clamp(Pitch - (deltaY * pitchSensitivity), PitchClampMinRadians, PitchClampMaxRadians);
    }
}
