using System.Numerics;
namespace CSharpGameEngine.Rendering.Pipeline;
/// <summary>One cascade of the directional (sun) shadow (ADR-0069).</summary>
public readonly record struct DirectionalShadowCascade(
    Matrix4x4 ViewProjection,Vector3 Center,float Radius,float DepthRangeMeters,ShadowAtlasTile Tile,float NearDistance,float FarDistance);
/// <summary>
/// Splits the camera frustum into <see cref="Count"/> slices and fits one stable orthographic shadow
/// projection per slice. Split distances follow Unreal Engine's cascade distribution exponent, each
/// slice is enclosed by a bounding sphere (so the projection size does not change as the camera
/// rotates) and the sphere centre is snapped to shadow-map texels (so it does not shimmer as the
/// camera moves). The four cascades share the directional atlas tile as 2x2 quadrants.
/// </summary>
public static class DirectionalShadowCascades {
    public const int Count=4;
    public const float DefaultMaxDistanceMeters=200f;
    public const float DistributionExponent=3f;
    public const int ResolutionPixels=PhysicalShadowAtlasPlan.TileSizePixels/2;
    /// <summary>Extra depth in front of each slice so off-screen casters between the sun and the slice still cast.</summary>
    public const float CasterMarginMeters=200f;
    public static ShadowAtlasTile TileFor(int Cascade){
        ArgumentOutOfRangeException.ThrowIfNegative(Cascade);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(Cascade,Count);
        var Parent=ShadowAtlasTile.FromIndex(PhysicalShadowAtlasPlan.DirectionalTileIndex);
        return new ShadowAtlasTile(Parent.Index,Parent.X+(Cascade%2)*ResolutionPixels,Parent.Y+(Cascade/2)*ResolutionPixels,ResolutionPixels,ResolutionPixels);
    }
    /// <summary>Distance (along the view direction) where cascade <paramref name="Cascade"/> ends.</summary>
    public static float SplitDistance(int Cascade,float Near,float Far){
        var Fraction=(MathF.Pow(DistributionExponent,Cascade+1)-1f)/(MathF.Pow(DistributionExponent,Count)-1f);
        return Near+(Far-Near)*Fraction;
    }
    public static DirectionalShadowCascade[]Build(Matrix4x4 ViewProjection,Vector3 CameraPosition,Vector3 DirectionTowardLight,float MaxDistanceMeters=DefaultMaxDistanceMeters){
        if(!Matrix4x4.Invert(ViewProjection,out var Inverse)){
            Inverse=Matrix4x4.Identity;
        }
        Span<Vector3>NearCorners=stackalloc Vector3[4];
        Span<Vector3>FarCorners=stackalloc Vector3[4];
        for(var Corner=0;Corner<4;Corner++){
            var X=Corner%2==0?-1f:1f;
            var Y=Corner/2==0?-1f:1f;
            NearCorners[Corner]=Unproject(Inverse,X,Y,0f);
            FarCorners[Corner]=Unproject(Inverse,X,Y,1f);
        }
        var NearCenter=(NearCorners[0]+NearCorners[1]+NearCorners[2]+NearCorners[3])/4f;
        var FarCenter=(FarCorners[0]+FarCorners[1]+FarCorners[2]+FarCorners[3])/4f;
        var Forward=SafeNormalize(FarCenter-NearCenter,Vector3.UnitZ);
        var Near=MathF.Max(Vector3.Dot(NearCenter-CameraPosition,Forward),0.01f);
        var Far=MathF.Max(MathF.Min(Vector3.Dot(FarCenter-CameraPosition,Forward),MaxDistanceMeters),Near+1f);
        var Toward=SafeNormalize(DirectionTowardLight,Vector3.UnitY);
        var Up=MathF.Abs(Toward.Y)>0.99f?Vector3.UnitZ:Vector3.UnitY;
        var LightRotation=Matrix4x4.CreateLookAt(Vector3.Zero,-Toward,Up);
        Matrix4x4.Invert(LightRotation,out var InverseLightRotation);
        var Result=new DirectionalShadowCascade[Count];
        var SliceNear=Near;
        Span<Vector3>SliceCorners=stackalloc Vector3[8];
        for(var Cascade=0;Cascade<Count;Cascade++){
            var SliceFar=SplitDistance(Cascade,Near,Far);
            for(var Corner=0;Corner<4;Corner++){
                var Ray=FarCorners[Corner]-CameraPosition;
                var RayDepth=MathF.Max(Vector3.Dot(Ray,Forward),1e-4f);
                SliceCorners[Corner]=CameraPosition+Ray*(SliceNear/RayDepth);
                SliceCorners[Corner+4]=CameraPosition+Ray*(SliceFar/RayDepth);
            }
            var Center=Vector3.Zero;
            foreach(var Point in SliceCorners){
                Center+=Point;
            }
            Center/=8f;
            var Radius=0f;
            foreach(var Point in SliceCorners){
                Radius=MathF.Max(Radius,Vector3.Distance(Point,Center));
            }
            Radius=MathF.Ceiling(Radius*16f)/16f;
            var TexelSize=2f*Radius/ResolutionPixels;
            var LightSpaceCenter=Vector3.Transform(Center,LightRotation);
            LightSpaceCenter.X=MathF.Floor(LightSpaceCenter.X/TexelSize)*TexelSize;
            LightSpaceCenter.Y=MathF.Floor(LightSpaceCenter.Y/TexelSize)*TexelSize;
            var SnappedCenter=Vector3.Transform(LightSpaceCenter,InverseLightRotation);
            var DepthRange=2f*Radius+CasterMarginMeters;
            var Eye=SnappedCenter+Toward*(Radius+CasterMarginMeters);
            var View=Matrix4x4.CreateLookAt(Eye,SnappedCenter,Up);
            var Projection=Matrix4x4.CreateOrthographic(2f*Radius,2f*Radius,0f,DepthRange);
            Result[Cascade]=new DirectionalShadowCascade(View*Projection,SnappedCenter,Radius,DepthRange,TileFor(Cascade),SliceNear,SliceFar);
            SliceNear=SliceFar;
        }
        return Result;
    }
    private static Vector3 Unproject(Matrix4x4 Inverse,float X,float Y,float Z){
        var Point=Vector4.Transform(new Vector4(X,Y,Z,1f),Inverse);
        return new Vector3(Point.X,Point.Y,Point.Z)/Point.W;
    }
    private static Vector3 SafeNormalize(Vector3 Value,Vector3 Fallback){
        var Length=Value.Length();
        return Length>1e-6f&&float.IsFinite(Length)?Value/Length:Fallback;
    }
}
