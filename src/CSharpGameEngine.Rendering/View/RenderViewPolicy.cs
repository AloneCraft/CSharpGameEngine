using System.Numerics;
namespace CSharpGameEngine.Rendering.View;
/// <summary>L5 投影方針。物理透視ビューでは対数深度、正射ビューでは線形深度を使う。</summary>
public static class RenderViewPolicy {
    public static RenderViewSource FromCameraSource(in RenderCamera Camera,float AspectRatioOverride=0f)=>
        RenderViewSource.Perspective(Camera.GetView(),Camera.GetEyePosition(),
            new(Camera.FovYRadians,Camera.NearZ,Camera.FarZ,Camera.RadialTransitionMeters),Camera.ExposureEv100,AspectRatioOverride);
    public static RenderView FromCamera(in RenderCamera Camera,float AspectRatio)=>FromCameraSource(Camera).Resolve(AspectRatio);
    public static RenderView FromOrthographic(in Matrix4x4 ViewProjection,Vector3 CameraPosition)=>
        RenderView.Create(ViewProjection,ViewProjection,CameraPosition,0f,RenderViewDepth.ForOrthographic(ViewProjection));
    public static RenderViewSource FromPerspectiveSource(in Matrix4x4 ViewProjection,Vector3 CameraPosition,in RadialViewParameters Parameters)=>
        RenderViewSource.PerspectiveMatrix(ViewProjection,CameraPosition,Parameters);
    public static RenderView FromPerspective(in Matrix4x4 ViewProjection,Vector3 CameraPosition,in RadialViewParameters Parameters,float AspectRatio)=>
        FromPerspectiveSource(ViewProjection,CameraPosition,Parameters).Resolve(AspectRatio);
}
