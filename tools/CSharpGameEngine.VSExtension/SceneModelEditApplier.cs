using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// 編集操作をimmutableな<see cref="SceneModel"/>へ関数的に適用する純粋層。
///
/// <para>
/// このmodelは1 gestureの予測・検証にだけ使う派生値で、authoring truthは常にC# sourceである。
/// 適用結果は同じtransaction内でsourceへ変換・再解析検証され、live bufferへcommitされる。
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
        if(Base.Root is not { }Root||Operations.Count==0
            ||(Base.HasErrors&&Operations.Any(static Operation=>
                Operation is not NormalizeComponentsOperation))){
            return false;
        }

        var CurrentRoot=Root;
        var CurrentProjection=Projection;

        foreach(var Operation in Operations){
            if(!IsSupported(Operation)||!CurrentProjection.TryGetNode(Operation.NodeId,out var TargetNode)){
                return false;
            }

            var ProjectionBeforeOp=CurrentProjection;
            if(!TryApplyOne(CurrentRoot,CurrentProjection,TargetNode,Operation,out CurrentRoot,out var MovedToPath)){
                return false;
            }

            // 値編集はPath/同一性を変えないが、常に再投影しても安価(シーンは小規模)であり、
            // 個別に安全判定を分岐させるより単純で確実。
            var Naive=Reconciler.Reconcile(Key,RebuildModel(Base,CurrentRoot),CurrentProjection);

            // SceneProjectionReconcilerの同一性判定は「同じ親の子」の範囲でしか一致を探さず、
            // 照合キーは「ノード名 + Component型名の連結」である(ドキュメント参照)。そのため
            // 素直に再投影すると次の2つでNode(と配下)へ新しいIDが振られてしまう:
            //   - Reparent: 移動先の親の下に旧兄弟が無いのでキーを突き合わせる相手が居ない。
            //   - Component増減: Pathは変わらないが照合キー自体が変わるので一致しない。
            // どちらも選択・Reveal・同じgesture内の後続操作が持つEditorNodeIdを宙に浮かせる(実機報告:
            // パレットからドロップするとノードの選択が外れる)ので、元のIDを明示的に引き継ぐ。
            CurrentProjection=Operation switch {
                ReparentNodeOperation when MovedToPath is { }NewPath=>
                    PreserveNodeIdentity(Key,ProjectionBeforeOp,Naive,TargetNode.Node,NewPath),
                AddComponentOperation or RemoveComponentOperation or NormalizeComponentsOperation or ConvertLightOperation=>
                    PreserveNodeIdentity(Key,ProjectionBeforeOp,Naive,TargetNode.Node,TargetNode.Node.Path),
                _=>Naive,
            };
        }

        Predicted=RebuildModel(Base,CurrentRoot);
        PredictedProjection=CurrentProjection;
        return true;
    }

    /// <summary>
    /// 再投影で新しいIDを振られてしまった部分木を、元のIDへ差し戻す。
    /// <paramref name="OldShape"/>(操作前のNode、Pathは操作前の絶対Path)と
    /// <paramref name="NewPath"/>(操作後の絶対Path。Component増減のようにPathが変わらない操作では
    /// <c>OldShape.Path</c>と同じ)を同じ相対構造で並走させ、
    /// 「Naiveでのミント済みID → Beforeでの元ID」の対応表を作ってから貼り替える。
    /// </summary>
    private static SceneProjection PreserveNodeIdentity(
        SceneKey Key,SceneProjection Before,SceneProjection Naive,SceneNode OldShape,NodePath NewPath){
        Dictionary<EditorNodeId,EditorNodeId>NaiveToOld=[];
        void Walk(SceneNode Shape,NodePath AtNewPath){
            if(Before.TryGetId(Shape.Path,out var OldId)&&Naive.TryGetId(AtNewPath,out var NaiveId)
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
        foreach(var Node in Naive.Nodes){
            var FinalId=NaiveToOld.TryGetValue(Node.Id,out var OldId)?OldId:Node.Id;
            EditorNodeId?FinalParentId=Node.ParentId is { }ParentId
                ?(NaiveToOld.TryGetValue(ParentId,out var OldParentId)?OldParentId:ParentId)
                :null;
            Corrected.Add(new SceneProjectionNode(FinalId,FinalParentId,Node.Node));
        }
        return new SceneProjection(Key,Corrected);
    }

    private static bool IsSupported(SceneEditOperation Operation)=>
        Operation is SetTransformOperation or SetComponentMemberOperation
            or RenameNodeOperation or AddNodeOperation or RemoveNodeOperation
            or ReparentNodeOperation or ReorderNodeOperation or DuplicateNodeOperation
            or AddComponentOperation or RemoveComponentOperation or NormalizeComponentsOperation or ConvertLightOperation;

    private static bool TryApplyOne(
        SceneNode Root,SceneProjection Projection,SceneProjectionNode TargetNode,SceneEditOperation Operation,
        out SceneNode Updated,out NodePath?MovedToPath){
        MovedToPath=null;
        var TargetPath=TargetNode.Path;
        switch(Operation){
            case SetTransformOperation SetTransform: {
                var Live=GetAtPath(Root,TargetPath);
                if(!TryApplySetTransform(Live,SetTransform,out var NewLive)){
                    Updated=Root;
                    return false;
                }
                Updated=ReferenceEquals(NewLive,Live)?Root:ReplaceAtPath(Root,TargetPath,_=>NewLive);
                return true;
            }
            case SetComponentMemberOperation SetMember: {
                var Live=GetAtPath(Root,TargetPath);
                if(!TryApplySetMember(Live,SetMember,out var NewLive)){
                    Updated=Root;
                    return false;
                }
                Updated=ReplaceAtPath(Root,TargetPath,_=>NewLive);
                return true;
            }
            case ConvertLightOperation Convert: {
                var Live=GetAtPath(Root,TargetPath);
                var Source=Live.FindComponent(Convert.SourceType);
                if(Source is null||Source.EmitExpressionText!=Convert.OriginalExpression||!LightConversionPlanner.MatchesLatest(Source,Convert)
                    ||!LightComponentContract.IsLight(Convert.Replacement.TypeFullyQualifiedName)
                    ||Live.Components.Count(Component=>LightComponentContract.IsLight(Component.TypeFullyQualifiedName))!=1){
                    Updated=Root;return false;
                }
                Updated=ReplaceAtPath(Root,TargetPath,_=>ReplaceComponent(Live,Source,Convert.Replacement));
                return true;
            }
            case AddComponentOperation AddComponent: {
                var Live=GetAtPath(Root,TargetPath);
                if(!TryApplyAddComponent(Live,AddComponent,out var NewLive)){
                    Updated=Root;
                    return false;
                }
                Updated=ReplaceAtPath(Root,TargetPath,_=>NewLive);
                return true;
            }
            case RemoveComponentOperation RemoveComponent: {
                var Live=GetAtPath(Root,TargetPath);
                if(!TryApplyRemoveComponent(Live,RemoveComponent,out var NewLive)){
                    Updated=Root;
                    return false;
                }
                Updated=ReplaceAtPath(Root,TargetPath,_=>NewLive);
                return true;
            }
            case NormalizeComponentsOperation: {
                var Live=GetAtPath(Root,TargetPath);
                if(!TryApplyNormalizeComponents(Live,out var NewLive)){
                    Updated=Root;
                    return false;
                }
                Updated=ReplaceAtPath(Root,TargetPath,_=>NewLive);
                return true;
            }
            case RenameNodeOperation Rename: {
                var NewName=Rename.NewName?.Trim()??"";
                if(NewName.Length==0){
                    Updated=Root;
                    return false;
                }
                Updated=ReplaceAtPath(Root,TargetPath,Live=>WithName(Live,NewName));
                return true;
            }
            case AddNodeOperation Add:
                return TryAddNode(Root,TargetPath,Add,out Updated);
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
            case DuplicateNodeOperation Duplicate:
                return TryDuplicate(Root,TargetNode,Duplicate,out Updated);
            default:
                Updated=Root;
                return false;
        }
    }

    /// <summary>
    /// M0036/M0038由来の複製(Alt+ドラッグ)。<see cref="SceneSourceEditor.ApplyDuplicateNode"/>と同じ方針:
    /// 元Nodeの構成をそのまま複製し、Transformだけ指定値へ差し替える。新しい兄弟として末尾に追記する
    /// (<see cref="TryAddNode"/>と同じ挿入位置)。
    /// </summary>
    private static bool TryDuplicate(
        SceneNode Root,SceneProjectionNode TargetNode,DuplicateNodeOperation Operation,out SceneNode Updated){
        Updated=Root;
        var TargetPath=TargetNode.Path;
        if(TargetPath.IsRoot||TargetPath.Parent is not { }ParentPath){
            return false;
        }

        var Source=GetAtPath(Root,TargetPath);
        var Transform=SceneComponentBindings.FindComponent(Source,SceneComponentRole.LocalTransform);
        if(Transform is null||SceneSourceEditor.IsGenerated(Transform.Location.FilePath)){
            return false;
        }

        if(!TryApplyTransformVectors(Transform,Operation.Transform,Operation.Members,out var Members)){
            return false;
        }

        var Clone=ReplaceComponent(Source,Transform,WithMembers(Transform,Members));
        var Parent=GetAtPath(Root,ParentPath);
        if(Source.Name is { Length:>0 }BaseName){
            Clone=WithName(Clone,CreateUniqueNodeName(Parent,BaseName));
        }

        Updated=ReplaceAtPath(Root,ParentPath,P=>WithChildrenList(P,P.Children.Add(Clone)));
        return true;
    }

    private static bool TryReparent(
        SceneNode Root,SceneProjection Projection,SceneProjectionNode TargetNode,ReparentNodeOperation Operation,
        out SceneNode Updated,out NodePath?NewPath){
        Updated=Root;
        NewPath=null;
        if(!Projection.TryGetNode(Operation.NewParentNodeId,out var NewParentNode)){
            return false;
        }

        var TargetPath=TargetNode.Path;
        var NewParentPath=NewParentNode.Path;
        if(TargetPath.Equals(NewParentPath)||NewParentPath.IsDescendantOf(TargetPath)){
            return false;
        }
        if(TargetPath.IsRoot||TargetPath.Parent is not { }OldParentPath){
            return false;
        }

        if(OldParentPath.Equals(NewParentPath)){
            var SameParentClamped=Math.Max(0,Math.Min(
                                                      Operation.NewIndex,GetAtPath(Root,OldParentPath).Children.Length-1));
            NewPath=OldParentPath.Append(SameParentClamped);
            return TryReorderChild(Root,OldParentPath,TargetPath.Indices[^1],Operation.NewIndex,out Updated);
        }

        var MovedContent=GetAtPath(Root,TargetPath);
        if(!TryRemoveChildAt(Root,OldParentPath,TargetPath.Indices[^1],out var AfterRemoval)){
            return false;
        }

        // NewParentのPathは削除で兄弟インデックスがずれている可能性があるため、削除後のツリーで
        // 同じEditorNodeIdを引き直す。IDの割り当ては呼び出し元(TryApply)のReconcilerと共有する
        // 必要があるため、ここでは新規Reconcilerを使わず、単純な名前+Component構成の再帰探索で
        // 対象を再特定する(削除操作はID空間を消費しないため、この場に限り軽量な再探索で足りる)。
        if(!TryFindByIdentity(AfterRemoval,NewParentNode.Node,out var ResolvedNewParentPath)){
            return false;
        }

        var ResolvedNewParent=GetAtPath(AfterRemoval,ResolvedNewParentPath);
        var ClampedIndex=Math.Max(0,Math.Min(Operation.NewIndex,ResolvedNewParent.Children.Length));
        NewPath=ResolvedNewParentPath.Append(ClampedIndex);
        return TryInsertChildAt(AfterRemoval,ResolvedNewParentPath,ClampedIndex,MovedContent,out Updated);
    }

    /// <summary>
    /// 削除操作の直後に、削除前のNode参照(Name+Component構成で一意なはず)から現在のPathを
    /// 再特定する。Reparent専用の軽量フォールバック — Reconcilerのように新規IDを払い出さない。
    ///
    /// <para>
    /// M0051 Step 1-4(バグ修正): 以前は<c>ReferenceEquals</c>で探していたが、直前の
    /// <see cref="TryRemoveChildAt"/>が木を関数的に作り直すため新親は**必ず別インスタンス**になり、
    /// この関数は常にfalseを返していた。結果、<b>親を跨ぐReparentはmodel適用時に必ず失敗し、
    /// Hierarchyのドラッグ&amp;ドロップでの
    /// 親子変更が一切効かなかった</b>(実機報告「ヒエラルキーで編集しても取り消される」の一因)。
    /// XMLコメントが記していた本来の意図(Name+Component構成での照合)どおりに実装し直した。
    /// </para>
    /// </summary>
    private static bool TryFindByIdentity(SceneNode Root,SceneNode Target,out NodePath Found){
        if(HasSameIdentity(Root,Target)){
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

    /// <summary>
    /// Name+Component型構成での同一性。<see cref="SceneProjectionReconciler"/>の
    /// <c>ExactKey</c>と同じ考え方(Nameだけでは一意にならないためComponent構成も併せて見る)。
    /// </summary>
    private static bool HasSameIdentity(SceneNode Left,SceneNode Right){
        if(ReferenceEquals(Left,Right)){
            return true;
        }
        if(!string.Equals(Left.Name,Right.Name,System.StringComparison.Ordinal)
            ||Left.Components.Length!=Right.Components.Length){
            return false;
        }
        for(var Index=0;Index<Left.Components.Length;Index++){
            if(!string.Equals(
                Left.Components[Index].TypeFullyQualifiedName,
                Right.Components[Index].TypeFullyQualifiedName,
                System.StringComparison.Ordinal)){
                return false;
            }
        }
        return true;
    }

    private static SceneNode GetAtPath(SceneNode Root,NodePath Path){
        var Current=Root;
        foreach(var Index in Path.Indices){
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
        var ChildIndex=Indices[Depth];
        var UpdatedChild=ReplaceAtPathCore(Node.Children[ChildIndex],Indices,Depth+1,Transform);
        return ReferenceEquals(UpdatedChild,Node.Children[ChildIndex])
            ?Node
            :WithChildren(Node,Node.Children.SetItem(ChildIndex,UpdatedChild));
    }

    /// <summary>
    /// 親の子リストを丸ごと差し替え、全子孫のPathを新しい位置から再計算する。子リストが変わると
    /// 影響を受けた要素以降の兄弟インデックスがずれるため、変更点だけでなく全要素を再計算する。
    /// </summary>
    private static SceneNode WithChildrenList(SceneNode Parent,ImmutableArray<SceneNode>NewChildrenContent){
        var Rebased=ImmutableArray.CreateBuilder<SceneNode>(NewChildrenContent.Length);
        for(var Index=0;Index<NewChildrenContent.Length;Index++){
            Rebased.Add(RebasePath(NewChildrenContent[Index],Parent.Path.Append(Index)));
        }
        return WithChildren(Parent,Rebased.MoveToImmutable());
    }

    private static SceneNode RebasePath(SceneNode Node,NodePath NewPath){
        var Children=Node.Children;
        var Changed=!Node.Path.Equals(NewPath);
        ImmutableArray<SceneNode>.Builder?NewChildren=null;
        for(var Index=0;Index<Children.Length;Index++){
            var Rebased=RebasePath(Children[Index],NewPath.Append(Index));
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

    private static bool TryAddNode(SceneNode Root,NodePath ParentPath,AddNodeOperation Operation,out SceneNode NewRoot){
        NewRoot=Root;
        var Parent=GetAtPath(Root,ParentPath);
        if(SceneSourceEditor.IsGenerated(Parent.Location.FilePath)){
            return false;
        }

        var Name=CreateUniqueNodeName(Parent,Operation.NodeName);
        ImmutableArray<SceneComponent>Components=CreateInitialComponents(Operation.InitialComponents);
        SceneNode NewChild=new(
            NodePath.Root,Name,Components,ImmutableArray<SceneNode>.Empty,
            SceneSourceLocation.None,null,null,
            Components.Length>0?SceneSourceLocation.None:null,null);
        NewRoot=ReplaceAtPath(Root,ParentPath,P=>WithChildrenList(P,P.Children.Add(NewChild)));
        return true;
    }

    private static ImmutableArray<SceneComponent>CreateInitialComponents(
        ImmutableArray<NewNodeComponent>InitialComponents){
        if(InitialComponents.IsDefaultOrEmpty){
            return ImmutableArray<SceneComponent>.Empty;
        }

        ImmutableArray<SceneComponent>.Builder Components=ImmutableArray.CreateBuilder<SceneComponent>();
        foreach(NewNodeComponent Initial in InitialComponents){
            if(string.IsNullOrWhiteSpace(Initial.ComponentTypeFullyQualifiedName)
                ||string.IsNullOrWhiteSpace(Initial.ComponentExpression)){
                continue;
            }
            Components.Add(new SceneComponent(
                TypeSymbol:null,
                Initial.ComponentTypeFullyQualifiedName,
                ImmutableArray<SceneValue>.Empty,
                Initial.Members.IsDefault?ImmutableArray<SceneComponentMember>.Empty:Initial.Members,
                EmitExpressionText:Initial.ComponentExpression,
                SceneSourceLocation.None,
                Initial.OrderKey));
        }
        return SceneComponentOrdering.Normalize(Components.ToImmutable());
    }
    private static bool TryRemoveChildAt(SceneNode Root,NodePath ParentPath,int LocalIndex,out SceneNode NewRoot){
        NewRoot=Root;
        var Parent=GetAtPath(Root,ParentPath);
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
        var Parent=GetAtPath(Root,ParentPath);
        if(LocalIndex<0||LocalIndex>=Parent.Children.Length){
            return false;
        }

        var Moved=Parent.Children[LocalIndex];
        var Without=Parent.Children.RemoveAt(LocalIndex);
        var Clamped=Math.Max(0,Math.Min(RequestedNewIndex,Without.Length));
        NewRoot=ReplaceAtPath(Root,ParentPath,P=>WithChildrenList(P,Without.Insert(Clamped,Moved)));
        return true;
    }

    private static bool TryInsertChildAt(
        SceneNode Root,NodePath ParentPath,int Index,SceneNode Content,out SceneNode NewRoot){
        NewRoot=Root;
        var Parent=GetAtPath(Root,ParentPath);
        var Clamped=Math.Max(0,Math.Min(Index,Parent.Children.Length));
        NewRoot=ReplaceAtPath(Root,ParentPath,P=>WithChildrenList(P,P.Children.Insert(Clamped,Content)));
        return true;
    }

    /// <summary><see cref="SceneSourceEditor"/>の<c>CreateUniqueNodeName</c>と同じ規則(重複時は " (N)" を付与)。</summary>
    private static string CreateUniqueNodeName(SceneNode Parent,string RequestedName){
        var BaseName=string.IsNullOrWhiteSpace(RequestedName)?"Node":RequestedName.Trim();
        var Names=Parent.Children
                       .Where(static Child=>Child.Name is not null)
                       .Select(static Child=>Child.Name!)
                       .ToHashSet(StringComparer.Ordinal);
        if(!Names.Contains(BaseName)){
            return BaseName;
        }
        for(var Suffix=1;;Suffix++){
            var Candidate=$"{BaseName} ({Suffix})";
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
        var Transform=SceneComponentBindings.FindComponent(Node,SceneComponentRole.LocalTransform);
        if(Transform is null||SceneSourceEditor.IsGenerated(Transform.Location.FilePath)){
            return false;
        }

        if(!TryApplyTransformVectors(Transform,Operation.Transform,Operation.Members,out var Members)){
            return false;
        }

        if(Members.Equals(Transform.Members)){
            return true;
        }
        Updated=ReplaceComponent(Node,Transform,WithMembers(Transform,Members));
        return true;
    }

    private static bool TryApplyTransformVectors(
        SceneComponent Transform,SceneTransformValue Value,SceneTransformMemberMask Mask,
        out ImmutableArray<SceneComponentMember> Members){
        Members=Transform.Members;
        foreach((string Name,Vector3 Vector,Vector3 Default)in SceneSourceEditor.EnumerateTransformVectors(Value,Mask)){
            Vector3 Current=SceneTransformBinding.ReadVector(Transform,Name,Default);
            if(SceneTransformMath.NearlyEqual(Current.X,Vector.X)
                &&SceneTransformMath.NearlyEqual(Current.Y,Vector.Y)
                &&SceneTransformMath.NearlyEqual(Current.Z,Vector.Z)){
                continue;
            }
            if(!SceneTransformBinding.CanEditVector(Transform,Name,0)
                ||!SceneTransformBinding.CanEditVector(Transform,Name,1)
                ||!SceneTransformBinding.CanEditVector(Transform,Name,2)){
                return false;
            }
            SceneComponentMember?Existing=FindMember(Members,Name);
            SceneValue VectorValue=CreateVectorValue(Name,Vector,Existing,Transform,Transform.Location);
            Members=SetMember(Members,Existing,Name,VectorValue,Transform.Location);
        }
        return true;
    }

    private static SceneValue CreateVectorValue(
        string Name,Vector3 Value,SceneComponentMember?Existing,SceneComponent Component,SceneSourceLocation Fallback){
        SceneValue?Old=Existing?.Value;
        Microsoft.CodeAnalysis.ITypeSymbol?Type=Old?.Type??GetDeclaredMemberType(Component,Name);
        ImmutableArray<SceneValue>Arguments=[
            CreateLiteral(Value.X,SceneSourceEditor.FormatFloat(Value.X),Existing,Fallback),
            CreateLiteral(Value.Y,SceneSourceEditor.FormatFloat(Value.Y),Existing,Fallback),
            CreateLiteral(Value.Z,SceneSourceEditor.FormatFloat(Value.Z),Existing,Fallback),
        ];
        string TypeName=Type?.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat)??"LogicalVector3";
        if(Name=="Rotation"&&SceneTransformBinding.IsCanonicalTransform(Component)){
            const string VectorTypeName="global::CSharpGameEngine.Numerics.LogicalVector3";
            string VectorText=$"new {VectorTypeName}({SceneSourceEditor.FormatFloat(Value.X)}, {SceneSourceEditor.FormatFloat(Value.Y)}, {SceneSourceEditor.FormatFloat(Value.Z)})";
            SceneValue EulerValue=new(
                SceneValueKind.ObjectCreation,Old?.Arguments.FirstOrDefault()?.Type,ConstantValue:null,ReferencedSymbol:null,
                ImmutableArray<SceneComponentMember>.Empty,Arguments,ImmutableArray<SceneValue>.Empty,
                VectorText,VectorText,Old?.Location??Fallback);
            string QuaternionText=$"new {TypeName}({VectorText})";
            return new SceneValue(
                SceneValueKind.ObjectCreation,Type,ConstantValue:null,ReferencedSymbol:null,
                ImmutableArray<SceneComponentMember>.Empty,[EulerValue],ImmutableArray<SceneValue>.Empty,
                QuaternionText,QuaternionText,Old?.Location??Fallback);
        }
        string Text=$"new {TypeName}({SceneSourceEditor.FormatFloat(Value.X)}, {SceneSourceEditor.FormatFloat(Value.Y)}, {SceneSourceEditor.FormatFloat(Value.Z)})";
        return new SceneValue(
            SceneValueKind.ObjectCreation,Type,ConstantValue:null,ReferencedSymbol:null,
            ImmutableArray<SceneComponentMember>.Empty,Arguments,ImmutableArray<SceneValue>.Empty,
            Text,Text,Old?.Location??Fallback);
    }

    private static bool TryApplySetMember(
        SceneNode Node,SetComponentMemberOperation Operation,out SceneNode Updated){
        Updated=Node;
        var Component=Node.FindComponent(Operation.ComponentTypeFullyQualifiedName);
        if(Component is null||SceneSourceEditor.IsGenerated(Component.Location.FilePath)){
            return false;
        }

        ImmutableArray<string>Path=Operation.MemberPath.IsDefaultOrEmpty
            ?ImmutableArray.Create(Operation.MemberName)
            :Operation.MemberPath;
        if(Path.Length>1){
            return TryApplyNestedSetMember(Node,Component,Path,Operation,out Updated);
        }
        var Existing=FindMember(Component.Members,Operation.MemberName);
        var Symbol=Existing?.MemberSymbol??Component.TypeSymbol?.GetMembers(Operation.MemberName).FirstOrDefault();
        var Contract=LightValueContract.Find(GetDeclaredMemberType(Component,Operation.MemberName)??Existing?.Value.Type);
        if(Contract is not null&&Symbol is not null){
            if(Existing is not null&&(!Contract.TryRead(Existing.Value,out _,out var Editable)||!Editable)){return false;}
            if(!Contract.TryReadExpression(Operation.ValueExpression,out var Values)){return false;}
            var Physical=SceneComponentInitializerPolicy.CreateValue(Symbol,Contract.Format(Values),true,null);
            Updated=ReplaceComponent(Node,Component,WithMembers(Component,SetMember(Component.Members,Existing,Operation.MemberName,Physical,Component.Location)));
            return true;
        }
        if(Existing is not null&&!Existing.Value.IsEditable){
            return false;
        }
        if(!TryParseLiteral(Operation.ValueExpression,Existing,out var ConstantValue)){
            return false;
        }

        var Value=CreateLiteral(
                                ConstantValue,Operation.ValueExpression.Trim(),Existing,Component.Location);
        var Members=SetMember(
                             Component.Members,Existing,Operation.MemberName,Value,Component.Location);
        Updated=ReplaceComponent(Node,Component,WithMembers(Component,Members));
        return true;
    }

    private static bool TryApplyNestedSetMember(
        SceneNode Node,SceneComponent Component,ImmutableArray<string>Path,
        SetComponentMemberOperation Operation,out SceneNode Updated){
        Updated=Node;
        SceneComponentMember?Top=FindMember(Component.Members,Path[0]);
        if(Top?.Value.Kind!=SceneValueKind.ObjectCreation||!TryGetVectorComponentIndex(Path[1],out int Index)
            ||Top.Value.Arguments.Length<=Index){
            return false;
        }
        SceneValue ExistingValue=Top.Value.Arguments[Index];
        SceneComponentMember ExistingChild=new(Path[1],MemberSymbol:null,ExistingValue,ExistingValue.Location);
        if(!ExistingValue.IsEditable||!TryParseLiteral(Operation.ValueExpression,ExistingChild,out var ConstantValue)){
            return false;
        }
        SceneValue UpdatedChild=CreateLiteral(ConstantValue,Operation.ValueExpression.Trim(),ExistingChild,Component.Location);
        SceneValue TopValue=new(
            Top.Value.Kind,Top.Value.Type,Top.Value.ConstantValue,Top.Value.ReferencedSymbol,
            Top.Value.Members,Top.Value.Arguments.SetItem(Index,UpdatedChild),Top.Value.Elements,
            Top.Value.EmitExpressionText,Top.Value.SourceExpressionText,Top.Value.Location);
        ImmutableArray<SceneComponentMember>Members=SetMember(
            Component.Members,Top,Path[0],TopValue,Component.Location);
        Updated=ReplaceComponent(Node,Component,WithMembers(Component,Members));
        return true;
    }

    private static bool TryGetVectorComponentIndex(string Name,out int Index){
        switch(Name){
            case "R": Index=0; return true;
            case "U": Index=1; return true;
            case "F": Index=2; return true;
            default: Index=-1; return false;
        }
    }
    /// <summary>
    /// M0051: <c>SceneComponentCatalog</c>由来のComponent追加。<see cref="SceneSourceEditor.ApplyAddComponent"/>
    /// と同じ2つの前提を、書き戻し(保存)より前のこの層で確かめる — ここで弾いておけば、
    /// 保存時になって初めて失敗するという体験を避けられる。
    ///
    /// <para>
    /// 追加するComponentは<see cref="SceneSourceLocation.None"/>で構築する。<see cref="TryAddNode"/>が
    /// 新規Nodeに対して行っているのと同じ扱いで、ソースへまだ存在しない要素はLocationを持たない。
    /// <c>Members</c>/<c>ConstructorArguments</c>は空 — <c>SceneComponentCatalog</c>が候補として出す型は
    /// すべて<c>required</c>メンバーを持たない(=<c>new Foo()</c>だけで妥当)ことを前提にしているため、
    /// パレット側は常に引数なしのobject creation式を渡す契約になる。
    /// </para>
    /// </summary>
    private static bool TryApplyAddComponent(
        SceneNode Node,AddComponentOperation Operation,out SceneNode Updated){
        Updated=Node;
        if(Node.FindComponent(Operation.ComponentTypeFullyQualifiedName)is not null){
            // SceneSourceEditor.ApplyAddComponentの「同じ型のComponentは既にNodeへ追加されています。」と
            // 同じ判定。保存時になって初めて失敗させず、その場で弾く。
            return false;
        }
        if(SceneSourceEditor.IsGenerated(Node.Location.FilePath)){
            // ComponentsListLocationが無い(Components=[…]が未記述の)Nodeへの追加は、
            // SceneSourceEditor.ApplyAddComponent側がまだ対応していない(「Nodeに編集可能な
            // Componentsコレクションがありません。」)。保存時に必ず失敗するので、ここで揃えて弾く。
            return false;
        }

        // Operation.Membersは追加要求を組み立てた側(Package境界)が型シンボルから作ったもの。
        // 空にすると再解析が着地するまでInspectorが「解析待ち…」のまま何も編集できないので、
        // 呼び出し側が型を解決できた場合は必ず載せる(解決できなければ従来どおり空へ縮退する)。
        SceneComponent NewComponent=new(
            TypeSymbol:null,
            Operation.ComponentTypeFullyQualifiedName,
            ImmutableArray<SceneValue>.Empty,
            Operation.Members.IsDefault?ImmutableArray<SceneComponentMember>.Empty:Operation.Members,
            EmitExpressionText:Operation.ComponentExpression,
            SceneSourceLocation.None,
            Operation.OrderKey);
        ImmutableArray<SceneComponent>Components=SceneComponentOrdering.Normalize(
            Node.Components.Add(NewComponent));
        Updated=new SceneNode(
            Node.Path,Node.Name,Components,Node.Children,
            Node.Location,Node.InitializerLocation,Node.NameValueLocation,
            Node.ComponentsListLocation??SceneSourceLocation.None,Node.ChildrenListLocation);
        return true;
    }

    /// <summary>M0051: <see cref="SceneSourceEditor.ApplyRemoveComponent"/>と対になる、予測モデル側のComponent削除。</summary>
    private static bool TryApplyRemoveComponent(
        SceneNode Node,RemoveComponentOperation Operation,out SceneNode Updated){
        Updated=Node;
        var Component=Node.FindComponent(Operation.ComponentTypeFullyQualifiedName);
        if(Component is null||SceneSourceEditor.IsGenerated(Component.Location.FilePath)){
            return false;
        }

        ImmutableArray<SceneComponent>Components=SceneComponentOrdering.Normalize(
            Node.Components.Remove(Component));
        Updated=new SceneNode(
            Node.Path,Node.Name,Components,Node.Children,
            Node.Location,Node.InitializerLocation,Node.NameValueLocation,
            Node.ComponentsListLocation,Node.ChildrenListLocation);
        return true;
    }

    private static bool TryApplyNormalizeComponents(SceneNode Node,out SceneNode Updated){
        Updated=Node;
        if(SceneSourceEditor.IsGenerated(Node.Location.FilePath)){
            return false;
        }
        ImmutableArray<SceneComponent>Components=SceneComponentOrdering.Normalize(Node.Components);
        if(Components.SequenceEqual(Node.Components)){
            return true;
        }
        Updated=new SceneNode(
            Node.Path,Node.Name,Components,Node.Children,
            Node.Location,Node.InitializerLocation,Node.NameValueLocation,
            Node.ComponentsListLocation,Node.ChildrenListLocation);
        return true;
    }
    /// <summary>
    /// リテラルとして確実に解釈できる式だけを受け付ける。定数式・静的参照・ファクトリ呼び出しなどは
    /// 解決にセマンティックモデルが要るため、ここでは素直に諦めて従来経路へ落とす。
    /// </summary>
    private static bool TryParseLiteral(
        string Expression,SceneComponentMember?Existing,out object?ConstantValue){
        ConstantValue=null;
        var Text=Expression.Trim();
        if(Text.Length==0){
            return false;
        }
        if(bool.TryParse(Text,out var Boolean)){
            ConstantValue=Boolean;
            return true;
        }

        var HasFloatSuffix=Text.EndsWith("f",System.StringComparison.OrdinalIgnoreCase);
        var Numeric=HasFloatSuffix?Text.Substring(0,Text.Length-1):Text;
        if(!float.TryParse(Numeric,NumberStyles.Float,CultureInfo.InvariantCulture,out var Number)
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
        var Text=SceneSourceEditor.FormatFloat(Value);
        var NewValue=CreateLiteral(Value,Text,Existing,Fallback);
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

    private static Microsoft.CodeAnalysis.ITypeSymbol? GetDeclaredMemberType(SceneComponent Component,string?Name)=>
        Name is null?null:Component.TypeSymbol?.GetMembers(Name).FirstOrDefault() switch {
            Microsoft.CodeAnalysis.IFieldSymbol Field=>Field.Type,
            Microsoft.CodeAnalysis.IPropertySymbol Property=>Property.Type,
            _=>null,
        };
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
        foreach(var Member in Members){
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
            Members,Component.EmitExpressionText,Component.Location,Component.OrderKey);

    private static SceneModel RebuildModel(SceneModel Base,SceneNode NewRoot)=>
        new(Base.Namespace,Base.ClassName,Base.ClassFullyQualifiedName,Base.ClassSymbol,
            NewRoot,Base.IsStartupScene,Base.StartupOrder,Base.Diagnostics,
            Base.FilePath,Base.DeclarationPosition);
}
