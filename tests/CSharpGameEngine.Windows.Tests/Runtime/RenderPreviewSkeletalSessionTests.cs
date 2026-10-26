extern alias PreviewHost;
using System.Numerics;
using System.Security.Cryptography;
using CSharpGameEngine.Contracts;
using PreviewHost::CSharpGameEngine.EditorPreview.Host;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.View;
namespace CSharpGameEngine.Windows.Tests.Runtime;
/// <summary>
/// プレビューホストのスケルタル glTF（ADR-0090、M0127）。テンプレートのマネキン（スキン付き .glb）とアニメーションだけの .glb を、
/// ホストが自分で読み込んで WARP で描き、要約・姿勢・ボーンの線が画素として現れることを確かめる。
/// </summary>
[Collection(CSharpGameEngine.Graphics.D3D12.Tests.D3D12GpuCollection.Name)]
public sealed class RenderPreviewSkeletalSessionTests {
    private static string ShaderArtifactPath=>Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private const int Size=160;
    private static readonly GraphicsDeviceOptions Options=new(){ UseWarp=true,EnableDebugLayer=true };
    private static string Mannequins=>Path.Combine(RepositoryRoot(),"Templates","CSharpGameEngine.Templates.FirstPerson","Assets","Characters","Mannequins");
    private static string Mesh=>Path.Combine(Mannequins,"SKM_Manny_Simple.glb");
    private static string Idle=>Path.Combine(Mannequins,"Anims","MM_Idle.glb");
    private static string RepositoryRoot(){
        for(var Directory=new DirectoryInfo(AppContext.BaseDirectory);Directory is not null;Directory=Directory.Parent){
            if(File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ return Directory.FullName; }
        }
        throw new InvalidOperationException("Repository root not found.");
    }
    private sealed class Preview:IDisposable {
        private readonly Win32Window Window;
        internal readonly RenderPreviewRenderSession Session;
        internal readonly RenderPreviewCamera Camera;
        internal Preview(RenderPreviewAssetSource Source){
            this.Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Preview Skeletal Test",Width=Size,Height=Size,Visible=false });
            var Fit=RenderCamera.CreateBoundsFit(new Vector3(-1f,0f,-1f),new Vector3(1f,1.8f,1f));
            var ViewProjection=Fit.GetView()*Fit.GetProjection(1f);
            var Eye=Fit.GetEyePosition();
            this.Camera=new RenderPreviewCamera {
                ViewProjection=ToWire(ViewProjection),Position=[Eye.X,Eye.Y,Eye.Z],FovYRadians=Fit.FovYRadians,NearZ=Fit.NearZ,FarZ=Fit.FarZ,
            };
            var Open=new RenderPreviewHostRequest {
                RequestId=Guid.NewGuid(),SessionId=Guid.NewGuid(),Sequence=1,Command=RenderPreviewHostCommand.Open,
                SurfaceHandle=this.Window.Handle,Width=this.Window.ClientWidth,Height=this.Window.ClientHeight,ShaderArtifactPath=ShaderArtifactPath,
                Scene=new RenderPreviewScene { Camera=this.Camera,Asset=Source },
            };
            Assert.Null(RenderPreviewHostProtocol.Validate(Open));
            this.Session=RenderPreviewRenderSession.Open(Open,Options);
        }
        internal string Render(RenderPreviewAnimation?Animation,bool ShowSkeleton=false,int SelectedJoint=-1,string?Snapshot=null){
            var Frame=new RenderPreviewFrame { Camera=this.Camera,Animation=Animation,ShowSkeleton=ShowSkeleton,SelectedJoint=SelectedJoint };
            Assert.Null(RenderPreviewHostProtocol.Validate(new RenderPreviewHostRequest {
                RequestId=Guid.NewGuid(),SessionId=Guid.NewGuid(),Sequence=2,Command=RenderPreviewHostCommand.UpdateFrame,Frame=Frame,
            }));
            this.Session.UpdateFrame(Frame);
            this.Session.RenderForVerification();
            Assert.Equal(0,this.Session.ErrorOrWorseMessageCount);
            if(Snapshot is not null){ PreviewFramePng.SaveIfRequested(this.Session.LastCapturedFrame!,Snapshot); }
            return Convert.ToHexString(SHA256.HashData(this.Session.LastCapturedFrame!.Pixels));
        }
        public void Dispose(){
            this.Session.Dispose();
            this.Window.Dispose();
        }
    }
    private static RenderPreviewMatrix4x4 ToWire(in Matrix4x4 Value)=>new(){ Values=[Value.M11,Value.M12,Value.M13,Value.M14,Value.M21,Value.M22,Value.M23,Value.M24,Value.M31,Value.M32,Value.M33,Value.M34,Value.M41,Value.M42,Value.M43,Value.M44] };
    [Fact]
    public void WARP_TheHostLoadsTheSkinAndReportsJointsAndClips(){
        if(!OperatingSystem.IsWindows()){ return; }
        using var Test=new Preview(new RenderPreviewAssetSource { MeshPath=Mesh,AnimationPath=Idle });
        var Summary=Test.Session.AssetSummary!;
        Assert.NotNull(Summary);
        Assert.True(Summary.JointNames.Length>20,"the mannequin rig has many joints");
        Assert.Equal(Summary.JointNames.Length,Summary.JointParents.Length);
        Assert.Contains(-1,Summary.JointParents);
        Assert.True(Summary.SkinnedMeshCount>0);
        var Clip=Assert.Single(Summary.Clips);
        Assert.True(Clip.DurationSeconds>0f);
        Assert.True(Clip.BoundTrackCount>0,"the animation-only file animates this skeleton's joints");
    }
    [Fact]
    public void WARP_AnimationTimeChangesThePoseAndTheRestPoseIsStable(){
        if(!OperatingSystem.IsWindows()){ return; }
        using var Test=new Preview(new RenderPreviewAssetSource { MeshPath=Mesh,AnimationPath=Idle });
        var Rest=Test.Render(new RenderPreviewAnimation { ClipIndex=-1 },Snapshot:"skeletal-rest");
        var Start=Test.Render(new RenderPreviewAnimation { ClipIndex=0,TimeSeconds=0f },Snapshot:"skeletal-idle-0");
        var Later=Test.Render(new RenderPreviewAnimation { ClipIndex=0,TimeSeconds=1.0f },Snapshot:"skeletal-idle-1");
        var RestAgain=Test.Render(new RenderPreviewAnimation { ClipIndex=-1 });
        Assert.Equal(Rest,RestAgain);
        Assert.NotEqual(Start,Later);
    }
    [Fact]
    public void WARP_TheSkeletonLinesAndTheSelectedJointChangeTheFrame(){
        if(!OperatingSystem.IsWindows()){ return; }
        using var Test=new Preview(new RenderPreviewAssetSource { MeshPath=Mesh });
        Assert.Empty(Test.Session.AssetSummary!.Clips);
        var Without=Test.Render(new RenderPreviewAnimation { ClipIndex=-1 });
        var With=Test.Render(new RenderPreviewAnimation { ClipIndex=-1 },ShowSkeleton:true,Snapshot:"skeletal-bones");
        var Selected=Test.Render(new RenderPreviewAnimation { ClipIndex=-1 },ShowSkeleton:true,SelectedJoint:5,Snapshot:"skeletal-bones-selected");
        Assert.NotEqual(Without,With);
        Assert.NotEqual(With,Selected);
    }
    [Fact]
    public void WARP_AMeshWithoutASkinIsRejectedWithAReason(){
        if(!OperatingSystem.IsWindows()){ return; }
        var Error=Assert.ThrowsAny<Exception>(()=>{ using var _=new Preview(new RenderPreviewAssetSource { MeshPath=Idle }); });
        Assert.Contains("skin",Error.Message,StringComparison.OrdinalIgnoreCase);
    }
}
