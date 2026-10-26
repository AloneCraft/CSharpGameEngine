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
/// WindowsとD3D12による実際の複数オブジェクトStageレンダラーを端から端まで実行する（地面＋「キャラクター」代用のボックス、
/// 移動可能なカメラ、フレーム／オブジェクトごとの定数バッファを二重化）。物理GPUなしでCIでも決定的に実行できるよう、
/// WARPソフトウェアアダプターを使う（<see cref="ModelGpuIntegrationTests"/>と同じ理由）。
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
    /// 実際の不具合に対する回帰テスト：FrameCBのフレームスロットごとのstride（ConstantAlignment=512）が、
    /// 宣言されたHLSLサイズ（FrameConstantsSize=560、ShadowViewProjection追加後）より小さかった。
    /// そのためSetDirectionalLightのたびにWriteShadowViewProjectionが別の処理中スロットのViewProjection行列を破壊していた。
    /// さらにSetCameraを毎フレームではなく一度しか呼んでいなかった（別の不具合）。二重化された2スロットのうち一方には、実際のカメラが一度も設定されなかった。
    /// その結果、1フレームおきに破損／古いビュー投影行列で描画し、正しい画像と乱れた画像が交互に表示された。
    ///
    /// 上の4つのテストは、実際のフレームごとのECSシステムのように、RenderFrame連続呼び出しの間でSet*/Add*を呼ばない。
    /// また、書き込み先のスロットに戻る呼び出し回数で常にキャプチャするため、どちらの不具合も検出できない。
    /// このテストでは実際の使用パターン（毎フレームSetを呼び、FrameIndex 0と1の両方をキャプチャ）を再現し、
    /// シーンが変わらなければ両スロットから同一ピクセルの画像を描画するという一般的な不変条件を固定する。
    /// 特定のピクセル位置を固定しない、レイアウト非依存のテストなので、今後FrameCBが拡張されても有効である。
    /// </summary>
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_BothFrameInFlightSlots_RenderIdenticalImagesForAnUnchangingScene(){
        RunWithTimeout(RunBothFrameSlotsIdenticalScenario,TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// シャドウマップがカメラではなくライトの視点を反映することを確認する振る舞いテスト。
    /// ディレクショナルライトと地面の間に小さな遮蔽物を置き、その真下のピクセルが、遮蔽されていない別の地点より暗くなることを検証する。
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
        // 赤、青、法線マップをそれぞれ一度と、色空間ごとの共有白テクスチャ（M0110：ベースカラーのミップ平均はsRGB、metallic-roughnessはリニア）。
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
                    // 物理APIが受け取るのは光の伝播方向。そのため-Yはレンダラーの
                    // 従来の面から光源方向である+Yとなり、この水平面を照らす。
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
                        // 直接照明の選択では、意図的に不利なこのシャドウ優先度を無視する。
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
                // 従来のsetter単独では値を保持する。一方、同じ
                // フレーム内でAddを呼ぶと、安定した送信順0番目として、そのフレームの加算リストへ入る。
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

        // RuntimeSystem.csのLevelLoad／DirectionalLightシステムは、実運用でも各
        // フレームにこの一式を送る。上の4つのテストでは再現されず、そのためFrameCBスロットの破損や
        // 古いカメラの不具合を検出できなかった使用パターン。
        void PushPerFrameState(){
            Renderer.SetCamera(Camera);
            Renderer.SetAmbientLight(new Vector3(0.25f,0.25f,0.25f));
            Renderer.SetDirectionalLight(LightDirection,Vector3.One,2f);
            Renderer.SetObjectTransform(0,Matrix4x4.Identity);
        }

        // FrameCount=2。RenderFrameの連続呼び出しごとにFrameIndexが交互に変わるため、2回連続でキャプチャすると
        // 二重化された両スロットを検証できる。
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
                                                    new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }, // 地面、オブジェクト0
                                                    new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 }, // 遮蔽物、オブジェクト1
                                                ]);

        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));

        // ワールド原点を狙う、ほぼ真上からのオービットカメラ。中央ピクセルには原点の地面が映る。
        // 光は斜めから届き、遮蔽物は原点の脇に吊るす。影はカメラから遮蔽されず、見えている地面に
        // 落ちる（以前の版では遮蔽物の上面をサンプリングしていた。シーンの下にあったシャドウカメラから見た場合にだけ
        // 「影がある」ように見えていた）。
        //（直前の行コメントと結合して読む）。
        Camera Camera=Camera.CreateDefault(Vector3.Zero);
        Camera.Pitch=-89f*MathF.PI/180f;
        Camera.Distance=20f;
        Camera.FarZ=200f;

        var LightDirection=Vector3.Normalize(new Vector3(0.6f,1f,0f));
        Vector3 FarAway=new(500f,500f,500f);
        // 光源方向は(0.6,1,0)。高さ4の点から、地面上で-X方向へ2.4 m離れた位置に影ができる。
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

        // 遮蔽物自体ではなく、軸外に投影される影を狙う。そうすればキャプチャに映るのは
        // 明るい遮蔽物上面ではなく、地面の受光面。
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
                // この強度0のキャスターは、計画上のDirectionalタイル0を意図的に予約する。
                // そのため物理Spotライトは固定UVではなく、計画が選んだタイル1を使う必要がある。
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

        // ライトから受光点までの線分はBlockerPositionを通る。この受光点は
        // Pointライトの-X主軸（キューブ面1）上にある。Directionalタイル0を予約すると、Pointライトの
        // 6面割り当てはタイル1から始まるため、面1のサンプルには
        // 固定タイルや面0の投影ではなく、計画のタイル2を使う必要がある。
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
                // PhysicalShadowAtlasPlanによる
                // Point割り当てとUVメタデータが固定値ではなく尊重されることを検証するため、Directionalタイルを予約する。
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
                // 1つ目のキャスターはShadowPriorityでPoint予算を使うが、放射輝度は0。
                // 2つ目のキャスターには6面の割り当てがなく、シェーダーのメタデータインデックスは-1になるが、
                // 直接照明は引き続き受けなければならない。
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
