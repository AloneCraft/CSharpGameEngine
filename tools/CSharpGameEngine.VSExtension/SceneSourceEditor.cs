using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
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
    string ValueExpression,
    string?OriginalValueExpression=null,
    ImmutableArray<string>MemberPath=default) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

/// <summary>
/// <paramref name="Members"/>は<b>メモリ内モデル専用</b>で、C#への書き戻しには使わない
/// (書き戻しは<paramref name="ComponentExpression"/>をそのまま構文として挿す)。
/// これが空だと、Roslynの再解析が着地するまでInspectorが「解析待ち…」のまま何も編集できない
/// (<c>SceneModelEditApplier.TryApplyAddComponent</c>)。両者は
/// <c>SceneComponentInitializerPolicy.CreatePlan</c>が同じ列挙から作るので食い違わない。
/// </summary>
internal sealed record AddComponentOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion,
    string ComponentTypeFullyQualifiedName,
    string ComponentExpression,
    ImmutableArray<SceneComponentMember>Members=default,
    SceneComponentOrderKey?OrderKey=null) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

internal sealed record RemoveComponentOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion,
    string ComponentTypeFullyQualifiedName) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

/// <summary>重複を first-wins で除去し、カテゴリ・order・完全修飾型名順へ整列する明示操作。</summary>
internal sealed record NormalizeComponentsOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

internal sealed record RenameNodeOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion,
    string NewName) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

/// <summary>新規Nodeを原子的に作るとき、同じNode初期化子へ入れるComponentの1件。</summary>
internal sealed record NewNodeComponent(
    string ComponentTypeFullyQualifiedName,
    string ComponentExpression,
    ImmutableArray<SceneComponentMember>Members=default,
    SceneComponentOrderKey?OrderKey=null);

internal sealed record AddNodeOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion,
    string NodeName,
    ImmutableArray<NewNodeComponent>InitialComponents=default) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

internal sealed record RemoveNodeOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

internal sealed record ReparentNodeOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion,
    EditorNodeId NewParentNodeId,
    int NewIndex) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

internal sealed record ReorderNodeOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion,
    int NewIndex) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

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

internal sealed record DuplicateNodeOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion,
    SceneTransformValue Transform,
    SceneTransformMemberMask Members) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

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
    NormalizeComponents,
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
/// M0051: <see cref="SceneSourceEditor.ApplyForSaveSteps"/>の結果。履歴エントリ1件ごとの
/// 中間構文木を、適用順に保持する。
///
/// <para>
/// <b>なぜ中間木を返すのか</b>: 呼び出し側はこれを1件ずつ別々のVS Undoトランザクションとして
/// 書き戻す。こうするとデザイナーの操作履歴とテキストのUndo履歴が1対1で対応し、どちらのタブで
/// Ctrl+Zしても同じ粒度で巻き戻る。差分は<c>Steps[i-1]</c>→<c>Steps[i]</c>で直接計算できるため、
/// 書き戻しのたびにバッファを再パースする必要が無い(各書き込み後のバッファ内容は
/// <c>Steps[i].ToFullString()</c>に一致する)。
/// </para>
/// </summary>
internal sealed class SceneSourceEditStepsResult {
    private SceneSourceEditStepsResult(
        bool Succeeded,IReadOnlyList<SyntaxNode>Steps,IReadOnlyList<string>Labels,string?ErrorMessage){
        this.Succeeded=Succeeded;
        this.Steps=Steps;
        this.Labels=Labels;
        this.ErrorMessage=ErrorMessage;
    }

    internal bool Succeeded { get; }

    /// <summary>履歴エントリ1件を適用したあとの構文木。<see cref="Labels"/>と同じ長さ・同じ順序。</summary>
    internal IReadOnlyList<SyntaxNode>Steps { get; }

    /// <summary>各ステップのUndo表示名(<see cref="SceneUndoEntry.Label"/>)。</summary>
    internal IReadOnlyList<string>Labels { get; }

    internal string?ErrorMessage { get; }

    /// <summary>最終的な構文木。1件も適用していなければ<see langword="null"/>。</summary>
    internal SyntaxNode?FinalRoot=>this.Steps.Count>0?this.Steps[this.Steps.Count-1]:null;

    internal static SceneSourceEditStepsResult Success(IReadOnlyList<SyntaxNode>Steps,IReadOnlyList<string>Labels)=>
        new(true,Steps,Labels,ErrorMessage:null);

    internal static SceneSourceEditStepsResult Failure(string ErrorMessage)=>
        new(false,[],[],ErrorMessage);
}

/// <summary>
/// SceneProjectionのNode IDから対象Syntaxを特定し、該当Expressionだけを置換したRoslyn Rootを返す
/// 純粋層。TextBufferへの反映は<see cref="SceneTextBufferWriter"/>がVS Undo transactionとしてlive
/// bufferへTextChangeを適用する(本クラス自体はVSサービスへ依存しない)。
/// </summary>
internal static class SceneSourceEditor {
    internal static SceneSourceEditResult Apply(SyntaxNode Root,SceneProjection Projection,SceneEditOperation Operation,IReadOnlyDictionary<EditorNodeId,SyntaxAnnotation>? Annotations=null){
        if(!Projection.Key.Equals(Operation.SceneKey)){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditSceneKeyMismatch"));
        }

        if(!Projection.TryGetNode(Operation.NodeId,out SceneProjectionNode Node)){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditNodeNotFound"));
        }

        if(IsGenerated(Node.Node.Location.FilePath)){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditGeneratedSceneReadOnly"));
        }

        return Operation switch {
            SetComponentMemberOperation SetMember=>ApplySetComponentMember(Root,Node,SetMember),
            AddComponentOperation AddComponent=>ApplyAddComponent(Root,Node,AddComponent,Annotations),
            RemoveComponentOperation RemoveComponent=>ApplyRemoveComponent(Root,Node,RemoveComponent,Annotations),
            NormalizeComponentsOperation Normalize=>ApplyNormalizeComponents(Root,Node,Normalize,Annotations),
            SetTransformOperation SetTransform=>ApplySetTransform(Root,Node,SetTransform,Annotations),
            DuplicateNodeOperation Duplicate=>ApplyDuplicateNode(Root,Projection,Node,Duplicate),
            RenameNodeOperation Rename=>ApplyRename(Root,Node,Rename,Annotations),
            AddNodeOperation Add=>ApplyAddNode(Root,Projection,Node,Add,Annotations),
            RemoveNodeOperation Remove=>ApplyRemoveNode(Root,Projection,Node,Remove,Annotations),
            ReparentNodeOperation Reparent=>ApplyReparentNode(Root,Projection,Node,Reparent,Annotations),
            ReorderNodeOperation Reorder=>ApplyReorderNode(Root,Projection,Node,Reorder,Annotations),
            _=>SceneSourceEditResult.Failure(VsixText.Get("SourceEditOperationUnsupported")),
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

        List<(SceneEditOperation Operation,int Start,int Sequence)>Ordered=[];
        var Sequence=0;
        foreach(SceneEditOperation Operation in Operations){
            if(!Projection.Key.Equals(Operation.SceneKey)
                ||!Projection.TryGetNode(Operation.NodeId,out SceneProjectionNode Node)){
                return SceneSourceEditResult.Failure(VsixText.Get("SourceEditProjectionTargetMissing"));
            }
            int Start=Node.Node.Location.Span.Start;
            if(Operation is DuplicateNodeOperation&&Node.Path.Parent is { }ParentPath
                &&Projection.TryGetId(ParentPath,out EditorNodeId ParentId)
                &&Projection.TryGetNode(ParentId,out SceneProjectionNode Parent)
                &&Parent.Node.ChildrenListLocation is { }ChildrenLocation){
                Start=ChildrenLocation.Span.Start;
            }
            Ordered.Add((Operation,Start,Sequence++));
        }

        SyntaxNode Current=Root;
        Dictionary<EditorNodeId,SyntaxAnnotation> Annotations=[];
        void Annotate(EditorNodeId Id){
            if(Annotations.ContainsKey(Id)||!Projection.TryGetNode(Id,out SceneProjectionNode Candidate)){
                return;
            }
            if(FindNodeCreation(Current,Candidate.Node) is not { } Creation){
                return;
            }
            SyntaxAnnotation Annotation=new($"SceneBatchNode_{Annotations.Count}");
            Current=Current.ReplaceNode(Creation,Creation.WithAdditionalAnnotations(Annotation));
            Annotations[Id]=Annotation;
        }
        foreach(SceneEditOperation Operation in Operations){
            Annotate(Operation.NodeId);
            if(Projection.TryGetNode(Operation.NodeId,out SceneProjectionNode OperationNode)
                &&OperationNode.ParentId is { } OperationParentId){
                Annotate(OperationParentId);
            }
            switch(Operation){
                case ReparentNodeOperation Reparent:
                    Annotate(Reparent.NewParentNodeId);
                    break;
                case AddNodeOperation:
                case RemoveNodeOperation:
                case ReorderNodeOperation:
                case RenameNodeOperation:
                    break;
            }
        }

        foreach((SceneEditOperation Operation,_,_)in Ordered
            .OrderByDescending(static Item=>Item.Start)
            .ThenBy(static Item=>Item.Sequence)){
            SceneSourceEditResult Applied=Apply(Current,Projection,Operation,Annotations);
            if(!Applied.Succeeded){
                return SceneSourceEditResult.Failure(Applied.ErrorMessage??VsixText.Get("SourceEditOperationFailed"));
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
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditLastKnownGoodUnavailable"));
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
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditLastKnownGoodUnavailable"));
        }

        SceneSourceEditResult Result=ApplyBatch(Root,Snapshot.Projection,Operations);
        if(!Result.Succeeded){
            return Result;
        }

        bool WasRebased=Operations.Any(Operation=>Operation.DocumentVersion!=Snapshot.DocumentVersion);
        return SceneSourceEditResult.Success(Result.Root!,WasRebased);
    }

    /// <summary>
    /// M0047: <see cref="SceneEditDocument"/>のセッション全履歴を、保存時に一度だけ実C#構文へ
    /// まとめて再生する。<see cref="ApplyBatch"/>と違い、履歴は<b>セッション中に新規追加された
    /// Node</b>を後続の操作(Rename/Reparent/Reorder/Remove)が参照しうる — そのNodeは保存対象の
    /// 実構文木にはまだ存在しないため、<paramref name="RealProjection"/>にも入っていない。
    ///
    /// <para>
    /// <see cref="SceneUndoEntry.After"/>スナップショットは、そのEntryが実際にどのID(Documentの
    /// Reconcilerが払い出した本物のID)を新規Nodeへ割り当てたかを知っている。保存時はこのIDを
    /// そのまま実側の投影へ登録することで、後続の操作(同じIDを参照している)を正しく解決できる。
    /// </para>
    /// </summary>
    internal static SceneSourceEditResult ApplyForSave(
        SyntaxNode Root,SceneKey Key,SceneProjection RealProjection,IReadOnlyList<SceneUndoEntry>History){
        SceneSourceEditStepsResult Steps=ApplyForSaveSteps(Root,Key,RealProjection,History);
        if(!Steps.Succeeded){
            return SceneSourceEditResult.Failure(Steps.ErrorMessage??VsixText.Get("SourceSaveReapplyFailed"));
        }

        return SceneSourceEditResult.Success(Steps.FinalRoot??Root);
    }

    /// <summary>
    /// M0051: <see cref="ApplyForSave"/>と同じ再生を行うが、<b>履歴エントリ1件ごとの中間構文木</b>を
    /// 順に返す(<see cref="SceneSourceEditStepsResult"/>のXMLコメント参照)。呼び出し側がこれを
    /// 1件ずつ別々のVS Undoトランザクションとして書き戻すことで、デザイナーの操作履歴と
    /// テキストのUndo履歴が1対1で対応する。
    /// </summary>
    internal static SceneSourceEditStepsResult ApplyForSaveSteps(
        SyntaxNode Root,SceneKey Key,SceneProjection RealProjection,IReadOnlyList<SceneUndoEntry>History){
        if(History.Count==0){
            return SceneSourceEditStepsResult.Success([],[]);
        }

        List<SyntaxNode>Steps=[];
        List<string>Labels=[];
        SyntaxNode Current=Root;
        Dictionary<EditorNodeId,SyntaxAnnotation>Annotations=[];
        List<SceneProjectionNode>WorkingNodes=RealProjection.Nodes.ToList();
        SceneProjection WorkingProjection=RealProjection;

        bool TryEnsureAnnotated(EditorNodeId Id){
            if(Annotations.ContainsKey(Id)){
                return true;
            }
            if(!WorkingProjection.TryGetNode(Id,out SceneProjectionNode Candidate)){
                return false;
            }
            if(FindNodeCreation(Current,Candidate.Node)is not { }Creation){
                return false;
            }
            SyntaxAnnotation Annotation=new($"SceneSaveNode_{Annotations.Count}");
            Current=Current.ReplaceNode(Creation,Creation.WithAdditionalAnnotations(Annotation));
            Annotations[Id]=Annotation;
            return true;
        }

        // 実在するNode(セッション中に新規追加された分は除く)は、最初の構造編集がテキストを
        // ずらす前に前もって全てAnnotateしておく — さもないと、後続のEntryで初めて参照される
        // 実Nodeの、Location.Spanベースの遅延解決が、既に適用済みの前段の編集でずれたSpanを
        // 掴んで失敗する(ApplyBatchの事前Annotateパスと同じ理由、同じ対策)。
        foreach(SceneUndoEntry Entry in History){
            foreach(SceneEditOperation Operation in Entry.Operations){
                TryEnsureAnnotated(Operation.NodeId);

                // M0051 Step 1-4(バグ修正): 操作対象の「親」も必ず注釈する。
                // Remove/Reorder/Reparent(旧親)は親の構文を書き換えるため親の解決が要るが、
                // ここが漏れていたため親はFindNodeCreationのSpan一致フォールバックへ落ち、
                // 履歴2件目以降(例: 兄弟をRenameしてからDelete)で前段の編集により親のSpanが
                // ずれて解決できず「削除対象が Children の静的な要素ではありません」で保存が
                // 中止されていた。ApplyBatchの事前Annotateパスは元から親を注釈しており、
                // 保存経路だけが取り残されていた。
                if(WorkingProjection.TryGetNode(Operation.NodeId,out SceneProjectionNode OperationNode)
                    &&OperationNode.ParentId is { }OperationParentId){
                    TryEnsureAnnotated(OperationParentId);
                }

                if(Operation is ReparentNodeOperation ReparentUpfront){
                    TryEnsureAnnotated(ReparentUpfront.NewParentNodeId);
                }
            }
        }

        foreach(SceneUndoEntry Entry in History){
            foreach(SceneEditOperation Operation in Entry.Operations){
                if(!TryEnsureAnnotated(Operation.NodeId)){
                    return SceneSourceEditStepsResult.Failure(VsixText.Get("SourceSaveNodeSyntaxMissing"));
                }
                if(Operation is ReparentNodeOperation Reparent&&!TryEnsureAnnotated(Reparent.NewParentNodeId)){
                    return SceneSourceEditStepsResult.Failure(VsixText.Get("SourceSaveDestinationNodeSyntaxMissing"));
                }

                SceneSourceEditResult Result=Apply(Current,WorkingProjection,Operation,Annotations);
                if(!Result.Succeeded||Result.Root is not { }NewRoot){
                    return SceneSourceEditStepsResult.Failure(Result.ErrorMessage??VsixText.Get("SourceSaveReapplyFailed"));
                }
                Current=NewRoot;

                if(Operation is AddNodeOperation Add
                    &&!TryRegisterNewNode(
                        Key,Add,Entry,ref Current,WorkingProjection,Annotations,ref WorkingNodes,out WorkingProjection)){
                    return SceneSourceEditStepsResult.Failure(VsixText.Get("SourceSaveAddedNodeSyntaxMissing"));
                }
            }

            // Entry内の全Operationを適用し終えた時点を1つのUndo単位として記録する
            // (Entryは Document.TryApply の1回=ユーザーの1操作に対応する)。
            Steps.Add(Current);
            Labels.Add(Entry.Label);
        }

        return SceneSourceEditStepsResult.Success(Steps,Labels);
    }

    /// <summary>
    /// <see cref="ApplyForSave"/>から呼ぶ。追加したばかりのNodeを親の子リストの末尾(常に追記)から
    /// 見つけ出し、Documentのセッション内IDのままAnnotation+投影へ登録して以降の操作から
    /// 解決可能にする。
    /// </summary>
    private static bool TryRegisterNewNode(
        SceneKey Key,AddNodeOperation Add,SceneUndoEntry Entry,ref SyntaxNode Current,SceneProjection ProjectionBeforeAdd,
        Dictionary<EditorNodeId,SyntaxAnnotation>Annotations,ref List<SceneProjectionNode>WorkingNodes,
        out SceneProjection UpdatedProjection){
        UpdatedProjection=ProjectionBeforeAdd;
        if(!ProjectionBeforeAdd.TryGetNode(Add.NodeId,out SceneProjectionNode Parent)
            ||AnnotationFor(Annotations,Parent.Id)is not { }ParentAnnotation){
            return false;
        }

        BaseObjectCreationExpressionSyntax?ParentCreation=Current.GetAnnotatedNodes(ParentAnnotation)
            .OfType<BaseObjectCreationExpressionSyntax>().SingleOrDefault();
        ExpressionSyntax?Children=ParentCreation is null?null:FindChildrenExpression(ParentCreation);
        BaseObjectCreationExpressionSyntax?NewChild=Children is null
            ?null
            :EnumerateChildCreations(Children).LastOrDefault();
        if(NewChild is null){
            return false;
        }

        // このEntryが実際に払い出した新規IDを、Before/Afterの投影の集合差から特定する。
        // 同じEntryで複数のAddNodeOperation(複数選択への一括AddChild)がありうるため、
        // 対応する親IDで絞り込む。
        HashSet<EditorNodeId>BeforeIds=Entry.Before.Projection.Nodes.Select(static Node=>Node.Id).ToHashSet();
        EditorNodeId?NewId=Entry.After.Projection.Nodes
            .Where(Node=>!BeforeIds.Contains(Node.Id)&&Node.ParentId is { }NodeParentId&&NodeParentId.Equals(Parent.Id))
            .Select(static Node=>(EditorNodeId?)Node.Id)
            .FirstOrDefault();
        if(NewId is not { }ResolvedId){
            return false;
        }

        SceneNode?PredictedNewNode=Entry.After.Projection.TryGetNode(
            ResolvedId,out SceneProjectionNode PredictedNode)?PredictedNode.Node:null;
        SyntaxAnnotation NewAnnotation=new($"SceneSaveNode_{Annotations.Count}");
        Current=Current.ReplaceNode(NewChild,NewChild.WithAdditionalAnnotations(NewAnnotation));
        Annotations[ResolvedId]=NewAnnotation;

        // SceneProjectionはIdだけでなくPathでも索引する(IdsByPath)。Rootと同じdefault Pathを
        // 渡すと索引が衝突する。追加は常に末尾へ追記されるため、現時点で同じ親を持つ既存子の数を
        // そのままローカルIndexとして使える。
        int LocalIndex=WorkingNodes.Count(N=>N.ParentId is { }P&&P.Equals(Parent.Id));
        // NameValueLocationをnullのままにすると、後続のRenameがCreateNodeCreationが書いた
        // Name="…"へ追記でなく"挿入"してしまい、Name代入が重複する。既存のName式を指す。
        SceneSourceLocation?NameValueLocation=NewChild.Initializer?.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(static Candidate=>Candidate.Left is IdentifierNameSyntax Identifier
                &&Identifier.Identifier.ValueText=="Name")
            is { Right:{ }NameValue }?SceneSourceLocation.FromNode(NameValue):null;
        SceneNode SyntheticContent=new(
            Parent.Node.Path.Append(LocalIndex),PredictedNewNode?.Name??Add.NodeName,
            PredictedNewNode?.Components??ImmutableArray<SceneComponent>.Empty,ImmutableArray<SceneNode>.Empty,
            SceneSourceLocation.FromNode(NewChild),null,NameValueLocation,
            PredictedNewNode?.ComponentsListLocation,null);
        WorkingNodes=[..WorkingNodes,new SceneProjectionNode(ResolvedId,Parent.Id,SyntheticContent)];
        UpdatedProjection=new SceneProjection(Key,WorkingNodes);
        return true;
    }

    private static SceneSourceEditResult ApplySetComponentMember(
        SyntaxNode Root,SceneProjectionNode Node,SetComponentMemberOperation Operation){
        SceneComponent?Component=Node.Node.FindComponent(Operation.ComponentTypeFullyQualifiedName);
        if(Component is null){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditComponentOrMemberMissing"));
        }
        ExpressionSyntax NewExpression=SyntaxFactory.ParseExpression(Operation.ValueExpression);
        if(NewExpression.ContainsDiagnostics){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditInvalidCSharpExpression"));
        }
        ImmutableArray<string>Path=Operation.MemberPath.IsDefaultOrEmpty
            ?ImmutableArray.Create(Operation.MemberName)
            :Operation.MemberPath;
        if(Path.Length>1){
            return ApplySetNestedComponentMember(Root,Component,Path,NewExpression);
        }

        SceneComponentMember?Member=Component.FindMember(Operation.MemberName);
        if(Member is null){
            bool MemberExists=Component.TypeSymbol?.GetMembers(Operation.MemberName).Length>0;
            if(!MemberExists){
                return SceneSourceEditResult.Failure(VsixText.Get("SourceEditMemberNotDeclared"));
            }
            return ApplyInsertComponentMember(Root,Component,Operation.MemberName,NewExpression);
        }
        ExpressionSyntax?OldExpression=FindExpression(Root,Member.Value.Location.Span);
        if(OldExpression is null){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditMemberValueMissing"));
        }
        return SceneSourceEditResult.Success(Root.ReplaceNode(OldExpression,NewExpression.WithTriviaFrom(OldExpression)));
    }

    private static SceneSourceEditResult ApplySetNestedComponentMember(
        SyntaxNode Root,SceneComponent Component,ImmutableArray<string> Path,ExpressionSyntax NewExpression){
        SceneComponentMember?Top=Component.FindMember(Path[0]);
        if(Top?.Value.Kind!=SceneValueKind.ObjectCreation){
            return SceneSourceEditResult.Failure(VsixText.Format("SourceEditPathNotEditableObjectCreation",Path[0]));
        }
        ExpressionSyntax?TopExpression=FindExpression(Root,Top.Value.Location.Span);
        if(TopExpression is not BaseObjectCreationExpressionSyntax Creation){
            return SceneSourceEditResult.Failure(VsixText.Format("SourceEditPathNotSourceObjectCreation",Path[0]));
        }
        string Child=Path[1];
        ArgumentSyntax?Argument=Creation.ArgumentList?.Arguments.FirstOrDefault(Candidate=>
            Candidate.NameColon?.Name.Identifier.ValueText==Child);
        if(Argument is null&&TryGetVectorComponentIndex(Child,out int Index)
            &&Creation.ArgumentList is { }Arguments&&Arguments.Arguments.Count>Index){
            Argument=Arguments.Arguments[Index];
        }
        if(Argument is null){
            return SceneSourceEditResult.Failure(VsixText.Format("SourceEditPathNotPositionalVector3",string.Join(".",Path)));
        }
        ArgumentSyntax UpdatedArgument=Argument.WithExpression(NewExpression.WithTriviaFrom(Argument.Expression));
        BaseObjectCreationExpressionSyntax UpdatedCreation=(BaseObjectCreationExpressionSyntax)Creation.ReplaceNode(Argument,UpdatedArgument);
        return SceneSourceEditResult.Success(Root.ReplaceNode(Creation,UpdatedCreation));
    }

    private static bool TryGetVectorComponentIndex(string Name,out int Index){
        switch(Name){
            case "R": Index=0; return true;
            case "U": Index=1; return true;
            case "F": Index=2; return true;
            default: Index=-1; return false;
        }
    }

    private static SceneSourceEditResult ApplyInsertComponentMember(
        SyntaxNode Root,SceneComponent Component,string MemberName,ExpressionSyntax NewExpression){
        BaseObjectCreationExpressionSyntax?Creation=Root.DescendantNodesAndSelf()
            .OfType<BaseObjectCreationExpressionSyntax>()
            .SingleOrDefault(Candidate=>Candidate.Span==Component.Location.Span);
        if(Creation is null){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditComponentCreationMissing"));
        }

        AssignmentExpressionSyntax Assignment=SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,SyntaxFactory.IdentifierName(MemberName),NewExpression);
        InitializerExpressionSyntax Initializer=Creation.Initializer
            ??SyntaxFactory.InitializerExpression(SyntaxKind.ObjectInitializerExpression);
        InitializerExpressionSyntax UpdatedInitializer=Initializer.WithExpressions(
            Initializer.Expressions.Add(Assignment));
        BaseObjectCreationExpressionSyntax Updated=Creation switch {
            ObjectCreationExpressionSyntax ObjectCreation=>ObjectCreation.WithInitializer(UpdatedInitializer),
            ImplicitObjectCreationExpressionSyntax ImplicitCreation=>ImplicitCreation.WithInitializer(UpdatedInitializer),
            _=>Creation,
        };
        return SceneSourceEditResult.Success(Root.ReplaceNode(Creation,Updated));
    }

    private static SceneSourceEditResult ApplyAddComponent(
        SyntaxNode Root,SceneProjectionNode Node,AddComponentOperation Operation){
        if(Node.Node.FindComponent(Operation.ComponentTypeFullyQualifiedName)is not null){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditDuplicateComponent"));
        }
        if(Node.Node.ComponentsListLocation is not { }Location){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditComponentsCollectionMissing"));
        }

        ExpressionSyntax NewExpression=SyntaxFactory.ParseExpression(Operation.ComponentExpression);
        if(NewExpression.ContainsDiagnostics||NewExpression is not BaseObjectCreationExpressionSyntax){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditInvalidComponentCreation"));
        }
        ExpressionSyntax?Collection=Root.DescendantNodesAndSelf().OfType<ExpressionSyntax>()
            .FirstOrDefault(Candidate=>Candidate.Span==Location.Span);
        ExpressionSyntax?Updated=Collection switch {
            CollectionExpressionSyntax Values=>Values.WithElements(
                Values.Elements.Add(SyntaxFactory.ExpressionElement(NewExpression))),
            ArrayCreationExpressionSyntax Array when Array.Initializer is { }Initializer=>
                Array.WithInitializer(Initializer.WithExpressions(Initializer.Expressions.Add(NewExpression))),
            ImplicitArrayCreationExpressionSyntax Array when Array.Initializer is { }Initializer=>
                Array.WithInitializer(Initializer.WithExpressions(Initializer.Expressions.Add(NewExpression))),
            _=>null,
        };
        return Collection is null||Updated is null
            ?SceneSourceEditResult.Failure(VsixText.Get("SourceEditComponentsSyntaxUnsupported"))
            :SceneSourceEditResult.Success(Root.ReplaceNode(Collection,Updated.WithTriviaFrom(Collection)));
    }

    private static SceneSourceEditResult ApplyRemoveComponent(
        SyntaxNode Root,SceneProjectionNode Node,RemoveComponentOperation Operation){
        SceneComponent?Component=Node.Node.FindComponent(Operation.ComponentTypeFullyQualifiedName);
        if(Component is null){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditComponentToRemoveMissing"));
        }
        BaseObjectCreationExpressionSyntax?Creation=Root.DescendantNodesAndSelf()
            .OfType<BaseObjectCreationExpressionSyntax>()
            .SingleOrDefault(Candidate=>Candidate.Span==Component.Location.Span);
        if(Creation is null){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditComponentCreationToRemoveMissing"));
        }

        if(Creation.Parent is ExpressionElementSyntax Element
            &&Element.Parent is CollectionExpressionSyntax Collection){
            CollectionExpressionSyntax Updated=Collection.WithElements(Collection.Elements.Remove(Element));
            return SceneSourceEditResult.Success(Root.ReplaceNode(Collection,Updated));
        }
        if(Creation.Parent is InitializerExpressionSyntax Initializer){
            InitializerExpressionSyntax Updated=Initializer.WithExpressions(
                Initializer.Expressions.Remove(Creation));
            return SceneSourceEditResult.Success(Root.ReplaceNode(Initializer,Updated));
        }
        return SceneSourceEditResult.Failure(VsixText.Get("SourceEditComponentRemovalFailed"));
    }

    private static SceneSourceEditResult ApplyAddComponent(
        SyntaxNode Root,SceneProjectionNode Node,AddComponentOperation Operation,
        IReadOnlyDictionary<EditorNodeId,SyntaxAnnotation>? Annotations){
        if(Node.Node.FindComponent(Operation.ComponentTypeFullyQualifiedName)is not null){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditDuplicateComponent"));
        }

        ExpressionSyntax NewExpression=SyntaxFactory.ParseExpression(Operation.ComponentExpression);
        if(NewExpression.ContainsDiagnostics||NewExpression is not BaseObjectCreationExpressionSyntax){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditInvalidComponentCreation"));
        }

        BaseObjectCreationExpressionSyntax?NodeCreation=FindNodeCreation(
            Root,Node.Node,AnnotationFor(Annotations,Node.Id));
        if(NodeCreation is null){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditAddTargetCreationMissing"));
        }

        ExpressionSyntax?Collection=FindComponentsExpression(NodeCreation);
        if(Collection is null){
            return SceneSourceEditResult.Success(Root.ReplaceNode(
                NodeCreation,AddComponentsMember(NodeCreation,NewExpression)));
        }
        if(!TryAppendComponent(Collection,NewExpression,out ExpressionSyntax Appended)){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditComponentsSyntaxUnsupported"));
        }

        ExpressionSyntax Updated=NormalizeComponentCollection(
            Appended,Node.Node.Components,Operation);
        return SceneSourceEditResult.Success(
            Root.ReplaceNode(Collection,Updated.WithTriviaFrom(Collection)));
    }

    private static SceneSourceEditResult ApplyRemoveComponent(
        SyntaxNode Root,SceneProjectionNode Node,RemoveComponentOperation Operation,
        IReadOnlyDictionary<EditorNodeId,SyntaxAnnotation>? Annotations){
        if(Node.Node.FindComponent(Operation.ComponentTypeFullyQualifiedName)is null){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditComponentToRemoveMissing"));
        }
        BaseObjectCreationExpressionSyntax?NodeCreation=FindNodeCreation(
            Root,Node.Node,AnnotationFor(Annotations,Node.Id));
        ExpressionSyntax?Collection=NodeCreation is null?null:FindComponentsExpression(NodeCreation);
        if(Collection is null){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditComponentCollectionToRemoveMissing"));
        }
        if(!TryRemoveComponent(Collection,Operation.ComponentTypeFullyQualifiedName,
            out ExpressionSyntax WithoutComponent)){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditComponentRemovalFailed"));
        }

        ExpressionSyntax Updated=NormalizeComponentCollection(
            WithoutComponent,Node.Node.Components);
        return SceneSourceEditResult.Success(
            Root.ReplaceNode(Collection,Updated.WithTriviaFrom(Collection)));
    }

    private static SceneSourceEditResult ApplyNormalizeComponents(
        SyntaxNode Root,SceneProjectionNode Node,NormalizeComponentsOperation Operation,
        IReadOnlyDictionary<EditorNodeId,SyntaxAnnotation>? Annotations){
        BaseObjectCreationExpressionSyntax?NodeCreation=FindNodeCreation(
            Root,Node.Node,AnnotationFor(Annotations,Node.Id));
        ExpressionSyntax?Collection=NodeCreation is null?null:FindComponentsExpression(NodeCreation);
        if(Collection is null){
            return SceneSourceEditResult.Success(Root);
        }

        ExpressionSyntax Updated=NormalizeComponentCollection(Collection,Node.Node.Components);
        return SceneSourceEditResult.Success(
            Root.ReplaceNode(Collection,Updated.WithTriviaFrom(Collection)));
    }

    private static ExpressionSyntax? FindComponentsExpression(
        BaseObjectCreationExpressionSyntax Creation)=>
        Creation.Initializer?.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(static Assignment=>Assignment.Left is IdentifierNameSyntax Identifier
                &&Identifier.Identifier.ValueText=="Components")?.Right;

    private static BaseObjectCreationExpressionSyntax AddComponentsMember(
        BaseObjectCreationExpressionSyntax Creation,ExpressionSyntax Component){
        CollectionExpressionSyntax Components=SyntaxFactory.CollectionExpression(
            SyntaxFactory.SingletonSeparatedList<CollectionElementSyntax>(
                SyntaxFactory.ExpressionElement(Component)));
        AssignmentExpressionSyntax Assignment=SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,SyntaxFactory.IdentifierName("Components"),Components);
        InitializerExpressionSyntax Initializer=Creation.Initializer
            ??SyntaxFactory.InitializerExpression(SyntaxKind.ObjectInitializerExpression);
        return WithInitializer(Creation,Initializer.WithExpressions(
            Initializer.Expressions.Add(Assignment)));
    }

    private static bool TryAppendComponent(
        ExpressionSyntax Collection,ExpressionSyntax Component,out ExpressionSyntax Updated){
        switch(Collection){
            case CollectionExpressionSyntax Values:
                Updated=Values.WithElements(
                    Values.Elements.Add(SyntaxFactory.ExpressionElement(Component)));
                return true;
            case ArrayCreationExpressionSyntax { Initializer:{ }Initializer }Array:
                Updated=Array.WithInitializer(Initializer.WithExpressions(
                    Initializer.Expressions.Add(Component)));
                return true;
            case ImplicitArrayCreationExpressionSyntax { Initializer:{ }Initializer }Implicit:
                Updated=Implicit.WithInitializer(Initializer.WithExpressions(
                    Initializer.Expressions.Add(Component)));
                return true;
            default:
                Updated=Collection;
                return false;
        }
    }

    private static bool TryRemoveComponent(
        ExpressionSyntax Collection,string ComponentTypeFullyQualifiedName,out ExpressionSyntax Updated){
        switch(Collection){
            case CollectionExpressionSyntax Values:
                ExpressionElementSyntax?Element=Values.Elements.OfType<ExpressionElementSyntax>()
                    .FirstOrDefault(Candidate=>IsComponentExpressionOfType(
                        Candidate.Expression,ComponentTypeFullyQualifiedName));
                Updated=Element is null?Collection:Values.WithElements(Values.Elements.Remove(Element));
                return Element is not null;
            case ArrayCreationExpressionSyntax { Initializer:{ }Initializer }Array:
                ExpressionSyntax?ArrayElement=Initializer.Expressions.FirstOrDefault(Candidate=>
                    IsComponentExpressionOfType(Candidate,ComponentTypeFullyQualifiedName));
                Updated=ArrayElement is null?Collection:Array.WithInitializer(
                    Initializer.WithExpressions(Initializer.Expressions.Remove(ArrayElement)));
                return ArrayElement is not null;
            case ImplicitArrayCreationExpressionSyntax { Initializer:{ }Initializer }Implicit:
                ExpressionSyntax?ImplicitElement=Initializer.Expressions.FirstOrDefault(Candidate=>
                    IsComponentExpressionOfType(Candidate,ComponentTypeFullyQualifiedName));
                Updated=ImplicitElement is null?Collection:Implicit.WithInitializer(
                    Initializer.WithExpressions(Initializer.Expressions.Remove(ImplicitElement)));
                return ImplicitElement is not null;
            default:
                Updated=Collection;
                return false;
        }
    }

    private static bool IsComponentExpressionOfType(
        ExpressionSyntax Expression,string ComponentTypeFullyQualifiedName)=>
        Expression is ObjectCreationExpressionSyntax Creation
        &&string.Equals(ComponentSimpleTypeName(Creation.Type.ToString()),
            ComponentSimpleTypeName(ComponentTypeFullyQualifiedName),StringComparison.Ordinal);

    private static ExpressionSyntax NormalizeComponentCollection(
        ExpressionSyntax Collection,ImmutableArray<SceneComponent> ExistingComponents,
        AddComponentOperation?AddedComponent=null)=>Collection switch {
            CollectionExpressionSyntax Values when Values.Elements.All(
                static Element=>Element is ExpressionElementSyntax)=>Values.WithElements(
                    SyntaxFactory.SeparatedList<CollectionElementSyntax>(
                        NormalizeComponentExpressions(
                            Values.Elements.OfType<ExpressionElementSyntax>()
                                .Select(static Element=>Element.Expression),
                            ExistingComponents,AddedComponent)
                        .Select(static Expression=>SyntaxFactory.ExpressionElement(Expression)))),
            CollectionExpressionSyntax=>Collection,
            ArrayCreationExpressionSyntax { Initializer:{ }Initializer }Array=>Array.WithInitializer(
                Initializer.WithExpressions(SyntaxFactory.SeparatedList<ExpressionSyntax>(
                    NormalizeComponentExpressions(Initializer.Expressions,ExistingComponents,AddedComponent)))),
            ImplicitArrayCreationExpressionSyntax { Initializer:{ }Initializer }Implicit=>Implicit.WithInitializer(
                Initializer.WithExpressions(SyntaxFactory.SeparatedList<ExpressionSyntax>(
                    NormalizeComponentExpressions(Initializer.Expressions,ExistingComponents,AddedComponent)))),
            _=>Collection,
        };

    private static IEnumerable<ExpressionSyntax> NormalizeComponentExpressions(
        IEnumerable<ExpressionSyntax> Expressions,ImmutableArray<SceneComponent> ExistingComponents,
        AddComponentOperation?AddedComponent){
        Dictionary<string,SceneComponentOrderKey>KnownOrdersByFullName=new(StringComparer.Ordinal);
        Dictionary<string,SceneComponentOrderKey>KnownOrdersBySimpleName=new(StringComparer.Ordinal);
        void AddKnownOrder(string FullyQualifiedName,SceneComponentOrderKey Key){
            KnownOrdersByFullName[FullyQualifiedName]=Key;
            string SimpleName=ComponentSimpleTypeName(FullyQualifiedName);
            if(!KnownOrdersBySimpleName.ContainsKey(SimpleName)){
                KnownOrdersBySimpleName.Add(SimpleName,Key);
            }
        }

        foreach(SceneComponent Component in ExistingComponents){
            AddKnownOrder(Component.TypeFullyQualifiedName,Component.OrderKey);
        }
        if(AddedComponent is not null){
            string FullyQualifiedName=AddedComponent.ComponentTypeFullyQualifiedName;
            AddKnownOrder(FullyQualifiedName,
                AddedComponent.OrderKey??SceneComponentOrdering.GetSortKey(
                    TypeSymbol:null,FullyQualifiedName));
        }

        HashSet<string>SeenTypes=new(StringComparer.Ordinal);
        List<(ExpressionSyntax Expression,SceneComponentOrderKey Key,int Sequence)>FirstDeclarations=[];
        var Sequence=0;
        foreach(ExpressionSyntax Expression in Expressions){
            if(Expression is not ObjectCreationExpressionSyntax Creation){
                FirstDeclarations.Add((Expression,
                    new SceneComponentOrderKey(int.MaxValue,0,Expression.ToString()),Sequence++));
                continue;
            }

            string DeclaredTypeName=Creation.Type.ToString();
            string CanonicalTypeName=CanonicalizeComponentTypeName(DeclaredTypeName);
            SceneComponentOrderKey Key;
            string Identity;
            if(KnownOrdersByFullName.TryGetValue(CanonicalTypeName,out SceneComponentOrderKey KnownKey)){
                Key=KnownKey;
                Identity=KnownKey.FullyQualifiedName;
            } else if(!IsQualifiedComponentTypeName(DeclaredTypeName)
                &&KnownOrdersBySimpleName.TryGetValue(
                    ComponentSimpleTypeName(DeclaredTypeName),out KnownKey)){
                Key=KnownKey;
                Identity=KnownKey.FullyQualifiedName;
            } else {
                Identity=CanonicalTypeName;
                Key=SceneComponentOrdering.GetSortKey(TypeSymbol:null,Identity);
            }
            if(SeenTypes.Add(Identity)){
                FirstDeclarations.Add((Expression,Key,Sequence));
            }
            Sequence++;
        }

        return FirstDeclarations
            .OrderBy(static Entry=>Entry.Key.Category)
            .ThenBy(static Entry=>Entry.Key.Order)
            .ThenBy(static Entry=>Entry.Key.FullyQualifiedName,StringComparer.Ordinal)
            .ThenBy(static Entry=>Entry.Sequence)
            .Select(static Entry=>Entry.Expression);
    }

    private static string CanonicalizeComponentTypeName(string TypeName)=>
        TypeName.StartsWith("global::",StringComparison.Ordinal)
            ?TypeName
            :"global::"+TypeName;

    private static bool IsQualifiedComponentTypeName(string TypeName){
        int GenericStart=TypeName.IndexOf('<');
        string NonGenericName=GenericStart>=0?TypeName.Substring(0,GenericStart):TypeName;
        return NonGenericName.StartsWith("global::",StringComparison.Ordinal)
            ||NonGenericName.IndexOf('.')>=0;
    }

    private static string ComponentSimpleTypeName(string TypeName){
        int GenericStart=TypeName.IndexOf('<');
        string NonGenericName=GenericStart>=0?TypeName.Substring(0,GenericStart):TypeName;
        int LastDot=NonGenericName.LastIndexOf('.');
        string Name=LastDot>=0?NonGenericName.Substring(LastDot+1):NonGenericName;
        const string GlobalPrefix="global::";
        return Name.StartsWith(GlobalPrefix,StringComparison.Ordinal)
            ?Name.Substring(GlobalPrefix.Length):Name;
    }
    /// <summary>
    /// M0051(既存バグの修正): 以前はTransformの構文を<c>Transform.Location.Span</c>との一致だけで
    /// 探していたため、<b>同一Nodeに対して先にRename等の構造編集が適用されるとSpanがずれて
    /// 「Transformのobject creationがSourceに見つかりません」で保存が失敗していた</b>
    /// (「リネーム→移動」は失敗、「移動→リネーム」「移動→移動」は成功、という非対称な壊れ方をしていた)。
    /// 他の操作は既に<see cref="SyntaxAnnotation"/>で追跡していたのに、ここだけSpan一致のままだったのが原因。
    /// 保存経路では対象Nodeが必ず事前Annotateされている(<see cref="ApplyForSaveSteps"/>)ので、
    /// まずAnnotationでNodeのobject creationを特定し、<b>その内側から</b>Transformを探す。
    /// Annotationが無い経路(単発Apply)では従来どおりSpan一致にフォールバックする。
    /// </summary>
    private static SceneSourceEditResult ApplySetTransform(
        SyntaxNode Root,SceneProjectionNode Node,SetTransformOperation Operation,
        IReadOnlyDictionary<EditorNodeId,SyntaxAnnotation>? Annotations){
        SceneComponent?Transform=SceneComponentBindings.FindComponent(Node.Node,SceneComponentRole.LocalTransform);
        if(Transform is null){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditTargetTransformMissing"));
        }
        if(IsGenerated(Transform.Location.FilePath)){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditGeneratedTransformReadOnly"));
        }

        BaseObjectCreationExpressionSyntax?Creation=
            FindNodeCreation(Root,Node.Node,AnnotationFor(Annotations,Node.Id))is { }NodeCreation
                ?FindComponentCreationWithin(NodeCreation,Transform)
                :null;
        Creation??=Root.DescendantNodesAndSelf()
            .OfType<BaseObjectCreationExpressionSyntax>()
            .SingleOrDefault(Candidate=>Candidate.Span==Transform.Location.Span);
        if(Creation is null){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditTransformCreationMissing"));
        }

        if(!TryUpdateTransformCreation(
            Creation,Transform,Operation.Transform,Operation.Members,
            out BaseObjectCreationExpressionSyntax UpdatedCreation,out string?ErrorMessage)){
            return SceneSourceEditResult.Failure(ErrorMessage??VsixText.Get("SourceEditTransformUpdateFailed"));
        }
        return SceneSourceEditResult.Success(Root.ReplaceNode(Creation,UpdatedCreation));
    }

    private static SceneSourceEditResult ApplyDuplicateNode(
        SyntaxNode Root,SceneProjection Projection,SceneProjectionNode Node,DuplicateNodeOperation Operation){
        if(Node.Path.Parent is not { }ParentPath
            ||!Projection.TryGetId(ParentPath,out EditorNodeId ParentId)
            ||!Projection.TryGetNode(ParentId,out SceneProjectionNode Parent)
            ||Parent.Node.ChildrenListLocation is not { }ChildrenLocation){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditNodeCannotDuplicate"));
        }

        BaseObjectCreationExpressionSyntax?SourceCreation=Root.DescendantNodesAndSelf()
            .OfType<BaseObjectCreationExpressionSyntax>()
            .SingleOrDefault(Candidate=>Candidate.Span==Node.Node.Location.Span);
        if(SourceCreation is null){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditDuplicateNodeCreationMissing"));
        }
        ExpressionSyntax?ChildrenExpression=SourceCreation.Ancestors()
            .OfType<ExpressionSyntax>()
            .FirstOrDefault(Candidate=>Candidate.SpanStart==ChildrenLocation.Span.Start);
        if(ChildrenExpression is null){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditParentChildrenMissing"));
        }

        SceneComponent?Transform=SceneComponentBindings.FindComponent(Node.Node,SceneComponentRole.LocalTransform);
        BaseObjectCreationExpressionSyntax?TransformCreation=Transform is null?null:SourceCreation.DescendantNodes()
            .OfType<BaseObjectCreationExpressionSyntax>()
            .SingleOrDefault(Candidate=>Candidate.Span==Transform.Location.Span);
        if(Transform is null||TransformCreation is null){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditDuplicateTransformMissing"));
        }
        if(!TryUpdateTransformCreation(
            TransformCreation,Transform,Operation.Transform,Operation.Members,
            out BaseObjectCreationExpressionSyntax UpdatedTransform,out string?ErrorMessage)){
            return SceneSourceEditResult.Failure(ErrorMessage??VsixText.Get("SourceEditDuplicateTransformUpdateFailed"));
        }

        BaseObjectCreationExpressionSyntax Clone=(BaseObjectCreationExpressionSyntax)
            SourceCreation.ReplaceNode(TransformCreation,UpdatedTransform);
        if(Node.Node.Name is { Length:>0 }BaseName){
            Clone=ReplaceNodeName(Clone,CreateUniqueSiblingName(BaseName,Parent.Node,ChildrenExpression));
        }
        if(!TryAppendChild(ChildrenExpression,Clone,out ExpressionSyntax UpdatedChildren)){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditDuplicateInsertionFailed"));
        }
        return SceneSourceEditResult.Success(Root.ReplaceNode(ChildrenExpression,UpdatedChildren));
    }

    private static bool TryUpdateTransformCreation(
        BaseObjectCreationExpressionSyntax Creation,SceneComponent Transform,SceneTransformValue Value,
        SceneTransformMemberMask Members,out BaseObjectCreationExpressionSyntax Updated,out string?ErrorMessage){
        Updated=Creation;
        ErrorMessage=null;
        if((Members&SceneTransformMemberMask.Position)!=0
            &&!TryUpdateTransformVector(Updated,Transform,"Position",Value.Position,Vector3.Zero,out Updated,out ErrorMessage)){
            return false;
        }
        if((Members&SceneTransformMemberMask.Rotation)!=0
            &&!TryUpdateTransformVector(Updated,Transform,"Rotation",Value.EulerRotation,Vector3.Zero,out Updated,out ErrorMessage)){
            return false;
        }
        if((Members&SceneTransformMemberMask.Scale)!=0
            &&!TryUpdateTransformVector(Updated,Transform,"Scale",Value.Scale,Vector3.One,out Updated,out ErrorMessage)){
            return false;
        }
        return true;
    }

    private static bool TryUpdateTransformVector(
        BaseObjectCreationExpressionSyntax Creation,SceneComponent Transform,string Name,Vector3 Value,Vector3 Default,
        out BaseObjectCreationExpressionSyntax Updated,out string?ErrorMessage){
        Updated=Creation;
        ErrorMessage=null;
        Vector3 Current=SceneTransformBinding.ReadVector(Transform,Name,Default);
        if(SceneTransformMath.NearlyEqual(Current.X,Value.X)
            &&SceneTransformMath.NearlyEqual(Current.Y,Value.Y)
            &&SceneTransformMath.NearlyEqual(Current.Z,Value.Z)){
            return true;
        }
        if(!SceneTransformBinding.CanEditVector(Transform,Name,0)
            ||!SceneTransformBinding.CanEditVector(Transform,Name,1)
            ||!SceneTransformBinding.CanEditVector(Transform,Name,2)){
            ErrorMessage=VsixText.Format("SourceEditTransformVectorNotLiteral",Name);
            return false;
        }

        SceneComponentMember?Member=Transform.FindMember(Name);
        ITypeSymbol?VectorType=Member?.Value.Type??GetDeclaredMemberType(Transform,Name);
        string TypeName=VectorType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)??"Logical.Vector3";
        ExpressionSyntax NewValue=SyntaxFactory.ParseExpression(
            $"new {TypeName}({FormatFloat(Value.X)},{FormatFloat(Value.Y)},{FormatFloat(Value.Z)})");
        if(NewValue.ContainsDiagnostics){
            ErrorMessage=VsixText.Format("SourceEditTransformVectorInvalid",Name);
            return false;
        }
        if(FindMemberValue(Creation,Name)is { }OldValue){
            Updated=(BaseObjectCreationExpressionSyntax)Creation.ReplaceNode(OldValue,NewValue.WithTriviaFrom(OldValue));
            return true;
        }

        InitializerExpressionSyntax Initializer=Creation.Initializer
            ??SyntaxFactory.InitializerExpression(SyntaxKind.ObjectInitializerExpression);
        AssignmentExpressionSyntax Added=SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,SyntaxFactory.IdentifierName(Name),NewValue);
        Updated=WithInitializer(Creation,Initializer.WithExpressions(Initializer.Expressions.Add(Added)));
        return true;
    }

    private static ITypeSymbol? GetDeclaredMemberType(SceneComponent Component,string Name)=>
        Component.TypeSymbol?.GetMembers(Name).FirstOrDefault() switch {
            IFieldSymbol Field=>Field.Type,
            IPropertySymbol Property=>Property.Type,
            _=>null,
        };
    private static BaseObjectCreationExpressionSyntax ReplaceNodeName(
        BaseObjectCreationExpressionSyntax Creation,string Name){
        AssignmentExpressionSyntax?Assignment=Creation.Initializer?.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(static Candidate=>Candidate.Left is IdentifierNameSyntax Identifier
                &&Identifier.Identifier.ValueText=="Name");
        if(Assignment is null){ return Creation; }
        ExpressionSyntax Value=SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,SyntaxFactory.Literal(Name)).WithTriviaFrom(Assignment.Right);
        return(BaseObjectCreationExpressionSyntax)Creation.ReplaceNode(Assignment,Assignment.WithRight(Value));
    }

    private static string CreateUniqueSiblingName(
        string BaseName,SceneNode Parent,ExpressionSyntax ChildrenExpression){
        HashSet<string>Names=new(StringComparer.Ordinal);
        foreach(SceneNode Child in Parent.Children){
            if(Child.Name is { }Name){ Names.Add(Name); }
        }
        foreach(BaseObjectCreationExpressionSyntax Creation in EnumerateChildCreations(ChildrenExpression)){
            AssignmentExpressionSyntax?Assignment=Creation.Initializer?.Expressions
                .OfType<AssignmentExpressionSyntax>()
                .FirstOrDefault(static Candidate=>Candidate.Left is IdentifierNameSyntax Identifier
                    &&Identifier.Identifier.ValueText=="Name");
            if(Assignment?.Right is LiteralExpressionSyntax { Token.ValueText:var Name }){ Names.Add(Name); }
        }

        for(int Suffix=1;;Suffix++){
            string Candidate=$"{BaseName} ({Suffix})";
            if(!Names.Contains(Candidate)){ return Candidate; }
        }
    }

    private static IEnumerable<BaseObjectCreationExpressionSyntax>EnumerateChildCreations(ExpressionSyntax Expression){
        IEnumerable<ExpressionSyntax>Elements=Expression switch {
            CollectionExpressionSyntax Collection=>Collection.Elements
                .OfType<ExpressionElementSyntax>().Select(static Element=>Element.Expression),
            ArrayCreationExpressionSyntax { Initializer:{ }Initializer }=>Initializer.Expressions,
            ImplicitArrayCreationExpressionSyntax { Initializer:{ }Initializer }=>Initializer.Expressions,
            _=>Array.Empty<ExpressionSyntax>(),
        };
        return Elements.OfType<BaseObjectCreationExpressionSyntax>();
    }

    private static bool TryAppendChild(
        ExpressionSyntax ChildrenExpression,BaseObjectCreationExpressionSyntax Clone,out ExpressionSyntax Updated){
        switch(ChildrenExpression){
            case CollectionExpressionSyntax Collection:
                Updated=Collection.WithElements(Collection.Elements.Add(SyntaxFactory.ExpressionElement(Clone)));
                return true;
            case ArrayCreationExpressionSyntax { Initializer:{ }Initializer }Array:
                Updated=Array.WithInitializer(Initializer.WithExpressions(Initializer.Expressions.Add(Clone)));
                return true;
            case ImplicitArrayCreationExpressionSyntax { Initializer:{ }Initializer }Implicit:
                Updated=Implicit.WithInitializer(Initializer.WithExpressions(Initializer.Expressions.Add(Clone)));
                return true;
            default:
                Updated=ChildrenExpression;
                return false;
        }
    }

    private static SceneSourceEditResult ApplyAddNode(
        SyntaxNode Root,SceneProjection Projection,SceneProjectionNode Parent,AddNodeOperation Operation,
        IReadOnlyDictionary<EditorNodeId,SyntaxAnnotation>? Annotations){
        if(Parent.Node.Location.FilePath is { } ParentFile&&IsGenerated(ParentFile)){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditGeneratedNodeAddReadOnly"));
        }

        BaseObjectCreationExpressionSyntax?Creation=FindNodeCreation(Root,Parent.Node,AnnotationFor(Annotations,Parent.Id));
        if(Creation is null){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditAddTargetSyntaxMissing"));
        }

        // M0051 Step 1-4(バグ修正): 以前は投影(Parent.Node)の子から採番していたが、これは
        // 「保存を開始した時点の実C#」であって書き戻し中の最新状態ではない。1セッションで同じ親へ
        // 2回AddChildすると、SceneModelEditApplier側は最新モデルから採番して Node / Node (1) に
        // なるのに、こちらは毎回 Node を採番してしまい、往復検証が必ず不一致になっていた。
        // 書き戻し中の構文木(Creation)の実際の子から集めることで両者の採番規則が一致する。
        string Name=CreateUniqueNodeName(EnumerateChildNodeNames(Creation),Operation.NodeName);
        if(!SceneInitialComponentFactory.TryCreateNodeCreation(Name,Operation.InitialComponents,
            out BaseObjectCreationExpressionSyntax NewNode,out string?ErrorMessage)){
            return SceneSourceEditResult.Failure(
                ErrorMessage??VsixText.Get("SourceEditInitialComponentsFailed"));
        }
        ExpressionSyntax?Children=FindChildrenExpression(Creation);
        if(Children is not null){
            if(!TryAppendChild(Children,NewNode,out ExpressionSyntax UpdatedChildren)){
                return SceneSourceEditResult.Failure(VsixText.Get("SourceEditChildrenExpressionUnsupported"));
            }

            return SceneSourceEditResult.Success(Root.ReplaceNode(Children,UpdatedChildren.WithTriviaFrom(Children)));
        }

        BaseObjectCreationExpressionSyntax Updated=AddChildrenMember(Creation,NewNode);
        return SceneSourceEditResult.Success(Root.ReplaceNode(Creation,Updated));
    }

    private static SceneSourceEditResult ApplyRemoveNode(
        SyntaxNode Root,SceneProjection Projection,SceneProjectionNode Node,RemoveNodeOperation Operation,
        IReadOnlyDictionary<EditorNodeId,SyntaxAnnotation>? Annotations){
        if(Node.ParentId is not { } ParentId||!Projection.TryGetNode(ParentId,out SceneProjectionNode Parent)){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditRootCannotRemove"));
        }

        if(IsGenerated(Node.Node.Location.FilePath)){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditGeneratedNodeRemoveReadOnly"));
        }

        BaseObjectCreationExpressionSyntax?Creation=FindNodeCreation(Root,Node.Node,AnnotationFor(Annotations,Node.Id));
        BaseObjectCreationExpressionSyntax?ParentCreation=FindNodeCreation(Root,Parent.Node,AnnotationFor(Annotations,Parent.Id));
        ExpressionSyntax?Children=ParentCreation is null?null:FindChildrenExpression(ParentCreation);
        if(Creation is null||Children is null||!TryRemoveChild(Children,Creation,out ExpressionSyntax UpdatedChildren)){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditRemoveTargetNotStaticChild"));
        }

        return SceneSourceEditResult.Success(Root.ReplaceNode(Children,UpdatedChildren.WithTriviaFrom(Children)));
    }

    private static SceneSourceEditResult ApplyReparentNode(
        SyntaxNode Root,SceneProjection Projection,SceneProjectionNode Node,ReparentNodeOperation Operation,
        IReadOnlyDictionary<EditorNodeId,SyntaxAnnotation>? Annotations){
        if(Node.ParentId is not { } OldParentId
            ||!Projection.TryGetNode(OldParentId,out SceneProjectionNode OldParent)
            ||!Projection.TryGetNode(Operation.NewParentNodeId,out SceneProjectionNode NewParent)){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditReparentParentMissing"));
        }

        if(Node.Path.Equals(NewParent.Path)||NewParent.Path.IsDescendantOf(Node.Path)){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditReparentCycle"));
        }

        if(OldParent.Id.Equals(NewParent.Id)){
            return ApplyReorderNode(
                Root,Projection,Node,new ReorderNodeOperation(
                    Operation.SceneKey,Operation.NodeId,Operation.DocumentVersion,Operation.NewIndex),Annotations);
        }

        BaseObjectCreationExpressionSyntax?Creation=FindNodeCreation(Root,Node.Node,AnnotationFor(Annotations,Node.Id));
        BaseObjectCreationExpressionSyntax?OldParentCreation=FindNodeCreation(Root,OldParent.Node,AnnotationFor(Annotations,OldParent.Id));
        BaseObjectCreationExpressionSyntax?NewParentCreation=FindNodeCreation(Root,NewParent.Node,AnnotationFor(Annotations,NewParent.Id));
        if(Creation is null||OldParentCreation is null||NewParentCreation is null){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditReparentSyntaxMissing"));
        }

        SyntaxAnnotation MovedAnnotation=new("SceneHierarchyMoved");
        SyntaxAnnotation OldParentAnnotation=new("SceneHierarchyOldParent");
        SyntaxAnnotation NewParentAnnotation=new("SceneHierarchyNewParent");
        SyntaxNode Current=Root;
        BaseObjectCreationExpressionSyntax CurrentCreation=FindNodeCreation(Current,Node.Node,AnnotationFor(Annotations,Node.Id))!;
        Current=Current.ReplaceNode(CurrentCreation,CurrentCreation.WithAdditionalAnnotations(MovedAnnotation));
        BaseObjectCreationExpressionSyntax CurrentOldParentForAnnotation=FindNodeCreation(Current,OldParent.Node,AnnotationFor(Annotations,OldParent.Id))!;
        Current=Current.ReplaceNode(CurrentOldParentForAnnotation,
            CurrentOldParentForAnnotation.WithAdditionalAnnotations(OldParentAnnotation));
        BaseObjectCreationExpressionSyntax CurrentNewParentForAnnotation=FindNodeCreation(Current,NewParent.Node,AnnotationFor(Annotations,NewParent.Id))!;
        Current=Current.ReplaceNode(CurrentNewParentForAnnotation,
            CurrentNewParentForAnnotation.WithAdditionalAnnotations(NewParentAnnotation));

        BaseObjectCreationExpressionSyntax Moved=Current.GetAnnotatedNodes(MovedAnnotation).OfType<BaseObjectCreationExpressionSyntax>().Single();
        BaseObjectCreationExpressionSyntax CurrentOldParent=Current.GetAnnotatedNodes(OldParentAnnotation).OfType<BaseObjectCreationExpressionSyntax>().Single();
        BaseObjectCreationExpressionSyntax CurrentNewParent=Current.GetAnnotatedNodes(NewParentAnnotation).OfType<BaseObjectCreationExpressionSyntax>().Single();
        ExpressionSyntax?OldChildren=FindChildrenExpression(CurrentOldParent);
        if(OldChildren is null||!TryRemoveChild(OldChildren,Moved,out ExpressionSyntax UpdatedOldChildren)){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditReparentSourceChildrenUnsupported"));
        }

        Current=Current.ReplaceNode(OldChildren,UpdatedOldChildren.WithTriviaFrom(OldChildren));
        CurrentNewParent=Current.GetAnnotatedNodes(NewParentAnnotation).OfType<BaseObjectCreationExpressionSyntax>().Single();
        ExpressionSyntax?NewChildren=FindChildrenExpression(CurrentNewParent);
        if(NewChildren is not null){
            if(!TryInsertChild(NewChildren,Moved,Operation.NewIndex,out ExpressionSyntax UpdatedNewChildren)){
                return SceneSourceEditResult.Failure(VsixText.Get("SourceEditReparentDestinationChildrenUnsupported"));
            }

            return SceneSourceEditResult.Success(Current.ReplaceNode(NewChildren,UpdatedNewChildren.WithTriviaFrom(NewChildren)));
        }

        return SceneSourceEditResult.Success(Current.ReplaceNode(
            CurrentNewParent,AddChildrenMember(CurrentNewParent,Moved)));
    }

    private static SceneSourceEditResult ApplyReorderNode(
        SyntaxNode Root,SceneProjection Projection,SceneProjectionNode Node,ReorderNodeOperation Operation,
        IReadOnlyDictionary<EditorNodeId,SyntaxAnnotation>? Annotations){
        if(Node.ParentId is not { } ParentId||!Projection.TryGetNode(ParentId,out SceneProjectionNode Parent)){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditRootCannotReorder"));
        }

        BaseObjectCreationExpressionSyntax?Creation=FindNodeCreation(Root,Node.Node,AnnotationFor(Annotations,Node.Id));
        BaseObjectCreationExpressionSyntax?ParentCreation=FindNodeCreation(Root,Parent.Node,AnnotationFor(Annotations,Parent.Id));
        ExpressionSyntax?Children=ParentCreation is null?null:FindChildrenExpression(ParentCreation);
        if(Creation is null||Children is null||!TryRemoveChild(Children,Creation,out ExpressionSyntax WithoutChild)){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditReorderTargetNotStaticChild"));
        }

        if(!TryInsertChild(WithoutChild,Creation,Operation.NewIndex,out ExpressionSyntax UpdatedChildren)){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditReorderChildrenUnsupported"));
        }

        return SceneSourceEditResult.Success(Root.ReplaceNode(Children,UpdatedChildren.WithTriviaFrom(Children)));
    }

    private static SyntaxAnnotation? AnnotationFor(
        IReadOnlyDictionary<EditorNodeId,SyntaxAnnotation>? Annotations,EditorNodeId Id)=>
        Annotations is not null&&Annotations.TryGetValue(Id,out SyntaxAnnotation? Annotation)?Annotation:null;

    private static BaseObjectCreationExpressionSyntax? FindNodeCreation(
        SyntaxNode Root,SceneNode Node,SyntaxAnnotation? Annotation=null)=>
        Annotation is not null
            ?Root.GetAnnotatedNodes(Annotation).OfType<BaseObjectCreationExpressionSyntax>().SingleOrDefault()
            :Root.DescendantNodesAndSelf().OfType<BaseObjectCreationExpressionSyntax>()
                .SingleOrDefault(Candidate=>Candidate.Span==Node.Location.Span);
    /// <summary>
    /// Nodeの<c>Components = [ … ]</c>の中から、対象Componentのobject creationを型名で特定する。
    /// Spanは前段の編集でずれうるが、1つのNodeが同じComponent型を2つ持つことは
    /// <c>SceneDeclarationAnalyzer</c>がSCENE108(重複)で禁じているため、型名だけで一意に決まる。
    /// </summary>
    private static ObjectCreationExpressionSyntax? FindComponentCreationWithin(
        BaseObjectCreationExpressionSyntax NodeCreation,SceneComponent Component){
        string SimpleName=SimpleTypeNameOf(Component.TypeFullyQualifiedName);
        return NodeCreation.Initializer?.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(static Assignment=>Assignment.Left is IdentifierNameSyntax Identifier
                &&Identifier.Identifier.ValueText=="Components")?.Right
            ?.DescendantNodesAndSelf()
            .OfType<ObjectCreationExpressionSyntax>()
            .FirstOrDefault(Candidate=>SimpleTypeNameOf(Candidate.Type.ToString())==SimpleName);
    }

    /// <summary>完全修飾名・エイリアス・単純名のいずれで書かれていても比較できるよう、末尾の識別子だけを取る。</summary>
    private static string SimpleTypeNameOf(string TypeName){
        var LastDot=TypeName.LastIndexOf('.');
        return LastDot>=0?TypeName.Substring(LastDot+1):TypeName;
    }

    private static ExpressionSyntax? FindChildrenExpression(BaseObjectCreationExpressionSyntax Creation)=>
        Creation.Initializer?.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(static Assignment=>Assignment.Left is IdentifierNameSyntax Identifier
                &&Identifier.Identifier.ValueText=="Children")?.Right;

    private static BaseObjectCreationExpressionSyntax CreateNodeCreation(string Name){
        InitializerExpressionSyntax Initializer=SyntaxFactory.InitializerExpression(
            SyntaxKind.ObjectInitializerExpression,
            SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName("Name"),
                    SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,SyntaxFactory.Literal(Name)))));
        // NormalizeWhitespaceが無いと"new"と"Node"の間にトリビアが入らず"newNode"という
        // 1トークンに読めてしまう(実機ではCollection要素の直後に連結され、再解析すると壊れる)。
        return(BaseObjectCreationExpressionSyntax)SyntaxFactory.ObjectCreationExpression(SyntaxFactory.IdentifierName("Node"))
            .WithInitializer(Initializer)
            .NormalizeWhitespace();
    }

    private static BaseObjectCreationExpressionSyntax AddChildrenMember(
        BaseObjectCreationExpressionSyntax Creation,BaseObjectCreationExpressionSyntax Child){
        CollectionExpressionSyntax Children=SyntaxFactory.CollectionExpression(
            SyntaxFactory.SingletonSeparatedList<CollectionElementSyntax>(
                SyntaxFactory.ExpressionElement(Child)));
        AssignmentExpressionSyntax Assignment=SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,SyntaxFactory.IdentifierName("Children"),Children);
        InitializerExpressionSyntax Initializer=Creation.Initializer
            ??SyntaxFactory.InitializerExpression(SyntaxKind.ObjectInitializerExpression);
        return WithInitializer(Creation,Initializer.WithExpressions(Initializer.Expressions.Add(Assignment)));
    }

    private static BaseObjectCreationExpressionSyntax WithInitializer(
        BaseObjectCreationExpressionSyntax Creation,InitializerExpressionSyntax Initializer)=>Creation switch {
            ObjectCreationExpressionSyntax Explicit=>Explicit.WithInitializer(Initializer),
            ImplicitObjectCreationExpressionSyntax Implicit=>Implicit.WithInitializer(Initializer),
            _=>Creation,
        };

    private static bool TryRemoveChild(
        ExpressionSyntax Children,BaseObjectCreationExpressionSyntax Child,out ExpressionSyntax Updated){
        switch(Children){
            case CollectionExpressionSyntax Collection:
                ExpressionElementSyntax?Element=Collection.Elements.OfType<ExpressionElementSyntax>()
                    .FirstOrDefault(Candidate=>Candidate.Expression.Span==Child.Span);
                Updated=Element is null?Children:Collection.WithElements(Collection.Elements.Remove(Element));
                return Element is not null;
            case ArrayCreationExpressionSyntax { Initializer:{ }Initializer }Array:
                Updated=Initializer.Expressions.Any(Candidate=>Candidate.Span==Child.Span)
                    ?Array.WithInitializer(Initializer.WithExpressions(
                        Initializer.Expressions.Remove(Initializer.Expressions.First(Candidate=>Candidate.Span==Child.Span))))
                    :Children;
                return Updated!=Children;
            case ImplicitArrayCreationExpressionSyntax { Initializer:{ }Initializer }Implicit:
                Updated=Initializer.Expressions.Any(Candidate=>Candidate.Span==Child.Span)
                    ?Implicit.WithInitializer(Initializer.WithExpressions(
                        Initializer.Expressions.Remove(Initializer.Expressions.First(Candidate=>Candidate.Span==Child.Span))))
                    :Children;
                return Updated!=Children;
            default:
                Updated=Children;
                return false;
        }
    }

    private static bool TryInsertChild(
        ExpressionSyntax Children,BaseObjectCreationExpressionSyntax Child,int Index,out ExpressionSyntax Updated){
        switch(Children){
            case CollectionExpressionSyntax Collection:
                int CollectionIndex=Math.Max(0,Math.Min(Index,Collection.Elements.Count));
                Updated=Collection.WithElements(Collection.Elements.Insert(
                    CollectionIndex,SyntaxFactory.ExpressionElement(Child)));
                return true;
            case ArrayCreationExpressionSyntax { Initializer:{ }Initializer }Array:
                int ArrayIndex=Math.Max(0,Math.Min(Index,Initializer.Expressions.Count));
                Updated=Array.WithInitializer(Initializer.WithExpressions(
                    Initializer.Expressions.Insert(ArrayIndex,Child)));
                return true;
            case ImplicitArrayCreationExpressionSyntax { Initializer:{ }Initializer }Implicit:
                int ImplicitIndex=Math.Max(0,Math.Min(Index,Initializer.Expressions.Count));
                Updated=Implicit.WithInitializer(Initializer.WithExpressions(
                    Initializer.Expressions.Insert(ImplicitIndex,Child)));
                return true;
            default:
                Updated=Children;
                return false;
        }
    }

    private static string CreateUniqueNodeName(IEnumerable<string>ExistingNames,string RequestedName){
        string BaseName=string.IsNullOrWhiteSpace(RequestedName)?"Node":RequestedName.Trim();
        HashSet<string>Names=ExistingNames.ToHashSet(StringComparer.Ordinal);
        if(!Names.Contains(BaseName)){ return BaseName; }

        for(int Suffix=1;;Suffix++){
            string Candidate=$"{BaseName} ({Suffix})";
            if(!Names.Contains(Candidate)){ return Candidate; }
        }
    }

    /// <summary>
    /// 親の<c>Children = [ … ]</c>に実際に書かれている子の<c>Name</c>を、書き戻し中の構文木から集める。
    /// 名前が省略された子は採番の衝突対象にならないので飛ばす。
    /// </summary>
    private static IEnumerable<string>EnumerateChildNodeNames(BaseObjectCreationExpressionSyntax ParentCreation){
        if(FindChildrenExpression(ParentCreation)is not { }Children){
            yield break;
        }

        // 子リストは TryAppendChild と同じ3形態を取りうる(コレクション式 / 明示配列 / 暗黙配列)。
        IEnumerable<ExpressionSyntax>Elements=Children switch {
            CollectionExpressionSyntax Collection=>Collection.Elements
                .OfType<ExpressionElementSyntax>().Select(static Element=>Element.Expression),
            ArrayCreationExpressionSyntax { Initializer:{ }ArrayInitializer }=>ArrayInitializer.Expressions,
            ImplicitArrayCreationExpressionSyntax { Initializer:{ }ImplicitInitializer }=>ImplicitInitializer.Expressions,
            _=>[],
        };

        foreach(ExpressionSyntax Element in Elements){
            if(Element is BaseObjectCreationExpressionSyntax Child
                &&FindMemberValue(Child,"Name")is LiteralExpressionSyntax { Token.Value:string Name }){
                yield return Name;
            }
        }
    }
    private static SceneSourceEditResult ApplyRename(
        SyntaxNode Root,SceneProjectionNode Node,RenameNodeOperation Operation,
        IReadOnlyDictionary<EditorNodeId,SyntaxAnnotation>? Annotations){
        if(string.IsNullOrWhiteSpace(Operation.NewName)){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditNodeNameEmpty"));
        }

        BaseObjectCreationExpressionSyntax?Creation=FindNodeCreation(Root,Node.Node,AnnotationFor(Annotations,Node.Id));
        if(Creation is null){
            return SceneSourceEditResult.Failure(VsixText.Get("SourceEditRenameSyntaxMissing"));
        }

        // M0051(既存バグの修正): 以前はNameの右辺を`NameValueLocation.Span`との一致だけで探していたため、
        // 同一Nodeを2回目にRenameすると(1回目でテキスト長が変わり)Spanがずれて
        // 「Name リテラルがソースに見つかりません」で失敗していた。Creation自体はAnnotationで
        // 正しく追跡できているので、その初期化子の中から`Name = …`を直接引く。
        // Spanでの検索は、Annotationが無い経路(単発Apply)のフォールバックとしてだけ残す。
        ExpressionSyntax?OldExpression=FindMemberValue(Creation,"Name")
            ??(Node.Node.NameValueLocation is { } Location?FindExpression(Root,Location.Span):null);
        if(OldExpression is not null){
            ExpressionSyntax NewExpression=SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(Operation.NewName));
            return SceneSourceEditResult.Success(Root.ReplaceNode(OldExpression,NewExpression.WithTriviaFrom(OldExpression)));
        }

        InitializerExpressionSyntax Initializer=Creation.Initializer
            ??SyntaxFactory.InitializerExpression(SyntaxKind.ObjectInitializerExpression);
        AssignmentExpressionSyntax Assignment=SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxFactory.IdentifierName("Name"),
            SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,SyntaxFactory.Literal(Operation.NewName)));
        BaseObjectCreationExpressionSyntax Updated=WithInitializer(
            Creation,Initializer.WithExpressions(Initializer.Expressions.Insert(0,Assignment)));
        return SceneSourceEditResult.Success(Root.ReplaceNode(Creation,Updated));
    }

    /// <summary>
    /// object初期化子から<c>MemberName = 値</c>の右辺を引く。Spanと違い前段の編集でずれない。
    /// </summary>
    private static ExpressionSyntax?FindMemberValue(BaseObjectCreationExpressionSyntax Creation,string MemberName)=>
        Creation.Initializer?.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(Assignment=>Assignment.Left is IdentifierNameSyntax Identifier
                &&Identifier.Identifier.ValueText==MemberName)?.Right;

    private static ExpressionSyntax?FindExpression(SyntaxNode Root,TextSpan Span)=>
        Root.DescendantNodesAndSelf().OfType<ExpressionSyntax>().SingleOrDefault(Node=>Node.Span==Span);

    /// <summary>Shared public RUF Transform vectors for source and predicted-model updates.</summary>
    internal static IEnumerable<(string Name,Vector3 Value,Vector3 Default)>EnumerateTransformVectors(
        SceneTransformValue Transform,SceneTransformMemberMask Members){
        if((Members&SceneTransformMemberMask.Position)!=0){ yield return("Position",Transform.Position,Vector3.Zero); }
        if((Members&SceneTransformMemberMask.Rotation)!=0){ yield return("Rotation",Transform.EulerRotation,Vector3.Zero); }
        if((Members&SceneTransformMemberMask.Scale)!=0){ yield return("Scale",Transform.Scale,Vector3.One); }
    }
    /// <summary>M0038: <see cref="SceneModelEditApplier"/>と共有(<see cref="EnumerateTransformMembers"/>参照)。</summary>
    internal static float ReadFloat(object?Value,float Default)=>Value switch {
        float Float=>Float,
        double Double=>(float)Double,
        decimal Decimal=>(float)Decimal,
        int Integer=>Integer,
        long Long=>Long,
        _=>Default,
    };

    /// <summary>M0038: <see cref="SceneModelEditApplier"/>と共有(<see cref="EnumerateTransformMembers"/>参照)。</summary>
    internal static string FormatFloat(float Value){
        if(float.IsNaN(Value)||float.IsInfinity(Value)){
            throw new ArgumentOutOfRangeException(nameof(Value),VsixText.Get("SourceEditNonFiniteTransform"));
        }
        return Value.ToString("R",CultureInfo.InvariantCulture)+"f";
    }

    /// <summary>M0038: <see cref="SceneModelEditApplier"/>と共有(<see cref="EnumerateTransformMembers"/>参照)。</summary>
    internal static bool IsGenerated(string?FilePath)=>
        FilePath is { Length:>0 }&&FilePath.EndsWith(".g.cs",StringComparison.OrdinalIgnoreCase);
}
