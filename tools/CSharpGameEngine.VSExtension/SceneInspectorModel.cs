using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.VSExtension;

/// <summary>Inspectorが1つのMemberへ提供すべき編集UIの種類。</summary>
internal enum InspectorEditorKind {
    Number,
    Boolean,
    Enum,
    Text,
    ReadOnly,
}

/// <summary>Enum候補1件。表示名と、commit時にそのままC#式として書き込める完全修飾参照を持つ。</summary>
internal readonly struct InspectorEnumCandidate {
    internal InspectorEnumCandidate(string Name,string EmitExpression){
        this.Name=Name;
        this.EmitExpression=EmitExpression;
    }

    internal string Name { get; }

    internal string EmitExpression { get; }
}

/// <summary>Inspectorに表示する1つのComponent memberの行。編集不能なC#式はread-onlyとして表示する。</summary>
internal sealed class SceneInspectorMember {
    internal SceneInspectorMember(
        EditorNodeId NodeId,string ComponentTypeFullyQualifiedName,string MemberName,string SourceExpression,
        bool IsEditable,InspectorEditorKind EditorKind,object?ConstantValue,SpecialType?NumericType,
        IReadOnlyList<InspectorEnumCandidate>EnumCandidates){
        this.NodeId=NodeId;
        this.ComponentTypeFullyQualifiedName=ComponentTypeFullyQualifiedName;
        this.MemberName=MemberName;
        this.SourceExpression=SourceExpression;
        this.IsEditable=IsEditable;
        this.EditorKind=EditorKind;
        this.ConstantValue=ConstantValue;
        this.NumericType=NumericType;
        this.EnumCandidates=EnumCandidates;
    }

    internal EditorNodeId NodeId { get; }

    internal string ComponentTypeFullyQualifiedName { get; }

    internal string MemberName { get; }

    internal string SourceExpression { get; }

    internal bool IsEditable { get; }

    internal InspectorEditorKind EditorKind { get; }

    /// <summary><see cref="InspectorEditorKind.Boolean"/>/<see cref="InspectorEditorKind.Enum"/>の現在値の種。</summary>
    internal object?ConstantValue { get; }

    /// <summary><see cref="InspectorEditorKind.Number"/>のときだけ非null。commit時に
    /// <see cref="SceneInspectorValueFormat.TryFormatNumber"/>へ渡す対象CLR型。</summary>
    internal SpecialType?NumericType { get; }

    /// <summary><see cref="InspectorEditorKind.Enum"/>のときだけ非空。</summary>
    internal IReadOnlyList<InspectorEnumCandidate>EnumCandidates { get; }
}

/// <summary>
/// Inspectorが描く1行。<see cref="SceneInspectorMemberRow"/>(葉)と<see cref="SceneInspectorNestedRow"/>
/// (入れ子グループ)の2種を持つ判別共用体。
/// </summary>
internal abstract class SceneInspectorRow {
}

/// <summary>単純な値を持つ1行。従来の(グルーピング導入前の)Inspector行そのもの。</summary>
internal sealed class SceneInspectorMemberRow:SceneInspectorRow {
    internal SceneInspectorMemberRow(SceneInspectorMember Member){
        this.Member=Member;
    }

    internal SceneInspectorMember Member { get; }
}

/// <summary>
/// <c>new Vector3(1,2,3)</c>のような、値そのものが<see cref="SceneValueKind.ObjectCreation"/>の
/// メンバーを表す入れ子グループ。<see cref="SceneInspectorComponentGroup"/>と同じExpander表現を
/// もう1段(以上)再帰的に重ねる。
///
/// <para>
/// <see cref="Rows"/>の葉は<b>常に読み取り専用として構築する</b>
/// (<see cref="SceneInspectorModelBuilder.BuildRow"/>の<c>AllowEdit:false</c>)。
/// <see cref="SceneModelEditApplier.TryApplySetMember"/>相当の適用経路はComponentの
/// トップレベルMembersでしか名前一致を探さず、見つからなければ<b>黙って新規メンバーを
/// Component直下に追加する</b>ため、ネストした子を安易に編集可能へ倒すと
/// <c>Transform { …, X=5f }</c>のような存在しない偽メンバーが生える実害あるバグになる。
/// </para>
/// </summary>
internal sealed class SceneInspectorNestedRow:SceneInspectorRow {
    internal SceneInspectorNestedRow(
        EditorNodeId NodeId,string ComponentTypeFullyQualifiedName,IReadOnlyList<string>MemberPath,
        string DisplayName,string TypeDisplayName,IReadOnlyList<SceneInspectorRow>Rows){
        this.NodeId=NodeId;
        this.ComponentTypeFullyQualifiedName=ComponentTypeFullyQualifiedName;
        this.MemberPath=MemberPath;
        this.DisplayName=DisplayName;
        this.TypeDisplayName=TypeDisplayName;
        this.Rows=Rows;
    }

    internal EditorNodeId NodeId { get; }

    internal string ComponentTypeFullyQualifiedName { get; }

    /// <summary>Component直下からこの行までのメンバー名の連なり(例: <c>["Position"]</c>)。展開状態キーに使う。</summary>
    internal IReadOnlyList<string>MemberPath { get; }

    /// <summary>メンバー名(例: "Position")。</summary>
    internal string DisplayName { get; }

    /// <summary>値の型名(例: "Vector3")。</summary>
    internal string TypeDisplayName { get; }

    internal IReadOnlyList<SceneInspectorRow>Rows { get; }
}

/// <summary>
/// Inspectorに表示するComponent 1件分のグループ(Expander 1つに対応)。
///
/// <para>
/// <see cref="Rows"/>が<b>空になり得る</b>のが要点。Componentを追加した直後は
/// <see cref="SceneModelEditApplier"/>が引数なしの<c>new Foo()</c>相当をメモリ内へ挿すため、
/// Roslynの再解析が着地するまでmemberは1件も無い。Inspectorはこの状態を「解析待ち」として
/// 描き分ける必要がある(空=非表示にしてしまうと、追加したComponentが一瞬消えて見える)。
/// </para>
/// </summary>
internal sealed class SceneInspectorComponentGroup {
    internal SceneInspectorComponentGroup(
        EditorNodeId NodeId,string ComponentTypeFullyQualifiedName,string DisplayName,
        IReadOnlyList<SceneInspectorRow>Rows){
        this.NodeId=NodeId;
        this.ComponentTypeFullyQualifiedName=ComponentTypeFullyQualifiedName;
        this.DisplayName=DisplayName;
        this.Rows=Rows;
    }

    internal EditorNodeId NodeId { get; }

    internal string ComponentTypeFullyQualifiedName { get; }

    /// <summary>Expanderのヘッダに出す短縮名。generic instantiationも短縮する。</summary>
    internal string DisplayName { get; }

    internal IReadOnlyList<SceneInspectorRow>Rows { get; }
}

/// <summary>
/// 完全修飾型名から名前空間を落とす。<c>LastIndexOf('.')</c>ではgeneric instantiationを誤って
/// 切る(<c>global::A.Pair&lt;global::A.Kind&gt;</c>が<c>Kind&gt;</c>になる)ため、区切り記号で
/// 区切った各セグメントごとに最後のドット以降を採る。
/// </summary>
internal static class SceneInspectorTypeName {
    internal static string Simplify(string FullyQualifiedName){
        System.Text.StringBuilder Builder=new();
        var SegmentStart=0;
        for(var Index=0;Index<=FullyQualifiedName.Length;Index++){
            if(Index<FullyQualifiedName.Length&&FullyQualifiedName[Index]is not('<'or'>'or','or' ')){
                continue;
            }

            AppendLastSegment(Builder,FullyQualifiedName,SegmentStart,Index);
            if(Index<FullyQualifiedName.Length){
                Builder.Append(FullyQualifiedName[Index]);
            }

            SegmentStart=Index+1;
        }

        return Builder.ToString();
    }

    private static void AppendLastSegment(System.Text.StringBuilder Builder,string Text,int Start,int End){
        var Cut=Start;
        for(var Index=Start;Index<End;Index++){
            if(Text[Index]=='.'){
                Cut=Index+1;
            }
        }

        Builder.Append(Text,Cut,End-Cut);
    }
}

/// <summary>選択中のNode/ComponentをSceneModelから読み出し、UI非依存のInspector行モデルを組み立てる。</summary>
internal static class SceneInspectorModelBuilder {
    /// <summary>
    /// Component単位にグルーピングしない平坦な行の列。<see cref="BuildGroups"/>の結果の葉
    /// (<see cref="SceneInspectorMemberRow"/>)を深さ優先で集めたものと常に一致する
    /// (グルーピング・ネストの有無で行の集合が変わってはいけない)。
    /// </summary>
    internal static IReadOnlyList<SceneInspectorMember>Build(SceneDocumentSnapshot Snapshot,SceneSelection?Selection)=>
        BuildGroups(Snapshot,Selection).SelectMany(static Group=>FlattenLeaves(Group.Rows)).ToArray();

    private static IEnumerable<SceneInspectorMember>FlattenLeaves(IReadOnlyList<SceneInspectorRow>Rows){
        foreach(var Row in Rows){
            switch(Row){
                case SceneInspectorMemberRow Leaf:
                    yield return Leaf.Member;
                    break;
                case SceneInspectorNestedRow Nested:
                    foreach(var Member in FlattenLeaves(Nested.Rows)){
                        yield return Member;
                    }

                    break;
            }
        }
    }

    internal static IReadOnlyList<SceneInspectorComponentGroup>BuildGroups(
        SceneDocumentSnapshot Snapshot,SceneSelection?Selection){
        if(Selection is null
            ||!Snapshot.Key.Equals(Selection.Key)
            ||Selection.PrimaryNodeId is not { } NodeId
            ||Snapshot.Projection?.TryGetNode(NodeId,out var Node)!=true){
            return [];
        }

        var Components=Selection.ComponentType is { } Type
                          ?Node.Node.Components.Where(Component=>string.Equals(Component.TypeFullyQualifiedName,Type,StringComparison.Ordinal))
                          :Node.Node.Components;
        return Components.Select(Component=>new SceneInspectorComponentGroup(
            NodeId,
            Component.TypeFullyQualifiedName,
            SceneInspectorTypeName.Simplify(Component.TypeFullyQualifiedName),
            Component.Members.Select(Member=>
                BuildRow(NodeId,Component.TypeFullyQualifiedName,[],Member,AllowEdit:true)).ToArray())).ToArray();
    }

    /// <summary>
    /// Component直下のMemberでも、ネストしたObjectCreationのMemberでも使う共通の行構築関数。
    /// <paramref name="AllowEdit"/>はComponent直下だけ<see langword="true"/>で渡す — ネストの葉は
    /// <see cref="SceneInspectorNestedRow"/>のドキュメントコメントにある理由で常に読み取り専用にする。
    /// </summary>
    private static SceneInspectorRow BuildRow(
        EditorNodeId NodeId,string ComponentTypeFullyQualifiedName,IReadOnlyList<string>ParentPath,
        SceneComponentMember Member,bool AllowEdit){
        if(TryBuildNested(NodeId,ComponentTypeFullyQualifiedName,ParentPath,Member.MemberName,Member.Value)is { }Nested){
            return Nested;
        }

        var Kind=AllowEdit?DetermineEditorKind(Member.Value):InspectorEditorKind.ReadOnly;
        return new SceneInspectorMemberRow(new SceneInspectorMember(
            NodeId,
            ComponentTypeFullyQualifiedName,
            Member.MemberName,
            Member.Value.SourceExpressionText,
            AllowEdit&&Member.Value.IsEditable,
            Kind,
            Member.Value.ConstantValue,
            Kind==InspectorEditorKind.Number?Member.Value.Type?.SpecialType:null,
            EnumerateEnumCandidates(Member.Value.Type)));
    }

    /// <summary>
    /// 値が<see cref="SceneValueKind.ObjectCreation"/>で、初期化子(<see cref="SceneValue.Members"/>)か
    /// 位置引数(<see cref="SceneValue.Arguments"/>)のどちらかを持つときだけネストを作る。
    /// 空の<c>new Vector3()</c>は展開する意味が無いので通常の(読み取り専用の)葉行のままにする。
    /// </summary>
    private static SceneInspectorNestedRow?TryBuildNested(
        EditorNodeId NodeId,string ComponentTypeFullyQualifiedName,IReadOnlyList<string>ParentPath,
        string MemberName,SceneValue Value){
        if(Value.Kind!=SceneValueKind.ObjectCreation||(Value.Members.IsEmpty&&Value.Arguments.IsEmpty)){
            return null;
        }

        IReadOnlyList<string>Path=[..ParentPath,MemberName];
        List<SceneInspectorRow>Rows=[];
        foreach(var Member in Value.Members){
            Rows.Add(BuildRow(NodeId,ComponentTypeFullyQualifiedName,Path,Member,AllowEdit:false));
        }

        var ArgumentLabels=ResolveArgumentLabels(Value.Type,Value.Arguments.Length);
        for(var Index=0;Index<Value.Arguments.Length;Index++){
            var Argument=Value.Arguments[Index];
            SceneComponentMember Synthetic=new(ArgumentLabels[Index],MemberSymbol:null,Argument,Argument.Location);
            Rows.Add(BuildRow(NodeId,ComponentTypeFullyQualifiedName,Path,Synthetic,AllowEdit:false));
        }

        return new SceneInspectorNestedRow(
            NodeId,
            ComponentTypeFullyQualifiedName,
            Path,
            MemberName,
            Value.Type is { }Type?SceneInspectorTypeName.Simplify(SceneTypePredicates.ToFullyQualifiedName(Type)):MemberName,
            Rows);
    }

    /// <summary>
    /// <see cref="SceneValueReader"/>は位置引数のパラメータ名を保持しない(<c>NameColon</c>付きの
    /// 名前付き引数以外は解決していない)ため、ここで<see cref="SceneValue.Type"/>のコンストラクタを
    /// 引数の個数だけで探す。1つに絞れなければ(オーバーロードが複数、または型が未解決)、
    /// <c>[0]</c>のような合成ラベルへ縮退する。
    /// </summary>
    private static IReadOnlyList<string>ResolveArgumentLabels(ITypeSymbol?Type,int ArgumentCount){
        if(ArgumentCount==0){
            return [];
        }

        if(Type is INamedTypeSymbol Named){
            var Matches=Named.Constructors.Where(Constructor=>Constructor.Parameters.Length==ArgumentCount).ToArray();
            if(Matches.Length==1){
                return Matches[0].Parameters.Select(static Parameter=>Parameter.Name).ToArray();
            }
        }

        return [..Enumerable.Range(0,ArgumentCount).Select(
            static Index=>"["+Index.ToString(CultureInfo.InvariantCulture)+"]")];
    }

    /// <summary>
    /// 分解編集(<c>new Vector3(...)</c>等の入れ子)は<see cref="TryBuildNested"/>が先に処理するので、
    /// ここへ来る時点で入れ子ではない。読み取り専用理由(定数式/静的参照/ファクトリ)は
    /// <see cref="SceneValue.IsEditable"/>で既に確定しているので、残りの編集可能種別(Literal/
    /// EnumMember/Default)をCLR型からUIの入力コントロールへ振り分けるだけでよい。
    /// </summary>
    private static InspectorEditorKind DetermineEditorKind(SceneValue Value){
        if(!Value.IsEditable){
            return InspectorEditorKind.ReadOnly;
        }
        if(Value.Type is { TypeKind:TypeKind.Enum }){
            return InspectorEditorKind.Enum;
        }

        return Value.Type?.SpecialType switch {
            SpecialType.System_Boolean=>InspectorEditorKind.Boolean,
            SpecialType.System_String=>InspectorEditorKind.Text,
            SpecialType.System_SByte or SpecialType.System_Byte
                or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Int32 or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64
                or SpecialType.System_Single or SpecialType.System_Double
                or SpecialType.System_Decimal=>InspectorEditorKind.Number,
            _=>InspectorEditorKind.ReadOnly,
        };
    }

    private static IReadOnlyList<InspectorEnumCandidate>EnumerateEnumCandidates(ITypeSymbol?Type){
        if(Type is not { TypeKind:TypeKind.Enum }){
            return [];
        }

        List<InspectorEnumCandidate>Candidates=[];
        foreach(var Member in Type.GetMembers()){
            if(Member is IFieldSymbol { IsConst:true }Field){
                Candidates.Add(new InspectorEnumCandidate(Field.Name,SceneEmitExpressionWriter.WriteSymbolReference(Field)));
            }
        }

        return Candidates;
    }
}

/// <summary>
/// Inspectorが入力したテキストを、対象CLR型に応じたC#リテラル式へ変換する。範囲外の値は各型固有の
/// <c>TryParse</c>で確実に弾く(intへ丸めてから振り分けると例えばshort宛ての"99999"がオーバーフロー
/// したまま素通りし、生成ソースがコンパイルエラーになる)。
/// </summary>
internal static class SceneInspectorValueFormat {
    internal static bool TryFormatNumber(SpecialType Type,string Text,out string Expression){
        Text=Text.Trim();
        switch(Type){
            case SpecialType.System_Single:
                if(!float.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var SingleValue)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=SingleValue.ToString("R",CultureInfo.InvariantCulture)+"f";
                return true;
            case SpecialType.System_Double:
                if(!double.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var DoubleValue)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=DoubleValue.ToString("R",CultureInfo.InvariantCulture)+"d";
                return true;
            case SpecialType.System_Decimal:
                if(!decimal.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var DecimalValue)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=DecimalValue.ToString(CultureInfo.InvariantCulture)+"m";
                return true;
            case SpecialType.System_Int64:
                if(!long.TryParse(Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var Int64Value)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=Int64Value.ToString(CultureInfo.InvariantCulture)+"L";
                return true;
            case SpecialType.System_UInt64:
                if(!ulong.TryParse(Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var UInt64Value)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=UInt64Value.ToString(CultureInfo.InvariantCulture)+"UL";
                return true;
            case SpecialType.System_UInt32:
                if(!uint.TryParse(Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var UInt32Value)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=UInt32Value.ToString(CultureInfo.InvariantCulture)+"U";
                return true;
            case SpecialType.System_Int32:
                if(!int.TryParse(Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var Int32Value)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=Int32Value.ToString(CultureInfo.InvariantCulture);
                return true;
            case SpecialType.System_Int16:
                if(!short.TryParse(Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var Int16Value)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=Int16Value.ToString(CultureInfo.InvariantCulture);
                return true;
            case SpecialType.System_SByte:
                if(!sbyte.TryParse(Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var SByteValue)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=SByteValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case SpecialType.System_UInt16:
                if(!ushort.TryParse(Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var UInt16Value)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=UInt16Value.ToString(CultureInfo.InvariantCulture);
                return true;
            case SpecialType.System_Byte:
                if(!byte.TryParse(Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var ByteValue)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=ByteValue.ToString(CultureInfo.InvariantCulture);
                return true;
            default:
                Expression=string.Empty;
                return false;
        }
    }

    internal static string FormatBoolean(bool Value)=>Value?"true":"false";

    internal static string FormatText(string Value)=>SymbolDisplay.FormatLiteral(Value,quote:true);
}

/// <summary>
/// Inspector行1件の編集確定を<see cref="SceneEditCommitRequest"/>へ組み立てる。
///
/// <para>
/// <see cref="SetComponentMemberOperation.OriginalValueExpression"/>は常に<c>null</c>で渡す。これを渡すと
/// <see cref="SceneEditConflictMerger"/>が差分リベース(<c>Current + (Final - Original)</c>)を行う ——
/// <see cref="PropertyGizmo"/>のドラッグ(相対ジェスチャ)には正しいが、Inspectorのタイプ入力(絶対値指定)
/// には誤りである。ユーザーが「5にする」と打ったなら5になるべきで「+2する」ではないため、pass-through
/// (last-write-wins)経路を通す。
/// </para>
/// </summary>
internal static class SceneInspectorCommitPlanner {
    internal static SceneEditCommitRequest CreateCommit(
        SceneKey SceneKey,long DocumentVersion,SceneInspectorMember Member,string NewValueExpression){
        SetComponentMemberOperation Operation=new(
            SceneKey,Member.NodeId,DocumentVersion,Member.ComponentTypeFullyQualifiedName,
            Member.MemberName,NewValueExpression);
        return new SceneEditCommitRequest(
            SceneKey,DocumentVersion,$"Edit {Member.MemberName}",
            Array.Empty<SceneTransformChange>(),SceneTransformMemberMask.None,
            DirectOperations:[Operation]);
    }
}
