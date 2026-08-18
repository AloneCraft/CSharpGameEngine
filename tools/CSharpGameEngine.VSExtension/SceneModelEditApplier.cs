using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0038/M0047: 編集操作をimmutableな<see cref="SceneModel"/>へ関数的に適用する純粋層。
///
/// <para>
/// M0047で<b>このモデルがセッション中の正本になった</b>(<see cref="SceneEditDocument"/>参照)。
/// C#ソースへの書き戻しは保存操作でのみ発生し、値編集・構造編集のどちらも、この層の上で
/// 即時・同期的に完結する。以前(M0038〜M0046)はここが「Roslyn再解析を待つ間の先読み表示」に
/// すぎなかったが、その位置づけは終わった。
/// </para>
///
/// <para>
/// <see cref="SceneNode.Path"/>はRootからの子インデックス列であり、構造操作(追加・削除・親変更・
/// 並べ替え)は影響を受けた親の子リスト以下のPathを再計算する必要がある。同一性(<see cref="EditorNodeId"/>)は
/// <see cref="SceneProjectionReconciler"/>が担うので、構造操作ごとに投影を再構築し直し、後続の操作の
/// NodeId解決を常に最新のツリーへ対して行う。
/// </para>
/// </summary>
internal static class SceneModelEditApplier {
    internal static bool TryApply(
        SceneModel Base,SceneProjection Projection,SceneProjectionReconciler Reconciler,SceneKey Key,
        IReadOnlyList<SceneEditOperation>Operations,out SceneModel Predicted,out SceneProjection PredictedProjection){
        Predicted=Base;
        PredictedProjection=Projection;
        if(Base.Root is not { }Root||Operations.Count==0||Base.HasErrors){
            return false;
        }

        SceneNode CurrentRoot=Root;
        SceneProjection CurrentProjection=Projection;

        foreach(SceneEditOperation Operation in Operations){
            if(!IsSupported(Operation)||!CurrentProjection.TryGetNode(Operation.NodeId,out SceneProjectionNode TargetNode)){
                return false;
            }

            SceneProjection ProjectionBeforeOp=CurrentProjection;
            if(!TryApplyOne(CurrentRoot,CurrentProjection,TargetNode,Operation,out CurrentRoot,out NodePath?MovedToPath)){
                return false;
            }

            // 値編集はPath/同一性を変えないが、常に再投影しても安価(シーンは小規模)であり、
            // 個別に安全判定を分岐させるより単純で確実。
            SceneProjection Naive=Reconciler.Reconcile(Key,RebuildModel(Base,CurrentRoot),CurrentProjection);

            // SceneProjectionReconcilerの同一性判定は「同じ親の子」の範囲でしか一致を探さない
            // (ドキュメント参照)。親を跨ぐReparentでは移動先の親の下に旧兄弟が無いため、素直に
            // 再投影すると移動したNode(と、あれば配下)へ新しいIDが振られてしまう。ここだけは
            // 「元のID」を明示的に引き継ぐ特別扱いをする。
            CurrentProjection=Operation is ReparentNodeOperation&&MovedToPath is { }NewPath
                ?PreserveIdentityAfterMove(Key,ProjectionBeforeOp,Naive,TargetNode.Node,NewPath)
                :Naive;
        }

        Predicted=RebuildModel(Base,CurrentRoot);
        PredictedProjection=CurrentProjection;
        return true;
    }

    /// <summary>
    /// Reparent直後の再投影で、移動した部分木(と配下)が新しいIDを振られてしまった分だけ、
    /// 元のIDへ差し戻す。<paramref name="OldShape"/>(移動前のNode、Pathは移動前の絶対Path)と
    /// <paramref name="NewPath"/>(移動後の絶対Path)を同じ相対構造で並走させ、
    /// 「Naiveでのミント済みID → Beforeでの元ID」の対応表を作ってから貼り替える。
    /// </summary>
    private static SceneProjection PreserveIdentityAfterMove(
        SceneKey Key,SceneProjection Before,SceneProjection Naive,SceneNode OldShape,NodePath NewPath){
        Dictionary<EditorNodeId,EditorNodeId>NaiveToOld=[];
        void Walk(SceneNode Shape,NodePath AtNewPath){
            if(Before.TryGetId(Shape.Path,out EditorNodeId OldId)&&Naive.TryGetId(AtNewPath,out EditorNodeId NaiveId)
                &&!NaiveId.Equals(OldId)){
                NaiveToOld[NaiveId]=OldId;
            }
            for(var Index=0;Index<Shape.Children.Length;Index++){
                Walk(Shape.Children[Index],AtNewPath.Append(Index));
            }
        }
        Walk(OldShape,NewPath);
        if(NaiveToOld.Count==0){
            return Naive;
        }

        List<SceneProjectionNode>Corrected=[];
        foreach(SceneProjectionNode Node in Naive.Nodes){
            EditorNodeId FinalId=NaiveToOld.TryGetValue(Node.Id,out EditorNodeId OldId)?OldId:Node.Id;
            EditorNodeId?FinalParentId=Node.ParentId is { }ParentId
                ?(NaiveToOld.TryGetValue(ParentId,out EditorNodeId OldParentId)?OldParentId:ParentId)
                :null;
            Corrected.Add(new SceneProjectionNode(FinalId,FinalParentId,Node.Node));
        }
        return new SceneProjection(Key,Corrected);
    }

    private static bool IsSupported(SceneEditOperation Operation)=>
        Operation is SetTransformOperation or SetComponentMemberOperation
            or RenameNodeOperation or AddNodeOperation or RemoveNodeOperation
            or ReparentNodeOperation or ReorderNodeOperation;

    private static bool TryApplyOne(
        SceneNode Root,SceneProjection Projection,SceneProjectionNode TargetNode,SceneEditOperation Operation,
        out SceneNode Updated,out NodePath?MovedToPath){
        MovedToPath=null;
        NodePath TargetPath=TargetNode.Path;
        switch(Operation){
            case SetTransformOperation SetTransform: {
                SceneNode Live=GetAtPath(Root,TargetPath);
                if(!TryApplySetTransform(Live,SetTransform,out SceneNode NewLive)){
                    Updated=Root;
                    return false;
                }
                Updated=ReferenceEquals(NewLive,Live)?Root:ReplaceAtPath(Root,TargetPath,_=>NewLive);
                return true;
            }
            case SetComponentMemberOperation SetMember: {
                SceneNode Live=GetAtPath(Root,TargetPath);
                if(!TryApplySetMember(Live,SetMember,out SceneNode NewLive)){
                    Updated=Root;
                    return false;
                }
                Updated=ReplaceAtPath(Root,TargetPath,_=>NewLive);
                return true;
            }
            case RenameNodeOperation Rename: {
                string NewName=Rename.NewName?.Trim()??"";
                if(NewName.Length==0){
                    Updated=Root;
                    return false;
                }
                Updated=ReplaceAtPath(Root,TargetPath,Live=>WithName(Live,NewName));
                return true;
            }
            case AddNodeOperation Add:
                return TryAddNode(Root,TargetPath,Add.NodeName,out Updated);
            case RemoveNodeOperation:
                if(TargetPath.IsRoot||TargetPath.Parent is not { }ParentPath){
                    Updated=Root;
                    return false;
                }
                return TryRemoveChildAt(Root,ParentPath,TargetPath.Indices[^1],out Updated);
            case ReorderNodeOperation Reorder:
                if(TargetPath.IsRoot||TargetPath.Parent is not { }ReorderParentPath){
                    Updated=Root;
                    return false;
                }
                return TryReorderChild(Root,ReorderParentPath,TargetPath.Indices[^1],Reorder.NewIndex,out Updated);
            case ReparentNodeOperation Reparent:
                return TryReparent(Root,Projection,TargetNode,Reparent,out Updated,out MovedToPath);
            default:
                Updated=Root;
                return false;
        }
    }

    private static bool TryReparent(
        SceneNode Root,SceneProjection Projection,SceneProjectionNode TargetNode,ReparentNodeOperation Operation,
        out SceneNode Updated,out NodePath?NewPath){
        Updated=Root;
        NewPath=null;
        if(!Projection.TryGetNode(Operation.NewParentNodeId,out SceneProjectionNode NewParentNode)){
            return false;
        }

        NodePath TargetPath=TargetNode.Path;
        NodePath NewParentPath=NewParentNode.Path;
        if(TargetPath.Equals(NewParentPath)||NewParentPath.IsDescendantOf(TargetPath)){
            return false;
        }
        if(TargetPath.IsRoot||TargetPath.Parent is not { }OldParentPath){
            return false;
        }

        if(OldParentPath.Equals(NewParentPath)){
            int SameParentClamped=Math.Max(0,Math.Min(
                Operation.NewIndex,GetAtPath(Root,OldParentPath).Children.Length-1));
            NewPath=OldParentPath.Append(SameParentClamped);
            return TryReorderChild(Root,OldParentPath,TargetPath.Indices[^1],Operation.NewIndex,out Updated);
        }

        SceneNode MovedContent=GetAtPath(Root,TargetPath);
        if(!TryRemoveChildAt(Root,OldParentPath,TargetPath.Indices[^1],out SceneNode AfterRemoval)){
            return false;
        }

        // NewParentのPathは削除で兄弟インデックスがずれている可能性があるため、削除後のツリーで
        // 同じEditorNodeIdを引き直す。IDの割り当ては呼び出し元(TryApply)のReconcilerと共有する
        // 必要があるため、ここでは新規Reconcilerを使わず、単純な名前+Component構成の再帰探索で
        // 対象を再特定する(削除操作はID空間を消費しないため、この場に限り軽量な再探索で足りる)。
        if(!TryFindByIdentity(AfterRemoval,NewParentNode.Node,out NodePath ResolvedNewParentPath)){
            return false;
        }

        SceneNode ResolvedNewParent=GetAtPath(AfterRemoval,ResolvedNewParentPath);
        int ClampedIndex=Math.Max(0,Math.Min(Operation.NewIndex,ResolvedNewParent.Children.Length));
        NewPath=ResolvedNewParentPath.Append(ClampedIndex);
        return TryInsertChildAt(AfterRemoval,ResolvedNewParentPath,ClampedIndex,MovedContent,out Updated);
    }

    /// <summary>
    /// 削除操作の直後に、削除前のNode参照(Name+Component構成で一意なはず)から現在のPathを
    /// 再特定する。Reparent専用の軽量フォールバック — Reconcilerのように新規IDを払い出さない。
    /// </summary>
    private static bool TryFindByIdentity(SceneNode Root,SceneNode Target,out NodePath Found){
        if(ReferenceEquals(Root,Target)){
            Found=Root.Path;
            return true;
        }
        for(var Index=0;Index<Root.Children.Length;Index++){
            if(TryFindByIdentity(Root.Children[Index],Target,out Found)){
                return true;
            }
        }
        Found=default;
        return false;
    }

    private static SceneNode GetAtPath(SceneNode Root,NodePath Path){
        SceneNode Current=Root;
        foreach(int Index in Path.Indices){
            Current=Current.Children[Index];
        }
        return Current;
    }

    private static SceneNode ReplaceAtPath(SceneNode Root,NodePath Path,Func<SceneNode,SceneNode>Transform)=>
        ReplaceAtPathCore(Root,Path.Indices,0,Transform);

    private static SceneNode ReplaceAtPathCore(
        SceneNode Node,ImmutableArray<int>Indices,int Depth,Func<SceneNode,SceneNode>Transform){
        if(Depth==Indices.Length){
            return Transform(Node);
        }
        int ChildIndex=Indices[Depth];
        SceneNode UpdatedChild=ReplaceAtPathCore(Node.Children[ChildIndex],Indices,Depth+1,Transform);
        return ReferenceEquals(UpdatedChild,Node.Children[ChildIndex])
            ?Node
            :WithChildren(Node,Node.Children.SetItem(ChildIndex,UpdatedChild));
    }

    /// <summary>
    /// 親の子リストを丸ごと差し替え、全子孫のPathを新しい位置から再計算する。子リストが変わると
    /// 影響を受けた要素以降の兄弟インデックスがずれるため、変更点だけでなく全要素を再計算する。
    /// </summary>
    private static SceneNode WithChildrenList(SceneNode Parent,ImmutableArray<SceneNode>NewChildrenContent){
        ImmutableArray<SceneNode>.Builder Rebased=ImmutableArray.CreateBuilder<SceneNode>(NewChildrenContent.Length);
        for(var Index=0;Index<NewChildrenContent.Length;Index++){
            Rebased.Add(RebasePath(NewChildrenContent[Index],Parent.Path.Append(Index)));
        }
        return WithChildren(Parent,Rebased.MoveToImmutable());
    }

    private static SceneNode RebasePath(SceneNode Node,NodePath NewPath){
        ImmutableArray<SceneNode>Children=Node.Children;
        bool Changed=!Node.Path.Equals(NewPath);
        ImmutableArray<SceneNode>.Builder?NewChildren=null;
        for(var Index=0;Index<Children.Length;Index++){
            SceneNode Rebased=RebasePath(Children[Index],NewPath.Append(Index));
            if(!ReferenceEquals(Rebased,Children[Index])){
                Changed=true;
                NewChildren??=Children.ToBuilder();
                NewChildren[Index]=Rebased;
            }
        }
        if(!Changed){
            return Node;
        }
        return new SceneNode(
            NewPath,Node.Name,Node.Components,NewChildren?.ToImmutable()??Children,
            Node.Location,Node.InitializerLocation,Node.NameValueLocation,
            Node.ComponentsListLocation,Node.ChildrenListLocation);
    }

    private static bool TryAddNode(SceneNode Root,NodePath ParentPath,string RequestedName,out SceneNode NewRoot){
        NewRoot=Root;
        SceneNode Parent=GetAtPath(Root,ParentPath);
        if(SceneSourceEditor.IsGenerated(Parent.Location.FilePath)){
            return false;
        }

        string Name=CreateUniqueNodeName(Parent,RequestedName);
        SceneNode NewChild=new(
            NodePath.Root,Name,ImmutableArray<SceneComponent>.Empty,ImmutableArray<SceneNode>.Empty,
            SceneSourceLocation.None,null,null,null,null);
        NewRoot=ReplaceAtPath(Root,ParentPath,P=>WithChildrenList(P,P.Children.Add(NewChild)));
        return true;
    }

    private static bool TryRemoveChildAt(SceneNode Root,NodePath ParentPath,int LocalIndex,out SceneNode NewRoot){
        NewRoot=Root;
        SceneNode Parent=GetAtPath(Root,ParentPath);
        if(LocalIndex<0||LocalIndex>=Parent.Children.Length
            ||SceneSourceEditor.IsGenerated(Parent.Children[LocalIndex].Location.FilePath)){
            return false;
        }

        NewRoot=ReplaceAtPath(Root,ParentPath,P=>WithChildrenList(P,P.Children.RemoveAt(LocalIndex)));
        return true;
    }

    private static bool TryReorderChild(
        SceneNode Root,NodePath ParentPath,int LocalIndex,int RequestedNewIndex,out SceneNode NewRoot){
        NewRoot=Root;
        SceneNode Parent=GetAtPath(Root,ParentPath);
        if(LocalIndex<0||LocalIndex>=Parent.Children.Length){
            return false;
        }

        SceneNode Moved=Parent.Children[LocalIndex];
        ImmutableArray<SceneNode>Without=Parent.Children.RemoveAt(LocalIndex);
        int Clamped=Math.Max(0,Math.Min(RequestedNewIndex,Without.Length));
        NewRoot=ReplaceAtPath(Root,ParentPath,P=>WithChildrenList(P,Without.Insert(Clamped,Moved)));
        return true;
    }

    private static bool TryInsertChildAt(
        SceneNode Root,NodePath ParentPath,int Index,SceneNode Content,out SceneNode NewRoot){
        NewRoot=Root;
        SceneNode Parent=GetAtPath(Root,ParentPath);
        int Clamped=Math.Max(0,Math.Min(Index,Parent.Children.Length));
        NewRoot=ReplaceAtPath(Root,ParentPath,P=>WithChildrenList(P,P.Children.Insert(Clamped,Content)));
        return true;
    }

    /// <summary><see cref="SceneSourceEditor"/>の<c>CreateUniqueNodeName</c>と同じ規則(重複時は " (N)" を付与)。</summary>
    private static string CreateUniqueNodeName(SceneNode Parent,string RequestedName){
        string BaseName=string.IsNullOrWhiteSpace(RequestedName)?"Node":RequestedName.Trim();
        HashSet<string>Names=Parent.Children
            .Where(static Child=>Child.Name is not null)
            .Select(static Child=>Child.Name!)
            .ToHashSet(StringComparer.Ordinal);
        if(!Names.Contains(BaseName)){
            return BaseName;
        }
        for(var Suffix=1;;Suffix++){
            string Candidate=$"{BaseName} ({Suffix})";
            if(!Names.Contains(Candidate)){
                return Candidate;
            }
        }
    }

    private static SceneNode WithName(SceneNode Node,string Name)=>
        new(Node.Path,Name,Node.Components,Node.Children,Node.Location,Node.InitializerLocation,
            Node.NameValueLocation,Node.ComponentsListLocation,Node.ChildrenListLocation);

    private static bool TryApplySetTransform(
        SceneNode Node,SetTransformOperation Operation,out SceneNode Updated){
        Updated=Node;
        SceneComponent?Transform=SceneComponentBindings.FindComponent(Node,SceneComponentRole.LocalTransform);
        if(Transform is null||SceneSourceEditor.IsGenerated(Transform.Location.FilePath)){
            return false;
        }

        ImmutableArray<SceneComponentMember>Members=Transform.Members;
        foreach((string Name,float Value,float Default)in
            SceneSourceEditor.EnumerateTransformMembers(Operation.Transform,Operation.Members)){
            SceneComponentMember?Existing=FindMember(Members,Name);
            float Current=Existing is null?Default:SceneSourceEditor.ReadFloat(Existing.Value.ConstantValue,Default);
            // ソース層(SceneSourceEditor.TryUpdateTransformCreation)と同じ判定にする。
            // 値が変わらないメンバーは書かないため、予測モデルにも書いてはならない。
            if(SceneTransformMath.NearlyEqual(Current,Value)){
                continue;
            }
            if(Existing is not null&&!Existing.Value.IsEditable){
                return false;
            }
            Members=SetFloatMember(Members,Existing,Name,Value,Transform.Location);
        }

        if(Members.Equals(Transform.Members)){
            return true;
        }
        Updated=ReplaceComponent(Node,Transform,WithMembers(Transform,Members));
        return true;
    }

    private static bool TryApplySetMember(
        SceneNode Node,SetComponentMemberOperation Operation,out SceneNode Updated){
        Updated=Node;
        SceneComponent?Component=Node.FindComponent(Operation.ComponentTypeFullyQualifiedName);
        if(Component is null||SceneSourceEditor.IsGenerated(Component.Location.FilePath)){
            return false;
        }

        SceneComponentMember?Existing=FindMember(Component.Members,Operation.MemberName);
        if(Existing is not null&&!Existing.Value.IsEditable){
            return false;
        }
        if(!TryParseLiteral(Operation.ValueExpression,Existing,out object?ConstantValue)){
            return false;
        }

        SceneValue Value=CreateLiteral(
            ConstantValue,Operation.ValueExpression.Trim(),Existing,Component.Location);
        ImmutableArray<SceneComponentMember>Members=SetMember(
            Component.Members,Existing,Operation.MemberName,Value,Component.Location);
        Updated=ReplaceComponent(Node,Component,WithMembers(Component,Members));
        return true;
    }

    /// <summary>
    /// リテラルとして確実に解釈できる式だけを受け付ける。定数式・静的参照・ファクトリ呼び出しなどは
    /// 解決にセマンティックモデルが要るため、ここでは素直に諦めて従来経路へ落とす。
    /// </summary>
    private static bool TryParseLiteral(
        string Expression,SceneComponentMember?Existing,out object?ConstantValue){
        ConstantValue=null;
        string Text=Expression.Trim();
        if(Text.Length==0){
            return false;
        }
        if(bool.TryParse(Text,out bool Boolean)){
            ConstantValue=Boolean;
            return true;
        }

        bool HasFloatSuffix=Text.EndsWith("f",System.StringComparison.OrdinalIgnoreCase);
        string Numeric=HasFloatSuffix?Text.Substring(0,Text.Length-1):Text;
        if(!float.TryParse(Numeric,NumberStyles.Float,CultureInfo.InvariantCulture,out float Number)
            ||float.IsNaN(Number)||float.IsInfinity(Number)){
            return false;
        }

        // 既存メンバーの定数型に合わせる(再解析後のConstantValueと型まで一致させるため)。
        // 既存が無い/型が読めない場合はfサフィックスと小数点の有無で判断する。
        if(Existing?.Value.ConstantValue is int&&!HasFloatSuffix&&Numeric.IndexOf('.')<0){
            ConstantValue=(int)Number;
            return true;
        }
        if(Existing?.Value.ConstantValue is null&&!HasFloatSuffix&&Numeric.IndexOf('.')<0){
            ConstantValue=(int)Number;
            return true;
        }
        ConstantValue=Number;
        return true;
    }

    private static ImmutableArray<SceneComponentMember>SetFloatMember(
        ImmutableArray<SceneComponentMember>Members,SceneComponentMember?Existing,
        string Name,float Value,SceneSourceLocation Fallback){
        string Text=SceneSourceEditor.FormatFloat(Value);
        SceneValue NewValue=CreateLiteral(Value,Text,Existing,Fallback);
        return SetMember(Members,Existing,Name,NewValue,Fallback);
    }

    private static ImmutableArray<SceneComponentMember>SetMember(
        ImmutableArray<SceneComponentMember>Members,SceneComponentMember?Existing,
        string Name,SceneValue Value,SceneSourceLocation Fallback){
        if(Existing is not null){
            var Replaced=new SceneComponentMember(Name,Existing.MemberSymbol,Value,Existing.Location);
            return Members.SetItem(Members.IndexOf(Existing),Replaced);
        }
        return Members.Add(new SceneComponentMember(Name,MemberSymbol:null,Value,Fallback));
    }

    private static SceneValue CreateLiteral(
        object?ConstantValue,string Text,SceneComponentMember?Existing,SceneSourceLocation Fallback)=>
        new(SceneValueKind.Literal,
            Existing?.Value.Type,
            ConstantValue,
            ReferencedSymbol:null,
            ImmutableArray<SceneComponentMember>.Empty,
            ImmutableArray<SceneValue>.Empty,
            ImmutableArray<SceneValue>.Empty,
            EmitExpressionText:Text,
            SourceExpressionText:Text,
            Existing?.Value.Location??Fallback);

    private static SceneComponentMember?FindMember(ImmutableArray<SceneComponentMember>Members,string Name){
        foreach(SceneComponentMember Member in Members){
            if(string.Equals(Member.MemberName,Name,System.StringComparison.Ordinal)){
                return Member;
            }
        }
        return null;
    }

    private static SceneNode ReplaceComponent(SceneNode Node,SceneComponent Old,SceneComponent New)=>
        new(Node.Path,Node.Name,Node.Components.SetItem(Node.Components.IndexOf(Old),New),Node.Children,
            Node.Location,Node.InitializerLocation,Node.NameValueLocation,
            Node.ComponentsListLocation,Node.ChildrenListLocation);

    private static SceneNode WithChildren(SceneNode Node,ImmutableArray<SceneNode>Children)=>
        new(Node.Path,Node.Name,Node.Components,Children,
            Node.Location,Node.InitializerLocation,Node.NameValueLocation,
            Node.ComponentsListLocation,Node.ChildrenListLocation);

    private static SceneComponent WithMembers(
        SceneComponent Component,ImmutableArray<SceneComponentMember>Members)=>
        new(Component.TypeSymbol,Component.TypeFullyQualifiedName,Component.ConstructorArguments,
            Members,Component.EmitExpressionText,Component.Location);

    private static SceneModel RebuildModel(SceneModel Base,SceneNode NewRoot)=>
        new(Base.Namespace,Base.ClassName,Base.ClassFullyQualifiedName,Base.ClassSymbol,
            NewRoot,Base.IsStartupScene,Base.StartupOrder,Base.Diagnostics,
            Base.FilePath,Base.DeclarationPosition);
}
