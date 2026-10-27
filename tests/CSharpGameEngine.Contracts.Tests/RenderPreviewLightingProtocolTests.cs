using System.Numerics;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Contracts.Tests;
/// <summary>プロトコル 10：照明・背景・環境・床マテリアルの検証。</summary>
public sealed class RenderPreviewLightingProtocolTests {
    private static RenderPreviewHostRequest FrameRequest(RenderPreviewLighting?Lighting){
        var Matrix=Matrix4x4.CreateLookAt(new(0f,1f,-4f),Vector3.Zero,Vector3.UnitY)*Matrix4x4.CreatePerspectiveFieldOfView(0.8f,1.5f,0.1f,300f);
        var Camera=new RenderPreviewCamera {
            ViewProjection=new RenderPreviewMatrix4x4 { Values=[
                Matrix.M11,Matrix.M12,Matrix.M13,Matrix.M14,Matrix.M21,Matrix.M22,Matrix.M23,Matrix.M24,
                Matrix.M31,Matrix.M32,Matrix.M33,Matrix.M34,Matrix.M41,Matrix.M42,Matrix.M43,Matrix.M44] },
            FovYRadians=0.8f,NearZ=0.1f,FarZ=300f,
        };
        return new RenderPreviewHostRequest {
            RequestId=Guid.NewGuid(),SessionId=Guid.NewGuid(),Sequence=2,
            Command=RenderPreviewHostCommand.UpdateFrame,
            Frame=new RenderPreviewFrame { Camera=Camera,Lighting=Lighting },
        };
    }
    private static RenderPreviewLighting Valid()=>new(){
        Ambient=[0.2f,0.2f,0.2f],
        Background=[0.3f,0.3f,0.3f],
        Environment=new(){ Atmosphere=true,PhysicalExposure=true,ExposureEv100=15f },
        Directionals=[new(){ Forward=[0.3f,-1f,0.2f],IlluminanceLux=3f,CastsShadows=true }],
        Points=[new(){ Position=[1f,2f,3f],FluxLumens=500f,RangeMeters=6f }],
        Spots=[new(){ Position=[0f,3f,0f],Forward=[0f,-1f,0f],InnerConeDegrees=20f,OuterConeDegrees=40f }],
    };
    [Fact]
    public void Protocol_IsVersionElevenAndAFrameWithoutLightingStaysValid(){
        Assert.Equal(13,RenderPreviewHostProtocol.CurrentVersion);
        Assert.Null(RenderPreviewHostProtocol.Validate(FrameRequest(null)));
        Assert.Null(RenderPreviewHostProtocol.Validate(FrameRequest(new RenderPreviewLighting())));
    }
    [Fact]
    public void ValidLightingPassesAndAProtocolNineRequestIsRejected(){
        var Request=FrameRequest(Valid());
        Assert.Null(RenderPreviewHostProtocol.Validate(Request));
        Request.ProtocolVersion=9;
        Assert.Contains("Unsupported",Assert.IsType<string>(RenderPreviewHostProtocol.Validate(Request)));
    }
    public static IEnumerable<object[]>InvalidLighting(){
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Ambient=[1f,2f]),"ambient"];
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Ambient=[-1f,0f,0f]),"ambient"];
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Ambient=[float.NaN,0f,0f]),"ambient"];
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Background=[0f,2f,0f]),"background"];
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Background=[0f,0f]),"background"];
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Environment!.ExposureEv100=float.PositiveInfinity),"exposure"];
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Environment!.ExposureEv100=100f),"exposure"];
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Environment!.SkyLightIntensity=-1f),"sky light"];
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Directionals=new RenderPreviewDirectionalLight[5].Select(static _=>new RenderPreviewDirectionalLight()).ToArray()),"Directional light count"];
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Directionals[0].Forward=[0f,0f,0f]),"Directional light 0"];
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Directionals[0].IlluminanceLux=-1f),"Directional light 0"];
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Directionals[0].Color=[1f,1f]),"Directional light 0"];
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Points=new RenderPreviewPointLight[9].Select(static _=>new RenderPreviewPointLight()).ToArray()),"Point light count"];
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Points[0].RangeMeters=0f),"Point light 0"];
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Points[0].Position=[float.NaN,0f,0f]),"Point light 0"];
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Spots[0].InnerConeDegrees=50f),"Spot light 0"];
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Spots[0].OuterConeDegrees=181f),"Spot light 0"];
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Spots[0].Forward=[0f,0f,0f]),"Spot light 0"];
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Spots=null!),"Spot light count"];
        yield return [(Action<RenderPreviewLighting>)(static L=>L.Directionals[0]=null!),"Directional light 0"];
    }
    [Theory]
    [MemberData(nameof(InvalidLighting))]
    public void InvalidLightingIsRejectedWithAnActionableMessage(Action<RenderPreviewLighting>Change,string Fragment){
        var Lighting=Valid();
        Change(Lighting);
        var Error=Assert.IsType<string>(RenderPreviewHostProtocol.Validate(FrameRequest(Lighting)));
        Assert.Contains(Fragment,Error,StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void LightingOnlyRidesOnUpdateFrame(){
        // ライトは毎フレーム送る方式。Open／ReplaceScene のシーンには持たせない（シーンの DTO に Lighting は無い）。
        Assert.Null(typeof(RenderPreviewScene).GetProperty("Lighting"));
        Assert.NotNull(typeof(RenderPreviewFrame).GetProperty("Lighting"));
    }
    private static RenderPreviewScene SceneWith(RenderPreviewMaterial Material){
        var Request=RenderPreviewHostProtocolTests.CreateOpenRequest();
        Request.Scene!.Materials[0]=Material;
        return Request.Scene;
    }
    [Fact]
    public void MaterialRoughnessMetallicAndWorldGridAreValidated(){
        var Open=RenderPreviewHostProtocolTests.CreateOpenRequest();
        Assert.Null(RenderPreviewHostProtocol.Validate(Open));
        var Material=Open.Scene!.Materials[0];
        Material.Roughness=0.4f;Material.Metallic=0.9f;
        Material.WorldGrid=new(){ GridSizeMeters=0.5f,SubGridDivisions=5,WireHalfWidth=0.02f };
        Assert.Null(RenderPreviewHostProtocol.Validate(Open));
        void Rejected(Action<RenderPreviewMaterial>Change,string Fragment){
            var Saved=Open.Scene.Materials[0];
            var Copy=new RenderPreviewMaterial { BaseColor=Saved.BaseColor,Roughness=Saved.Roughness,Metallic=Saved.Metallic,WorldGrid=new(){ GridSizeMeters=0.5f,SubGridDivisions=5,WireHalfWidth=0.02f } };
            Change(Copy);
            Open.Scene.Materials[0]=Copy;
            Assert.Contains(Fragment,Assert.IsType<string>(RenderPreviewHostProtocol.Validate(Open)),StringComparison.OrdinalIgnoreCase);
            Open.Scene.Materials[0]=Saved;
        }
        Rejected(static M=>M.Roughness=1.5f,"roughness");
        Rejected(static M=>M.Metallic=-0.1f,"roughness");
        Rejected(static M=>M.Roughness=float.NaN,"roughness");
        Rejected(static M=>M.WorldGrid!.GridSizeMeters=0f,"world grid");
        Rejected(static M=>M.WorldGrid!.SubGridDivisions=0,"world grid");
        Rejected(static M=>M.WorldGrid!.SubGridDivisions=1000,"world grid");
        Rejected(static M=>M.WorldGrid!.WireHalfWidth=0.7f,"world grid");
        Rejected(static M=>M.WorldGrid!.GridColor=[1f],"world grid");
    }
    [Fact]
    public void LegacyMaterialsKeepTheirPreviousLook(){
        var Material=new RenderPreviewMaterial();
        Assert.Equal(1f,Material.Roughness);
        Assert.Equal(0f,Material.Metallic);
        Assert.Null(Material.WorldGrid);
    }
}
