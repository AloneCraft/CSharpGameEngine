using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0038: 編集操作をimmutableな<see cref="SceneModel"/>へ関数的に適用し、Roslyn再解析を待たずに
/// 「編集後のモデル」を予測する純粋層。
///
/// <para>
/// <b>これは正本ではない。</b>ADR-0040どおりC#ソースが唯一の正本であり続け、書き戻しも再解析も
/// 今までどおり走る。ここが作るモデルはM0030/M0036と同じ「どこにも書き込まない表示専用の先読み」を
/// モデル層まで引き上げたもので、再解析が landすれば真実に置き換わる。予測が当たっていれば
/// そのdiffは空になり、<b>再解析がそのまま予測の自動検証になる</b>。
/// </para>
///
/// <para>
/// そのため<b>全操作を扱う必要が無い</b>。扱えない操作が1つでも混ざっていれば
/// <see cref="TryApply"/>は<c>false</c>を返して丸ごと諦め、従来どおりの往復へ落とす(部分適用はしない
/// — 中途半端な予測はHierarchy表示とソースの食い違いを生むため)。今回対応するのは高頻度の
/// <see cref="SetTransformOperation"/>(ギズモ)と<see cref="SetComponentMemberOperation"/>
/// (Inspectorのリテラル編集)だけで、構造編集は対象外。
/// </para>
///
/// <para>
/// Transformのメンバー語彙・既定値・数値フォーマット・編集可否の判定は
/// <see cref="SceneSourceEditor"/>と共有し、二重定義を最小化している。残る差(「構文のどこへ書くか」
/// 対「モデルのどこへ書くか」)は契約テストで両者の一致を固定する。
/// </para>
///
/// <para>
/// 生成される<see cref="SceneValue.Location"/>は書き換え前のspanのまま(予測はソースを持たないため)。
/// 実際のソース書き戻しは<c>SceneEditorSessionService.CommitAsync</c>が別途取り直した最新snapshotの
/// spanで行うので、この古いspanが書き込みに使われることはない。
/// </para>
/// </summary>
internal static class SceneModelEditApplier {
    internal static bool TryApply(
        SceneModel Base,SceneProjection Projection,IReadOnlyList<SceneEditOperation>Operations,
        out SceneModel Predicted){
        Predicted=Base;
        if(Base.Root is not { }Root||Operations.Count==0||Base.HasErrors){
            return false;
        }

        Dictionary<NodePath,List<SceneEditOperation>>ByPath=[];
        foreach(SceneEditOperation Operation in Operations){
            if(!IsSupported(Operation)||!Projection.TryGetNode(Operation.NodeId,out SceneProjectionNode Node)){
                return false;
            }
            if(!ByPath.TryGetValue(Node.Path,out List<SceneEditOperation>?Pending)){
                Pending=[];
                ByPath.Add(Node.Path,Pending);
            }
            Pending.Add(Operation);
        }

        if(!TryRebuildNode(Root,ByPath,out SceneNode UpdatedRoot)){
            return false;
        }

        Predicted=new SceneModel(
            Base.Namespace,Base.ClassName,Base.ClassFullyQualifiedName,Base.ClassSymbol,
            UpdatedRoot,Base.IsStartupScene,Base.StartupOrder,Base.Diagnostics,
            Base.FilePath,Base.DeclarationPosition);
        return true;
    }

    private static bool IsSupported(SceneEditOperation Operation)=>
        Operation is SetTransformOperation or SetComponentMemberOperation;

    private static bool TryRebuildNode(
        SceneNode Node,IReadOnlyDictionary<NodePath,List<SceneEditOperation>>ByPath,out SceneNode Updated){
        Updated=Node;
        if(ByPath.TryGetValue(Node.Path,out List<SceneEditOperation>?Pending)){
            foreach(SceneEditOperation Operation in Pending){
                bool Applied=Operation switch {
                    SetTransformOperation SetTransform=>TryApplySetTransform(Updated,SetTransform,out Updated),
                    SetComponentMemberOperation SetMember=>TryApplySetMember(Updated,SetMember,out Updated),
                    _=>false,
                };
                if(!Applied){
                    return false;
                }
            }
        }

        ImmutableArray<SceneNode>Children=Updated.Children;
        for(var Index=0;Index<Children.Length;Index++){
            if(!TryRebuildNode(Children[Index],ByPath,out SceneNode UpdatedChild)){
                return false;
            }
            if(!ReferenceEquals(UpdatedChild,Children[Index])){
                Children=Children.SetItem(Index,UpdatedChild);
            }
        }

        if(!Children.Equals(Updated.Children)){
            Updated=WithChildren(Updated,Children);
        }
        return true;
    }

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
}
