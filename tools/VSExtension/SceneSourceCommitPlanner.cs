using System;
using System.Collections.Generic;
using System.Threading;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// 1 回の Roslyn ソリューション生成から取得した、VS に依存しない入力。Visual Studio の <c>DocumentId</c> とライブバッファーは <see cref="SceneWorkspaceEditContext"/> に残し、純粋な計画境界には含めない。
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
}

/// <summary>
/// 1 回限りのソースコミット。最新の Roslyn コンテキストから導出し、エディター所有の作成状態として保持しない。ライブのシーンドキュメントバッファーだけが作成元の正となる。
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

    /// <summary>ユーザー操作 1 回につき、ネイティブ Visual Studio の Undo 項目は最大 1 つ。</summary>
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
/// 最新のシーンモデルから編集内容を再導出する。古い要求は <see cref="SceneEditConflictMerger"/> で明示的に再適用するか、拒否する。2 つ目のメモリ内作成ドキュメントに再適用することはない。書き込み処理は <see cref="SceneJsonCommitPlanner"/> が担う。
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
        // ADR-0070：シーンは JSON だけで作成し、ユーザー操作は編集後のモデルから書き戻す。
        return SceneJsonCommitPlanner.IsJsonDocument(Context.Model.FilePath)
            ?SceneJsonCommitPlanner.Plan(Context,Latest,Request,Reconciler,CancellationToken)
            :SceneSourceCommitPlan.Failure(VsixText.Get("SessionSaveSourceUnavailable"));
    }
}
