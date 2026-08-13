using System;
using System.Collections.Generic;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.VSExtension;

/// <summary>Designer?Source Editor?????????????UI?Roslyn API?????????</summary>
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

/// <summary>??????????????ViewModel?Roslyn?????????????????</summary>
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
/// SceneProjection???Node ID????Syntax????????Expression???Roslyn????????
/// ???????????TextBuffer???????VS Undo transaction?live buffer????TextChange
/// ????????????VS?????????????
/// </summary>
internal static class SceneSourceEditor {
    internal static SceneSourceEditResult Apply(SyntaxNode Root,SceneProjection Projection,SceneEditOperation Operation){
        if(!Projection.Key.Equals(Operation.SceneKey)){
            return SceneSourceEditResult.Failure("????Scene??????SceneKey????????");
        }

        if(!Projection.TryGetNode(Operation.NodeId,out SceneProjectionNode Node)){
            return SceneSourceEditResult.Failure("????Node????Scene????????");
        }

        if(IsGenerated(Node.Node.Location.FilePath)){
            return SceneSourceEditResult.Failure("???? .g.cs ?Scene Designer??????????");
        }

        return Operation switch {
            SetComponentMemberOperation SetMember=>ApplySetComponentMember(Root,Node,SetMember),
            RenameNodeOperation Rename=>ApplyRename(Root,Node,Rename),
            _=>SceneSourceEditResult.Failure("??Scene???????Source Editor???????????"),
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
            return SceneSourceEditResult.Failure("????Component???Member????Scene????????");
        }

        ExpressionSyntax?OldExpression=FindExpression(Root,Member.Value.Location.Span);
        ExpressionSyntax NewExpression=SyntaxFactory.ParseExpression(Operation.ValueExpression);
        if(OldExpression is null||NewExpression.ContainsDiagnostics){
            return SceneSourceEditResult.Failure("???????C#????????????");
        }

        return SceneSourceEditResult.Success(Root.ReplaceNode(OldExpression,NewExpression.WithTriviaFrom(OldExpression)));
    }

    private static SceneSourceEditResult ApplyRename(SyntaxNode Root,SceneProjectionNode Node,RenameNodeOperation Operation){
        if(Node.Node.NameValueLocation is not { } Location){
            return SceneSourceEditResult.Failure("Name?????Node??????????????????");
        }

        ExpressionSyntax?OldExpression=FindExpression(Root,Location.Span);
        ExpressionSyntax NewExpression=SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(Operation.NewName));
        if(OldExpression is null){
            return SceneSourceEditResult.Failure("??????Name????Source????????");
        }

        return SceneSourceEditResult.Success(Root.ReplaceNode(OldExpression,NewExpression.WithTriviaFrom(OldExpression)));
    }

    private static ExpressionSyntax?FindExpression(SyntaxNode Root,TextSpan Span)=>
        Root.DescendantNodesAndSelf().OfType<ExpressionSyntax>().SingleOrDefault(Node=>Node.Span==Span);

    private static bool IsGenerated(string?FilePath)=>
        FilePath is { Length:>0 }&&FilePath.EndsWith(".g.cs",StringComparison.OrdinalIgnoreCase);
}
