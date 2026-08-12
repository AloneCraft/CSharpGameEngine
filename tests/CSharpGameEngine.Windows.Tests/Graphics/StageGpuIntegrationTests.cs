using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>
/// Exercises the real Windows + D3D12 multi-object stage renderer end to end (ground plane +
/// box "character" placeholder, movable camera, double-buffered per-frame/per-object constant
/// buffers). Runs against the WARP software adapter so it is deterministic in CI without
/// requiring a physical GPU (same rationale as <see cref="ModelGpuIntegrationTests"/>).
/// </summary>
public class StageGpuIntegrationTests {
    private static readonly string ShaderPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.hlsl");

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_DebugLayer_Verify_RendersGroundAndCharacter(){
        RunWithTimeout(()=>RunStageGpuScenario(),TimeSpan.FromSeconds(60));
    }

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_ResizeMidRun_StillRendersAfterResize(){
        RunWithTimeout(RunStageResizeScenario,TimeSpan.FromSeconds(60));
    }

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_TwoObjectsWithDifferentMaterials_EachSamplesItsOwnDescriptorTableSlot(){
        RunWithTimeout(RunTwoMaterialsScenario,TimeSpan.FromSeconds(60));
    }

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_DirectionalLightAndAmbientChangeRenderedColor(){
        RunWithTimeout(RunLightingApiScenario,TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// Regression test for a real bug: FrameCB's per-frame-slot stride (ConstantAlignment=512) was
    /// smaller than its declared HLSL size (FrameConstantsSize=560, after ShadowViewProjection was
    /// added), so WriteShadowViewProjection corrupted the *other* frame-in-flight slot's
    /// ViewProjection matrix every time SetDirectionalLight ran. Combined with SetCamera being
    /// called only once instead of every frame (a second bug - one of the two double-buffered slots
    /// never got the real camera at all), every other frame rendered from a corrupted/stale
    /// view-projection matrix - a scrambled ("garbled") image alternating with a correct one.
    ///
    /// The 4 tests above never call Set*/Add* between consecutive RenderFrame calls the way real
    /// per-frame ECS systems do, and always capture on a call count that happens to land back on
    /// the directly-written slot - so they cannot see either bug. This test reproduces the actual
    /// usage pattern (Set every frame, capture both FrameIndex 0 and 1) and pins the general
    /// invariant that must hold for it: with an unchanging scene, both frame-in-flight slots must
    /// render pixel-identical images. This is layout-agnostic - it does not hardcode any specific
    /// pixel position - so it stays valid if FrameCB grows again in the future.
    /// </summary>
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_BothFrameInFlightSlots_RenderIdenticalImagesForAnUnchangingScene(){
        RunWithTimeout(RunBothFrameSlotsIdenticalScenario,TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// Behavioral check that the shadow map reflects the light's viewpoint (not the camera's): a
    /// small blocker sits directly between the directional light and the ground plane, and the
    /// pixel underneath it must be darker than a pixel elsewhere on the plane that the blocker does
    /// not occlude.
    /// </summary>
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_BlockerCastsAVisibleShadowOnTheGroundPlane(){
        RunWithTimeout(RunShadowCastingScenario,TimeSpan.FromSeconds(60));
    }

    private static void RunWithTimeout(Action Action,TimeSpan Timeout){
        Task Task=Task.Run(Action);
        if(!Task.Wait(Timeout)){
            throw new TimeoutException($"GPU test did not complete within {Timeout}.");
        }
    }

    private static void RunStageGpuScenario(){
        ProceduralMeshData Ground=ProceduralMeshFactory.CreatePlane(Width:20f,Depth:20f);
        ProceduralMeshData Character=ProceduralMeshFactory.CreateBox(new Vector3(1f,2f,1f));

        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Stage GPU Test",
            Width=256,
            Height=256,
            Visible=false,
        });

        GraphicsDeviceOptions DeviceOptions=new(){
            UseWarp=true,
            EnableDebugLayer=true,
        };

        using StageRenderer Renderer=StageRenderer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderPath,
            [
                new MeshCreateInfo { Positions=Ground.Positions,Normals=Ground.Normals,Indices=Ground.Indices,BaseColorFactor=new Vector4(0.3f,0.6f,0.3f,1f)},
                new MeshCreateInfo { Positions=Character.Positions,Normals=Character.Normals,Indices=Character.Indices,BaseColorFactor=new Vector4(0.8f,0.2f,0.2f,1f)},
            ]);

        Assert.True(Renderer.ShaderCompilationSucceeded,string.Join(Environment.NewLine,Renderer.ShaderDiagnostics));
        Assert.True(Renderer.IsWarp);

        Camera Camera=Camera.CreateDefault(new Vector3(0f,1f,0f));
        Renderer.SetCamera(Camera);
        Renderer.SetObjectTransform(0,Matrix4x4.Identity);
        Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(0f,1f,0f));

        for(var I=0;I<3;I++){
            Renderer.RenderFrame(CaptureForVerification:I==2);
        }

        Assert.Equal(3u,Renderer.FramesRendered);
        Assert.NotNull(Renderer.LastCapturedFrame);

        ModelVerificationResult Verification=ModelPixelVerifier.Verify(Renderer.LastCapturedFrame!,StageRenderer.ClearColor);
        Assert.True(Verification.Success,Verification.Details);

        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static void RunStageResizeScenario(){
        ProceduralMeshData Ground=ProceduralMeshFactory.CreatePlane(Width:10f,Depth:10f);

        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Stage Resize GPU Test",
            Width=128,
            Height=128,
            Visible=false,
        });

        GraphicsDeviceOptions DeviceOptions=new(){
            UseWarp=true,
            EnableDebugLayer=true,
        };

        using StageRenderer Renderer=StageRenderer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderPath,
            [new MeshCreateInfo { Positions=Ground.Positions,Normals=Ground.Normals,Indices=Ground.Indices,BaseColorFactor=Vector4.One }]);

        Camera Camera=Camera.CreateDefault(Vector3.Zero);
        Renderer.SetCamera(Camera);
        Renderer.SetObjectTransform(0,Matrix4x4.Identity);
        Renderer.RenderFrame();

        Renderer.Resize(256,256);
        Renderer.SetCamera(Camera);
        Renderer.SetObjectTransform(0,Matrix4x4.Identity);
        Renderer.RenderFrame(CaptureForVerification:true);

        Assert.NotNull(Renderer.LastCapturedFrame);
        Assert.Equal(256,Renderer.LastCapturedFrame!.Width);
        Assert.Equal(256,Renderer.LastCapturedFrame.Height);
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static void RunTwoMaterialsScenario(){
        ProceduralMeshData Box=ProceduralMeshFactory.CreateBox(new Vector3(1.2f,1.2f,1.2f));

        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Stage Multi-Material GPU Test",
            Width=256,
            Height=256,
            Visible=false,
        });

        GraphicsDeviceOptions DeviceOptions=new(){ UseWarp=true,EnableDebugLayer=true };

        var Uvs=new Vector2[Box.Positions.Length];
        var Tangents=new Vector4[Box.Positions.Length];
        for(var I=0;I<Box.Positions.Length;I++){
            Tangents[I]=new Vector4(1,0,0,1);
        }

        StageMeshCreateInfo Mesh=new(){ Positions=Box.Positions,Normals=Box.Normals,Tangents=Tangents,Uvs=Uvs,Indices=Box.Indices };
        StageMaterialCreateInfo RedMaterial=new(){ BaseColorFactor=new Vector4(1,0,0,1),RoughnessFactor=1f,MetallicFactor=0f };
        StageMaterialCreateInfo BlueMaterial=new(){ BaseColorFactor=new Vector4(0,0,1,1),RoughnessFactor=1f,MetallicFactor=0f };

        using StageRenderer Renderer=StageRenderer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderPath,
            [Mesh],
            [RedMaterial,BlueMaterial],
            [
                new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },
                new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=1 },
            ]);

        Assert.True(Renderer.ShaderCompilationSucceeded,string.Join(Environment.NewLine,Renderer.ShaderDiagnostics));

        Camera Camera=Camera.CreateDefault(Vector3.Zero);
        Renderer.SetCamera(Camera);
        Renderer.SetAmbientLight(new Vector3(0.4f,0.4f,0.4f));
        Renderer.SetDirectionalLight(new Vector3(0.3f,0.8f,0.4f),Vector3.One,2f);
        Renderer.SetObjectTransform(0,Matrix4x4.CreateTranslation(-1.0f,0f,0f));
        Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(1.0f,0f,0f));

        for(var I=0;I<3;I++){
            Renderer.RenderFrame(CaptureForVerification:I==2);
        }

        Assert.NotNull(Renderer.LastCapturedFrame);
        PixelReadbackResult Frame=Renderer.LastCapturedFrame!;

        var FoundReddish=false;
        var FoundBluish=false;
        for(var Y=0;Y<Frame.Height&&!(FoundReddish&&FoundBluish);Y++){
            for(var X=0;X<Frame.Width;X++){
                PixelColor Pixel=ReadPixel(Frame,X,Y);
                if(Pixel.R>Pixel.G+15&&Pixel.R>Pixel.B+15){
                    FoundReddish=true;
                }
                if(Pixel.B>Pixel.R+15&&Pixel.B>Pixel.G+15){
                    FoundBluish=true;
                }
            }
        }

        Assert.True(FoundReddish,"Expected at least one red-dominant pixel from the object using materialIndex 0.");
        Assert.True(FoundBluish,"Expected at least one blue-dominant pixel from the object using materialIndex 1.");
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static void RunLightingApiScenario(){
        ProceduralMeshData Ground=ProceduralMeshFactory.CreatePlane(Width:20f,Depth:20f);

        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Stage Lighting API GPU Test",
            Width=128,
            Height=128,
            Visible=false,
        });

        GraphicsDeviceOptions DeviceOptions=new(){ UseWarp=true,EnableDebugLayer=true };

        var Uvs=new Vector2[Ground.Positions.Length];
        var Tangents=new Vector4[Ground.Positions.Length];
        for(var I=0;I<Ground.Positions.Length;I++){
            Tangents[I]=new Vector4(1,0,0,1);
        }

        StageMeshCreateInfo Mesh=new(){ Positions=Ground.Positions,Normals=Ground.Normals,Tangents=Tangents,Uvs=Uvs,Indices=Ground.Indices };
        StageMaterialCreateInfo Material=new(){ BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f };

        using StageRenderer Renderer=StageRenderer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderPath,
            [Mesh],[Material],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);

        Camera Camera=Camera.CreateDefault(Vector3.Zero);
        Renderer.SetCamera(Camera);
        Renderer.SetObjectTransform(0,Matrix4x4.Identity);

        Renderer.SetAmbientLight(Vector3.Zero);
        Renderer.SetDirectionalLight(Vector3.UnitY,Vector3.Zero,0f);
        Renderer.RenderFrame();
        Renderer.RenderFrame();
        Renderer.RenderFrame(CaptureForVerification:true);
        PixelColor DarkCenter=ReadPixel(Renderer.LastCapturedFrame!,Renderer.LastCapturedFrame!.Width/2,Renderer.LastCapturedFrame!.Height/2);

        Renderer.SetCamera(Camera);
        Renderer.SetAmbientLight(new Vector3(0.3f,0.3f,0.3f));
        Renderer.SetDirectionalLight(Vector3.UnitY,Vector3.One,3f);
        Renderer.SetObjectTransform(0,Matrix4x4.Identity);
        Renderer.RenderFrame();
        Renderer.RenderFrame();
        Renderer.RenderFrame(CaptureForVerification:true);
        PixelColor BrightCenter=ReadPixel(Renderer.LastCapturedFrame!,Renderer.LastCapturedFrame!.Width/2,Renderer.LastCapturedFrame!.Height/2);

        var DarkSum=DarkCenter.R+DarkCenter.G+DarkCenter.B;
        var BrightSum=BrightCenter.R+BrightCenter.G+BrightCenter.B;
        Assert.True(DarkSum<15, $"Expected a near-black center pixel with zero light/ambient, got {DarkCenter} (sum={DarkSum}).");
        Assert.True(BrightSum>DarkSum+200, $"Expected SetDirectionalLight/SetAmbientLight to meaningfully brighten the center pixel: dark={DarkCenter}, bright={BrightCenter}.");
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static void RunBothFrameSlotsIdenticalScenario(){
        ProceduralMeshData Box=ProceduralMeshFactory.CreateBox(new Vector3(1.5f,1.5f,1.5f));

        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Stage Frame-Slot Parity GPU Test",
            Width=128,
            Height=128,
            Visible=false,
        });

        GraphicsDeviceOptions DeviceOptions=new(){ UseWarp=true,EnableDebugLayer=true };

        var Uvs=new Vector2[Box.Positions.Length];
        var Tangents=new Vector4[Box.Positions.Length];
        for(var I=0;I<Box.Positions.Length;I++){
            Tangents[I]=new Vector4(1,0,0,1);
        }

        StageMeshCreateInfo Mesh=new(){ Positions=Box.Positions,Normals=Box.Normals,Tangents=Tangents,Uvs=Uvs,Indices=Box.Indices };
        StageMaterialCreateInfo Material=new(){ BaseColorFactor=new Vector4(0.8f,0.3f,0.2f,1f),RoughnessFactor=0.6f,MetallicFactor=0f };

        using StageRenderer Renderer=StageRenderer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderPath,
            [Mesh],[Material],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);

        Assert.True(Renderer.ShaderCompilationSucceeded,string.Join(Environment.NewLine,Renderer.ShaderDiagnostics));

        Camera Camera=Camera.CreateDefault(Vector3.Zero);
        Vector3 LightDirection=Vector3.Normalize(new Vector3(0.3f,0.8f,0.4f));

        // RuntimeSystem.cs's LevelLoad/DirectionalLight systems push this exact set every real
        // frame - the pattern the 4 tests above never reproduce, which is why none of them could
        // see the FrameCB slot-corruption/stale-camera bugs this test guards against.
        void PushPerFrameState(){
            Renderer.SetCamera(Camera);
            Renderer.SetAmbientLight(new Vector3(0.25f,0.25f,0.25f));
            Renderer.SetDirectionalLight(LightDirection,Vector3.One,2f);
            Renderer.SetObjectTransform(0,Matrix4x4.Identity);
        }

        // FrameCount=2: consecutive RenderFrame calls alternate FrameIndex, so two captures in a
        // row exercise both double-buffered slots.
        PushPerFrameState();
        Renderer.RenderFrame(CaptureForVerification:true);
        PixelReadbackResult FirstSlotFrame=Renderer.LastCapturedFrame!;

        PushPerFrameState();
        Renderer.RenderFrame(CaptureForVerification:true);
        PixelReadbackResult SecondSlotFrame=Renderer.LastCapturedFrame!;

        Assert.Equal(FirstSlotFrame.Width,SecondSlotFrame.Width);
        Assert.Equal(FirstSlotFrame.Height,SecondSlotFrame.Height);
        Assert.Equal(FirstSlotFrame.RowPitch,SecondSlotFrame.RowPitch);

        var MismatchedPixels=0;
        const int Tolerance=2;
        for(var Y=0;Y<FirstSlotFrame.Height;Y++){
            for(var X=0;X<FirstSlotFrame.Width;X++){
                PixelColor A=ReadPixel(FirstSlotFrame,X,Y);
                PixelColor B=ReadPixel(SecondSlotFrame,X,Y);
                if(Math.Abs(A.R-B.R)>Tolerance||Math.Abs(A.G-B.G)>Tolerance||Math.Abs(A.B-B.B)>Tolerance){
                    MismatchedPixels++;
                }
            }
        }

        var TotalPixels=FirstSlotFrame.Width*FirstSlotFrame.Height;
        Assert.True(
            MismatchedPixels==0,
            $"Expected both frame-in-flight slots to render an identical image for an unchanging scene, but {MismatchedPixels}/{TotalPixels} pixels differed by more than the tolerance. A mismatch here means the two double-buffered constant-buffer slots disagree - exactly the symptom of the FrameCB slot-corruption/stale-camera bugs this test guards against.");

        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static void RunShadowCastingScenario(){
        ProceduralMeshData Ground=ProceduralMeshFactory.CreatePlane(Width:16f,Depth:16f);
        ProceduralMeshData Blocker=ProceduralMeshFactory.CreateBox(new Vector3(3f,3f,3f));

        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Stage Shadow GPU Test",
            Width=128,
            Height=128,
            Visible=false,
        });

        GraphicsDeviceOptions DeviceOptions=new(){ UseWarp=true,EnableDebugLayer=true };

        var GroundUvs=new Vector2[Ground.Positions.Length];
        var GroundTangents=new Vector4[Ground.Positions.Length];
        for(var I=0;I<Ground.Positions.Length;I++){
            GroundTangents[I]=new Vector4(1,0,0,1);
        }

        var BlockerUvs=new Vector2[Blocker.Positions.Length];
        var BlockerTangents=new Vector4[Blocker.Positions.Length];
        for(var I=0;I<Blocker.Positions.Length;I++){
            BlockerTangents[I]=new Vector4(1,0,0,1);
        }

        StageMeshCreateInfo GroundMesh=new(){ Positions=Ground.Positions,Normals=Ground.Normals,Tangents=GroundTangents,Uvs=GroundUvs,Indices=Ground.Indices };
        StageMeshCreateInfo BlockerMesh=new(){ Positions=Blocker.Positions,Normals=Blocker.Normals,Tangents=BlockerTangents,Uvs=BlockerUvs,Indices=Blocker.Indices };
        StageMaterialCreateInfo Material=new(){ BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f };

        using StageRenderer Renderer=StageRenderer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderPath,
            [GroundMesh,BlockerMesh],[Material],
            [
                new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }, // ground, object 0
                new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 }, // blocker, object 1
            ]);

        Assert.True(Renderer.ShaderCompilationSucceeded,string.Join(Environment.NewLine,Renderer.ShaderDiagnostics));

        // Nearly-top-down orbit camera targeting the world origin, so the captured frame's center
        // pixel corresponds to the ground point directly below the blocker once the blocker is
        // moved above the origin - no per-pixel-to-world-space projection math needed.
        Camera Camera=Camera.CreateDefault(Vector3.Zero);
        Camera.Pitch=-89f*MathF.PI/180f;
        Camera.Distance=20f;
        Camera.FarZ=200f;

        Vector3 LightDirection=Vector3.Normalize(new Vector3(0.05f,1f,0.05f));
        Vector3 FarAway=new(500f,500f,500f);
        Vector3 AboveOrigin=new(0f,4f,0f);

        PixelColor RenderCenterWithBlockerAt(Vector3 BlockerPosition){
            Renderer.SetCamera(Camera);
            Renderer.SetAmbientLight(new Vector3(0.15f,0.15f,0.15f));
            Renderer.SetDirectionalLight(LightDirection,Vector3.One,3f);
            Renderer.SetObjectTransform(0,Matrix4x4.Identity);
            Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(BlockerPosition));

            for(var I=0;I<3;I++){
                Renderer.RenderFrame(CaptureForVerification:I==2);
            }

            return ReadPixel(Renderer.LastCapturedFrame!,Renderer.LastCapturedFrame!.Width/2,Renderer.LastCapturedFrame!.Height/2);
        }

        PixelColor Baseline=RenderCenterWithBlockerAt(FarAway);
        PixelColor Shadowed=RenderCenterWithBlockerAt(AboveOrigin);

        var BaselineSum=Baseline.R+Baseline.G+Baseline.B;
        var ShadowedSum=Shadowed.R+Shadowed.G+Shadowed.B;
        Assert.True(
            ShadowedSum<BaselineSum-15,
            $"Expected the ground pixel directly beneath the blocker to be measurably darker than the same pixel with the blocker moved away (i.e. the shadow map reflects the light's viewpoint): baseline={Baseline} (sum={BaselineSum}), shadowed={Shadowed} (sum={ShadowedSum}).");

        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static PixelColor ReadPixel(PixelReadbackResult Readback,int X,int Y){
        const int BytesPerPixel=4;
        var Offset=((long)Y*Readback.RowPitch)+(X*BytesPerPixel);
        return new PixelColor(Readback.Pixels[Offset],Readback.Pixels[Offset+1],Readback.Pixels[Offset+2],Readback.Pixels[Offset+3]);
    }
}
