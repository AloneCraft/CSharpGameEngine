using Xunit;
using CSharpGameEngine.Rendering.Pipeline;
using CSharpGameEngine.ShaderArtifacts;
namespace CSharpGameEngine.Rendering.Tests;
/// <summary>L4 の PSO の一覧（ADR-0087 §1-4）。L0 はこの一覧どおりに PSO を作るので、入口名と組み合わせの誤りはここで見つける。</summary>
public sealed class StagePipelinesTests {
    [Fact]
    public void EveryPipelineNamesAnAbiEntryOfTheRightStage(){
        var Entries=ShaderArtifactAbi.GetEntries(ShaderArtifactTargetKind.Stage).ToDictionary(static Entry=>Entry.LogicalName,static Entry=>Entry.Stage);
        foreach(var Desc in StagePipelines.All){
            Assert.True(Entries.TryGetValue(Desc.VertexEntry,out var VertexStage),Desc.VertexEntry);
            Assert.Equal(ShaderArtifactEntryStage.Vertex,VertexStage);
            if(Desc.PixelEntry is{ }PixelEntry){
                Assert.True(Entries.TryGetValue(PixelEntry,out var PixelStage),PixelEntry);
                Assert.Equal(ShaderArtifactEntryStage.Pixel,PixelStage);
            }
        }
    }
    [Fact]
    public void EveryStageEntryIsUsedByAPipeline(){
        var Used=StagePipelines.All.SelectMany(static Desc=>new[]{Desc.VertexEntry,Desc.PixelEntry}).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var Unused=ShaderArtifactAbi.GetEntries(ShaderArtifactTargetKind.Stage).Select(static Entry=>Entry.LogicalName).Where(Name=>!Used.Contains(Name)).ToArray();
        Assert.Empty(Unused);
    }
    [Fact]
    public void PipelinesAreDistinctValuesAndEqualByValue(){
        Assert.Equal(StagePipelines.All.Count,StagePipelines.All.Distinct().Count());
        var Copy=StagePipelines.Lit with{ };
        Assert.Equal(StagePipelines.Lit,Copy);
        Assert.NotEqual(StagePipelines.Presentation,StagePipelines.Wireframe);
    }
    [Fact]
    public void OnlyTheShadowAtlasIsDepthOnly(){
        var DepthOnly=StagePipelines.All.Where(static Desc=>Desc.PixelEntry is null).ToArray();
        Assert.Equal([StagePipelines.Shadow,StagePipelines.VirtualGeometryShadow],DepthOnly);
        Assert.All(DepthOnly,static Desc=>{
            Assert.Equal(StageRenderTargets.None,Desc.Targets);
            Assert.Equal(StageDepthTarget.ShadowAtlas,Desc.Depth);
        });
    }
    /// <summary>仮想ジオメトリの版は、頂点シェーダーと頂点の入力のほかは元の設定と同じ（同じ画素シェーダー・描き込む先・深さ）。版は VSMain と VSShadowMain にだけある。</summary>
    [Fact]
    public void VirtualGeometryVariantsOnlySwapTheVertexStage(){
        foreach(var Desc in StagePipelines.All){
            var Variant=StagePipelines.VirtualGeometry(Desc);
            if(Desc.VertexEntry is not ("VSMain" or "VSShadowMain")){
                Assert.Null(Variant);
                continue;
            }
            Assert.NotNull(Variant);
            Assert.Equal(StageInputLayout.None,Variant.Value.InputLayout);
            Assert.Equal(Desc.VertexEntry=="VSMain"?StagePipelines.VirtualGeometryVertexEntry:StagePipelines.VirtualGeometryShadowVertexEntry,Variant.Value.VertexEntry);
            Assert.Equal(Desc with{ VertexEntry=Variant.Value.VertexEntry,InputLayout=StageInputLayout.None },Variant.Value);
        }
        Assert.Equal(StagePipelines.All.Count,StagePipelines.All.Distinct().Count());
    }
    [Fact]
    public void PreviewAndObserverViewsDoNotWriteTheSceneDistance(){
        StagePipelineDesc[]WithoutDistance=[StagePipelines.CameraPreview,StagePipelines.CameraPreviewWireframe,StagePipelines.Observer,StagePipelines.ObserverWireframe,
            StagePipelines.VirtualGeometryCameraPreview,StagePipelines.VirtualGeometryCameraPreviewWireframe];
        Assert.All(WithoutDistance,static Desc=>Assert.False(Desc.WriteSceneDistance));
        Assert.All(StagePipelines.All.Except(WithoutDistance),static Desc=>Assert.True(Desc.WriteSceneDistance));
    }
    [Fact]
    public void ScreenSpacePassesDrawOverTheBackBufferWithoutDepth(){
        Assert.All([StagePipelines.OverlayTriangles,StagePipelines.OverlayLines,StagePipelines.PostProcess,StagePipelines.Screen],static Desc=>{
            Assert.Equal(StageRenderTargets.BackBuffer,Desc.Targets);
            Assert.False(Desc.DepthTest);
        });
        Assert.Equal(StagePrimitiveTopology.Line,StagePipelines.OverlayLines.Topology);
        Assert.Equal(StageBlend.AlphaOverKeepingFrameAlpha,StagePipelines.Screen.Blend);
    }
}
