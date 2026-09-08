using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.EditorPreview.Host;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;

namespace CSharpGameEngine.Windows.Tests.Runtime;

[Collection(CSharpGameEngine.Graphics.D3D12.Tests.D3D12GpuCollection.Name)]
public sealed class RenderPreviewRenderSessionTests {
    private static string ShaderArtifactPath=>
        Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");

    [Fact]
    public void WARP_SessionUsesCommonRenderServerAndProducesVerifiedPixels(){
        if(!OperatingSystem.IsWindows()){ return; }
        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Isolated Preview Test",
            Width=128,
            Height=128,
            Visible=false,
        });
        RenderPreviewHostRequest Request=CreateOpen(Window);
        GraphicsDeviceOptions Options=new(){ UseWarp=true,EnableDebugLayer=true };

        using RenderPreviewRenderSession Session=
            RenderPreviewRenderSession.Open(Request,Options);
        Session.RenderForVerification();

        Assert.Equal(1u,Session.DeviceGeneration);
        Assert.NotNull(Session.LastCapturedFrame);
        var Verification=ModelPixelVerifier.Verify(
            Session.LastCapturedFrame!,CSharpGameEngine.Runtime.Windows.RenderServer.ClearColor);
        Assert.True(Verification.Success,Verification.Details);
        Assert.Equal(0,Session.ErrorOrWorseMessageCount);

        string CapturedHash=Session.LastFrameHash;
        RenderPreviewDebugSnapshot FirstDebug=Session.CaptureDebugSnapshot();
        RenderPreviewDebugSnapshot SecondDebug=Session.CaptureDebugSnapshot();
        Assert.Null(RenderPreviewHostProtocol.ValidateDebugSnapshot(FirstDebug));
        Assert.Equal(RenderPreviewDebugGraphKind.DeclaredPlan,FirstDebug.GraphKind);
        Assert.Equal(RenderPreviewDebugDeviceState.Ready,FirstDebug.DeviceState);
        Assert.True(FirstDebug.IsWarp);
        Assert.Equal("Compatibility",FirstDebug.QualityProfile);
        Assert.Equal(1,FirstDebug.SceneObjectCount);
        Assert.Equal(1,FirstDebug.PrimaryObjectDrawCount);
        Assert.Contains(FirstDebug.Passes,Pass=>Pass.Name=="Present");
        Assert.True(FirstDebug.Platform.CapturedCounter>0);
        Assert.Equal(PlatformDebugSubsystemState.NotOwned,FirstDebug.Platform.Input.State);
        Assert.Equal(PlatformDebugSubsystemState.NotOwned,FirstDebug.Platform.Audio.State);
        Assert.Equal(PlatformDebugAudioBackendMode.NotOwned,FirstDebug.Platform.Audio.Mode);
        Assert.Contains("does not own",FirstDebug.Platform.Input.Detail,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not own",FirstDebug.Platform.Audio.Detail,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FirstDebug.DeviceGeneration,SecondDebug.DeviceGeneration);
        Assert.Equal(FirstDebug.CompletedFence,SecondDebug.CompletedFence);
        Assert.Equal(FirstDebug.FramesRendered,SecondDebug.FramesRendered);
        Assert.Equal(CapturedHash,Session.LastFrameHash);

        string LitHash=Session.LastFrameHash;
        Session.UpdateFrame(new RenderPreviewFrame {
            Camera=Request.Scene!.Camera,
            PresentationMode=RenderPreviewPresentationMode.BaseColor,
            Objects=[
                new RenderPreviewObjectTransform {
                    ObjectId="Root/Box",
                    World=RenderPreviewMatrix4x4.Identity,
                    SelectionWeight=1f,
                },
            ],
        });
        Session.RenderForVerification();
        Assert.NotEqual(LitHash,Session.LastFrameHash);
        string PresentationHash=Session.LastFrameHash;

        Session.UpdateFrame(new RenderPreviewFrame {
            Camera=Request.Scene.Camera,
            PresentationMode=RenderPreviewPresentationMode.BaseColor,
            Objects=[
                new RenderPreviewObjectTransform {
                    ObjectId="Root/Box",
                    World=RenderPreviewMatrix4x4.Identity,
                    SelectionWeight=1f,
                },
            ],
            OverlayTriangles=[
                OverlayVertex(-0.75f,-0.75f),
                OverlayVertex(0.75f,-0.75f),
                OverlayVertex(0f,0.75f),
            ],
            CameraPreviews=[
                new RenderPreviewCameraPass {
                    X=16,Y=16,Width=64,Height=36,
                    Camera=Request.Scene.Camera,
                    ExcludedObjectId="Root/Box",
                },
            ],
        });
        Session.RenderForVerification();
        string OverlayHash=Session.LastFrameHash;
        Assert.NotEqual(PresentationHash,OverlayHash);
        RenderPreviewCameraDiagnostics CameraDiagnostics=
            Assert.Single(Session.CameraPreviewDiagnostics);
        Assert.Equal(1,CameraDiagnostics.SceneObjectCount);
        Assert.Equal(0,CameraDiagnostics.DrawCallCount);

        Session.Resize(0,0);
        Assert.Throws<CSharpGameEngine.Rendering.RenderDeviceUnavailableException>(
            ()=>{ Session.Render(CaptureForVerification:false); });
        Session.Resize(Window.ClientWidth,Window.ClientHeight);
        Session.RenderForVerification();
        Assert.Equal(2u,Session.DeviceGeneration);
        Assert.Equal(OverlayHash,Session.LastFrameHash);
        Assert.NotNull(Session.LastCapturedFrame);
        Assert.True(ModelPixelVerifier.Verify(
            Session.LastCapturedFrame!,CSharpGameEngine.Runtime.Windows.RenderServer.ClearColor).Success);

        Session.ReplaceScene(Request.Scene);
        Assert.Empty(Session.CameraPreviewDiagnostics);
        Assert.Equal(string.Empty,Session.LastFrameHash);
    }

    [Fact]
    public void WARP_EmptyAuthoringSceneRendersOnlyClearFrame(){
        if(!OperatingSystem.IsWindows()){ return; }
        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Empty Isolated Preview Test",
            Width=64,
            Height=64,
            Visible=false,
        });
        RenderPreviewHostRequest Request=CreateOpen(Window);
        Request.Scene!.Meshes=[];
        Request.Scene.Materials=[];
        Request.Scene.Objects=[];
        GraphicsDeviceOptions Options=new(){ UseWarp=true,EnableDebugLayer=true };

        using RenderPreviewRenderSession Session=
            RenderPreviewRenderSession.Open(Request,Options);
        Session.UpdateFrame(new RenderPreviewFrame {
            Camera=Request.Scene.Camera,
            Objects=[],
        });
        Session.RenderForVerification();

        PixelReadbackResult Frame=Assert.IsType<PixelReadbackResult>(
            Session.LastCapturedFrame);
        AssertClearFrame(
            Frame,CSharpGameEngine.Runtime.Windows.RenderServer.ClearColor);
        Assert.Equal(0,Session.ErrorOrWorseMessageCount);
    }

    [Fact]
    public void WARP_OverlayAloneChangesPixelsAndClearingItRestoresTheBaseline(){
        if(!OperatingSystem.IsWindows()){ return; }
        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Transform Overlay Test",
            Width=128,
            Height=128,
            Visible=false,
        });
        RenderPreviewHostRequest Request=CreateOpen(Window);
        GraphicsDeviceOptions Options=new(){ UseWarp=true,EnableDebugLayer=true };
        RenderPreviewFrame BaselineFrame=new(){
            Camera=Request.Scene!.Camera,
            PresentationMode=RenderPreviewPresentationMode.BaseColor,
            Objects=[new RenderPreviewObjectTransform {
                ObjectId="Root/Box",
                World=RenderPreviewMatrix4x4.Identity,
                SelectionWeight=0f,
            }],
        };

        using RenderPreviewRenderSession Session=RenderPreviewRenderSession.Open(Request,Options);
        Session.UpdateFrame(BaselineFrame);
        Session.RenderForVerification();
        string BaselineHash=Session.LastFrameHash;
        byte[]BaselinePixels=Assert.IsType<PixelReadbackResult>(Session.LastCapturedFrame).Pixels.ToArray();

        Session.UpdateFrame(new RenderPreviewFrame {
            Camera=BaselineFrame.Camera,
            PresentationMode=BaselineFrame.PresentationMode,
            Objects=BaselineFrame.Objects,
            OverlayTriangles=[
                OverlayVertex(-0.75f,-0.75f),
                OverlayVertex(0.75f,-0.75f),
                OverlayVertex(0f,0.75f),
            ],
        });
        Session.RenderForVerification();

        PixelReadbackResult Overlay=Assert.IsType<PixelReadbackResult>(Session.LastCapturedFrame);
        Assert.NotEqual(BaselineHash,Session.LastFrameHash);
        Assert.True(CountChangedPixels(BaselinePixels,Overlay)>16);

        Session.UpdateFrame(BaselineFrame);
        Session.RenderForVerification();
        Assert.Equal(BaselineHash,Session.LastFrameHash);
        Assert.Equal(0,Session.ErrorOrWorseMessageCount);
    }

    internal static RenderPreviewHostRequest CreateOpenForHost(Win32Window Window)=>CreateOpen(Window);

    private static RenderPreviewHostRequest CreateOpen(Win32Window Window){
        ProceduralMeshData Box=ProceduralMeshFactory.CreateBox(Vector3.One);
        var Camera=CSharpGameEngine.Graphics.D3D12.Camera.CreateDefault(Vector3.Zero);
        Matrix4x4 ViewProjection=Camera.GetView()*Camera.GetProjection(
            (float)Window.ClientWidth/Window.ClientHeight);
        return new RenderPreviewHostRequest {
            RequestId=Guid.NewGuid(),
            SessionId=Guid.NewGuid(),
            Sequence=1,
            Command=RenderPreviewHostCommand.Open,
            SurfaceHandle=Window.Handle,
            Width=Window.ClientWidth,
            Height=Window.ClientHeight,
            ShaderArtifactPath=ShaderArtifactPath,
            Scene=new RenderPreviewScene {
                Camera=new RenderPreviewCamera { ViewProjection=Matrix(ViewProjection) },
                Meshes=[new RenderPreviewMesh {
                    Positions=Flatten(Box.Positions),
                    Normals=Flatten(Box.Normals),
                    Indices=Box.Indices,
                }],
                Materials=[new RenderPreviewMaterial { BaseColor=[0.6f,0.6f,0.6f,1f] }],
                Objects=[new RenderPreviewObject {
                    ObjectId="Root/Box",
                    MeshIndex=0,
                    MaterialIndex=0,
                    World=RenderPreviewMatrix4x4.Identity,
                }],
            },
        };
    }

    private static float[]Flatten(IReadOnlyList<Vector3>Values){
        float[]Result=new float[Values.Count*3];
        for(int Index=0;Index<Values.Count;Index++){
            Result[Index*3]=Values[Index].X;
            Result[Index*3+1]=Values[Index].Y;
            Result[Index*3+2]=Values[Index].Z;
        }
        return Result;
    }

    private static RenderPreviewMatrix4x4 Matrix(in Matrix4x4 Value)=>new(){
        Values=[
            Value.M11,Value.M12,Value.M13,Value.M14,
            Value.M21,Value.M22,Value.M23,Value.M24,
            Value.M31,Value.M32,Value.M33,Value.M34,
            Value.M41,Value.M42,Value.M43,Value.M44,
        ],
    };

    private static RenderPreviewOverlayVertex OverlayVertex(float X,float Y)=>new(){
        Position=[X,Y,-0.75f],
        Color=[1f,0f,1f,0.75f],
    };

    private static PixelColor ReadPixel(PixelReadbackResult Readback,int X,int Y){
        const int BytesPerPixel=4;
        long Offset=((long)Y*Readback.RowPitch)+(X*BytesPerPixel);
        return new PixelColor(
            Readback.Pixels[Offset],
            Readback.Pixels[Offset+1],
            Readback.Pixels[Offset+2],
            Readback.Pixels[Offset+3]);
    }

    private static int CountChangedPixels(byte[]Baseline,PixelReadbackResult Actual){
        const int BytesPerPixel=4;
        Assert.Equal(Baseline.Length,Actual.Pixels.Length);
        int Changed=0;
        for(int Y=0;Y<Actual.Height;Y++){
            for(int X=0;X<Actual.Width;X++){
                long Offset=((long)Y*Actual.RowPitch)+(X*BytesPerPixel);
                if(Baseline[Offset]!=Actual.Pixels[Offset]
                    ||Baseline[Offset+1]!=Actual.Pixels[Offset+1]
                    ||Baseline[Offset+2]!=Actual.Pixels[Offset+2]
                    ||Baseline[Offset+3]!=Actual.Pixels[Offset+3]){
                    Changed++;
                }
            }
        }
        return Changed;
    }

    private static void AssertClearFrame(
        PixelReadbackResult Readback,PixelColor Expected){
        for(int Y=0;Y<Readback.Height;Y++){
            for(int X=0;X<Readback.Width;X++){
                PixelColor Actual=ReadPixel(Readback,X,Y);
                Assert.InRange(Math.Abs(Actual.R-Expected.R),0,1);
                Assert.InRange(Math.Abs(Actual.G-Expected.G),0,1);
                Assert.InRange(Math.Abs(Actual.B-Expected.B),0,1);
                Assert.InRange(Math.Abs(Actual.A-Expected.A),0,1);
            }
        }
    }
}
