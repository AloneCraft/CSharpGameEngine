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
    /// <c>log2(1 + x)</c> for <c>x ≥ 0</c> with float arithmetic only, so the shader (whose hardware <c>log2</c> is an
    /// approximation, M0100) evaluates the same expression: <c>ln(1 + x) = 2 atanh(s)</c>, with <c>s = x / (2 + x)</c>
    /// below <c>√2 − 1</c> and, above it, <c>1 + x = m · 2^e</c> (<c>m</c> in <c>(√½, √2]</c>) and
    /// <c>s = (m − 1) / (m + 1)</c>. <c>|s| ≤ 0.1716</c>, so the odd series to <c>s⁹</c> is exact to float precision.
    /// </summary>
    public static float Log2OnePlus(float X){
        if(!float.IsFinite(X)){
            return X;
        }
        var Exponent=0f;
        float S;
        if(X<0.41421356f){
            S=X/(2f+X);
        } else {
            var Bits=BitConverter.SingleToUInt32Bits(1f+X);
            Exponent=(int)(Bits>>23)-127;
            var M=BitConverter.UInt32BitsToSingle((Bits&0x007FFFFFu)|0x3F800000u);
            if(M>1.41421356f){
                M*=0.5f;
                Exponent+=1f;
            }
            S=(M-1f)/(M+1f);
        }
        var S2=S*S;
        var Series=S*(2f+S2*(0.6666667f+S2*(0.4f+S2*(0.2857143f+S2*0.2222222f))));
        return Exponent+Series*1.4426950f;
    }
}

/// <summary>
/// What a radial view handed over as a view-projection matrix (an editor's camera, M0100) needs besides the matrix: its
/// vertical field of view, clip range and transition length. The matrix stays authoritative for coverage.
/// </summary>
public readonly record struct RadialViewParameters(float FovYRadians,float NearZ,float FarZ,float TransitionMeters=RadialDepthCurve.DefaultTransitionMeters);

/// <summary>
/// A conservative radial raster depth <c>max(Limit − Slope / w, 0)</c> (w: view depth): a hyperbola tangent to a radial
/// view's encoded depth and below it at every distance (ADR-0076), so a radial pixel shader can declare
/// SV_DepthGreaterEqual and the hardware can still reject hidden fragments early.
/// </summary>
public readonly record struct RadialConservativeDepth(float Limit,float Slope){
    /// <summary>The raster depth at view depth <paramref name="ViewDepth"/>, clamped at zero like the rasterizer.</summary>
    public float At(float ViewDepth)=>MathF.Max(this.Limit-this.Slope/ViewDepth,0f);
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
    private RadialDepthProjection(RadialDepthCurve Curve,float NearZ,float FarZ,float FrustumCornerDistance){
        this.Curve=Curve;
        this.NearZ=NearZ;
        this.FarZ=FarZ;
        this.FrustumCornerDistance=FrustumCornerDistance;
        this.InverseMaximumG=1f/Curve.G(FrustumCornerDistance);
        this.AuxiliaryNearZ=MathF.Max(NearZ,MathF.Min(AuxiliaryNearFloorMeters,FarZ*0.5f));
        this.AuxiliaryFarZ=MathF.Min(FarZ,this.AuxiliaryNearZ*AuxiliaryDepthRatio);
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
    /// The conservative raster depth tangent to the encoded depth at <paramref name="TangentViewDepth"/> (clamped to the
    /// view's range; NaN takes the near plane). The hyperbola <c>a − b/w</c> tangent to <c>G(w)/G(rMax)</c> at w0 stays below it for every w > 0:
    /// their difference has its only minimum, zero, at w0 because <c>w²/(T + w)</c> increases; a 1/4096 margin covers
    /// rounding. Hidden fragments are rejected early only near the tangent, so the renderer gives each object the tangent
    /// at its own view depth (M0100).
    /// </summary>
    public RadialConservativeDepth ConservativeDepthAt(float TangentViewDepth){
        var Tangent=float.IsNaN(TangentViewDepth)?this.NearZ:Math.Clamp(TangentViewDepth,this.NearZ,this.FrustumCornerDistance);
        var Slope=1f/(MathF.Log(2f)*(this.Curve.TransitionMeters+Tangent));
        return new RadialConservativeDepth(
            (this.Curve.G(Tangent)+Tangent*Slope)*this.InverseMaximumG*(1f-1f/4096f),
            Tangent*Tangent*Slope*this.InverseMaximumG);
    }
    /// <summary><see cref="CreateAuxiliaryProjection"/> for a view handed over as one perspective view-projection.</summary>
    public Matrix4x4 CreateAuxiliaryViewProjection(in Matrix4x4 PerspectiveViewProjection){
        // The same expressions as Matrix4x4.CreatePerspectiveFieldOfView, so an unchanged range rebuilds the given matrix.
        var Range=this.AuxiliaryFarZ/(this.AuxiliaryNearZ-this.AuxiliaryFarZ);
        return WithDepthColumn(PerspectiveViewProjection,Range,Range*this.AuxiliaryNearZ);
    }
    /// <summary>Whether a view-projection is a perspective one: its W (the view depth) depends on the position.</summary>
    public static bool IsPerspective(in Matrix4x4 ViewProjection)=>
        ViewProjection.M14!=0f||ViewProjection.M24!=0f||ViewProjection.M34!=0f;
    /// <summary>
    /// Replaces the depth column of a perspective view-projection by <c>M33 · z + M43</c> (z: view-space z). A perspective's
    /// W is <c>−z</c>, so the view's z column is the negated fourth column and nothing has to be inverted; the products are
    /// the ones <c>View · Projection</c> forms, so the result equals building the projection with these entries (an
    /// editor's matrix, M0100).
    /// </summary>
    private static Matrix4x4 WithDepthColumn(in Matrix4x4 ViewProjection,float M33,float M43){
        var Result=ViewProjection;
        Result.M13=-M33*ViewProjection.M14;
        Result.M23=-M33*ViewProjection.M24;
        Result.M33=-M33*ViewProjection.M34;
        Result.M43=-M33*ViewProjection.M44+M43;
        return Result;
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
