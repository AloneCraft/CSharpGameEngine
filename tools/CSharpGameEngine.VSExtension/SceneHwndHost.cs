using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// In-process D3D12 embedding (same pattern as <see cref="AssetPreviewHwndHost"/>, ADR-0013) for the
/// Scene Designer tool window (ADR-0040 Phase 9). Targets a specific <c>IScene</c>-implementing class
/// via <see cref="SetTarget"/>, which receives the <see cref="SceneDocumentSnapshot"/> that
/// <see cref="SceneEditorSessionService"/> publishes from live Roslyn (no build step), converts it via
/// <see cref="SceneModelRenderObjectBuilder"/> and renders every node via
/// <see cref="SceneRenderer"/>. Replaces the earlier <c>.scene</c>-JSON-based version of this class
/// (ADR-0020/ADR-0021), whose remaining write path was deleted in M0020 Phase 0.
///
/// <para>
/// This host owns the editing view state that must survive renderer re-creation: the
/// <see cref="SceneRenderer"/> is disposed and rebuilt on every re-analysis, so the camera lives here
/// and is pushed in through <see cref="SceneRenderer.SetCamera"/> (ADR-0041 decision 1). It is also
/// the only place that learns the real surface size, via <c>Surface.Resized</c>.
/// </para>
/// </summary>
internal sealed class SceneHwndHost:HwndHost {
    private string?RepoRoot;
    private string?TargetFilePath;
    private string?TargetClassName;
    private SceneModel?TargetModel;
    private SceneKey?TargetKey;
    private SceneDocumentSnapshot?TargetSnapshot;
    private NodePath?SelectedNodePath;
    private RenderSurfaceWindow?Surface;
    private SceneRenderer?Renderer;
    private LatestOnlyThreadWorker<SceneRenderBuildRequest,SceneRenderBuildResult>?BuildWorker;
    private EventHandler?RenderingHandler;
    private Action<int,int>?ResizeHandler;

    // 編集ビュー状態。Renderer は再解析ごとに破棄・再生成されるのでここに置く(ADR-0041 決定事項1)。
    private int SurfaceWidth=800;
    private int SurfaceHeight=600;
    private Vector3 SceneBoundsCenter;
    private float SceneBoundsRadius=1f;

    private float AspectRatio=>SurfaceWidth/(float)SurfaceHeight;

    internal event Action<string>?StatusChanged;

    public SceneHwndHost(){
        // Surfaceは描画先でありキーボード入力を持つコントロールではない。HwndHostの既定
        // TabInto経路を残すと、float/dockやマウスクリック後にVSのTab等がSurfaceへ入り、
        // WPFとWin32のフォーカスがずれてコードエディタの制御キーが効かなくなる。
        Focusable=false;
        KeyboardNavigation.SetIsTabStop(this,RenderSurfaceFocusPolicy.AllowTabInto);
    }

    protected override bool TabIntoCore(TraversalRequest Request)
        =>RenderSurfaceFocusPolicy.AllowTabInto;

    protected override HandleRef BuildWindowCore(HandleRef HwndParent){
        // Rendererが最初のPresentを行えるまで子HWNDを隠す。NOREDIRECTIONBITMAPの空Surfaceを
        // 表示すると、ドッキング前に同じ領域にいたSolution Explorerが残像のように露出する。
        Surface=new RenderSurfaceWindow(HwndParent.Handle,800,600,InitiallyVisible:false);
        SynchronizationContext UiContext=SynchronizationContext.Current
            ??new DispatcherSynchronizationContext(this.Dispatcher);
#pragma warning disable VSTHRD001 // 生workerの完了を非同期Postするだけで、同期wait/message pumpは行わない。
        BuildWorker=new LatestOnlyThreadWorker<SceneRenderBuildRequest,SceneRenderBuildResult>(
            Work:BuildRenderObjects,
            Dispatch:Callback=>UiContext.Post(_=>Callback(),null),
            Completed:this.OnBuildCompleted);
#pragma warning restore VSTHRD001
        LoadIntoSurface();
        return new HandleRef(this,Surface.Handle);
    }

    protected override void DestroyWindowCore(HandleRef Hwnd){
        BuildWorker?.Dispose();
        BuildWorker=null;
        DetachSubscriptions();
        DisposeRenderer();

        Surface?.Dispose();
        Surface=null;
    }

    /// <summary>
    /// Session が pin した snapshot を描画対象にします。解析中の blocking error は Last Known Good
    /// projection を保持するので、ここでrendererを空へ戻しません。
    /// </summary>
    internal void SetTarget(SceneDocumentSnapshot Snapshot){
        this.TargetKey=Snapshot.Key;
        this.TargetSnapshot=Snapshot;
        SceneModel?Model=Snapshot.LastKnownGoodModel??Snapshot.CurrentModel;
        if(Model is not null){
            this.TargetFilePath=Model.FilePath;
            this.TargetClassName=Model.ClassName;
            this.TargetModel=Model;
        }

        if(Surface is not null){
            Rebuild();
        }
    }

    /// <summary>EditorNodeIdを現在snapshotのNodePathへ解決して既存Rendererへ渡します。</summary>
    internal void SetSelection(SceneDocumentSnapshot Snapshot,SceneSelection Selection){
        if(this.TargetKey is not { } Key||!Key.Equals(Snapshot.Key)){
            return;
        }

        this.SelectedNodePath=Selection.PrimaryNodeId is { } Id
            &&Snapshot.Projection?.TryGetNode(Id,out SceneProjectionNode Node)==true
            ?Node.Path
            :null;
        Renderer?.SetSelectedNode(this.SelectedNodePath);
    }

    private void LoadIntoSurface(){
        if(Surface is null){
            return;
        }

        if(RenderingHandler is null){
            RenderingHandler=(_,_)=>{
                ApplyCamera();
                Renderer?.RenderFrame();
            };
            CompositionTarget.Rendering+=RenderingHandler;

            // Subscribed once here (not inside Rebuild, which runs on every re-analysis) - it always
            // resizes whatever Renderer currently holds via the field, so re-subscribing per rebuild
            // would just accumulate redundant duplicate calls per resize.
            // これはサーフェスの実サイズを知る唯一の場所でもあるので、アスペクト比の元もここで更新する。
            ResizeHandler=(W,H)=>{
                this.SurfaceWidth=Math.Max(1,W);
                this.SurfaceHeight=Math.Max(1,H);
                Renderer?.Resize(W,H);
                ApplyCamera();
            };
            Surface.Resized+=ResizeHandler;
        }

        Rebuild();
    }

    /// <summary>
    /// 現在のカメラを <see cref="Renderer"/> へ注入する。Phase 0 の値はバウンズ由来の固定フレーミング
    /// だが、M0020 Phase 1 でここが <c>SceneViewState</c> 由来へ差し替わる(<see cref="SceneRenderer"/>
    /// 側は無改変で済む唯一の継ぎ目)。値が変わらなければ <see cref="SceneRenderer.SetCamera"/> は
    /// 何もしないので毎フレーム呼んで構わない。
    /// </summary>
    private void ApplyCamera(){
        if(Renderer is not { } Active){
            return;
        }

        (Matrix4x4 View,Matrix4x4 Projection)=SceneCameraFraming.FrameBounds(
            this.SceneBoundsCenter,this.SceneBoundsRadius,SceneCameraFraming.DefaultFovYRadians,this.AspectRatio);
        Active.SetCamera(View,Projection);
    }

    /// <summary>(Re)builds <see cref="Renderer"/> from the pin 済み <see cref="SceneDocumentSnapshot"/>.</summary>
    private void Rebuild(){
        if(Surface is null||TargetKey is null||TargetSnapshot is null||TargetFilePath is null||TargetClassName is null){
            return;
        }

        if(TargetModel is not { } Model){
            // Not (yet) found - e.g. analysis hasn't caught up with a just-opened tool window, or
            // the class was renamed/deleted out from under us. Leave the last good frame on screen.
            SetStatus($"解析結果を待っています: {TargetClassName}");
            return;
        }

        if(Model.Root is null){
            DisposeRenderer();
            Microsoft.CodeAnalysis.Diagnostic?Diagnostic=Model.Diagnostics.FirstOrDefault();
            string Detail=Diagnostic is null
                ?"Rootを解析できませんでした。"
                :$"{Diagnostic.Id}: {Diagnostic.GetMessage()}";
            ActivityLog.LogError(nameof(SceneHwndHost),$"{TargetClassName}: {Detail}");
            SetStatus($"シーン解析エラー: {Detail}");
            return;
        }

        SetStatus($"読み込み中: {TargetClassName}");

        // 旧 .scene 実装の WarmMeshCache は、WPF の BuildWindowCore 内でJTF/Taskを同期wait
        // するデッドロックを避けるため、生ThreadへI/Oを隔離してからUI側でJoinしていた。
        // Scene DSL版ではさらに一歩進め、Primitive.Cli生成と後続のGLTF MeshLoaderを含む
        // Build全体をこの単一workerへ渡し、UIスレッドではJoin/Waitを一切行わない。完了後の
        // SceneRenderer生成だけをDispatcherへ戻す。live解析が連続してもworkerはlatest-winsで
        // 未処理要求を置換するため、ブリッジプロセスが無制限に増えることもない。
        BuildWorker?.Request(new SceneRenderBuildRequest(
            Model,
            TargetKey.Value,
            TargetSnapshot.AnalysisGeneration,
            TargetFilePath,
            TargetClassName,
            Surface.Handle));
    }

    private SceneRenderBuildResult BuildRenderObjects(SceneRenderBuildRequest Request){
        this.RepoRoot??=RepoLocator.ResolveRepoRoot();
        if(this.RepoRoot is null){
            throw new InvalidOperationException($"リポジトリルート({RepoLocator.RepoMarkerFileName})が見つかりませんでした。");
        }

        string RepoRoot=this.RepoRoot;
        IReadOnlyList<SceneRenderObject>RenderObjects=SceneModelRenderObjectBuilder.Build(
            Request.Model,
            EnumMemberName=>SceneModelRenderObjectBuilder.ResolveViaBridge(EnumMemberName,RepoRoot),
            AssetPath=>SceneModelRenderObjectBuilder.ResolveStaticMeshViaBridge(AssetPath,RepoRoot));
        return new SceneRenderBuildResult(Request,RenderObjects);
    }

    private void OnBuildCompleted(SceneRenderBuildRequest Request,SceneRenderBuildResult?Result,Exception?Error){
        if(Surface is null
            ||Surface.Handle!=Request.SurfaceHandle
            ||TargetKey is not { } Key
            ||!Key.Equals(Request.Key)
            ||TargetSnapshot?.AnalysisGeneration!=Request.AnalysisGeneration){
            return;
        }

        if(Error is not null){
            ActivityLog.LogError(nameof(SceneHwndHost),Error.ToString());
            if(Renderer is null){
                Surface.SetVisible(false);
            }
            SetStatus($"表示に失敗しました: {Error.Message}");
            return;
        }

        if(Result is null){
            return;
        }

        // 描画オブジェクト0件でもレンダラーは作る(グリッド/ギズモだけのフレームは正当、
        // M0020 Phase 0/Phase 3)。ここでサーフェスを隠すと前フレームの残像や背面ペインの露出を
        // 招くだけで、0件でも RenderFrame は clear + Present を行う。
        try {
            DisposeRenderer();
            (this.SceneBoundsCenter,this.SceneBoundsRadius)=SceneCameraFraming.ComputeBounds(Result.RenderObjects);
            (Matrix4x4 View,Matrix4x4 Projection)=SceneCameraFraming.FrameBounds(
                this.SceneBoundsCenter,this.SceneBoundsRadius,SceneCameraFraming.DefaultFovYRadians,this.AspectRatio);
            // 実サイズは Surface.Resized で既に届いているので、800x600 のハードコードは使わない。
            Renderer=new SceneRenderer(Surface.Handle,SurfaceWidth,SurfaceHeight,Result.RenderObjects,View,Projection);
            Renderer.SetSelectedNode(this.SelectedNodePath);
            Surface.SetVisible(true);
            SetStatus($"表示中: {Result.Request.ClassName}");
        } catch(Exception Ex){
            ActivityLog.LogError(nameof(SceneHwndHost),Ex.ToString());
            DisposeRenderer();
            SetStatus($"表示に失敗しました: {Ex.Message}");
        }
    }

    private void DisposeRenderer(){
        Renderer?.Dispose();
        Renderer=null;
        Surface?.SetVisible(false);
    }

    private void DetachSubscriptions(){
        if(RenderingHandler is not null){
            CompositionTarget.Rendering-=RenderingHandler;
            RenderingHandler=null;
        }

        if(ResizeHandler is not null&&Surface is not null){
            Surface.Resized-=ResizeHandler;
            ResizeHandler=null;
        }
    }

    private void SetStatus(string Status)=>StatusChanged?.Invoke(Status);

    private sealed record SceneRenderBuildRequest(
        SceneModel Model,
        SceneKey Key,
        long AnalysisGeneration,
        string FilePath,
        string ClassName,
        IntPtr SurfaceHandle);

    private sealed record SceneRenderBuildResult(
        SceneRenderBuildRequest Request,
        IReadOnlyList<SceneRenderObject>RenderObjects);
}
