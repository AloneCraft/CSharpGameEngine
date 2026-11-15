using CSharpGameEngine.Rendering.Pipeline;
namespace CSharpGameEngine.Rendering.Tests;
/// <summary>L4 のフレーム計画（M0120 単位 5a）。L0 の RenderFrame から移したパスの順と主パスの PSO の選び方を、GPU を使わずに確かめる。</summary>
public sealed class StagePipelinePlannerTests {
    private static readonly StageFrameInputs Plain=new(RenderPresentationMode.Lit,HasSelection:false,ShadowComparison:false,Atmosphere:false,AutoExposure:false);
    [Fact]
    public void APlainFrameRunsTheFixedOrderAndPresents(){
        var Plan=StagePipelinePlanner.Build(new StagePipelinePlanKey(Plain,Capture:false));
        Assert.Equal([
            StagePass.Skinning,StagePass.FramePreparation,StagePass.ShadowAtlas,StagePass.VirtualShadows,StagePass.FrameConstants,
            StagePass.AutoExposureInitialize,StagePass.GlobalDistanceField,StagePass.GlobalIllumination,StagePass.Lumen,StagePass.AmbientOcclusion,
            StagePass.MainPass,StagePass.LumenSceneCopy,StagePass.RestoreMainBindings,StagePass.CameraPreviews,StagePass.PostProcess,
            StagePass.Overlay,StagePass.ScreenLayer,StagePass.PresentTransition,
        ],Plan.Passes);
        Assert.Equal(StagePipelines.Lit,Plan.MainPipeline);
    }
    [Fact]
    public void SkyAndMeteringFollowTheMainPassInThatOrderAndCaptureReplacesPresent(){
        var Inputs=Plain with{ Atmosphere=true,AutoExposure=true,ShadowComparison=true };
        var Plan=StagePipelinePlanner.Build(new StagePipelinePlanKey(Inputs,Capture:true));
        var Passes=Plan.Passes.ToList();
        Assert.DoesNotContain(StagePass.MainPass,Passes);
        Assert.Equal(Passes.IndexOf(StagePass.ShadowComparison)+1,Passes.IndexOf(StagePass.SkyComposite));
        Assert.Equal(Passes.IndexOf(StagePass.SkyComposite)+1,Passes.IndexOf(StagePass.ExposureMetering));
        Assert.Equal(Passes.IndexOf(StagePass.ExposureMetering)+1,Passes.IndexOf(StagePass.LumenSceneCopy));
        Assert.Equal(StagePass.Capture,Passes[^1]);
        Assert.DoesNotContain(StagePass.PresentTransition,Passes);
    }
    [Fact]
    public void TheMainPipelineFollowsTheDisplayModeAndSelection(){
        Assert.Equal(StagePipelines.Lit,StagePipelinePlanner.SelectMainPipeline(Plain));
        Assert.Equal(StagePipelines.Presentation,StagePipelinePlanner.SelectMainPipeline(Plain with{ HasSelection=true }));
        Assert.Equal(StagePipelines.Presentation,StagePipelinePlanner.SelectMainPipeline(Plain with{ PresentationMode=RenderPresentationMode.BaseColor }));
        Assert.Equal(StagePipelines.Wireframe,StagePipelinePlanner.SelectMainPipeline(Plain with{ PresentationMode=RenderPresentationMode.Wireframe,HasSelection=true }));
    }
    [Fact]
    public void EveryPlanIsCachedAndItsGraphRunsThePassesInOrder(){
        foreach(var Mode in Enum.GetValues<RenderPresentationMode>()){
            for(var Flags=0;Flags<32;Flags++){
                var Key=new StagePipelinePlanKey(new StageFrameInputs(Mode,(Flags&1)!=0,(Flags&2)!=0,(Flags&4)!=0,(Flags&8)!=0),(Flags&16)!=0);
                var Plan=StagePipelinePlanner.Build(Key);
                Assert.Same(Plan,StagePipelinePlanner.Build(Key));
                Assert.Equal(Plan.Passes.Select(static Pass=>Pass.ToString()),Plan.Graph.Passes.Select(static Pass=>Pass.Name));
                Assert.Contains(Plan.MainPipeline,StagePipelines.All);
                Assert.Equal(Plan.Passes.Count,Plan.Passes.Distinct().Count());
            }
        }
    }
}
