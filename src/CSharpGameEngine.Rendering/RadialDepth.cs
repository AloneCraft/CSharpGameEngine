using System.Numerics;

namespace CSharpGameEngine.Rendering;

/// <summary>How a view turns distance into depth (M0099, ADR-0076).</summary>
public enum RenderDepthMapping {
    /// <summary>The hyperbolic depth of the perspective projection.</summary>
    Standard=0,
    /// <summary>Depth is the logarithm of the physical distance from the eye; the image is the standard one.</summary>
    Radial=1,
    /// <summary>
    /// The world is compressed radially around another camera and seen from outside (Omni's RadialObserverRig).
    /// Only camera previews use it.
    /// </summary>
    RadialObserver=2,
}

/// <summary>
/// Omni's Smooth radial curve in metres: <c>G(r) = log2(1 + r / T)</c>. It is linear near the eye
/// (slope <c>1 / (T ln 2)</c>) and logarithmic beyond the transition length <c>T</c>, so a 32-bit depth keeps a
/// near-constant relative resolution from millimetres to the Q64.64 range. The Stage shader's
/// <c>RadialG</c> evaluates the same expression; this type is its CPU definition and test oracle.
/// </summary>
public readonly record struct RadialDepthCurve {
    public const float DefaultTransitionMeters=1f;
    private RadialDepthCurve(float TransitionMeters){
        this.TransitionMeters=TransitionMeters;
    }
    /// <summary>Distance where the curve turns from linear to logarithmic.</summary>
    public float TransitionMeters { get; }
    public static RadialDepthCurve Default=>new(DefaultTransitionMeters);
    public static bool IsValidTransition(float TransitionMeters)=>float.IsFinite(TransitionMeters)&&TransitionMeters>0f;
    public static bool TryCreate(float TransitionMeters,out RadialDepthCurve Curve){
        Curve=IsValidTransition(TransitionMeters)?new RadialDepthCurve(TransitionMeters):default;
        return Curve.TransitionMeters>0f;
    }
    /// <summary><c>log2(1 + r / T)</c> for a non-negative distance in metres.</summary>
    public float G(float DistanceMeters)=>Log2OnePlus(DistanceMeters/this.TransitionMeters);
    /// <summary>The compressed distance in metres, <c>T · G(r)</c>, used by the observer view.</summary>
    public float Map(float DistanceMeters)=>this.TransitionMeters*this.G(DistanceMeters);
    /// <summary>
    /// <c>log2(1 + x)</c> without losing small arguments to the rounding of <c>1 + x</c>: the rounded sum's
    /// logarithm is rescaled by <c>x / (u − 1)</c> (Kahan's log1p). The shader uses the same expression.
    /// </summary>
    public static float Log2OnePlus(float X){
        var U=1f+X;
        return U==1f?X/MathF.Log(2f):MathF.Log2(U)*(X/(U-1f));
    }
}

/// <summary>
/// Everything a radial view derives from its camera (M0099): the depth normaliser, the far plane kept per fragment,
/// the finite view-projection for the passes that invert it, and the observer view's compressed-world radius.
/// </summary>
public readonly record struct RadialDepthProjection {
    /// <summary>The largest float below 1: geometry never reaches the cleared depth, so the sky composite keeps working.</summary>
    public const float MaximumDepth=0.99999994f;
    /// <summary>Ratio of the auxiliary far plane to its near plane; it keeps the matrix invertible in float.</summary>
    public const float AuxiliaryDepthRatio=65536f;
    /// <summary>Lower bound of the auxiliary near plane: surfaces nearer than this get no ambient occlusion.</summary>
    public const float AuxiliaryNearFloorMeters=0.01f;
    /// <summary>
    /// Where the conservative raster bound touches the curve, as a multiple of the larger of the transition length and
    /// the near plane (so the touch is always in view). Occluders up to about 2.6 times this distance can reject hidden
    /// fragments early; fragments nearer than about 0.3 times it are never rejected early.
    /// </summary>
    public const float ConservativeTangentRatio=8f;
    private RadialDepthProjection(RadialDepthCurve Curve,float NearZ,float FarZ,float FrustumCornerDistance){
        this.Curve=Curve;
        this.NearZ=NearZ;
        this.FarZ=FarZ;
        this.FrustumCornerDistance=FrustumCornerDistance;
        this.InverseMaximumG=1f/Curve.G(FrustumCornerDistance);
        this.AuxiliaryNearZ=MathF.Max(NearZ,MathF.Min(AuxiliaryNearFloorMeters,FarZ*0.5f));
        this.AuxiliaryFarZ=MathF.Min(FarZ,this.AuxiliaryNearZ*AuxiliaryDepthRatio);
        // The hyperbola a - b/w tangent to G(w)/G(rMax) at w0 stays below it for every w > 0: their difference has
        // its only minimum, zero, at w0 because w^2/(T + w) increases. A 1/4096 margin covers rounding.
        var Tangent=this.ConservativeTangentMeters;
        var Slope=1f/(MathF.Log(2f)*(Curve.TransitionMeters+Tangent));
        this.ConservativeDepthSlope=Tangent*Tangent*Slope*this.InverseMaximumG;
        this.ConservativeDepthLimit=(Curve.G(Tangent)+Tangent*Slope)*this.InverseMaximumG*(1f-1f/4096f);
    }
    public RadialDepthCurve Curve { get; }
    public float NearZ { get; }
    /// <summary>The camera's far plane, enforced per fragment because the raster far plane becomes infinite in float.</summary>
    public float FarZ { get; }
    /// <summary>
    /// The physical view depth beyond which a radial fragment is discarded: <see cref="FarZ"/> plus 1/1024, so rounding
    /// never removes a fragment that the projection's own far plane kept.
    /// </summary>
    public float FarDiscardDepth=>this.FarZ*(1f+1f/1024f);
    /// <summary>Distance from the eye to a corner of the far plane: the farthest visible point.</summary>
    public float FrustumCornerDistance { get; }
    /// <summary><c>1 / G(FrustumCornerDistance)</c>: depth is <c>G(r)</c> times this.</summary>
    public float InverseMaximumG { get; }
    /// <summary>Near plane of the view-projection given to the sky direction, the sun cascades and the AO prepass.</summary>
    public float AuxiliaryNearZ { get; }
    /// <summary>Far plane of that view-projection: <c>min(FarZ, AuxiliaryNearZ · 65536)</c>.</summary>
    public float AuxiliaryFarZ { get; }
    /// <summary>
    /// <c>a</c> of the conservative raster depth <c>a − b/w</c> (w: view depth), a lower bound of the encoded depth, so
    /// a radial pixel shader can declare SV_DepthGreaterEqual and keep early depth rejection (ADR-0076).
    /// </summary>
    public float ConservativeDepthLimit { get; }
    /// <summary>Where the conservative raster bound touches the encoded depth's curve: 8 · max(T, NearZ).</summary>
    public float ConservativeTangentMeters=>ConservativeTangentRatio*MathF.Max(this.Curve.TransitionMeters,this.NearZ);
    /// <summary><c>b</c> of the conservative raster depth <c>a − b/w</c>.</summary>
    public float ConservativeDepthSlope { get; }
    /// <summary>The conservative raster depth at view depth <paramref name="ViewDepth"/>, clamped at zero like the rasterizer.</summary>
    public float ConservativeDepth(float ViewDepth)=>MathF.Max(this.ConservativeDepthLimit-this.ConservativeDepthSlope/ViewDepth,0f);
    /// <summary>
    /// The camera's perspective with its depth row replaced by the conservative bound. X, Y and W are the standard ones,
    /// so coverage is unchanged; the pipeline must disable depth clipping, because the bound reaches zero beyond the near
    /// plane, and enforce NearZ per fragment.
    /// </summary>
    public Matrix4x4 CreateConservativeRasterProjection(float FovYRadians,float AspectRatio){
        var Projection=Matrix4x4.CreatePerspectiveFieldOfView(FovYRadians,AspectRatio,this.NearZ,this.FarZ);
        Projection.M33=-this.ConservativeDepthLimit;
        Projection.M43=-this.ConservativeDepthSlope;
        return Projection;
    }
    /// <summary>Radius of the compressed world seen by an observer: <c>T · G(FrustumCornerDistance)</c>.</summary>
    public float ObservedWorldRadius=>this.Curve.Map(this.FrustumCornerDistance);
    public static bool TryCreate(RadialDepthCurve Curve,float NearZ,float FarZ,float FovYRadians,float AspectRatio,out RadialDepthProjection Projection){
        Projection=default;
        if(Curve.TransitionMeters<=0f||!float.IsFinite(NearZ)||NearZ<=0f||!float.IsFinite(FarZ)||FarZ<=NearZ
            ||!float.IsFinite(FovYRadians)||FovYRadians<=0f||FovYRadians>=MathF.PI||!float.IsFinite(AspectRatio)||AspectRatio<=0f){
            return false;
        }
        var TanHalfFov=MathF.Tan(FovYRadians*0.5f);
        var Corner=FarZ*MathF.Sqrt(1f+TanHalfFov*TanHalfFov*(1f+AspectRatio*AspectRatio));
        if(!float.IsFinite(Corner)||!float.IsFinite(1f/Curve.G(Corner))){
            return false;
        }
        Projection=new RadialDepthProjection(Curve,NearZ,FarZ,Corner);
        return true;
    }
    /// <summary>The depth written for a fragment at a physical distance from the eye.</summary>
    public float EncodeDepth(float DistanceMeters)=>MathF.Min(this.Curve.G(DistanceMeters)*this.InverseMaximumG,MaximumDepth);
    /// <summary>A finite perspective with the camera's field of view and the auxiliary near/far planes.</summary>
    public Matrix4x4 CreateAuxiliaryProjection(float FovYRadians,float AspectRatio)=>
        Matrix4x4.CreatePerspectiveFieldOfView(FovYRadians,AspectRatio,this.AuxiliaryNearZ,this.AuxiliaryFarZ);
}
