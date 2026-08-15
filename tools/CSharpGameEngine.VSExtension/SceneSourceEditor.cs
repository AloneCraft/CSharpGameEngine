using System;
using System.Collections.Generic;
using System.Globalization;
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

[Flags]
internal enum SceneTransformMemberMask {
    None=0,
    Position=1,
    Rotation=2,
    Scale=4,
    All=Position|Rotation|Scale,
}

internal sealed record SetTransformOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion,
    SceneTransformValue Transform,
    SceneTransformMemberMask Members=SceneTransformMemberMask.All) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

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
            SetTransformOperation SetTransform=>ApplySetTransform(Root,Node,SetTransform),
            RenameNodeOperation Rename=>ApplyRename(Root,Node,Rename),
            _=>SceneSourceEditResult.Failure("このScene編集操作はSource Editorで未実装です。"),
        };
    }

    /// <summary>
    /// 1ユーザー操作の複数Node編集を1つのRootへ原子的に適用する。後方のsyntaxから処理するため、
    /// 先に行った置換で未処理Nodeのsource spanがずれない。
    /// </summary>
    internal static SceneSourceEditResult ApplyBatch(
        SyntaxNode Root,SceneProjection Projection,IReadOnlyList<SceneEditOperation>Operations){
        if(Operations.Count==0){
            return SceneSourceEditResult.Success(Root);
        }

        List<(SceneEditOperation Operation,int Start)>Ordered=[];
        foreach(SceneEditOperation Operation in Operations){
            if(!Projection.Key.Equals(Operation.SceneKey)
                ||!Projection.TryGetNode(Operation.NodeId,out SceneProjectionNode Node)){
                return SceneSourceEditResult.Failure("対象のSceneまたはNodeが現在の投影に見つかりません。");
            }
            Ordered.Add((Operation,Node.Node.Location.Span.Start));
        }

        SyntaxNode Current=Root;
        foreach((SceneEditOperation Operation,_)in Ordered.OrderByDescending(static Item=>Item.Start)){
            SceneSourceEditResult Applied=Apply(Current,Projection,Operation);
            if(!Applied.Succeeded){
                return SceneSourceEditResult.Failure(Applied.ErrorMessage??"Scene編集操作に失敗しました。");
            }
            Current=Applied.Root!;
        }
        return SceneSourceEditResult.Success(Current);
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

    internal static SceneSourceEditResult ApplyLatestBatch(
        SyntaxNode Root,SceneDocumentSnapshot Snapshot,IReadOnlyList<SceneEditOperation>Operations){
        if(Snapshot.Projection is null){
            return SceneSourceEditResult.Failure("現在のSceneには編集可能なLast Known Good投影がありません。");
        }

        SceneSourceEditResult Result=ApplyBatch(Root,Snapshot.Projection,Operations);
        if(!Result.Succeeded){
            return Result;
        }

        bool WasRebased=Operations.Any(Operation=>Operation.DocumentVersion!=Snapshot.DocumentVersion);
        return SceneSourceEditResult.Success(Result.Root!,WasRebased);
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

    private static SceneSourceEditResult ApplySetTransform(
        SyntaxNode Root,SceneProjectionNode Node,SetTransformOperation Operation){
        SceneComponent?Transform=SceneComponentBindings.FindComponent(Node.Node,SceneComponentRole.LocalTransform);
        if(Transform is null){
            return SceneSourceEditResult.Failure("対象Nodeに編集可能なTransform Componentがありません。");
        }
        if(IsGenerated(Transform.Location.FilePath)){
            return SceneSourceEditResult.Failure("生成された .g.cs のTransformは編集できません。");
        }

        BaseObjectCreationExpressionSyntax?Creation=Root.DescendantNodesAndSelf()
            .OfType<BaseObjectCreationExpressionSyntax>()
            .SingleOrDefault(Candidate=>Candidate.Span==Transform.Location.Span);
        if(Creation is null){
            return SceneSourceEditResult.Failure("Transformのobject creationがSourceに見つかりません。");
        }

        InitializerExpressionSyntax Initializer=Creation.Initializer
            ??SyntaxFactory.InitializerExpression(SyntaxKind.ObjectInitializerExpression);
        SeparatedSyntaxList<ExpressionSyntax>Expressions=Initializer.Expressions;
        Dictionary<string,int>AssignmentIndices=new(StringComparer.Ordinal);
        for(int Index=0;Index<Expressions.Count;Index++){
            if(Expressions[Index]is AssignmentExpressionSyntax Assignment
                &&Assignment.Left is IdentifierNameSyntax Identifier){
                AssignmentIndices[Identifier.Identifier.ValueText]=Index;
            }
        }

        foreach((string Name,float Value,float Default)in EnumerateTransformMembers(Operation.Transform,Operation.Members)){
            SceneComponentMember?Member=Transform.FindMember(Name);
            float Current=Member is null?Default:ReadFloat(Member.Value.ConstantValue,Default);
            if(SceneTransformMath.NearlyEqual(Current,Value)){
                continue;
            }
            if(Member is not null&&!Member.Value.IsEditable){
                return SceneSourceEditResult.Failure($"Transform.{Name}は式を保持するためギズモから編集できません。");
            }

            ExpressionSyntax NewValue=SyntaxFactory.ParseExpression(FormatFloat(Value));
            if(NewValue.ContainsDiagnostics){
                return SceneSourceEditResult.Failure($"Transform.{Name}の値をC#式へ変換できません。");
            }

            if(AssignmentIndices.TryGetValue(Name,out int AssignmentIndex)){
                AssignmentExpressionSyntax Assignment=(AssignmentExpressionSyntax)Expressions[AssignmentIndex];
                AssignmentExpressionSyntax Replaced=Assignment.WithRight(NewValue.WithTriviaFrom(Assignment.Right));
                Expressions=Expressions.Replace(Assignment,Replaced);
            } else {
                AssignmentExpressionSyntax Added=SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,SyntaxFactory.IdentifierName(Name),NewValue);
                Expressions=Expressions.Add(Added);
                AssignmentIndices.Add(Name,Expressions.Count-1);
            }
        }

        InitializerExpressionSyntax UpdatedInitializer=Initializer.WithExpressions(Expressions);
        SyntaxNode UpdatedCreation=Creation switch {
            ObjectCreationExpressionSyntax Explicit=>Explicit.WithInitializer(UpdatedInitializer),
            ImplicitObjectCreationExpressionSyntax Implicit=>Implicit.WithInitializer(UpdatedInitializer),
            _=>Creation,
        };
        return SceneSourceEditResult.Success(Root.ReplaceNode(Creation,UpdatedCreation));
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

    private static IEnumerable<(string Name,float Value,float Default)>EnumerateTransformMembers(
        SceneTransformValue Transform,SceneTransformMemberMask Members){
        if((Members&SceneTransformMemberMask.Position)!=0){
            yield return("PositionX",Transform.Position.X,0f);
            yield return("PositionY",Transform.Position.Y,0f);
            yield return("PositionZ",Transform.Position.Z,0f);
        }
        if((Members&SceneTransformMemberMask.Rotation)!=0){
            yield return("RotationX",Transform.Rotation.X,0f);
            yield return("RotationY",Transform.Rotation.Y,0f);
            yield return("RotationZ",Transform.Rotation.Z,0f);
            yield return("RotationW",Transform.Rotation.W,1f);
        }
        if((Members&SceneTransformMemberMask.Scale)!=0){
            yield return("ScaleX",Transform.Scale.X,1f);
            yield return("ScaleY",Transform.Scale.Y,1f);
            yield return("ScaleZ",Transform.Scale.Z,1f);
        }
    }

    private static float ReadFloat(object?Value,float Default)=>Value switch {
        float Float=>Float,
        double Double=>(float)Double,
        decimal Decimal=>(float)Decimal,
        int Integer=>Integer,
        long Long=>Long,
        _=>Default,
    };

    private static string FormatFloat(float Value){
        if(float.IsNaN(Value)||float.IsInfinity(Value)){
            throw new ArgumentOutOfRangeException(nameof(Value),"Transformへ非有限値は書き込めません。");
        }
        return Value.ToString("R",CultureInfo.InvariantCulture)+"f";
    }

    private static bool IsGenerated(string?FilePath)=>
        FilePath is { Length:>0 }&&FilePath.EndsWith(".g.cs",StringComparison.OrdinalIgnoreCase);
}
