namespace CSharpGameEngine.Rendering.Pipeline;
/// <summary>Stage の描画パスが使うルート署名。L0 が D3D12 のルート署名へ対応付ける。</summary>
public enum StageRootSignature {
    /// <summary>物体の描画とオーバーレイ（フレーム・物体の定数、材質、光源、影、GI）。</summary>
    Main,
    /// <summary>影のアトラス（フレーム・物体の定数だけ）。</summary>
    Shadow,
    /// <summary>ポストプロセス（シーン色のテクスチャ 1 つと定数 4 つ）。</summary>
    PostProcess,
    /// <summary>画面レイヤー（ポストプロセスと同じ形で、頂点入力あり）。</summary>
    Screen,
}
/// <summary>頂点の入力の形式。</summary>
public enum StageInputLayout {
    /// <summary>頂点バッファを使わない（全画面の三角形）。</summary>
    None,
    /// <summary>位置・法線・接線・UV（Stage のメッシュの頂点）。</summary>
    Mesh,
    /// <summary>位置と色（オーバーレイ）。</summary>
    OverlayColor,
    /// <summary>画面上の位置・UV・色（画面レイヤー）。</summary>
    Screen,
}
/// <summary>三角形を塗るか、辺だけを描くか。</summary>
public enum StageFillMode {
    Solid,
    Wireframe,
}
/// <summary>図形の種類。</summary>
public enum StagePrimitiveTopology {
    Triangle,
    Line,
}
/// <summary>描き込む先の組。</summary>
public enum StageRenderTargets {
    /// <summary>色を書かない（深度だけ）。</summary>
    None,
    /// <summary>HDR のシーン色と、カメラからの距離。</summary>
    SceneColorAndDistance,
    /// <summary>Lumen の表面の前処理（法線と距離）。</summary>
    LumenSurface,
    /// <summary>Lumen のカード撮影（アルベド・法線・発光）。</summary>
    LumenCard,
    /// <summary>表示するバックバッファ。</summary>
    BackBuffer,
}
/// <summary>深度バッファ。</summary>
public enum StageDepthTarget {
    None,
    /// <summary>主パスの深度。</summary>
    Scene,
    /// <summary>影のアトラスの深度。</summary>
    ShadowAtlas,
}
/// <summary>色の混ぜ方。</summary>
public enum StageBlend {
    /// <summary>上書き。</summary>
    Opaque,
    /// <summary>ストレートアルファで重ね、アルファは「1 と 1−元のアルファ」で合成する（オーバーレイ）。</summary>
    AlphaOver,
    /// <summary>ストレートアルファで重ね、バックバッファのアルファはそのまま残す（画面レイヤー）。</summary>
    AlphaOverKeepingFrameAlpha,
}
/// <summary>
/// Stage の PSO（GPU へ渡す描き方の設定の束）を値で表す（ADR-0087 §1-4）。値が等しければ同じ PSO で、L0 の PSO のキャッシュのキーになる。
/// 入口名は Stage のシェーダー成果物のエントリー名（<c>ShaderArtifactAbi</c>）。
/// </summary>
/// <param name="VertexEntry">頂点シェーダーのエントリー名。</param>
/// <param name="PixelEntry">ピクセルシェーダーのエントリー名。深度だけのパスでは null。</param>
/// <param name="RootSignature">ルート署名。</param>
/// <param name="InputLayout">頂点の入力の形式。</param>
/// <param name="Fill">塗りか線か。</param>
/// <param name="Topology">図形の種類。</param>
/// <param name="Targets">描き込む先の組。</param>
/// <param name="WriteSceneDistance">シーン色と距離へ描くとき、距離も書くか。カメラプレビューと観察ビューは書かない。</param>
/// <param name="DepthClip">深度のクリップをするか。物体の描画は対数深度のため行わない。</param>
/// <param name="Depth">深度バッファ。</param>
/// <param name="DepthTest">深度の比較と書き込みをするか。</param>
/// <param name="Blend">色の混ぜ方。</param>
public readonly record struct StagePipelineDesc(
    string VertexEntry,
    string?PixelEntry,
    StageRootSignature RootSignature,
    StageInputLayout InputLayout,
    StageFillMode Fill,
    StagePrimitiveTopology Topology,
    StageRenderTargets Targets,
    bool WriteSceneDistance,
    bool DepthClip,
    StageDepthTarget Depth,
    bool DepthTest,
    StageBlend Blend);
/// <summary>Stage が使う PSO の一覧。L0 は起動時とシェーダー成果物の再読み込み時に、ここにあるものをすべて作る。</summary>
public static class StagePipelines {
    private static StagePipelineDesc Mesh(string PixelEntry,StageFillMode Fill=StageFillMode.Solid,string VertexEntry="VSMain",StageRenderTargets Targets=StageRenderTargets.SceneColorAndDistance,bool WriteSceneDistance=true)=>
        new(VertexEntry,PixelEntry,StageRootSignature.Main,StageInputLayout.Mesh,Fill,StagePrimitiveTopology.Triangle,Targets,WriteSceneDistance,DepthClip:false,StageDepthTarget.Scene,DepthTest:true,StageBlend.Opaque);
    private static StagePipelineDesc Overlay(StagePrimitiveTopology Topology)=>
        new("OverlayVSMain","OverlayPSMain",StageRootSignature.Main,StageInputLayout.OverlayColor,StageFillMode.Solid,Topology,StageRenderTargets.BackBuffer,WriteSceneDistance:true,DepthClip:true,StageDepthTarget.Scene,DepthTest:false,StageBlend.AlphaOver);
    /// <summary>照明済みの通常の描画。</summary>
    public static StagePipelineDesc Lit{get;}=Mesh("PSMain");
    /// <summary>表示モード（アルベド・法線など）と選択の強調。</summary>
    public static StagePipelineDesc Presentation{get;}=Mesh("PreviewPSMain");
    /// <summary>ワイヤーフレーム表示。</summary>
    public static StagePipelineDesc Wireframe{get;}=Mesh("PreviewPSMain",StageFillMode.Wireframe);
    /// <summary>カメラプレビュー（距離を書かない）。</summary>
    public static StagePipelineDesc CameraPreview{get;}=Mesh("PreviewCleanPSMain",WriteSceneDistance:false);
    /// <summary>カメラプレビューのワイヤーフレーム。</summary>
    public static StagePipelineDesc CameraPreviewWireframe{get;}=Mesh("PreviewCleanPSMain",StageFillMode.Wireframe,WriteSceneDistance:false);
    /// <summary>観察ビュー（距離を書かない）。</summary>
    public static StagePipelineDesc Observer{get;}=Mesh("PreviewCleanPSMain",VertexEntry:"ObserverVSMain",WriteSceneDistance:false);
    /// <summary>観察ビューのワイヤーフレーム。</summary>
    public static StagePipelineDesc ObserverWireframe{get;}=Mesh("PreviewCleanPSMain",StageFillMode.Wireframe,VertexEntry:"ObserverVSMain",WriteSceneDistance:false);
    /// <summary>Lumen の表面の前処理。</summary>
    public static StagePipelineDesc LumenSurface{get;}=Mesh("SurfacePrepassPSMain",Targets:StageRenderTargets.LumenSurface);
    /// <summary>Lumen のカード撮影。</summary>
    public static StagePipelineDesc LumenCard{get;}=Mesh("LumenCardPSMain",Targets:StageRenderTargets.LumenCard);
    /// <summary>オーバーレイの三角形。</summary>
    public static StagePipelineDesc OverlayTriangles{get;}=Overlay(StagePrimitiveTopology.Triangle);
    /// <summary>オーバーレイの線。</summary>
    public static StagePipelineDesc OverlayLines{get;}=Overlay(StagePrimitiveTopology.Line);
    /// <summary>影のアトラス（深度だけ）。</summary>
    public static StagePipelineDesc Shadow{get;}=new("VSShadowMain",null,StageRootSignature.Shadow,StageInputLayout.Mesh,StageFillMode.Solid,StagePrimitiveTopology.Triangle,StageRenderTargets.None,WriteSceneDistance:true,DepthClip:true,StageDepthTarget.ShadowAtlas,DepthTest:true,StageBlend.Opaque);
    /// <summary>トーンマップとブルーム。</summary>
    public static StagePipelineDesc PostProcess{get;}=new("FullscreenVS","PostProcessPS",StageRootSignature.PostProcess,StageInputLayout.None,StageFillMode.Solid,StagePrimitiveTopology.Triangle,StageRenderTargets.BackBuffer,WriteSceneDistance:true,DepthClip:true,StageDepthTarget.None,DepthTest:false,StageBlend.Opaque);
    /// <summary>画面レイヤー（文字と画像）。</summary>
    public static StagePipelineDesc Screen{get;}=new("ScreenVSMain","ScreenPSMain",StageRootSignature.Screen,StageInputLayout.Screen,StageFillMode.Solid,StagePrimitiveTopology.Triangle,StageRenderTargets.BackBuffer,WriteSceneDistance:true,DepthClip:true,StageDepthTarget.None,DepthTest:false,StageBlend.AlphaOverKeepingFrameAlpha);
    /// <summary>仮想ジオメトリの頂点シェーダーのエントリー名(ADR-0100 決定 3、M0130)。</summary>
    public const string VirtualGeometryVertexEntry="VirtualGeometryVSMain";
    /// <summary>
    /// 仮想ジオメトリで描く変種。頂点シェーダーだけを差し替え、頂点の入力を使わない(頂点は塊の番号と頂点の番号から構造化バッファで引く)。
    /// 画素シェーダーと描き込む先は元の設定のまま。
    /// </summary>
    public static StagePipelineDesc VirtualGeometry(in StagePipelineDesc Desc)=>Desc with{ VertexEntry=VirtualGeometryVertexEntry,InputLayout=StageInputLayout.None };
    /// <summary>照明済みの通常の描画の、仮想ジオメトリの変種。</summary>
    public static StagePipelineDesc VirtualGeometryLit{get;}=VirtualGeometry(Lit);
    /// <summary>表示モードの、仮想ジオメトリの変種。</summary>
    public static StagePipelineDesc VirtualGeometryPresentation{get;}=VirtualGeometry(Presentation);
    /// <summary>ワイヤーフレームの、仮想ジオメトリの変種。</summary>
    public static StagePipelineDesc VirtualGeometryWireframe{get;}=VirtualGeometry(Wireframe);
    /// <summary>作る PSO のすべて。重複は無い。</summary>
    public static IReadOnlyList<StagePipelineDesc>All{get;}=[Lit,Presentation,Wireframe,CameraPreview,CameraPreviewWireframe,Observer,ObserverWireframe,LumenSurface,LumenCard,OverlayTriangles,OverlayLines,Shadow,PostProcess,Screen,VirtualGeometryLit,VirtualGeometryPresentation,VirtualGeometryWireframe];
}
