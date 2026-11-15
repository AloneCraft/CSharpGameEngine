using System.Numerics;
namespace CSharpGameEngine.Rendering.View;
/// <summary>L5 投影方針。透視ビューの対数深度またはIdentityを明示し、正射ビューでは線形深度を使う。</summary>
public static class RenderViewPolicy {
    public static RenderViewSource FromCameraSource(in RenderCamera Camera,float AspectRatioOverride=0f)=>
        RenderViewSource.Perspective(Camera.GetView(),Camera.GetEyePosition(),
            new(Camera.FovYRadians,Camera.NearZ,Camera.FarZ,Camera.RadialTransitionMeters),Camera.ExposureEv100,AspectRatioOverride);
    public static RenderView FromCamera(in RenderCamera Camera,float AspectRatio)=>FromCameraSource(Camera).Resolve(AspectRatio);
    public static RenderViewSource FromIdentityCameraSource(in RenderCamera Camera,float AspectRatioOverride=0f)=>
        RenderViewSource.IdentityPerspective(Camera.GetView(),Camera.GetEyePosition(),
            new(Camera.FovYRadians,Camera.NearZ,Camera.FarZ,0f),Camera.ExposureEv100,AspectRatioOverride);
    public static RenderView FromIdentityCamera(in RenderCamera Camera,float AspectRatio)=>FromIdentityCameraSource(Camera).Resolve(AspectRatio);
    public static RenderViewSource FromIdentityPerspectiveSource(in Matrix4x4 ViewProjection,Vector3 CameraPosition)=>
        RenderViewSource.IdentityPerspectiveMatrix(ViewProjection,CameraPosition);
    public static RenderView FromOrthographic(in Matrix4x4 ViewProjection,Vector3 CameraPosition)=>
        RenderView.Create(ViewProjection,ViewProjection,CameraPosition,0f,RenderViewDepth.ForOrthographic(ViewProjection));
    public static RenderViewSource FromPerspectiveSource(in Matrix4x4 ViewProjection,Vector3 CameraPosition,in RadialViewParameters Parameters)=>
        RenderViewSource.PerspectiveMatrix(ViewProjection,CameraPosition,Parameters);
    public static RenderView FromPerspective(in Matrix4x4 ViewProjection,Vector3 CameraPosition,in RadialViewParameters Parameters,float AspectRatio)=>
        FromPerspectiveSource(ViewProjection,CameraPosition,Parameters).Resolve(AspectRatio);
}
