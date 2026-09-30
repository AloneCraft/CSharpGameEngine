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
/// editor-owned authoring state. The live C# buffer remains the only authoring source of truth.
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
/// Re-derives one edit from the latest C# model. A stale request is either explicitly rebased by
/// <see cref="SceneEditConflictMerger"/> or rejected; it is never replayed over a second in-memory
/// authoring document.
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
        if(SceneJsonCommitPlanner.IsJsonScene(Context.Model.FilePath)){
            // ADR-0070: a JSON scene is written back from the edited model, not by C# syntax edits.
            return SceneJsonCommitPlanner.Plan(Context,Latest,Request,Reconciler,CancellationToken);
        }

        if(Context.Model.HasErrors||Context.Model.Root is null||Latest.HasBlockingErrors
            ||Latest.Projection is null){
            return SceneSourceCommitPlan.Failure(VsixText.Get("SessionSaveAnalysisErrors"));
        }

        SceneEditConflictMergeResult Merged=SceneEditConflictMerger.Merge(Request,Latest);
        if(!Merged.Succeeded){
            return SceneSourceCommitPlan.Failure(
                Merged.ErrorMessage??VsixText.Get("SourceEditOperationFailed"));
        }

        if(!SceneModelEditApplier.TryApply(
            Context.Model,Latest.Projection,Reconciler,Request.SceneKey,Merged.Operations,
            out SceneModel PredictedModel,out SceneProjection PredictedProjection)){
            return SceneSourceCommitPlan.Failure(VsixText.Get("SourceEditOperationFailed"));
        }

        SceneSourceEditResult Edited=SceneSourceEditor.ApplyBatch(
            Context.Root,Latest.Projection,Merged.Operations);
        if(!Edited.Succeeded||Edited.Root is null){
            return SceneSourceCommitPlan.Failure(
                Edited.ErrorMessage??VsixText.Get("SourceEditOperationFailed"));
        }

        if(!SceneSaveVerification.TryVerifyAndAnalyze(
            Edited.Root,Context.Root,Context.Compilation,PredictedModel,
            Request.SceneKey.ClassFullyQualifiedName,CancellationToken,
            out SceneModel?CanonicalModel,out string?VerificationError)){
            return SceneSourceCommitPlan.Failure(
                VerificationError??VsixText.Get("SessionSaveFailed"));
        }
        SceneProjection CanonicalProjection=Reconciler.Reconcile(
            Request.SceneKey,CanonicalModel!,PredictedProjection);

        IReadOnlyList<TextChange>Changes=SceneTextChangePlanner.Plan(
            Context.Root.SyntaxTree,Edited.Root.SyntaxTree);
        string ExpectedText=Edited.Root.ToFullString();
        if(!string.Equals(
            Context.Text.WithChanges(Changes).ToString(),ExpectedText,StringComparison.Ordinal)){
            Changes=SceneTextChangePlanner.Plan(
                Context.Text,SourceText.From(ExpectedText,Context.Text.Encoding));
        }

        SceneTextWriteStep WriteStep=new(
            Context.Text,Changes,SceneUndoTransactionNaming.Format(Request.UndoDescription));
        return SceneSourceCommitPlan.Success(
            Request.DocumentVersion!=Latest.DocumentVersion,
            CanonicalModel!,CanonicalProjection,Edited.Root,WriteStep);
    }
}
