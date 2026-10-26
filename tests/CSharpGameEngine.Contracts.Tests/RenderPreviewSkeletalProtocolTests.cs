using System.Numerics;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Contracts.Tests;
/// <summary>プロトコル 11：ホストが読み込むスケルタル glTF の入力とアニメーションフレームの検証（ADR-0090）。</summary>
public sealed class RenderPreviewSkeletalProtocolTests {
    private static RenderPreviewCamera Camera(){
        var Matrix=Matrix4x4.CreateLookAt(new(0f,1f,-4f),Vector3.Zero,Vector3.UnitY)*Matrix4x4.CreatePerspectiveFieldOfView(0.8f,1.5f,0.1f,300f);
        return new RenderPreviewCamera {
            ViewProjection=new RenderPreviewMatrix4x4 { Values=[
                Matrix.M11,Matrix.M12,Matrix.M13,Matrix.M14,Matrix.M21,Matrix.M22,Matrix.M23,Matrix.M24,
                Matrix.M31,Matrix.M32,Matrix.M33,Matrix.M34,Matrix.M41,Matrix.M42,Matrix.M43,Matrix.M44] },
            FovYRadians=0.8f,NearZ=0.1f,FarZ=300f,
        };
    }
    private static RenderPreviewHostRequest Open(RenderPreviewAssetSource?Asset)=>new(){
        RequestId=Guid.NewGuid(),SessionId=Guid.NewGuid(),Sequence=1,Command=RenderPreviewHostCommand.Open,
        SurfaceHandle=1,Width=64,Height=64,ShaderArtifactPath="Stage.cgeshaderartifact",
        Scene=new RenderPreviewScene { Camera=Camera(),Asset=Asset },
    };
    private static RenderPreviewHostRequest FrameRequest(Action<RenderPreviewFrame>Edit){
        var Frame=new RenderPreviewFrame { Camera=Camera() };
        Edit(Frame);
        return new RenderPreviewHostRequest {
            RequestId=Guid.NewGuid(),SessionId=Guid.NewGuid(),Sequence=2,Command=RenderPreviewHostCommand.UpdateFrame,Frame=Frame,
        };
    }
    [Fact]
    public void ThePreviousHostProtocolIsRejectedBeforeLoadingAnAsset(){
        var Request=Open(new RenderPreviewAssetSource { MeshPath=@"C:\Game\Assets\Hero.glb" });
        Request.ProtocolVersion=10;
        Assert.Contains("Unsupported",Assert.IsType<string>(RenderPreviewHostProtocol.Validate(Request)));
    }
    [Fact]
    public void AnAssetOnlySceneWithoutExplicitObjectsIsValid(){
        Assert.Null(RenderPreviewHostProtocol.Validate(Open(new RenderPreviewAssetSource { MeshPath=@"C:\Game\Assets\Hero.glb" })));
        Assert.Null(RenderPreviewHostProtocol.Validate(Open(new RenderPreviewAssetSource { MeshPath=@"C:\Game\Assets\Hero.glb",AnimationPath=@"C:\Game\Assets\Anims\Run.GLTF" })));
        Assert.Null(RenderPreviewHostProtocol.Validate(Open(null)));
    }
    [Theory]
    [InlineData("")]
    [InlineData(@"Assets\Hero.glb")]
    [InlineData(@"C:\Game\Hero.fbx")]
    [InlineData(@"C:\Game\Hero")]
    [InlineData("C:\\Game\\Hero\0.glb")]
    public void AnInvalidMeshPathIsRejected(string Path){
        var Error=RenderPreviewHostProtocol.Validate(Open(new RenderPreviewAssetSource { MeshPath=Path }));
        Assert.Contains("Asset mesh path",Assert.IsType<string>(Error));
    }
    [Fact]
    public void AnInvalidAnimationPathIsRejectedAndAnOverlongOneToo(){
        var Error=RenderPreviewHostProtocol.Validate(Open(new RenderPreviewAssetSource { MeshPath=@"C:\Game\Hero.glb",AnimationPath=@"C:\Game\Run.fbx" }));
        Assert.Contains("Asset animation path",Assert.IsType<string>(Error));
        var Long=@"C:\"+new string('a',RenderPreviewAssetSource.MaximumPathLength)+".glb";
        Assert.NotNull(RenderPreviewHostProtocol.Validate(Open(new RenderPreviewAssetSource { MeshPath=Long })));
    }
    [Fact]
    public void AFrameWithoutAnimationOrWithAValidOneIsAccepted(){
        Assert.Null(RenderPreviewHostProtocol.Validate(FrameRequest(static _=>{ })));
        Assert.Null(RenderPreviewHostProtocol.Validate(FrameRequest(static F=>{ F.Animation=new(){ ClipIndex=-1 };F.ShowSkeleton=true;F.SelectedJoint=3; })));
        Assert.Null(RenderPreviewHostProtocol.Validate(FrameRequest(static F=>F.Animation=new(){ ClipIndex=2,TimeSeconds=12.5f,Loop=false })));
    }
    public static IEnumerable<object[]>InvalidFrames(){
        yield return [(Action<RenderPreviewFrame>)(static F=>F.Animation=new(){ TimeSeconds=float.NaN }),"time"];
        yield return [(Action<RenderPreviewFrame>)(static F=>F.Animation=new(){ TimeSeconds=float.PositiveInfinity }),"time"];
        yield return [(Action<RenderPreviewFrame>)(static F=>F.Animation=new(){ TimeSeconds=-0.5f }),"time"];
        yield return [(Action<RenderPreviewFrame>)(static F=>F.Animation=new(){ TimeSeconds=RenderPreviewSkeletalValidation.MaximumTimeSeconds*2f }),"time"];
        yield return [(Action<RenderPreviewFrame>)(static F=>F.Animation=new(){ ClipIndex=-2 }),"clip"];
        yield return [(Action<RenderPreviewFrame>)(static F=>F.Animation=new(){ ClipIndex=100_000 }),"clip"];
        yield return [(Action<RenderPreviewFrame>)(static F=>F.SelectedJoint=-2),"joint"];
        yield return [(Action<RenderPreviewFrame>)(static F=>F.SelectedJoint=int.MaxValue),"joint"];
    }
    [Theory]
    