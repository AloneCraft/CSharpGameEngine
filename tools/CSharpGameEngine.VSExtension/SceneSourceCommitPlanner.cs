using System;
using System.Collections.Generic;
using System.Threading;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// VS-independent inputs captured from one Roslyn solution generation. The Visual Studio
/// <c>DocumentId</c> and live buffer remain in <see cref="SceneWorkspaceEditContext"/> and are not
/// part of the pure planning boundary.
/// </summary>
internal sealed class SceneSourceCommitContext {
    internal SceneSourceCommitContext(
        SceneModel Model,SyntaxNode Root,SourceText Text,Compilation Compilation){
        this.Model=Model??throw new ArgumentNullException(nameof(Model));
        this.Root=Root??throw new ArgumentNullException(nameof(Root));
        this.Text=Text??throw new ArgumentNullException(nameof(Text));
        this.Compilation=Compilation??throw new ArgumentNullException(nameof(Compilation));
    }

    internal SceneModel Model { get; }

    internal SyntaxNode Root { get; }

    internal SourceText Text { get; }

    internal Compilation Compilation { get; }

    internal static SceneSourceCommitContext FromWorkspace(SceneWorkspaceEditContext Context)=>
        new(Context.Model,Context.Root,Context.Text,Context.Compilation);
}

/// <summary>
/// A one-shot source commit. It is derived from the latest Roslyn context and is never retained as
/// editor-owned authoring state. The live scene document buffer remains the only authoring source of truth.
/// </summary>
internal sealed class SceneSourceCommitPlan {
    private SceneSourceCommitPlan(
        bool Succeeded,string?ErrorMessage,bool WasRebased,SceneModel?PredictedModel,
        SceneProjection?PredictedProjection,SyntaxNode?FinalRoot,SceneTextWriteStep?WriteStep){
        this.Succeeded=Succeeded;
        this.ErrorMessage=ErrorMessage;
        this.WasRebased=WasRebased;
        this.PredictedModel=PredictedModel;
        this.PredictedProjection=PredictedProjection;
        this.FinalRoot=FinalRoot;
        this.WriteStep=WriteStep;
    }

    internal bool Succeeded { get; }

    internal string?ErrorMessage { get; }

    internal bool WasRebased { get; }

    internal SceneModel?PredictedModel { get; }

    internal SceneProjection?PredictedProjection { get; }

    internal SyntaxNode?FinalRoot { get; }

    /// <summary>Exactly one user gesture maps to at most one native Visual Studio Undo unit.</summary>
    internal SceneTextWriteStep?WriteStep { get; }

    internal static SceneSourceCommitPlan Success(
        bool WasRebased,SceneModel PredictedModel,SceneProjection PredictedProjection,
        SyntaxNode FinalRoot,SceneTextWriteStep WriteStep)=>
        new(true,ErrorMessage:null,WasRebased,PredictedModel,PredictedProjection,FinalRoot,WriteStep);

    internal static SceneSourceCommitPlan Failure(string ErrorMessage)=>
        new(false,ErrorMessage,WasRebased:false,PredictedModel:null,PredictedProjection:null,
            FinalRoot:null,WriteStep:null);
}

/// <summary>
/// Re-derives one edit from the latest scene model. A stale request is either explicitly rebased by
/// <see cref="SceneEditConflictMerger"/> or rejected; it is never replayed over a second in-memory
/// authoring document. The write itself is <see cref="SceneJsonCommitPlanner"/>'s.
/// </summary>
internal static class SceneSourceCommitPlanner {
    internal static SceneSourceCommitPlan Plan(
        SceneSourceCommitContext Context,SceneDocumentSnapshot Latest,
        SceneEditCommitRequest Request,SceneProjectionReconciler Reconciler,
        CancellationToken CancellationToken){
        if(Context is null){ throw new ArgumentNullException(nameof(Context)); }
        if(Latest is null){ throw new ArgumentNullException(nameof(Latest)); }
        if(Request is null){ throw new ArgumentNullException(nameof(Request)); }
        if(Reconciler is null){ throw new ArgumentNullException(nameof(Reconciler)); }
        CancellationToken.ThrowIfCancellationRequested();
        // ADR-0070: only *.scene.json scenes are authored; a gesture is written back from the edited model.
        return SceneJsonCommitPlanner.IsJsonScene(Context.Model.FilePath)
            ?SceneJsonCommitPlanner.Plan(Context,Latest,Request,Reconciler,CancellationToken)
            :SceneSourceCommitPlan.Failure(VsixText.Get("SessionSaveSourceUnavailable"));
    }
}
