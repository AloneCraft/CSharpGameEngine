using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// In-process D3D12 embedding (same pattern as <see cref="AssetPreviewHwndHost"/>, ADR-0013) for the
/// Scene Designer tool window (ADR-0040 Phase 9). Targets a specific <c>IScene</c>-implementing
/// class by <see cref="SetTarget"/>, resolves it out of
/// <see cref="SceneEditorHub.ActiveAllSceneModels"/> (published by <see cref="SceneWorkspaceService"/>
/// - live Roslyn, no build step), converts it via <see cref="SceneModelRenderObjectBuilder"/> and
/// renders every node via <see cref="SceneRenderer"/>. Replaces the earlier <c>.scene</c>-JSON-based
/// version of this class (ADR-0020/ADR-0021) - re-analyzed in place rather than duplicated, since the
/// old data source goes away entirely in this same phase (see ADR-0040 Phase 9's "known limitations":
/// drag-and-drop placement, which still writes to <c>.scene</c>, is temporarily non-functional until
/// Phase 10 moves it onto Roslyn-based source edits too).
/// </summary>
internal sealed class SceneHwndHost:HwndHost {
    private string?RepoRoot;
    private string?TargetFilePath;
    private string?TargetClassName;
    private RenderSurfaceWindow?Surface;
    private SceneRenderer?Renderer;
    private EventHandler?RenderingHandler;
    private Action<IReadOnlyList<SceneModel>>?ModelsHandler;
    private Action<NodePath?>?SelectionHandler;

    protected override HandleRef BuildWindowCore(HandleRef HwndParent){
        Surface=new RenderSurfaceWindow(HwndParent.Handle,800,600);
        LoadIntoSurface();
        return new HandleRef(this,Surface.Handle);
    }

    protected override void DestroyWindowCore(HandleRef Hwnd){
        TearDownRenderer();

        Surface?.Dispose();
        Surface=null;
    }

    /// <summary>Points this host at a specific class in a specific file - called once by <c>OpenSceneDesignerCommand</c> when the tool window is shown/retargeted.</summary>
    public void SetTarget(string FilePath,string ClassName){
        this.TargetFilePath=FilePath;
        this.TargetClassName=ClassName;

        if(Surface is not null){
            TearDownRenderer();
            LoadIntoSurface();
        }
    }

    private void LoadIntoSurface(){
        if(Surface is null){
            return;
        }

        if(ModelsHandler is null){
            ModelsHandler=_=>Rebuild();
            SceneEditorHub.AllSceneModelsChanged+=ModelsHandler;
        }

        if(SelectionHandler is null){
            SelectionHandler=Path=>Renderer?.SetSelectedNode(Path);
            SceneEditorHub.SelectedNodePathChanged+=SelectionHandler;
        }

        if(RenderingHandler is null){
            RenderingHandler=(_,_)=>Renderer?.RenderFrame();
            CompositionTarget.Rendering+=RenderingHandler;

            // Subscribed once here (not inside Rebuild, which runs on every re-analysis) - it always
            // resizes whatever Renderer currently holds via the field, so re-subscribing per rebuild
            // would just accumulate redundant duplicate calls per resize.
            Surface.Resized+=(W,H)=>Renderer?.Resize(W,H);
        }

        Rebuild();
    }

    /// <summary>(Re)builds <see cref="Renderer"/> from the current <see cref="SceneEditorHub.ActiveAllSceneModels"/> - called on first load and every live re-analysis.</summary>
    private void Rebuild(){
        if(Surface is null||TargetFilePath is null||TargetClassName is null){
            return;
        }

        try {
            this.RepoRoot??=RepoLocator.ResolveRepoRoot();
            if(this.RepoRoot is null){
                throw new InvalidOperationException($"リポジトリルート({RepoLocator.RepoMarkerFileName})が見つかりませんでした。");
            }
            var RepoRoot=this.RepoRoot;

            SceneModel?Model=SceneEditorHub.ActiveAllSceneModels.FirstOrDefault(
                M=>string.Equals(M.FilePath,TargetFilePath,StringComparison.OrdinalIgnoreCase)
                    &&string.Equals(M.ClassName,TargetClassName,StringComparison.Ordinal));

            if(Model is null){
                // Not (yet) found - e.g. analysis hasn't caught up with a just-opened tool window, or
                // the class was renamed/deleted out from under us. Leave the last good frame on
                // screen rather than tearing the renderer down on every transient miss.
                return;
            }

            var RenderObjects=SceneModelRenderObjectBuilder.Build(
                Model,
                EnumMemberName=>SceneModelRenderObjectBuilder.ResolveViaBridge(EnumMemberName,RepoRoot));

            if(RenderObjects.Count==0){
                // SceneRenderer requires at least one object; an empty scene (e.g. mid-edit with a
                // syntax error, or a genuinely componentless root) has nothing to draw yet.
                Renderer?.Dispose();
                Renderer=null;
                return;
            }

            Renderer?.Dispose();
            Renderer=new SceneRenderer(Surface.Handle,800,600,RenderObjects);
            Surface.Resized+=(W,H)=>Renderer?.Resize(W,H);
        } catch(Exception Ex){
            System.Windows.MessageBox.Show(
                $"シーン デザイナーの表示に失敗しました: {Ex}",
                "C# Game Engine Scene Designer",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void TearDownRenderer(){
        if(RenderingHandler is not null){
            CompositionTarget.Rendering-=RenderingHandler;
            RenderingHandler=null;
        }

        if(ModelsHandler is not null){
            SceneEditorHub.AllSceneModelsChanged-=ModelsHandler;
            ModelsHandler=null;
        }

        if(SelectionHandler is not null){
            SceneEditorHub.SelectedNodePathChanged-=SelectionHandler;
            SelectionHandler=null;
        }

        Renderer?.Dispose();
        Renderer=null;
    }
}
