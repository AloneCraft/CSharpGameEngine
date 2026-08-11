using System.Numerics;
using CSharpGameEngine.Authoring;
using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Unity;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>
/// UE5 ThirdPerson-style spring-arm orbit camera: yaw/pitch around a target point, at a fixed
/// distance. Pure System.Numerics logic with no GPU/TerraFX dependency, so it is unit-testable
/// without a device (mirrors the "pure-logic types stay separate for testability" precedent
/// already used for <see cref="ViewerRunOptions"/>/<see cref="TriangleRunOptions"/>, ADR-0003 §5).
/// Yaw is measured around +Y from +Z toward +X; pitch tilts toward +Y. Forward/Right direction math
/// is derived in RUF (<see cref="RufVector"/>/<see cref="RufQuaternion"/>) space under
/// <see cref="UnityCoordinateConvention"/> (ADR-0026) and converted to <see cref="Vector3"/> only at
/// each method's return - <see cref="GetView"/>/<see cref="GetProjection"/> stay on raw
/// <see cref="Vector3"/>/<see cref="Matrix4x4"/> since D3D's view/clip-space convention is outside
/// RUF's scope (see Docs/Specifications/Spatial-RUF.md "Rendering境界").
/// </summary>
public struct Camera {
    public const float MinPitchRadians=-80f*MathF.PI/180f;
    public const float MaxPitchRadians=40f*MathF.PI/180f;

    /// <summary>Wider than the orbit camera's range (a first-person view has no orbit-collision reason to stay near-level) - see <see cref="CreateFirstPerson"/>.</summary>
    public const float MinPitchRadiansFirstPerson=-85f*MathF.PI/180f;
    public const float MaxPitchRadiansFirstPerson=85f*MathF.PI/180f;

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

    /// <summary>
    /// <c>[SceneFactory]</c>: Scene DSLから呼べる(ADR-0040)。Scene DSLは任意のメソッド呼び出しを
    /// 許可しないが、既存のstaticファクトリを全部オブジェクト初期化子へ展開させるのは実用的でないため、
    /// 明示的にオプトインする。引数自体はScene DSLの値式でなければならない。
    /// </summary>
    [SceneFactory]
    public static Camera CreateDefault(Vector3 target){
        return new Camera {
            Target=target,
            Yaw=0f,
            Pitch=-15f*MathF.PI/180f,
            Distance=4.5f,
            FovYRadians=MathF.PI/4f,
            NearZ=0.1f,
            FarZ=1000f,
            PitchClampMinRadians=MinPitchRadians,
            PitchClampMaxRadians=MaxPitchRadians,
        };
    }

    /// <summary>
    /// A first-person eye camera: <see cref="Distance"/> is zero, so <see cref="Target"/> doubles
    /// as the eye position itself (the caller updates it every frame to the player's eye position,
    /// exactly like the orbit camera's <see cref="Target"/> is already updated to the player's
    /// position each frame). <see cref="GetView"/> looks along <see cref="GetForward"/> from there
    /// rather than toward a distinct target point, since eye and target would otherwise coincide.
    /// </summary>
    [SceneFactory]
    public static Camera CreateFirstPerson(Vector3 EyePosition){
        return new Camera {
            Target=EyePosition,
            Yaw=0f,
            Pitch=0f,
            Distance=0f,
            FovYRadians=MathF.PI/4f,
            // Non-reversed-Z perspective (CreatePerspectiveFieldOfView) concentrates almost all
            // depth-buffer precision within the first few units of Near, so Near/Far ratio matters
            // far more than either value alone. The original 0.05/1000 (20000:1) caused visible
            // Z-fighting across whole walls/ceilings in a ~38-unit room-scale level (M0004) - values
            // this close to the eye were never exercised by earlier vertical-slice scenes, where the
            // camera sat right next to the only two objects in view. 0.1/300 (3000:1) keeps a wide
            // safety margin over this room's scale while cutting the ratio by ~7x.
            NearZ=0.1f,
            FarZ=300f,
            PitchClampMinRadians=MinPitchRadiansFirstPerson,
            PitchClampMaxRadians=MaxPitchRadiansFirstPerson,
        };
    }

    /// <summary>Full 3D forward direction (includes pitch), from the eye toward the target.</summary>
    public readonly Vector3 GetForward(){
        // Child (pitch, in the camera's un-yawed local frame) applied first, then parent (yaw) -
        // see RufQuaternion.operator* semantics. Pitch rotates around RufVector.Left (not Right):
        // rotating Forward by +Pitch around Right would tilt it toward -Up, the opposite of this
        // type's documented "pitch tilts toward +Y" (verified by hand via the standard axis-angle
        // rotation formula, and pinned down permanently by
        // CameraTests.GetForward_MatchesLegacyTrigFormula_AcrossYawPitchSweep).
        RufQuaternion Facing=RufQuaternion.FromAxisAngle(RufVector.Up,Yaw)*RufQuaternion.FromAxisAngle(RufVector.Left,Pitch);
        RufVector Forward=RufQuaternion.RotateVector(Facing,RufVector.Forward).Normalize();
        return(Vector3)Spatial<UnityCoordinateConvention>.ToXyz(Forward);
    }

    /// <summary>Forward direction projected onto the XZ plane (pitch removed) - the basis for WASD movement.</summary>
    public readonly Vector3 GetFlatForward(){
        return(Vector3)Spatial<UnityCoordinateConvention>.ToXyz(GetFlatForwardRuf());
    }

    /// <summary>
    /// Right direction on the XZ plane. Built from <see cref="Spatial{TConvention}.RightFromUpForward"/>,
    /// which is Handedness-aware (ADR-0025) - under <see cref="UnityCoordinateConvention"/> (identity
    /// mapping, LeftHanded) this is provably identical to the previous ad-hoc
    /// <c>Cross(Vector3.UnitY, GetFlatForward())</c> formula (verified permanently by
    /// CameraTests.GetFlatRight_MatchesLegacyCrossProductFormula_AcrossYawSweep). The sign itself
    /// still awaits interactive confirmation that it *feels* correct when strafing (ADR-0004 §6
    /// precedent) - this change only proves the formula is internally consistent, not that left/right
    /// matches human intuition at the keyboard.
    /// </summary>
    public readonly Vector3 GetFlatRight(){
        RufVector Right=Spatial<UnityCoordinateConvention>.RightFromUpForward(RufVector.Up,GetFlatForwardRuf());
        return(Vector3)Spatial<UnityCoordinateConvention>.ToXyz(Right);
    }

    private readonly RufVector GetFlatForwardRuf(){
        RufQuaternion YawOnly=RufQuaternion.FromAxisAngle(RufVector.Up,Yaw);
        return RufQuaternion.RotateVector(YawOnly,RufVector.Forward).Normalize();
    }

    public readonly Vector3 GetEyePosition(){
        return Target-(GetForward()*Distance);
    }

    /// <summary>
    /// At Distance > 0 (orbit cameras) this looks from the eye toward <see cref="Target"/>, as
    /// before. At Distance == 0 (see <see cref="CreateFirstPerson"/>) the eye and Target coincide,
    /// so CreateLookAt's from/to points would be identical (an undefined/degenerate view direction)
    /// - instead it looks from the eye along <see cref="GetForward"/>.
    /// </summary>
    public readonly Matrix4x4 GetView(){
        Vector3 Eye=GetEyePosition();
        Vector3 LookAtPoint=Distance>1e-4f?Target:Eye+GetForward();
        return BuildViewMatrix(Eye,LookAtPoint,Vector3.UnitY);
    }

    public readonly Matrix4x4 GetProjection(float AspectRatio){
        return Matrix4x4.CreatePerspectiveFieldOfView(FovYRadians,AspectRatio,NearZ,FarZ);
    }

    /// <summary>
    /// Thin shared wrapper around <see cref="Matrix4x4.CreateLookAt"/> - lets <see cref="ModelRenderer"/>'s
    /// independent bounds-fit view (which does not go through a <see cref="Camera"/> instance) build
    /// its view matrix the same way <see cref="GetView"/> does, instead of duplicating the call
    /// (ADR-0026).
    /// </summary>
    public static Matrix4x4 BuildViewMatrix(Vector3 Eye,Vector3 target,Vector3 Up)=>Matrix4x4.CreateLookAt(Eye,target,Up);

    /// <summary>Applies raw mouse-delta input to yaw/pitch, clamping pitch to avoid gimbal flip.</summary>
    public void ApplyMouseDelta(float DeltaX,float DeltaY,float YawSensitivity,float PitchSensitivity){
        Yaw+=DeltaX*YawSensitivity;
        Pitch=Math.Clamp(Pitch-(DeltaY*PitchSensitivity),PitchClampMinRadians,PitchClampMaxRadians);
    }
}
