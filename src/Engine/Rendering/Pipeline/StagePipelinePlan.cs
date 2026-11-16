namespace CSharpGameEngine.Rendering.Pipeline;
/// <summary>
/// Stage の 1 フレームのパス（ADR-0087 §1-4）。L0 の実行器は <see cref="StagePipelinePlan.Passes"/> の順に記録する。
/// 各パスの中の「準備ができていなければ何もしない」判定（Lumen が使えるか、スキニングするメッシュがあるかなど）は実行器が持つ。
/// </summary>
public enum StagePass {
    /// <summary>スキニングの compute。</summary>
    Skinning,
    /// <summary>物体の表と材質の表を、このフレームの値で書く（ADR-0100 決定 2）。物体を描くどのパスよりも前。</summary>
    SceneUpload,
    /// <summary>Lumen を使うかの確定、影の時間の読み取り、光源と局所光の影の準備。</summary>
    FramePreparation,
    /// <summary>局所光の影のアトラス。</summary>
    ShadowAtlas,
    /// <summary>仮想影の準備と、影の時間の記録。</summary>
    VirtualShadows,
    /// <summary>フレームの定数（環境光、霧、深度、カメラプレビュー、露出、環境）。</summary>
    FrameConstants,
    /// <summary>自動露出の初期化（自動露出でなければ初期化済みの印を消す）。</summary>
    AutoExposureInitialize,
    /// <summary>グローバル距離場の更新。</summary>
    GlobalDistanceField,
    /// <summary>GI のプローブの撮影。</summary>
    GlobalIllumination,
    /// <summary>Lumen の GI と反射。</summary>
    Lumen,
    /// <summary>アンビエントオクルージョン。</summary>
    AmbientOcclusion,
    /// <summary>主パス（HDR の色と距離と深度を消して、物体を描く）。</summary>
    MainPass,
    /// <summary>影の比較表示の主パス（左右で影の方式を変えて描く）。</summary>
    ShadowComparison,
    /// <summary>空の合成。</summary>
    SkyComposite,
    /// <summary>自動露出の測光。</summary>
    ExposureMetering,
    /// <summary>Lumen の次フレーム用のシーン色の写しと、Lumen の可視化。</summary>
    LumenSceneCopy,
    /// <summary>主パスの後に compute が走ったら、主パスの束縛を戻す。</summary>
    RestoreMainBindings,
    /// <summary>カメラプレビュー。</summary>
    CameraPreviews,
    /// <summary>トーンマップとブルームでバックバッファへ描く。</summary>
    PostProcess,
    /// <summary>オーバーレイ。</summary>
    Overlay,
    /// <summary>画面レイヤー。</summary>
    ScreenLayer,
    /// <summary>検証用にバックバッファを読み戻しへ写し、表示できる状態にする。</summary>
    Capture,
    /// <summary>バックバッファを表示できる状態にする。</summary>
    PresentTransition,
}
/// <summary>計画を決める、描画面の今の状態。</summary>
/// <param name="PresentationMode">表示モード。</param>
/// <param name="HasSelection">選択の強調をする物体があるか。</param>
/// <param name="ShadowComparison">影の比較表示か。</param>
/// <param name="Atmosphere">物理空を合成するか。</param>
/// <param name="AutoExposure">自動露出か。</param>
public readonly record struct StageFrameInputs(RenderPresentationMode PresentationMode,bool HasSelection,bool ShadowComparison,bool Atmosphere,bool AutoExposure);
/// <summary>計画のキャッシュのキー。</summary>
/// <param name="Inputs">描画面の今の状態。</param>
/// <param name="Capture">検証用にフレームを読み戻すか。</param>
public readonly record struct StagePipelinePlanKey(StageFrameInputs Inputs,bool Capture);
/// <summary>1 フレームで実行するパスの列と、主パスの PSO。</summary>
public sealed class StagePipelinePlan {
    internal StagePipelinePlan(StagePipelinePlanKey Key,StagePass[]Passes,StagePipelineDesc MainPipeline,RenderGraph Graph){
        this.Key=Key;
        this.Passes=Passes;
        this.MainPipeline=MainPipeline;
        this.Graph=Graph;
    }
    public StagePipelinePlanKey Key { get; }
    /// <summary>実行する順のパス。</summary>
    public IReadOnlyList<StagePass>Passes { get; }
    /// <summary>主パスと、主パスの後に戻す束縛の PSO。</summary>
    public StagePipelineDesc MainPipeline { get; }
    /// <summary>パスが読み書きする資源を宣言して検証したグラフ（診断用）。</summary>
    public RenderGraph Graph { get; }
}
/// <summary>
/// Stage のフレーム計画を作る（ADR-0087 §1-4 の StagePipelinePlanner）。M0120 の移行で、L0 の <c>StageRenderer.RenderFrame</c> が持っていた
/// パスの順と、主パスの PSO の選び方を移した。計画はキーごとに作り直さずに使い回す。
/// </summary>
public static class StagePipelinePlanner {
    private static readonly Lock Gate=new();
    private static readonly Dictionary<StagePipelinePlanKey,StagePipelinePlan>Cache=[];
    /// <summary>キーの計画を返す。同じキーなら同じインスタンス。</summary>
    public static StagePipelinePlan Build(in StagePipelinePlanKey Key){
        lock(Gate){
            if(Cache.TryGetValue(Key,out var Cached)){ return Cached; }
            var Created=Create(Key);
            Cache.Add(Key,Created);
            return Created;
        }
    }
    /// <summary>主パスの PSO。ワイヤーフレームならワイヤーフレーム、照明以外の表示か選択があれば表示用、それ以外は照明済み。</summary>
    public static StagePipelineDesc SelectMainPipeline(in StageFrameInputs Inputs)=>
        Inputs.PresentationMode==RenderPresentationMode.Wireframe?StagePipelines.Wireframe
        :Inputs.PresentationMode!=RenderPresentationMode.Lit||Inputs.HasSelection?StagePipelines.Presentation
        :StagePipelines.Lit;
    private static StagePipelinePlan Create(in StagePipelinePlanKey Key){
        var Inputs=Key.Inputs;
        List<StagePass>Passes=[
            StagePass.Skinning,
            StagePass.SceneUpload,
            StagePass.FramePreparation,
            StagePass.ShadowAtlas,
            StagePass.VirtualShadows,
            StagePass.FrameConstants,
            StagePass.AutoExposureInitialize,
            StagePass.GlobalDistanceField,
            StagePass.GlobalIllumination,
            StagePass.Lumen,
            StagePass.AmbientOcclusion,
            Inputs.ShadowComparison?StagePass.ShadowComparison:StagePass.MainPass,
        ];
        if(Inputs.Atmosphere){ Passes.Add(StagePass.SkyComposite); }
        if(Inputs.AutoExposure){ Passes.Add(StagePass.ExposureMetering); }
        Passes.AddRange([
            StagePass.LumenSceneCopy,
            StagePass.RestoreMainBindings,
            StagePass.CameraPreviews,
            StagePass.PostProcess,
            StagePass.Overlay,
            StagePass.ScreenLayer,
            Key.Capture?StagePass.Capture:StagePass.PresentTransition,
        ]);
        var Array=Passes.ToArray();
        return new StagePipelinePlan(Key,Array,SelectMainPipeline(Inputs),Declare(Array));
    }
    /// <summary>パスが読み書きする資源を宣言し、宣言した順に実行されることを依存で固定して検証する。大きさは診断用の 1×1。</summary>
    private static RenderGraph Declare(StagePass[]Passes){
        RenderGraphBuilder Builder=new();
        var Texture=RenderResourceDescriptor.Texture(1,1,"rgba16f");
        var ShadowAtlas=Builder.Import<StageShadowAtlasResource>("ShadowAtlas",RenderResourceDescriptor.Texture(1,1,"d32"));
        var LumenHistory=Builder.Import<StageLumenHistoryResource>("LumenHistory",Texture);
        var Exposure=Builder.Import<StageExposureResource>("Exposure",RenderResourceDescriptor.Buffer(16));
        var HdrColor=Builder.Import<StageHdrColorResource>("HdrColor",Texture);
        var SceneDistance=Builder.Import<StageSceneDistanceResource>("SceneDistance",RenderResourceDescriptor.Texture(1,1,"r32f"));
        var Depth=Builder.Import<StageDepthResource>("Depth",RenderResourceDescriptor.Texture(1,1,"d32"));
        var BackBuffer=Builder.Import<StageBackBufferResource>("BackBuffer",RenderResourceDescriptor.Texture(1,1,"rgba8"));
        RenderPassHandle?Previous=null;
        foreach(var Pass in Passes){
            var Handle=Builder.AddPass(Pass.ToString(),Setup=>{
                switch(Pass){
                    case StagePass.ShadowAtlas:
                    case StagePass.VirtualShadows:
                        Setup.ReadWrite(ShadowAtlas);
                        break;
                    case StagePass.AutoExposureInitialize:
                    case StagePass.ExposureMetering:
                        Setup.ReadWrite(Exposure);
                        if(Pass==StagePass.ExposureMetering){ Setup.Read(HdrColor); }
                        break;
                    case StagePass.Lumen:
                        Setup.ReadWrite(LumenHistory);
                        break;
                    case StagePass.MainPass:
                    case StagePass.ShadowComparison:
                        Setup.Read(ShadowAtlas);
                        Setup.Read(LumenHistory);
                        Setup.Write(HdrColor);
                        Setup.Write(SceneDistance);
                        Setup.Write(Depth);
                        break;
                    case StagePass.SkyComposite:
                        Setup.Read(SceneDistance);
                        Setup.ReadWrite(HdrColor);
                        break;
                    case StagePass.LumenSceneCopy:
                        Setup.Read(HdrColor);
                        Setup.ReadWrite(LumenHistory);
                        break;
                    case StagePass.CameraPreviews:
                        Setup.ReadWrite(HdrColor);
                        Setup.ReadWrite(Depth);
                        break;
                    case StagePass.PostProcess:
                        Setup.Read(HdrColor);
                        Setup.Write(BackBuffer);
                        break;
                    case StagePass.Overlay:
                    case StagePass.ScreenLayer:
                    case StagePass.Capture:
                    case StagePass.PresentTransition:
                        Setup.ReadWrite(BackBuffer);
                        break;
                }
                if(Previous is{ }Before){ Setup.DependsOn(Before); }
            });
            Previous=Handle;
        }
        return Builder.Compile();
    }
    private readonly struct StageShadowAtlasResource;
    private readonly struct StageLumenHistoryResource;
    private readonly struct StageExposureResource;
    private readonly struct StageHdrColorResource;
    private readonly struct StageSceneDistanceResource;
    private readonly struct StageDepthResource;
    private readonly struct StageBackBufferResource;
}
