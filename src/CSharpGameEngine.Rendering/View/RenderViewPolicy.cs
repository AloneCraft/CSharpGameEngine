using System.Numerics;
namespace CSharpGameEngine.Rendering.View;
/// <summary>L5 projection policy: physical perspective views are logarithmic, orthographic views are linear.</summary>
public static class RenderViewPolicy {
    public static RenderView FromCamera(in RenderCamera Camera,float AspectRatio){
        var Projection=Perspective(Camera.FovYRadians,Camera.NearZ,Camera.FarZ,Camera.RadialTransitionMeters,AspectRatio);
        var View=Camera.GetView();
        var Basis=RenderViewBasis.Create(new Vector3(View.M11,View.M21,View.M31),new Vector3(View.M12,View.M22,View.M32),
            -new Vector3(View.M13,View.M23,View.M33),MathF.Tan(Camera.FovYRadians*0.5f),AspectRatio);
        return RenderView.Create(View*Camera.GetProjection(AspectRatio),
            View*Projection.CreateAuxiliaryProjection(Camera.FovYRadians,AspectRatio),Camera.GetEyePosition(),Camera.ExposureEv100,
            RenderViewDepth.ForPerspective(Projection),Basis);
    }
    public static RenderView FromOrthographic(in Matrix4x4 ViewProjection,Vector3 CameraPosition)=>
        RenderView.Create(ViewProjection,ViewProjection,CameraPosition,0f,RenderViewDepth.ForOrthographic(ViewProjection));
    public static RenderView FromPerspective(in Matrix4x4 ViewProjection,Vector3 CameraPosition,in RadialViewParameters Parameters,float AspectRatio){
        var Projection=Perspective(Parameters.FovYRadians,Parameters.NearZ,Parameters.FarZ,Parameters.TransitionMeters,AspectRatio);
        return RenderView.Create(ViewProjection,Projection.CreateAuxiliaryViewProjection(ViewProjection),CameraPosition,0f,RenderViewDepth.ForPerspective(Projection));
    }
    private static RadialDepthProjection Perspective(float FovYRadians,float NearZ,float FarZ,float TransitionMeters,float AspectRatio){
        if(!RadialDepthCurve.TryCreate(TransitionMeters,out var Curve)
            ||!RadialDepthProjection.TryCreate(Curve,NearZ,FarZ,FovYRadians,AspectRatio,out var Projection)){
            throw new ArgumentException("A logarithmic perspective view needs a positive finite curve, valid clip range and field of view.");
        }
        return Projection;
    }
}
