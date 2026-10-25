using CSharpGameEngine.Lighting;
using System.Numerics;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;

namespace CSharpGameEngine.Runtime.Windows.Tests;

/// <summary>
/// Exercises primitive roster conversion and the owning Render Server end to end through WARP,
/// including suspension and artifact-only device-generation recovery.
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public class RenderServerTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");

    [WindowsOnlyFact]
    public void BuildPrimitiveRoster_SamePrimitiveTypeRepeated_DedupesToOneMeshEntry(){
        (var Meshes,var Materials,var Objects)=
            PrimitiveRoster.Build([PrimitiveType.Box,PrimitiveType.Box,PrimitiveType.Box],MeshIndexOffset:0,MaterialIndexOffset:0);

        Assert.Single(Meshes);
        Assert.Single(Materials);
        Assert.Equal(3,Objects.Length);
        Assert.All(Objects,Obj=>Assert.Equal(0,Obj.MeshIndex));
        Assert.All(Objects,Obj=>Assert.Equal(0,Obj.MaterialIndex));
    }

    [WindowsOnlyFact]
    public void BuildPrimitiveRoster_DistinctPrimitiveTypes_EachGetsItsOwnMeshIndexOffsetCorrectly(){
        (var Meshes,var Materials,var Objects)=
            PrimitiveRoster.Build([PrimitiveType.Box,PrimitiveType.Sphere,PrimitiveType.Box],MeshIndexOffset:5,MaterialIndexOffset:2);

        Assert.Equal(2,Meshes.Length);
        Assert.Equal(3,Objects.Length);
        Assert.Equal(5,Objects[0].MeshIndex); // Box, first seen -> offset+0
        Assert.Equal(6,Objects[1].MeshIndex); // Sphere, second seen -> offset+1
        Assert.Equal(5,Objects[2].MeshIndex); // Box again -> dedups to the same index as Objects[0]
        Assert.All(Objects,Obj=>Assert.Equal(2,Obj.MaterialIndex));
        Assert.Single(Materials);
    }

    [WindowsOnlyFact]
    public void BuildPrimitiveRoster_EveryPrimitiveType_ProducesAStageRendererValidMesh(){
        foreach(var Type in Enum.GetValues<PrimitiveType>()){
            (var Meshes,_,_)=PrimitiveRoster.Build([Type],0,0);
            var Mesh=Assert.Single(Meshes);

            Assert.NotEmpty(Mesh.Positions);
            Assert.Equal(Mesh.Positions.Count,Mesh.Normals.Count);
            Assert.Equal(Mesh.Positions.Count,Mesh.Tangents.Count);
            Assert.Equal(Mesh.Positions.Count,Mesh.Uvs.Count);
            Assert.NotEmpty(Mesh.Indices);
        }
    }

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void BuildPrimitiveRoster_Box_RendersARecognizableSilhouetteAgainstTheClearColor(){
        RunWithTimeout(RunBoxRenderScenario,TimeSpan.FromSeconds(60));
    }

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void PresentationModesAndSelection_RenderDistinctPixelsAndSurviveDeviceRecovery(){
        RunWithTimeout(RunPresentationScenario,TimeSpan.FromSeconds(60));
    }

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void CameraPreview_RendersIsolatedCleanPassAndSurvivesDeviceRecovery(){
        RunWithTimeout(RunCameraPreviewScenario,TimeSpan.FromSeconds(60));
    }

    private static void RunWithTimeout(Action Action,TimeSpan Timeout){
        Task Task=Task.Run(Action);
        if(!Task.Wait(Timeout)){
            throw new TimeoutException($"GPU test did not complete within {Timeout}.");
        }
    }

    private static void RunBoxRenderScenario(){
        string TemporaryArtifactPath=Path.Combine(
            Path.GetTempPath(),$"CSharpGameEngine-RenderRecovery-{Guid.NewGuid():N}.cgeshaderartifact");
        File.Copy(ShaderArtifactPath,TemporaryArtifactPath);
        try {
            RunBoxRenderScenario(TemporaryArtifactPath);
        } finally {
            File.Delete(TemporaryArtifactPath);
        }
    }

    private static void RunPresentationScenario(){
        (StageMeshCreateInfo[]Meshes,StageMaterialCreateInfo[]Materials,StageObjectCreateInfo[]Objects)=
            PrimitiveRoster.Build([PrimitiveType.Box],0,0);
        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Render Presentation GPU Test",
            Width=128,
            Height=128,
            Visible=false,
        });
        using RenderServer Renderer=RenderServer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,
            new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },
            ShaderArtifactPath,Meshes,Materials,Objects);

        static string Capture(
            RenderServer Renderer,RenderPresentationMode Mode,float SelectionWeight,
            bool WithOverlay=false,float OverlayAlpha=0.75f){
            var Camera=CSharpGameEngine.Rendering.View.RenderCamera.CreateDefault(Vector3.Zero);
            Renderer.SetCamera(Camera);
            Renderer.SetAmbientLight(new Vector3(0.4f));
            Renderer.SetDirectionalLight(new DirectionalLightSubmission(Vector3.UnitY,new LinearLightColor(1f,1f,1f),new Illuminance(2f),new LightShadowSettings(0,new ShadowBias(0.0015f))));
            Renderer.SetObjectTransform(0,Matrix4x4.Identity);
            Renderer.SetPresentationMode(Mode);
            Renderer.SetObjectSelectionWeight(0,SelectionWeight);
            Renderer.SetOverlay(
                WithOverlay
                    ?[
                        new RenderOverlayVertex(
                            new Vector3(-0.75f,-0.75f,-0.75f),
                            new Vector4(1f,0f,1f,OverlayAlpha)),
                        new RenderOverlayVertex(
                            new Vector3(0.75f,-0.75f,-0.75f),
                            new Vector4(1f,0f,1f,OverlayAlpha)),
                        new RenderOverlayVertex(
                            new Vector3(0f,0.75f,-0.75f),
                            new Vector4(1f,0f,1f,OverlayAlpha)),
                    ]
                    :[],
                []);
            Renderer.RenderFrame(CaptureForVerification:true);
            Assert.NotNull(Renderer.LastCapturedFrame);
            Assert.True(Renderer.CompletedFence>0);
            return Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    Renderer.LastCapturedFrame!.Pixels));
        }

        string Lit=Capture(Renderer,RenderPresentationMode.Lit,0f);
        string BaseColor=Capture(Renderer,RenderPresentationMode.BaseColor,0f);
        string Normal=Capture(Renderer,RenderPresentationMode.Normal,0f);
        string Wireframe=Capture(Renderer,RenderPresentationMode.Wireframe,0f);
        string Selected=Capture(Renderer,RenderPresentationMode.Lit,1f);
        string TransparentOverlay=Capture(
            Renderer,RenderPresentationMode.Lit,1f,WithOverlay:true,OverlayAlpha:0f);
        string Overlay=Capture(
            Renderer,RenderPresentationMode.Lit,1f,WithOverlay:true);

        Assert.Equal(Selected,TransparentOverlay);
        Assert.Equal(6,new HashSet<string>(
            [Lit,BaseColor,Normal,Wireframe,Selected,Overlay],
            StringComparer.Ordinal).Count);

        Renderer.RemoveDeviceForTesting();
        Renderer.RenderFrame(CaptureForVerification:true);
        Assert.NotNull(Renderer.LastCapturedFrame);
        string Recovered=Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                Renderer.LastCapturedFrame!.Pixels));
        Assert.Equal(Overlay,Recovered);
        Assert.Equal(2u,Renderer.DeviceSnapshot.Generation);
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static void RunCameraPreviewScenario(){
        (StageMeshCreateInfo[]Meshes,StageMaterialCreateInfo[]Materials,StageObjectCreateInfo[]Objects)=
            PrimitiveRoster.Build([PrimitiveType.Box],0,0);
        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Camera Preview GPU Test",
            Width=128,
            Height=128,
            Visible=false,
        });
        using RenderServer Renderer=RenderServer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,
            new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },
            ShaderArtifactPath,Meshes,Materials,Objects);

        var Camera=CSharpGameEngine.Rendering.View.RenderCamera.CreateDefault(Vector3.Zero);
        Matrix4x4 ViewProjection=Camera.GetView()*Camera.GetProjection(1f);
        Assert.True(RadialDepthProjection.TryCreate(RadialDepthCurve.Default,Camera.NearZ,Camera.FarZ,Camera.FovYRadians,1f,out var Projection));
        RenderCameraPreview Included=new(
            16,16,64,64,ViewProjection,Camera.GetEyePosition(),RenderViewDepth.ForPerspective(Projection,ConservativeRasterDepth:false));
        RenderCameraPreview Excluded=Included with { ExcludedObjectIndex=0 };

        static (string Full,string Region)Capture(
            RenderServer Renderer,
            CSharpGameEngine.Rendering.View.RenderCamera Camera,
            RenderCameraPreview Preview,float SelectionWeight){
            Renderer.SetCamera(Camera,1f);
            Renderer.SetAmbientLight(new Vector3(0.4f));
            Renderer.SetDirectionalLight(new DirectionalLightSubmission(Vector3.UnitY,new LinearLightColor(1f,1f,1f),new Illuminance(2f),new LightShadowSettings(0,new ShadowBias(0.0015f))));
            Renderer.SetObjectTransform(0,Matrix4x4.Identity);
            Renderer.SetPresentationMode(RenderPresentationMode.BaseColor);
            Renderer.SetObjectSelectionWeight(0,SelectionWeight);
            Renderer.SetCameraPreviews([Preview]);
            Renderer.SetOverlay([],[]);
            Renderer.RenderFrame(CaptureForVerification:true);
            PixelReadbackResult Frame=Assert.IsType<PixelReadbackResult>(
                Renderer.LastCapturedFrame);
            return(Hash(Frame.Pixels),HashRegion(Frame,16,16,64,64));
        }

        (string UnselectedFull,string UnselectedRegion)=
            Capture(Renderer,Camera,Included,0f);
        (string SelectedFull,string SelectedRegion)=
            Capture(Renderer,Camera,Included,1f);
        Assert.NotEqual(UnselectedFull,SelectedFull);
        Assert.Equal(UnselectedRegion,SelectedRegion);

        (string ExcludedFull,string ExcludedRegion)=
            Capture(Renderer,Camera,Excluded,1f);
        Assert.NotEqual(SelectedRegion,ExcludedRegion);
        RenderCameraPreviewDiagnostics Diagnostics=
            Assert.Single(Renderer.GetCameraPreviewDiagnostics());
        Assert.Equal(1,Diagnostics.SceneObjectCount);
        Assert.Equal(0,Diagnostics.DrawCallCount);
        Assert.NotEqual(0u,Diagnostics.ViewProjectionHash);

        Renderer.RemoveDeviceForTesting();
        Renderer.RenderFrame(CaptureForVerification:true);
        PixelReadbackResult Recovered=Assert.IsType<PixelReadbackResult>(
            Renderer.LastCapturedFrame);
        Assert.Equal(ExcludedFull,Hash(Recovered.Pixels));
        Assert.Equal(2u,Renderer.DeviceSnapshot.Generation);
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static string Hash(byte[]Bytes)=>Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(Bytes));

    private static string HashRegion(
        PixelReadbackResult Frame,int X,int Y,int Width,int Height){
        const int BytesPerPixel=4;
        byte[]Region=new byte[checked(Width*Height*BytesPerPixel)];
        int TargetOffset=0;
        for(int Row=0;Row<Height;Row++){
            int SourceOffset=checked((int)(((Y+Row)*Frame.RowPitch)+(X*BytesPerPixel)));
            int Count=checked(Width*BytesPerPixel);
            Buffer.BlockCopy(Frame.Pixels,SourceOffset,Region,TargetOffset,Count);
            TargetOffset+=Count;
        }
        return Hash(Region);
    }

    private static void RunBoxRenderScenario(string EffectiveShaderArtifactPath){
        (var Meshes,var Materials,var Objects)=
            PrimitiveRoster.Build([PrimitiveType.Box],0,0);

        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine RenderServer GPU Test",
            Width=128,
            Height=128,
            Visible=false,
        });

        GraphicsDeviceOptions DeviceOptions=new(){ UseWarp=true,EnableDebugLayer=true };

        using var Renderer=RenderServer.Create(
                                                Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,EffectiveShaderArtifactPath,
                                                Meshes,Materials,Objects);

        Assert.Equal(CSharpGameEngine.Rendering.RenderDeviceState.Ready,
            Renderer.DeviceSnapshot.State);
        Assert.Equal(1u,Renderer.DeviceSnapshot.Generation);
        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));

        var Camera=CSharpGameEngine.Rendering.View.RenderCamera.CreateDefault(Vector3.Zero);
        Renderer.SetCamera(Camera);
        Renderer.SetAmbientLight(new Vector3(0.4f,0.4f,0.4f));
        Renderer.SetDirectionalLight(new DirectionalLightSubmission(Vector3.UnitY,new LinearLightColor(1f,1f,1f),new Illuminance(2f),new LightShadowSettings(0,new ShadowBias(0.0015f))));
        Renderer.SetObjectTransform(0,Matrix4x4.Identity);

        for(var I=0;I<3;I++){
            Renderer.RenderFrame(CaptureForVerification:I==2);
        }

        Assert.NotNull(Renderer.LastCapturedFrame);
        var Verification=ModelPixelVerifier.Verify(
            Renderer.LastCapturedFrame!,RenderServer.ClearColor);
        Assert.True(Verification.Success,Verification.Details);

        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);

        File.WriteAllBytes(EffectiveShaderArtifactPath,[0x42,0x41,0x44]);
        Renderer.RemoveDeviceForTesting();
        Renderer.RenderFrame(CaptureForVerification:true);

        Assert.Equal(CSharpGameEngine.Rendering.RenderDeviceState.Ready,
            Renderer.DeviceSnapshot.State);
        Assert.Equal(2u,Renderer.DeviceSnapshot.Generation);
        Assert.Equal(CSharpGameEngine.Rendering.RenderRecoveryReason.DeviceRemoved,
            Renderer.DeviceSnapshot.LastRecoveryReason);
        Assert.Equal(4,Renderer.DeviceSnapshot.RebuiltArtifacts.Count);
        Assert.NotNull(Renderer.LastCapturedFrame);
        Verification=ModelPixelVerifier.Verify(
            Renderer.LastCapturedFrame!,RenderServer.ClearColor);
        Assert.True(Verification.Success,Verification.Details);

        Renderer.Resize(0,0);
        Assert.Equal(CSharpGameEngine.Rendering.RenderDeviceState.Suspended,
            Renderer.DeviceSnapshot.State);
        Assert.Throws<CSharpGameEngine.Rendering.RenderDeviceUnavailableException>(
            ()=>Renderer.RenderFrame());

        Renderer.Resize(Window.ClientWidth,Window.ClientHeight);
        Assert.Equal(CSharpGameEngine.Rendering.RenderDeviceState.Ready,
            Renderer.DeviceSnapshot.State);
        Assert.Equal(3u,Renderer.DeviceSnapshot.Generation);
        Assert.Equal(4,Renderer.DeviceSnapshot.RebuiltArtifacts.Count);

        // No command is resubmitted here. The server must reconstruct the exact prior frame from
        // its completed snapshot rather than asking ECS or the caller to rebuild native state.
        Renderer.RenderFrame(CaptureForVerification:true);

        Assert.NotNull(Renderer.LastCapturedFrame);
        Verification=ModelPixelVerifier.Verify(
            Renderer.LastCapturedFrame!,RenderServer.ClearColor);
        Assert.True(Verification.Success,Verification.Details);
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }
}
