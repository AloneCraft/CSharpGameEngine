using System.Numerics;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>
/// Identity-LTC evaluation for one-sided Lambertian rectangular emitters. The returned value is
/// the cosine-weighted solid angle at the receiver, so RGB luminance in nits multiplies it directly
/// to produce irradiance in lux before the material BRDF is applied.
/// </summary>
public static class AreaLightLtc {
    private const float MinimumDirectionLengthSquared=1e-12f;
    private const float BasisOrthogonalityTolerance=1e-3f;
    private const float MinimumRightHandedness=0.999f;

    /// <summary>
    /// Evaluates the non-negative cosine-weighted solid angle of a one-sided rectangle. The source
    /// emits in <paramref name="Forward"/> (Transform local +Z); <paramref name="Right"/> and
    /// <paramref name="Up"/> define its local +X/+Y rectangle axes and must form a right-handed
    /// orthonormal frame with it.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when a vector, extent, or basis is invalid.</exception>
    public static float EvaluateOneSidedProjectedSolidAngle(
        Vector3 Center,Vector3 Forward,Vector3 Right,Vector3 Up,
        float HalfWidth,float HalfHeight,
        Vector3 ReceiverPosition,Vector3 ReceiverNormal){
        if(!TryEvaluateOneSidedProjectedSolidAngle(
            Center,Forward,Right,Up,HalfWidth,HalfHeight,
            ReceiverPosition,ReceiverNormal,out float Factor)){
            throw new ArgumentException(
                "A rectangular Area Light requires finite positions, positive extents, and a right-handed orthonormal basis.");
        }
        return Factor;
    }

    /// <summary>
    /// Attempts to evaluate <see cref="EvaluateOneSidedProjectedSolidAngle"/> without allocating
    /// or throwing for malformed source data. A valid back-facing or horizon-clipped rectangle
    /// succeeds with a zero factor.
    /// </summary>
    public static bool TryEvaluateOneSidedProjectedSolidAngle(
        Vector3 Center,Vector3 Forward,Vector3 Right,Vector3 Up,
        float HalfWidth,float HalfHeight,
        Vector3 ReceiverPosition,Vector3 ReceiverNormal,out float Factor){
        Factor=0f;
        if(!IsFinite(Center)||!IsFinite(ReceiverPosition)
            ||!float.IsFinite(HalfWidth)||!float.IsFinite(HalfHeight)
            ||HalfWidth<=0f||HalfHeight<=0f
            ||!TryNormalize(Forward,out Vector3 UnitForward)
            ||!TryNormalize(Right,out Vector3 UnitRight)
            ||!TryNormalize(Up,out Vector3 UnitUp)
            ||!TryNormalize(ReceiverNormal,out Vector3 UnitReceiverNormal)){
            return false;
        }

        if(MathF.Abs(Vector3.Dot(UnitForward,UnitRight))>BasisOrthogonalityTolerance
            ||MathF.Abs(Vector3.Dot(UnitForward,UnitUp))>BasisOrthogonalityTolerance
            ||MathF.Abs(Vector3.Dot(UnitRight,UnitUp))>BasisOrthogonalityTolerance
            ||Vector3.Dot(Vector3.Cross(UnitRight,UnitUp),UnitForward)<MinimumRightHandedness){
            return false;
        }

        // A Lambertian emitter has no direct contribution through its back face. The whole
        // rectangle lies in one plane, so its center-side test is sufficient and stable.
        if(Vector3.Dot(UnitForward,ReceiverPosition-Center)<=0f){
            return true;
        }

        Span<Vector3> Directions=stackalloc Vector3[4];
        Vector3 Width=UnitRight*HalfWidth;
        Vector3 Height=UnitUp*HalfHeight;
        // The rectangle's Right/Up winding points toward Forward at the emitter. From a receiver
        // on that emitting side, the directions point back toward the emitter, so use the reverse
        // winding to make the spherical area vector face the incoming hemisphere.
        if(!TryDirection(Center-Width-Height,ReceiverPosition,out Directions[0])
            ||!TryDirection(Center-Width+Height,ReceiverPosition,out Directions[1])
            ||!TryDirection(Center+Width+Height,ReceiverPosition,out Directions[2])
            ||!TryDirection(Center+Width-Height,ReceiverPosition,out Directions[3])){
            return false;
        }

        Span<Vector3> ClippedDirections=stackalloc Vector3[8];
        int ClippedCount=ClipToReceiverHemisphere(
            Directions,UnitReceiverNormal,ClippedDirections);
        if(ClippedCount<3){
            return true;
        }

        Vector3 Integral=Vector3.Zero;
        for(int Index=0;Index<ClippedCount;Index++){
            Integral+=IntegrateSphericalEdge(
                ClippedDirections[Index],ClippedDirections[(Index+1)%ClippedCount]);
        }

        float ProjectedSolidAngle=Vector3.Dot(UnitReceiverNormal,Integral);
        if(!float.IsFinite(ProjectedSolidAngle)){
            return false;
        }

        // The integral of cos(theta) over one receiver hemisphere is pi. Clamp only round-off
        // outside the physical interval; no distance attenuation is applied here because the
        // rectangular solid angle already contains the inverse-square behavior.
        Factor=Math.Clamp(ProjectedSolidAngle,0f,MathF.PI);
        return true;
    }

    private static bool TryDirection(Vector3 Corner,Vector3 ReceiverPosition,out Vector3 Direction){
        Vector3 ToCorner=Corner-ReceiverPosition;
        return TryNormalize(ToCorner,out Direction);
    }

    private static int ClipToReceiverHemisphere(
        ReadOnlySpan<Vector3> Directions,Vector3 ReceiverNormal,Span<Vector3> Clipped){
        Vector3 Previous=Directions[^1];
        float PreviousHeight=Vector3.Dot(ReceiverNormal,Previous);
        bool PreviousInside=PreviousHeight>0f;
        int Count=0;

        for(int Index=0;Index<Directions.Length;Index++){
            Vector3 Current=Directions[Index];
            float CurrentHeight=Vector3.Dot(ReceiverNormal,Current);
            bool CurrentInside=CurrentHeight>0f;

            if(CurrentInside!=PreviousInside){
                float Denominator=PreviousHeight-CurrentHeight;
                if(MathF.Abs(Denominator)>float.Epsilon
                    &&TryNormalize(Previous+((Current-Previous)*(PreviousHeight/Denominator)),
                        out Vector3 Intersection)){
                    Clipped[Count++]=Intersection;
                }
            }

            if(CurrentInside){
                Clipped[Count++]=Current;
            }

            Previous=Current;
            PreviousHeight=CurrentHeight;
            PreviousInside=CurrentInside;
        }

        return Count;
    }

    /// <summary>
    /// Returns the vector integral over one great-circle edge. The factor one-half is the
    /// spherical area-vector identity: integral(omega dOmega)=1/2 integral(r cross dr).
    /// </summary>
    private static Vector3 IntegrateSphericalEdge(Vector3 From,Vector3 To){
        Vector3 Cross=Vector3.Cross(From,To);
        float SinAngle=Cross.Length();
        if(SinAngle<=float.Epsilon){
            return Vector3.Zero;
        }

        float CosAngle=Math.Clamp(Vector3.Dot(From,To),-1f,1f);
        float Angle=MathF.Atan2(SinAngle,CosAngle);
        return Cross*(0.5f*Angle/SinAngle);
    }

    private static bool TryNormalize(Vector3 Value,out Vector3 Normalized){
        if(!IsFinite(Value)){
            Normalized=default;
            return false;
        }

        float LengthSquared=Value.LengthSquared();
        if(!float.IsFinite(LengthSquared)||LengthSquared<=MinimumDirectionLengthSquared){
            Normalized=default;
            return false;
        }

        Normalized=Value/MathF.Sqrt(LengthSquared);
        return IsFinite(Normalized);
    }

    private static bool IsFinite(Vector3 Value)=>
        float.IsFinite(Value.X)&&float.IsFinite(Value.Y)&&float.IsFinite(Value.Z);
}
