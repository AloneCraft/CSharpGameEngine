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
    /// <summary>World-space camera up. Transform-driven cameras set this from their resolved local +Y basis so roll is preserved by <see cref="GetView"/>.</summary>
    public Vector3 Up;

    public float Yaw;
    public float Pitch;
    public float Distance;
    public float FovYRadians;
    public float NearZ;
    public float FarZ;
    /// <summary>HDR radiance to display exposure in EV100. Zero preserves the legacy exposure.</summary>
    public float ExposureEv100;


    /// <summary>Pitch clamp range applied by <see cref="ApplyMouseDelta"/>. Set by the <c>Create*</c> factories; <see cref="MinPitchRadians"/>/<see cref="MaxPitchRadians"/> remain the orbit-camera default values callers may still compare against directly.</summary>
    public float PitchClampMinRadians;
    public float PitchClampMaxRadians;

    /// <summary>
    /// <c>[SceneFactory]</c>: Scene DSLから呼べる(ADR-0040)。Scene DSLは任意のメソッド呼び出しを
    /// 許可しないが、既存のstaticファクトリを全部オブジェクト初期化子へ展開させるのは実用的でないため、
    /// 明示的にオプトインする。引数自体はScene DSLの値式でなければならない。
    /// </summary>
    [SceneFactory]
    public static Camera CreateDefault(Vector3 Target){
        return new Camera {
            Target=Target,
            Up=Vector3.UnitY,
            Yaw=0f,
            Pitch=-15f*MathF.PI/180f,
            Distance=4.5f,
            FovYRadians=MathF.PI/4f,
            NearZ=0.1f,
            FarZ=1000f,
            ExposureEv100=0f,
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
            Up=Vector3.UnitY,
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
            ExposureEv100=0f,
            PitchClampMinRadians=MinPitchRadiansFirstPerson,
            PitchClampMaxRadians=MaxPitchRadiansFirstPerson,
        };
    }

    /// <summary>
    /// An orbit camera framed so the given world-space bounds fit entirely in view, seen from a
    /// fixed elevated diagonal angle. Reuses the exact (1,0.75,1) eye direction and
    /// Radius/sin(FovY/2)*1.5 distance relationship already used by the accepted thumbnail and
    /// shared Preview Host camera baselines, instead of inventing a differently-angled fifth
    /// "frame everything" convention. Since <see cref="Camera"/>
    /// has no raw eye-position constructor, the direction is converted into this type's orbit
    /// (<see cref="Target"/>/<see cref="Yaw"/>/<see cref="Pitch"/>/<see cref="Distance"/>)
    /// representation by inverting <see cref="GetForward"/>'s documented
    /// Forward=(CosPitch*SinYaw, SinPitch, CosPitch*CosYaw) formula.
    /// </summary>
    [SceneFactory]
    public static Camera CreateBoundsFit(Vector3 BoundsMin,Vector3 BoundsMax){
        var Target=(BoundsMin+BoundsMax)/2f;
        var Radius=MathF.Max(Vector3.Distance(BoundsMin,BoundsMax)/2f,0.001f);
        var FovY=MathF.PI/4f;
        // Distance=Radius/sin(FovY/2) is the exact tangent distance at which the enclosing sphere
        // (radius=half the AABB diagonal, so it bounds the box from every direction, not just this
        // one) fills the vertical FOV edge-to-edge - true regardless of view direction, since a
        // sphere's silhouette is the same from anywhere outside it. *1.1f only adds a small safety
        // margin over that exact value, unlike the accepted thumbnail/preview *1.5f/*3.0f baselines
        // (tuned for arbitrary-rotation previews); a fixed demo angle can sit close to the tangent
        // distance and still keep every corner of the level in frame.
        var Distance=Radius/MathF.Sin(FovY/2f)*1.1f;

        var Forward=-Vector3.Normalize(new Vector3(1f,0.75f,1f));

        return new Camera {
            Target=Target,
            Up=Vector3.UnitY,
            Yaw=MathF.Atan2(Forward.X,Forward.Z),
            Pitch=MathF.Asin(Forward.Y),
            Distance=Distance,
            FovYRadians=FovY,
            NearZ=0.1f,
            FarZ=Distance+(Radius*4f),
            ExposureEv100=0f,
            PitchClampMinRadians=MinPitchRadians,
            PitchClampMaxRadians=MaxPitchRadians,
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
        var Facing=RufQuaternion.FromAxisAngle(RufVector.Up, this.Yaw)*RufQuaternion.FromAxisAngle(RufVector.Left, this.Pitch);
        var Forward=RufQuaternion.RotateVector(Facing,RufVector.Forward).Normalize();
        return(Vector3)Spatial<UnityCoordinateConvention>.ToXyz(Forward);
    }

    /// <summary>
    /// Camera up after removing any component parallel to <see cref="GetForward"/>.
    /// A default-constructed legacy camera has no stored up vector, so it remains world-up
    /// compatible instead of producing a degenerate view.
    /// </summary>
    public readonly Vector3 GetUp(){
        Vector3 Forward=this.GetForward();
        return TryNormalizeOrientation(Forward,this.Up,out _,out Vector3 Up)
            ?Up
            :GetFallbackUp(Forward);
    }

    /// <summary>
    /// Applies a resolved Transform basis to this camera. <paramref name="Forward"/> is the
    /// Transform's local +Z direction and <paramref name="Up"/> is local +Y. Both are
    /// orthonormalized before mutating the camera, so a rejected non-finite or collapsed basis
    /// leaves its last valid view untouched.
    /// </summary>
    public bool TrySetWorldOrientation(Vector3 Forward,Vector3 Up){
        if(!TryNormalizeOrientation(Forward,Up,out Vector3 NormalizedForward,out Vector3 NormalizedUp)){
            return false;
        }

        this.Yaw=MathF.Atan2(NormalizedForward.X,NormalizedForward.Z);
        this.Pitch=MathF.Asin(Math.Clamp(NormalizedForward.Y,-1f,1f));
        this.Up=NormalizedUp;
        return true;
    }

    /// <summary>
    /// Determines whether a resolved Transform basis is visually the legacy identity orientation.
    /// Both axes are checked so a pure local +Z roll is not mistaken for an identity camera.
    /// </summary>
    public static bool IsIdentityWorldOrientation(Vector3 Forward,Vector3 Up){
        const float CosineTolerance=1e-5f;
        return TryNormalizeOrientation(Forward,Up,out Vector3 NormalizedForward,out Vector3 NormalizedUp)
            &&Vector3.Dot(NormalizedForward,Vector3.UnitZ)>=1f-CosineTolerance
            &&Vector3.Dot(NormalizedUp,Vector3.UnitY)>=1f-CosineTolerance;
    }

    /// <summary>
    /// Normalizes a Transform's local +Z/+Y world basis while preserving the +Z direction.
    /// The up vector is projected off forward so a non-uniform parent scale cannot skew the
    /// GPU view matrix. A collapsed or non-finite basis is rejected.
    /// </summary>
    public static bool TryNormalizeOrientation(
        Vector3 Forward,Vector3 Up,
        out Vector3 NormalizedForward,out Vector3 NormalizedUp){
        if(!TryNormalize(Forward,out NormalizedForward)){
            NormalizedUp=default;
            return false;
        }

        Vector3 OrthogonalUp=Up-(NormalizedForward*Vector3.Dot(Up,NormalizedForward));
        return TryNormalize(OrthogonalUp,out NormalizedUp);
    }

    private static bool TryNormalize(Vector3 Value,out Vector3 Normalized){
        var LengthSquared=Value.LengthSquared();
        if(!float.IsFinite(Value.X)||!float.IsFinite(Value.Y)||!float.IsFinite(Value.Z)
            ||!float.IsFinite(LengthSquared)||LengthSquared<=1e-12f){
            Normalized=default;
            return false;
        }

        Normalized=Vector3.Normalize(Value);
        return true;
    }

    private static Vector3 GetFallbackUp(Vector3 Forward){
        Vector3 Reference=MathF.Abs(Forward.Y)<0.999f?Vector3.UnitY:Vector3.UnitZ;
        Vector3 Orthogonal=Reference-(Forward*Vector3.Dot(Reference,Forward));
        return Vector3.Normalize(Orthogonal);
    }

    /// <summary>Forward direction projected onto the XZ plane (pitch removed) - the basis for WASD movement.</summary>
    public readonly Vector3 GetFlatForward(){
        return(Vector3)Spatial<UnityCoordinateConvention>.ToXyz(this.GetFlatForwardRuf());
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
        var Right=Spatial<UnityCoordinateConvention>.RightFromUpForward(RufVector.Up, this.GetFlatForwardRuf());
        return(Vector3)Spatial<UnityCoordinateConvention>.ToXyz(Right);
    }

    private readonly RufVector GetFlatForwardRuf(){
        var YawOnly=RufQuaternion.FromAxisAngle(RufVector.Up, this.Yaw);
        return RufQuaternion.RotateVector(YawOnly,RufVector.Forward).Normalize();
    }

    public readonly Vector3 GetEyePosition(){
        return this.Target-(this.GetForward()* this.Distance);
    }

    /// <summary>
    /// At Distance > 0 (orbit cameras) this looks from the eye toward <see cref="Target"/>, as
    /// before. At Distance == 0 (see <see cref="CreateFirstPerson"/>) the eye and Target coincide,
    /// so CreateLookAt's from/to points would be identical (an undefined/degenerate view direction)
    /// - instead it looks from the eye along <see cref="GetForward"/>.
    /// </summary>
    public readonly Matrix4x4 GetView(){
        var Eye= this.GetEyePosition();
        var LookAtPoint= this.Distance>1e-4f? this.Target:Eye+ this.GetForward();
        return BuildViewMatrix(Eye,LookAtPoint,this.GetUp());
    }

    public readonly Matrix4x4 GetProjection(float AspectRatio){
        return Matrix4x4.CreatePerspectiveFieldOfView(this.FovYRadians,AspectRatio, this.NearZ, this.FarZ);
    }

    /// <summary>
    /// Thin shared wrapper around <see cref="Matrix4x4.CreateLookAt"/> - lets <see cref="ModelRenderer"/>'s
    /// independent bounds-fit view (which does not go through a <see cref="Camera"/> instance) build
    /// its view matrix the same way <see cref="GetView"/> does, instead of duplicating the call
    /// (ADR-0026).
    /// </summary>
    public static Matrix4x4 BuildViewMatrix(Vector3 Eye,Vector3 Target,Vector3 Up)=>Matrix4x4.CreateLookAt(Eye,Target,Up);

    /// <summary>Applies raw mouse-delta input to yaw/pitch, clamping pitch to avoid gimbal flip.</summary>
    public void ApplyMouseDelta(float DeltaX,float DeltaY,float YawSensitivity,float PitchSensitivity){
        this.Yaw+=DeltaX*YawSensitivity;
        this.Pitch=Math.Clamp(this.Pitch-(DeltaY*PitchSensitivity), this.PitchClampMinRadians, this.PitchClampMaxRadians);
    }
}
