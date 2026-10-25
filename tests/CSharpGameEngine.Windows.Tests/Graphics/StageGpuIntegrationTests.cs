using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using CSharpGameEngine.Rendering.Pipeline;
using System.Numerics;
using System.Text;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>
/// Exercises the real Windows + D3D12 multi-object stage renderer end to end (ground plane +
/// box "character" placeholder, movable camera, double-buffered per-frame/per-object constant
/// buffers). Runs against the WARP software adapter so it is deterministic in CI without
/// requiring a physical GPU (same rationale as <see cref="ModelGpuIntegrationTests"/>).
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public class StageGpuIntegrationTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private static readonly StableId MaterialNamespace=
        StableId.Parse("a19ac848-6ec2-50a2-8333-6e31dbcfc19d");
    private static readonly ArtifactProvenance MaterialProvenance=new(
        "test-1","asset://stage-materials",
        Hash256.Compute(Encoding.UTF8.GetBytes("stage-materials")),
        "test",Hash256.Compute(Encoding.UTF8.GetBytes("options")));

    [Fact]
    public void StageShadowAtlas_DirectionalTileZeroMatchesPhysicalPlan(){
        ShadowAtlasTile Tile=ShadowAtlasTile.FromIndex(StageRenderer.DirectionalShadowAtlasTileIndex);

        Assert.Equal(PhysicalShadowAtlasPlan.AtlasSizePixels,StageRenderer.ShadowAtlasSizePixels);
        Assert.Equal(PhysicalShadowAtlasPlan.TileSizePixels,StageRenderer.ShadowAtlasTileSizePixels);
        Assert.Equal(0,Tile.Index);
        Assert.Equal(0,Tile.X);
        Assert.Equal(0,Tile.Y);
        Assert.Equal(StageRenderer.ShadowAtlasTileSizePixels,Tile.Width);
        Assert.Equal(StageRenderer.ShadowAtlasTileSizePixels,Tile.Height);
        Assert.Equal(new Vector4(0.25f,0.25f,0f,0f),Tile.UvScaleOffset);
    }

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

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_FourPhysicalDirectionalLights_AreAdditive(){
        RunWithTimeout(RunDirectionalAggregationScenario,TimeSpan.FromSeconds(60));
    }

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_ForwardPlusStructuredLocalLights_IlluminateAndReportTileOverflow(){
        RunWithTimeout(RunForwardPlusLocalLightScenario,TimeSpan.FromSeconds(60));
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

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_PhysicalSpotLightCastsAVisibleAtlasShadowOnTheGroundPlane(){
        RunWithTimeout(RunSpotShadowCastingScenario,TimeSpan.FromSeconds(60));
    }

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_PhysicalPointLightCastsAVisibleAtlasShadowOnTheGroundPlane(){
        RunWithTimeout(RunPointShadowCastingScenario,TimeSpan.FromSeconds(60));
    }

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_NonSelectedPhysicalPointShadowCasterStillContributesDirectLight(){
        RunWithTimeout(RunPointShadowOverflowKeepsDirectLightScenario,TimeSpan.FromSeconds(60));
    }

    private static void RunWithTimeout(Action Action,TimeSpan Timeout){
        Task Task=Task.Run(Action);
        if(!Task.Wait(Timeout)){
            throw new TimeoutException($"GPU test did not complete within {Timeout}.");
        }
    }

    private static void RunStageGpuScenario(){
        var Ground=ProceduralMeshFactory.CreatePlane(Width:20f,Depth:20f);
        var Character=ProceduralMeshFactory.CreateBox(new Vector3(1f,2f,1f));

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

        using var Renderer=StageRenderer.Create(
                                                Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderArtifactPath,
                                                [
                                                    new MeshCreateInfo { Positions=Ground.Positions,Normals=Ground.Normals,Indices=Ground.Indices,BaseColorFactor=new Vector4(0.3f,0.6f,0.3f,1f)},
                                                    new MeshCreateInfo { Positions=Character.Positions,Normals=Character.Normals,Indices=Character.Indices,BaseColorFactor=new Vector4(0.8f,0.2f,0.2f,1f)},
                                                ]);

        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
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

        var Verification=ModelPixelVerifier.Verify(Renderer.LastCapturedFrame!,StageRenderer.ClearColor);
        Assert.True(Verification.Success,Verification.Details);

        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static void RunStageResizeScenario(){
        var Ground=ProceduralMeshFactory.CreatePlane(Width:10f,Depth:10f);

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

        using var Renderer=StageRenderer.Create(
                                                Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderArtifactPath,
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
        var Box=ProceduralMeshFactory.CreateBox(new Vector3(1.2f,1.2f,1.2f));

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
        RenderMaterialCatalog MaterialCatalog=new();
        StageMaterialCreateInfo RedMaterial=ToStageMaterial(
            CreateArtifactMaterial(MaterialCatalog,"red",[255,0,0,255]));
        StageMaterialCreateInfo BlueMaterial=ToStageMaterial(
            CreateArtifactMaterial(MaterialCatalog,"blue",[0,0,255,255]));

        using var Renderer=StageRenderer.Create(
                                                Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderArtifactPath,
                                                [Mesh],
                                                [RedMaterial,BlueMaterial],
                                                [
                                                    new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },
                                                    new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=1 },
                                                ]);

        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
        // Red, blue and the normal map once each, and the shared white once per colour space (M0110: base colour averages its mips in sRGB, metallic-roughness linearly).
        Assert.Equal(5,Renderer.ResidentMaterialTextureCount);

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
        var Frame=Renderer.LastCapturedFrame!;

        var FoundReddish=false;
        var FoundBluish=false;
        for(var Y=0;Y<Frame.Height&&!(FoundReddish&&FoundBluish);Y++){
            for(var X=0;X<Frame.Width;X++){
                var Pixel=ReadPixel(Frame,X,Y);
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

    private static RenderMaterialResource CreateArtifactMaterial(
        RenderMaterialCatalog Catalog,string Name,ReadOnlySpan<byte>BaseColorPixels){
        AssetKey BaseColorKey=MaterialKey($"{Name}/base-color");
        AssetKey NormalKey=MaterialKey("shared/normal");
        AssetKey WhiteKey=MaterialKey("shared/white");
        RenderTextureResource BaseColor=Catalog.RegisterTexture(
            BaseColorKey,RenderArtifactCodec.CreateTextureEnvelope(
                MaterialProvenance,1,1,BaseColorPixels));
        RenderTextureResource Normal=Catalog.RegisterTexture(
            NormalKey,RenderArtifactCodec.CreateTextureEnvelope(
                MaterialProvenance,1,1,[128,128,255,255]));
        RenderTextureResource White=Catalog.RegisterTexture(
            WhiteKey,RenderArtifactCodec.CreateTextureEnvelope(
                MaterialProvenance,1,1,[255,255,255,255]));
        RenderMaterialArtifactDefinition Definition=new(
            Vector4.One,1f,0f,Vector3.Zero,1f,
            new AssetArtifactReference(BaseColorKey,BaseColor.ContentHash),
            new AssetArtifactReference(NormalKey,Normal.ContentHash),
            new AssetArtifactReference(WhiteKey,White.ContentHash),
            new AssetArtifactReference(WhiteKey,White.ContentHash));
        return Catalog.RegisterMaterial(
            MaterialKey($"{Name}/material"),
            RenderArtifactCodec.CreateMaterialEnvelope(MaterialProvenance,Definition));
    }

    private static StageMaterialCreateInfo ToStageMaterial(RenderMaterialResource Material)=>new(){
        BaseColorFactor=Material.BaseColorFactor,
        RoughnessFactor=Material.RoughnessFactor,
        MetallicFactor=Material.MetallicFactor,
        EmissiveNits=Material.EmissiveNits,
        EmissiveTextureLuminanceScale=Material.EmissiveTextureLuminanceScale,
        BaseColorTexture=Material.BaseColorTexture,
        NormalTexture=Material.NormalTexture,
        MetallicRoughnessTexture=Material.MetallicRoughnessTexture,
        EmissiveTexture=Material.EmissiveTexture,
    };

    private static AssetKey MaterialKey(string Name)=>new(
        new AssetId(StableId.FromName(MaterialNamespace,Name)),null);

    private static void RunLightingApiScenario(){
        var Ground=ProceduralMeshFactory.CreatePlane(Width:20f,Depth:20f);

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

        using var Renderer=StageRenderer.Create(
                                                Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderArtifactPath,
                                                [Mesh],[Material],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);

        Camera Camera=Camera.CreateDefault(Vector3.Zero);
        Renderer.SetCamera(Camera);
        Renderer.SetObjectTransform(0,Matrix4x4.Identity);

        Renderer.SetAmbientLight(Vector3.Zero);
        Renderer.SetDirectionalLight(Vector3.UnitY,Vector3.Zero,0f);
        Renderer.RenderFrame();
        Renderer.RenderFrame();
        Renderer.RenderFrame(CaptureForVerification:true);
        var DarkCenter=ReadPixel(Renderer.LastCapturedFrame!,Renderer.LastCapturedFrame!.Width/2,Renderer.LastCapturedFrame!.Height/2);

        Renderer.SetCamera(Camera);
        Renderer.SetAmbientLight(new Vector3(0.3f,0.3f,0.3f));
        Renderer.SetDirectionalLight(Vector3.UnitY,Vector3.One,3f);
        Renderer.SetObjectTransform(0,Matrix4x4.Identity);
        Renderer.RenderFrame();
        Renderer.RenderFrame();
        Renderer.RenderFrame(CaptureForVerification:true);
        var BrightCenter=ReadPixel(Renderer.LastCapturedFrame!,Renderer.LastCapturedFrame!.Width/2,Renderer.LastCapturedFrame!.Height/2);

        var DarkSum=DarkCenter.R+DarkCenter.G+DarkCenter.B;
        var BrightSum=BrightCenter.R+BrightCenter.G+BrightCenter.B;
        Assert.True(DarkSum<15, $"Expected a near-black center pixel with zero light/ambient, got {DarkCenter} (sum={DarkSum}).");
        Assert.True(BrightSum>DarkSum+200, $"Expected SetDirectionalLight/SetAmbientLight to meaningfully brighten the center pixel: dark={DarkCenter}, bright={BrightCenter}.");
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static void RunForwardPlusLocalLightScenario(){
        var Ground=ProceduralMeshFactory.CreatePlane(Width:20f,Depth:20f);
        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Forward+ GPU Test",
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
        using var Renderer=StageRenderer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderArtifactPath,
            [Mesh],[Material],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
        Camera Camera=Camera.CreateDefault(Vector3.Zero);

        PixelColor RenderWithPointLights(int Count){
            for(var Frame=0;Frame<3;Frame++){
                Renderer.SetCamera(Camera);
                Renderer.SetAmbientLight(Vector3.Zero);
                Renderer.SetDirectionalLight(Vector3.UnitY,Vector3.Zero,0f);
                Renderer.SetObjectTransform(0,Matrix4x4.Identity);
                for(var LightIndex=0;LightIndex<Count;LightIndex++){
                    Renderer.AddPointLight(new Vector3(0f,4f,0f),Vector3.One,80f,12f);
                }
                Renderer.RenderFrame(CaptureForVerification:Frame==2);
            }
            PixelReadbackResult CapturedFrame=Assert.IsType<PixelReadbackResult>(Renderer.LastCapturedFrame);
            return ReadPixel(CapturedFrame,CapturedFrame.Width/2,CapturedFrame.Height/2);
        }

        PixelColor Dark=RenderWithPointLights(0);
        PixelColor Lit=RenderWithPointLights(1);
        int DarkSum=Dark.R+Dark.G+Dark.B;
        int LitSum=Lit.R+Lit.G+Lit.B;
        Assert.True(LitSum>DarkSum+100,
            $"Expected the Forward+ StructuredBuffer point light to illuminate the center pixel: dark={Dark} (sum={DarkSum}), lit={Lit} (sum={LitSum}).");

        _=RenderWithPointLights(StageRenderer.MaxLightsPerForwardPlusTile+1);
        Assert.Equal(0,Renderer.DroppedLocalLightsLastFrame);
        Assert.True(Renderer.DroppedTileLightReferencesLastFrame>0,
            "Expected 65 lights projected into the same 16x16 tile to report at least one rejected tile candidate.");
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static void RunDirectionalAggregationScenario(){
        var Ground=ProceduralMeshFactory.CreatePlane(Width:20f,Depth:20f);

        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Four Directional GPU Test",
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
        using var Renderer=StageRenderer.Create(
                                                Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderArtifactPath,
                                                [Mesh],[Material],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
        Camera Camera=Camera.CreateDefault(Vector3.Zero);

        PixelColor RenderWithDirectionalCount(int Count){
            for(var Frame=0;Frame<3;Frame++){
                Renderer.SetCamera(Camera);
                Renderer.SetAmbientLight(Vector3.Zero);
                Renderer.SetObjectTransform(0,Matrix4x4.Identity);
                for(var LightIndex=0;LightIndex<Count;LightIndex++){
                    // Physical API takes light travel direction, so -Y maps to the renderer's
                    // legacy surface-to-light +Y input and illuminates this horizontal plane.
                    Renderer.AddDirectionalLight(
                        -Vector3.UnitY,Vector3.One,IlluminanceLux:0.4f,
                        CastsShadows:false,ShadowPriority:LightIndex,ShadowBiasMeters:0f);
                }
                Renderer.RenderFrame(CaptureForVerification:Frame==2);
            }
            return ReadPixel(Renderer.LastCapturedFrame!,Renderer.LastCapturedFrame!.Width/2,Renderer.LastCapturedFrame!.Height/2);
        }

        PixelColor RenderWithDirectionalLuxes(IReadOnlyList<float> Luxes){
            for(var Frame=0;Frame<3;Frame++){
                Renderer.SetCamera(Camera);
                Renderer.SetAmbientLight(Vector3.Zero);
                Renderer.SetObjectTransform(0,Matrix4x4.Identity);
                for(var LightIndex=0;LightIndex<Luxes.Count;LightIndex++){
                    Renderer.AddDirectionalLight(
                        -Vector3.UnitY,Vector3.One,IlluminanceLux:Luxes[LightIndex],
                        CastsShadows:false,
                        // Direct selection must ignore this deliberately adverse shadow priority.
                        ShadowPriority:LightIndex==Luxes.Count-1?-100:100,
                        ShadowBiasMeters:0f);
                }
                Renderer.RenderFrame(CaptureForVerification:Frame==2);
            }
            return ReadPixel(Renderer.LastCapturedFrame!,Renderer.LastCapturedFrame!.Width/2,Renderer.LastCapturedFrame!.Height/2);
        }

        PixelColor FourDark=RenderWithDirectionalLuxes([0f,0f,0f,0f]);
        PixelColor RenderWithSetThenAdd(){
            for(var Frame=0;Frame<3;Frame++){
                Renderer.SetCamera(Camera);
                Renderer.SetAmbientLight(Vector3.Zero);
                Renderer.SetObjectTransform(0,Matrix4x4.Identity);
                // Legacy setter is persistent on its own, but when Add follows in the same
                // frame it contributes as stable submission zero to that frame's additive list.
                Renderer.SetDirectionalLight(
                    Vector3.UnitY,Vector3.One,0.4f,CastsShadows:false,
                    ShadowBiasMeters:0f,ShadowPriority:100);
                Renderer.AddDirectionalLight(
                    -Vector3.UnitY,Vector3.One,IlluminanceLux:0.4f,
                    CastsShadows:false,ShadowPriority:-100,ShadowBiasMeters:0f);
                Renderer.RenderFrame(CaptureForVerification:Frame==2);
            }
            return ReadPixel(Renderer.LastCapturedFrame!,Renderer.LastCapturedFrame!.Width/2,Renderer.LastCapturedFrame!.Height/2);
        }

        PixelColor FifthDominant=RenderWithDirectionalLuxes([0f,0f,0f,0f,1.5f]);
        PixelColor OneDirectional=RenderWithDirectionalCount(1);
        PixelColor SetThenAdd=RenderWithSetThenAdd();
        PixelColor FourDirectionals=RenderWithDirectionalCount(StageRenderer.MaxDirectionalLights);
        int OneSum=OneDirectional.R+OneDirectional.G+OneDirectional.B;
        int SetThenAddSum=SetThenAdd.R+SetThenAdd.G+SetThenAdd.B;
        int FourSum=FourDirectionals.R+FourDirectionals.G+FourDirectionals.B;

        Assert.True(
            FourSum>OneSum+100,
            $"Expected four additive physical directional lights to brighten the center pixel: one={OneDirectional} (sum={OneSum}), four={FourDirectionals} (sum={FourSum}).");
        Assert.Equal(0,Renderer.DroppedDirectionalLightsLastFrame);

        Assert.True(SetThenAddSum>OneSum+30,
            $"Expected legacy SetDirectionalLight plus same-frame AddDirectionalLight to keep both lights: one={OneDirectional} (sum={OneSum}), mixed={SetThenAdd} (sum={SetThenAddSum}).");

        _=RenderWithDirectionalCount(StageRenderer.MaxDirectionalLights+1);
        Assert.Equal(1,Renderer.DroppedDirectionalLightsLastFrame);
        int FourDarkSum=FourDark.R+FourDark.G+FourDark.B;
        int FifthDominantSum=FifthDominant.R+FifthDominant.G+FifthDominant.B;
        Assert.True(FifthDominantSum>FourDarkSum+100,
            $"Expected the fifth, highest-contribution Directional to replace a lower-contribution input regardless of its shadow priority: dark={FourDark} (sum={FourDarkSum}), selected={FifthDominant} (sum={FifthDominantSum}).");
        Assert.Equal(1,Renderer.DroppedDirectionalLightsLastFrame);
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static void RunBothFrameSlotsIdenticalScenario(){
        var Box=ProceduralMeshFactory.CreateBox(new Vector3(1.5f,1.5f,1.5f));

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

        using var Renderer=StageRenderer.Create(
                                                Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderArtifactPath,
                                                [Mesh],[Material],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);

        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));

        Camera Camera=Camera.CreateDefault(Vector3.Zero);
        var LightDirection=Vector3.Normalize(new Vector3(0.3f,0.8f,0.4f));

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
        var FirstSlotFrame=Renderer.LastCapturedFrame!;

        PushPerFrameState();
        Renderer.RenderFrame(CaptureForVerification:true);
        var SecondSlotFrame=Renderer.LastCapturedFrame!;

        Assert.Equal(FirstSlotFrame.Width,SecondSlotFrame.Width);
        Assert.Equal(FirstSlotFrame.Height,SecondSlotFrame.Height);
        Assert.Equal(FirstSlotFrame.RowPitch,SecondSlotFrame.RowPitch);

        var MismatchedPixels=0;
        const int Tolerance=2;
        for(var Y=0;Y<FirstSlotFrame.Height;Y++){
            for(var X=0;X<FirstSlotFrame.Width;X++){
                var A=ReadPixel(FirstSlotFrame,X,Y);
                var B=ReadPixel(SecondSlotFrame,X,Y);
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
        var Ground=ProceduralMeshFactory.CreatePlane(Width:16f,Depth:16f);
        var Blocker=ProceduralMeshFactory.CreateBox(new Vector3(3f,3f,3f));

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

        using var Renderer=StageRenderer.Create(
                                                Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderArtifactPath,
                                                [GroundMesh,BlockerMesh],[Material],
                                                [
                                                    new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }, // ground, object 0
                                                    new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 }, // blocker, object 1
                                                ]);

        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));

        // Nearly-top-down orbit camera targeting the world origin; the center pixel is the ground at
        // the origin. The light arrives at an angle and the blocker hangs beside the origin, so its
        // shadow lands on that visible ground point instead of the blocker hiding it from the camera
        // (an earlier revision sampled the blocker's own top face, which only looked "shadowed"
        // while the shadow camera sat below the scene).
        Camera Camera=Camera.CreateDefault(Vector3.Zero);
        Camera.Pitch=-89f*MathF.PI/180f;
        Camera.Distance=20f;
        Camera.FarZ=200f;

        var LightDirection=Vector3.Normalize(new Vector3(0.6f,1f,0f));
        Vector3 FarAway=new(500f,500f,500f);
        // Toward-light (0.6,1,0): a point at height 4 shadows the ground 2.4 m back along -X.
        Vector3 AboveOrigin=new(2.4f,4f,0f);

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

        var Baseline=RenderCenterWithBlockerAt(FarAway);
        var Shadowed=RenderCenterWithBlockerAt(AboveOrigin);

        var BaselineSum=Baseline.R+Baseline.G+Baseline.B;
        var ShadowedSum=Shadowed.R+Shadowed.G+Shadowed.B;
        Assert.True(
            ShadowedSum<BaselineSum-15,
            $"Expected the ground pixel directly beneath the blocker to be measurably darker than the same pixel with the blocker moved away (i.e. the shadow map reflects the light's viewpoint): baseline={Baseline} (sum={BaselineSum}), shadowed={Shadowed} (sum={ShadowedSum}).");

        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static void RunSpotShadowCastingScenario(){
        var Ground=ProceduralMeshFactory.CreatePlane(Width:16f,Depth:16f);
        var Blocker=ProceduralMeshFactory.CreateBox(new Vector3(3f,3f,3f));

        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Spot Shadow Atlas GPU Test",
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

        StageMeshCreateInfo GroundMesh=new(){
            Positions=Ground.Positions,Normals=Ground.Normals,Tangents=GroundTangents,
            Uvs=GroundUvs,Indices=Ground.Indices,
        };
        StageMeshCreateInfo BlockerMesh=new(){
            Positions=Blocker.Positions,Normals=Blocker.Normals,Tangents=BlockerTangents,
            Uvs=BlockerUvs,Indices=Blocker.Indices,
        };
        StageMaterialCreateInfo Material=new(){
            BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f,
        };

        using var Renderer=StageRenderer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderArtifactPath,
            [GroundMesh,BlockerMesh],[Material],
            [
                new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },
                new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 },
            ]);
        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));

        // Aim at the off-axis projected shadow, not the blocker itself, so the capture sees the
        // ground receiver rather than the blocker's lit top face.
        Vector3 ShadowReceiver=new(-4f,0f,-4f);
        Camera Camera=Camera.CreateDefault(ShadowReceiver);
        Camera.Pitch=-89f*MathF.PI/180f;
        Camera.Distance=20f;
        Camera.FarZ=200f;
        Vector3 FarAway=new(500f,500f,500f);
        Vector3 AboveOrigin=new(0f,4f,0f);

        PixelReadbackResult RenderWithBlockerAt(Vector3 BlockerPosition){
            for(var I=0;I<3;I++){
                Renderer.SetCamera(Camera);
                Renderer.SetAmbientLight(new Vector3(0.03f,0.03f,0.03f));
                // This zero-intensity caster intentionally reserves the plan's directional tile 0,
                // so the physical Spot must use the plan-selected tile 1 rather than a hard-coded UV.
                Renderer.SetDirectionalLight(Vector3.UnitY,Vector3.Zero,0f,CastsShadows:true,ShadowBiasMeters:0.001f);
                Renderer.AddSpotLight(
                    Position:new Vector3(4f,8f,4f),LightForward:Vector3.Normalize(new Vector3(-4f,-8f,-4f)),LinearRgb:Vector3.One,
                    LuminousFluxLumens:200f,RangeMeters:20f,
                    InnerConeFullAngleDegrees:70f,OuterConeFullAngleDegrees:90f,
                    CastsShadows:true,ShadowPriority:0,ShadowBiasMeters:0.001f);
                Renderer.SetObjectTransform(0,Matrix4x4.Identity);
                Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(BlockerPosition));
                Renderer.RenderFrame(CaptureForVerification:I==2);
            }
            return Renderer.LastCapturedFrame!;
        }

        PixelReadbackResult UnoccludedFrame=RenderWithBlockerAt(FarAway);
        PixelReadbackResult ShadowedFrame=RenderWithBlockerAt(AboveOrigin);
        PixelColor UnoccludedReceiver=ReadWorldPixel(UnoccludedFrame,Camera,ShadowReceiver);
        PixelColor ShadowedReceiver=ReadWorldPixel(ShadowedFrame,Camera,ShadowReceiver);
        int UnoccludedSum=UnoccludedReceiver.R+UnoccludedReceiver.G+UnoccludedReceiver.B;
        int ShadowedSum=ShadowedReceiver.R+ShadowedReceiver.G+ShadowedReceiver.B;
        Assert.True(
            ShadowedSum<UnoccludedSum-15,
            $"Expected the physical Spot's plan-selected atlas tile to darken its projected ground receiver against a separate unoccluded readback: shadowed={ShadowedReceiver} (sum={ShadowedSum}), unoccluded={UnoccludedReceiver} (sum={UnoccludedSum}).");
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }
    private static void RunPointShadowCastingScenario(){
        var Ground=ProceduralMeshFactory.CreatePlane(Width:16f,Depth:16f);
        var Blocker=ProceduralMeshFactory.CreateBox(new Vector3(3f,3f,3f));

        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Point Shadow Atlas GPU Test",
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

        StageMeshCreateInfo GroundMesh=new(){
            Positions=Ground.Positions,Normals=Ground.Normals,Tangents=GroundTangents,
            Uvs=GroundUvs,Indices=Ground.Indices,
        };
        StageMeshCreateInfo BlockerMesh=new(){
            Positions=Blocker.Positions,Normals=Blocker.Normals,Tangents=BlockerTangents,
            Uvs=BlockerUvs,Indices=Blocker.Indices,
        };
        StageMaterialCreateInfo Material=new(){
            BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f,
        };

        using var Renderer=StageRenderer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderArtifactPath,
            [GroundMesh,BlockerMesh],[Material],
            [
                new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },
                new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 },
            ]);
        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));

        // The light-to-receiver segment passes through BlockerPosition. This receiver lies on the
        // Point's -X major axis (cube face 1). Reserving directional tile 0 means the Point's
        // six-face allocation starts at tile 1, and the face-1 sample must therefore use the
        // plan's tile 2 rather than a fixed tile or face-0 projection.
        Vector3 ShadowReceiver=new(-6f,0f,-3f);
        Camera Camera=Camera.CreateDefault(ShadowReceiver);
        Camera.Pitch=-89f*MathF.PI/180f;
        Camera.Distance=20f;
        Camera.FarZ=200f;
        Vector3 FarAway=new(500f,500f,500f);
        Vector3 BlockerPosition=new(0f,3f,0f);

        PixelReadbackResult RenderWithBlockerAt(Vector3 CurrentBlockerPosition){
            for(var I=0;I<3;I++){
                Renderer.SetCamera(Camera);
                Renderer.SetAmbientLight(new Vector3(0.03f,0.03f,0.03f));
                // Reserve the directional tile so the test proves PhysicalShadowAtlasPlan's
                // Point allocation and UV metadata are honored rather than hard-coded.
                Renderer.SetDirectionalLight(Vector3.UnitY,Vector3.Zero,0f,CastsShadows:true,ShadowBiasMeters:0.001f);
                Renderer.AddPointLight(
                    Position:new Vector3(6f,6f,3f),LinearRgb:Vector3.One,
                    LuminousFluxLumens:1200f,RangeMeters:30f,
                    CastsShadows:true,ShadowPriority:0,ShadowBiasMeters:0.001f);
                Renderer.SetObjectTransform(0,Matrix4x4.Identity);
                Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(CurrentBlockerPosition));
                Renderer.RenderFrame(CaptureForVerification:I==2);
            }
            return Renderer.LastCapturedFrame!;
        }

        PixelReadbackResult UnoccludedFrame=RenderWithBlockerAt(FarAway);
        PixelReadbackResult ShadowedFrame=RenderWithBlockerAt(BlockerPosition);
        PixelColor UnoccludedReceiver=ReadWorldPixel(UnoccludedFrame,Camera,ShadowReceiver);
        PixelColor ShadowedReceiver=ReadWorldPixel(ShadowedFrame,Camera,ShadowReceiver);
        int UnoccludedSum=UnoccludedReceiver.R+UnoccludedReceiver.G+UnoccludedReceiver.B;
        int ShadowedSum=ShadowedReceiver.R+ShadowedReceiver.G+ShadowedReceiver.B;
        Assert.True(
            ShadowedSum<UnoccludedSum-15,
            $"Expected the physical Point's plan-selected cube face/tile to darken its projected ground receiver against a separate unoccluded readback: shadowed={ShadowedReceiver} (sum={ShadowedSum}), unoccluded={UnoccludedReceiver} (sum={UnoccludedSum}).");
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static void RunPointShadowOverflowKeepsDirectLightScenario(){
        var Ground=ProceduralMeshFactory.CreatePlane(Width:16f,Depth:16f);
        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Point Shadow Direct-Light GPU Test",
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
        StageMeshCreateInfo GroundMesh=new(){
            Positions=Ground.Positions,Normals=Ground.Normals,Tangents=GroundTangents,
            Uvs=GroundUvs,Indices=Ground.Indices,
        };
        StageMaterialCreateInfo Material=new(){
            BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f,
        };

        using var Renderer=StageRenderer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderArtifactPath,
            [GroundMesh],[Material],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));

        Camera Camera=Camera.CreateDefault(Vector3.Zero);
        Camera.Pitch=-89f*MathF.PI/180f;
        Camera.Distance=20f;
        Camera.FarZ=200f;

        PixelColor RenderCenter(bool IncludeNonSelectedPoint){
            for(var I=0;I<3;I++){
                Renderer.SetCamera(Camera);
                Renderer.SetAmbientLight(Vector3.Zero);
                Renderer.SetObjectTransform(0,Matrix4x4.Identity);
                // The first caster owns the Point budget through ShadowPriority but has zero
                // radiance. The second caster must retain direct illumination even though it has
                // no six-face allocation and therefore receives metadata index -1 in the shader.
                Renderer.AddPointLight(
                    Position:new Vector3(6f,6f,3f),LinearRgb:Vector3.Zero,
                    LuminousFluxLumens:0f,RangeMeters:30f,
                    CastsShadows:true,ShadowPriority:10,ShadowBiasMeters:0.001f);
                if(IncludeNonSelectedPoint){
                    Renderer.AddPointLight(
                        Position:new Vector3(0f,6f,0f),LinearRgb:Vector3.One,
                        LuminousFluxLumens:1200f,RangeMeters:20f,
                        CastsShadows:true,ShadowPriority:0,ShadowBiasMeters:0.001f);
                }
                Renderer.RenderFrame(CaptureForVerification:I==2);
            }
            PixelReadbackResult Frame=Assert.IsType<PixelReadbackResult>(Renderer.LastCapturedFrame);
            return ReadPixel(Frame,Frame.Width/2,Frame.Height/2);
        }

        PixelColor AtlasOwnerOnly=RenderCenter(IncludeNonSelectedPoint:false);
        PixelColor WithNonSelectedDirectPoint=RenderCenter(IncludeNonSelectedPoint:true);
        int OwnerOnlySum=AtlasOwnerOnly.R+AtlasOwnerOnly.G+AtlasOwnerOnly.B;
        int WithNonSelectedSum=WithNonSelectedDirectPoint.R+WithNonSelectedDirectPoint.G+WithNonSelectedDirectPoint.B;
        Assert.True(
            WithNonSelectedSum>OwnerOnlySum+15,
            $"Expected a Point light that loses the one-light shadow atlas budget to continue direct lighting: owner-only={AtlasOwnerOnly} (sum={OwnerOnlySum}), with-non-selected={WithNonSelectedDirectPoint} (sum={WithNonSelectedSum}).");
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }


    private static PixelColor ReadWorldPixel(PixelReadbackResult Readback,in Camera Camera,Vector3 WorldPosition){
        Matrix4x4 ViewProjection=Camera.GetView()*Camera.GetProjection((float)Readback.Width/Readback.Height);
        Vector4 Clip=Vector4.Transform(new Vector4(WorldPosition,1f),ViewProjection);
        Assert.True(Clip.W>1e-4f,$"Expected {WorldPosition} to be in front of the camera.");
        float NdcX=Clip.X/Clip.W;
        float NdcY=Clip.Y/Clip.W;
        int X=(int)MathF.Round((NdcX*0.5f+0.5f)*(Readback.Width-1));
        int Y=(int)MathF.Round((-NdcY*0.5f+0.5f)*(Readback.Height-1));
        Assert.InRange(X,0,Readback.Width-1);
        Assert.InRange(Y,0,Readback.Height-1);
        return ReadPixel(Readback,X,Y);
    }

    private static PixelColor ReadPixel(PixelReadbackResult Readback,int X,int Y){
        const int BytesPerPixel=4;
        var Offset=((long)Y*Readback.RowPitch)+(X*BytesPerPixel);
        return new PixelColor(Readback.Pixels[Offset],Readback.Pixels[Offset+1],Readback.Pixels[Offset+2],Readback.Pixels[Offset+3]);
    }
}
