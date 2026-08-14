using System;
using System.Collections.Generic;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.VSExtension;

/// <summary>DesignerからSource Editorへ渡す編集操作の基底。UI層がRoslyn APIへ直接触れないための語彙。</summary>
internal abstract record SceneEditOperation(SceneKey SceneKey,EditorNodeId NodeId,long DocumentVersion);

internal sealed record SetComponentMemberOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion,
    string ComponentTypeFullyQualifiedName,
    string MemberName,
    string ValueExpression) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

internal sealed record RenameNodeOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion,
    string NewName) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

/// <summary>編集操作の全語彙。ViewModelとRoslyn書き込み層が同じ列挙を共有する(未実装の種類も先に確定させる)。</summary>
internal enum SceneEditOperationKind {
    SetComponentMember,
    SetTransform,
    RenameNode,
    AddNode,
    RemoveNode,
    DuplicateNode,
    ReparentNode,
    ReorderNode,
    AddComponent,
    RemoveComponent,
    AssignAsset,
}

internal sealed class SceneSourceEditResult {
    private SceneSourceEditResult(bool Succeeded,SyntaxNode?Root,string?ErrorMessage,bool WasRebased){
        this.Succeeded=Succeeded;
        this.Root=Root;
        this.ErrorMessage=ErrorMessage;
        this.WasRebased=WasRebased;
    }

    internal bool Succeeded { get; }

    internal SyntaxNode?Root { get; }

    internal string?ErrorMessage { get; }

    internal bool WasRebased { get; }

    internal static SceneSourceEditResult Success(SyntaxNode Root,bool WasRebased=false)=>new(true,Root,ErrorMessage:null,WasRebased);

    internal static SceneSourceEditResult Failure(string ErrorMessage)=>new(false,Root:null,ErrorMessage,WasRebased:false);
}

/// <summary>
/// SceneProjectionのNode IDから対象Syntaxを特定し、該当Expressionだけを置換したRoslyn Rootを返す
/// 純粋層。TextBufferへの反映は<see cref="SceneTextBufferWriter"/>がVS Undo transactionとしてlive
/// bufferへTextChangeを適用する(本クラス自体はVSサービスへ依存しない)。
/// </summary>
internal static class SceneSourceEditor {
    internal static SceneSourceEditResult Apply(SyntaxNode Root,SceneProjection Projection,SceneEditOperation Operation){
        if(!Projection.Key.Equals(Operation.SceneKey)){
            return SceneSourceEditResult.Failure("対象のSceneと操作のSceneKeyが一致しません。");
        }

        if(!Projection.TryGetNode(Operation.NodeId,out SceneProjectionNode Node)){
            return SceneSourceEditResult.Failure("対象のNodeが現在のSceneに見つかりません。");
        }

        if(IsGenerated(Node.Node.Location.FilePath)){
            return SceneSourceEditResult.Failure("生成された .g.cs はScene Designerから編集できません。");
        }

        return Operation switch {
            SetComponentMemberOperation SetMember=>ApplySetComponentMember(Root,Node,SetMember),
            RenameNodeOperation Rename=>ApplyRename(Root,Node,Rename),
            _=>SceneSourceEditResult.Failure("このScene編集操作はSource Editorで未実装です。"),
        };
    }

    /// <summary>
    /// Commit時に最新snapshotで操作対象を再解決します。開始Versionと異なる場合も、session内の
    /// EditorNodeIdが一意に残っていれば一回だけrebaseして適用します。見つからなければ無変更で失敗します。
    /// </summary>
    internal static SceneSourceEditResult ApplyLatest(SyntaxNode Root,SceneDocumentSnapshot Snapshot,SceneEditOperation Operation){
        if(Snapshot.Projection is null){
            return SceneSourceEditResult.Failure("現在のSceneには編集可能なLast Known Good投影がありません。");
        }

        SceneSourceEditResult Result=Apply(Root,Snapshot.Projection,Operation);
        if(!Result.Succeeded||Snapshot.DocumentVersion==Operation.DocumentVersion){
            return Result;
        }

        return SceneSourceEditResult.Success(Result.Root!,WasRebased:true);
    }
    private static SceneSourceEditResult ApplySetComponentMember(
        SyntaxNode Root,SceneProjectionNode Node,SetComponentMemberOperation Operation){
        SceneComponent?Component=Node.Node.FindComponent(Operation.ComponentTypeFullyQualifiedName);
        SceneComponentMember?Member=Component?.FindMember(Operation.MemberName);
        if(Member is null){
            return SceneSourceEditResult.Failure("対象のComponentまたはMemberが現在のSceneに見つかりません。");
        }

        ExpressionSyntax?OldExpression=FindExpression(Root,Member.Value.Location.Span);
        ExpressionSyntax NewExpression=SyntaxFactory.ParseExpression(Operation.ValueExpression);
        if(OldExpression is null||NewExpression.ContainsDiagnostics){
            return SceneSourceEditResult.Failure("新しい値が妥当なC#式ではありません。");
        }

        return SceneSourceEditResult.Success(Root.ReplaceNode(OldExpression,NewExpression.WithTriviaFrom(OldExpression)));
    }

    private static SceneSourceEditResult ApplyRename(SyntaxNode Root,SceneProjectionNode Node,RenameNodeOperation Operation){
        if(Node.Node.NameValueLocation is not { } Location){
            return SceneSourceEditResult.Failure("NameリテラルのないNodeはリネームできません。");
        }

        ExpressionSyntax?OldExpression=FindExpression(Root,Location.Span);
        ExpressionSyntax NewExpression=SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(Operation.NewName));
        if(OldExpression is null){
            return SceneSourceEditResult.Failure("対象のNameリテラルがSourceに見つかりません。");
        }

        return SceneSourceEditResult.Success(Root.ReplaceNode(OldExpression,NewExpression.WithTriviaFrom(OldExpression)));
    }

    private static ExpressionSyntax?FindExpression(SyntaxNode Root,TextSpan Span)=>
        Root.DescendantNodesAndSelf().OfType<ExpressionSyntax>().SingleOrDefault(Node=>Node.Span==Span);

    private static bool IsGenerated(string?FilePath)=>
        FilePath is { Length:>0 }&&FilePath.EndsWith(".g.cs",StringComparison.OrdinalIgnoreCase);
}
