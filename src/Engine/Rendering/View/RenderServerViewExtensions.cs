using System.Numerics;
using CSharpGameEngine.Rendering.Server;
namespace CSharpGameEngine.Rendering.View;
/// <summary>L5 互換エントリーポイントでビュー方針を選び、値を所有する L4 入力を送信する。</summary>
public static class RenderServerViewExtensions {
    public static void SetCamera(this RenderServer Server,in RenderCamera Camera,float AspectRatioOverride=0f){
        ArgumentNullException.ThrowIfNull(Server);
        Server.SetViewSource(RenderViewPolicy.FromCameraSource(Camera,AspectRatioOverride));
    }
    public static void SetViewProjection(this RenderServer Server,in Matrix4x4 ViewProjection)=>Server.SetViewProjection(ViewProjection,Vector3.Zero);
    public static void SetViewProjection(this RenderServer Server,in Matrix4x4 ViewProjection,in Vector3 CameraPosition){
        ArgumentNullException.ThrowIfNull(Server);
        Server.SetView(RenderViewPolicy.FromOrthographic(ViewProjection,CameraPosition));
    }
    public static void SetViewProjection(this RenderServer Server,in Matrix4x4 ViewProjection,in Vector3 CameraPosition,in RadialViewParameters Radial){
        ArgumentNullException.ThrowIfNull(Server);
        Server.SetViewSource(RenderViewPolicy.FromPerspectiveSource(ViewProjection,CameraPosition,Radial));
    }
}
