using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.VSExtension;

/// <summary>Inspectorが1つのMemberへ提供すべき編集UIの種類。</summary>
internal enum InspectorEditorKind {
    Number,
    PhysicalValue,
    Boolean,
    Enum,
    AssetReference,
    Text,
    ReadOnly,
}

/// <summary>Enum候補1件。表示名と、commit時にそのままC#式として書き込める完全修飾参照を持つ。</summary>
internal readonly struct InspectorChoiceCandidate {
    internal InspectorChoiceCandidate(string Name,string EmitExpression){
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
        IReadOnlyList<InspectorChoiceCandidate>Candidates,
        IReadOnlyList<string>?DefinitionMemberPath=null,LightValueContract?PhysicalContract=null,SceneAngleUnit?AngleUnit=null,
        string?DisplayName=null,string?AssetTypeName=null,CSharpGameEngine.Contracts.TextureUsage?TextureUsage=null){
        this.AssetTypeName=AssetTypeName;this.TextureUsage=TextureUsage;
        this.PhysicalContract=PhysicalContract;
        this.AngleUnit=AngleUnit;
        this.DisplayName=DisplayName??MemberName;
        this.NodeId=NodeId;
        this.ComponentTypeFullyQualifiedName=ComponentTypeFullyQualifiedName;
        this.MemberName=MemberName;
        this.SourceExpression=SourceExpression;
        this.IsEditable=IsEditable;
        this.EditorKind=EditorKind;
        this.ConstantValue=ConstantValue;
        this.NumericType=NumericType;
        this.Candidates=Candidates;
        this.DefinitionMemberPath=DefinitionMemberPath is null||DefinitionMemberPath.Count==0
            ?[MemberName]
            :DefinitionMemberPath;
    }

    internal string?AssetTypeName{get;}
    internal CSharpGameEngine.Contracts.TextureUsage?TextureUsage{get;}
    internal LightValueContract?PhysicalContract{get;}
    /// <summary>
    /// <c>[Angle]</c> メンバー内の数値に設定します。行には degree で表示し、degree で入力します。ソースではこの単位を維持します（<see cref="SceneInspectorValueFormat.FormatAngle"/>、<see cref="SceneInspectorValueFormat.TryFormatAngle"/>）。
    /// </summary>
    internal SceneAngleUnit?AngleUnit { get; }
    internal EditorNodeId NodeId { get; }

    internal string ComponentTypeFullyQualifiedName { get; }

    internal string MemberName { get; }

    /// <summary>
    /// 表示ラベルはメンバーのシーン JSON key です。角度では C# の単位語を除いた key になります（ADR-0074）。そのため degree 表示の行を「...Radians」と命名しません。<see cref="MemberName"/> は編集対象となる C# 名のままです。
    /// </summary>
    internal string DisplayName { get; }

    internal string SourceExpression { get; }

    internal bool IsEditable { get; }

    internal InspectorEditorKind EditorKind { get; }

    /// <summary><see cref="InspectorEditorKind.Boolean"/>/<see cref="InspectorEditorKind.Enum"/>の現在値の種。</summary>
    internal object?ConstantValue { get; }

    /// <summary><see cref="InspectorEditorKind.Number"/>のときだけ非null。commit時に
    /// <see cref="SceneInspectorValueFormat.TryFormatNumber"/>へ渡す対象CLR型。</summary>
    internal SpecialType?NumericType { get; }

    /// <summary><see cref="InspectorEditorKind.Enum"/>または<see cref="InspectorEditorKind.AssetReference"/>の候補。</summary>
    internal IReadOnlyList<InspectorChoiceCandidate>Candidates { get; }

    internal IReadOnlyList<string>DefinitionMemberPath { get; }
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
/// <see cref="Rows"/>の葉は、メンバー直下のRUFベクトル(<c>R</c>/<c>U</c>/<c>F</c>)だけを編集可能にし、
/// それ以外は読み取り専用として構築する。<see cref="SceneModelEditApplier"/>の入れ子の適用は
/// <c>Member.R</c>のような2段だけを扱い、それより深い子(<c>Rotation.EulerRuf.R</c>)や
/// RUF以外の名前を編集可能へ倒すと、<c>Transform { …, X=5f }</c>のような存在しない偽メンバーを
/// 生やす実害あるバグになる。
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
            ConstructorArgumentRows(NodeId,Component).Concat(Component.Members.Select(Member=>
                BuildRow(NodeId,Component.TypeFullyQualifiedName,[],Member,AllowEdit:true))).ToArray())).ToArray();
    }

    /// <summary>
    /// コンストラクター引数で記述されたコンポーネント（シーン JSON では positional record と同様にコンストラクターのパラメーター名を使う）は、各引数を parameter 名付きの読み取り専用値として表示します。入れ子の値の引数と同じ扱いです。編集パスで設定するのは initializer member だけなので、これらの値は JSON view で編集します。
    /// </summary>
    private static IEnumerable<SceneInspectorRow>ConstructorArgumentRows(EditorNodeId NodeId,SceneComponent Component){
        var Labels=ResolveArgumentLabels(Component.TypeSymbol,Component.ConstructorArguments.Length,out var Parameters);
        for(var Index=0;Index<Component.ConstructorArguments.Length;Index++){
            var Argument=Component.ConstructorArguments[Index];
            yield return BuildRow(NodeId,Component.TypeFullyQualifiedName,[],new SceneComponentMember(Labels[Index],MemberSymbol:null,Argument,Argument.Location),
                AllowEdit:false,Parameters.Count>Index?SceneJsonSceneAnalyzer.AngleUnitOf(Parameters[Index]):null,
                Parameters.Count>Index?SceneJsonSceneAnalyzer.KeyOf(Parameters[Index]):null);
        }
    }

    /// <summary>
    /// Component直下のMemberでも、ネストしたObjectCreationのMemberでも使う共通の行構築関数。
    /// <paramref name="AllowEdit"/>はComponent直下だけ<see langword="true"/>で渡す — ネストの葉は
    /// <see cref="SceneInspectorNestedRow"/>のドキュメントコメントにある理由で常に読み取り専用にする。
    /// </summary>
    private static SceneInspectorRow BuildRow(
        EditorNodeId NodeId,string ComponentTypeFullyQualifiedName,IReadOnlyList<string>ParentPath,
        SceneComponentMember Member,bool AllowEdit,SceneAngleUnit?InheritedAngle=null,string?DisplayName=null,string?AssetTypeName=null,CSharpGameEngine.Contracts.TextureUsage?TextureUsage=null){
        var Angle=InheritedAngle??SceneJsonSceneAnalyzer.AngleUnitOf(Member.MemberSymbol);
        var Display=DisplayName??(Member.MemberSymbol is { }Symbol?SceneJsonSceneAnalyzer.KeyOf(Symbol):Member.MemberName);
        var Physical=LightValueContract.Find(Member.Value.Type);
        if(Physical is not null&&Physical.TryRead(Member.Value,out var Values,out var Editable)){
            var CanEdit=AllowEdit&&Editable&&Physical.Validate(Values);
            return new SceneInspectorMemberRow(new SceneInspectorMember(
                NodeId,ComponentTypeFullyQualifiedName,Member.MemberName,Member.Value.SourceExpressionText,
                CanEdit,CanEdit?InspectorEditorKind.PhysicalValue:InspectorEditorKind.ReadOnly,
                string.Join(", ",Values.Select(Value=>Value.ToString("R",CultureInfo.InvariantCulture))),null,[],
                [..ParentPath,Member.MemberName],Physical,DisplayName:Display));
        }
        if(TryBuildNested(NodeId,ComponentTypeFullyQualifiedName,ParentPath,Member.MemberName,Display,Member.Value,AllowEdit,Angle)is { }Nested){
            return Nested;
        }

        ITypeSymbol?DeclaredType=Member.MemberSymbol switch {
            IFieldSymbol Field=>Field.Type,
            IPropertySymbol Property=>Property.Type,
            _=>Member.Value.Type,
        };
        var Kind=AllowEdit?DetermineEditorKind(Member.Value,DeclaredType):InspectorEditorKind.ReadOnly;
        IReadOnlyList<InspectorChoiceCandidate>Candidates=Kind switch {
            InspectorEditorKind.Enum=>EnumerateEnumCandidates(DeclaredType),
            InspectorEditorKind.AssetReference=>EnumerateAssetCandidates(Member.Value,DeclaredType,Member.MemberSymbol?.ContainingAssembly),
            _=>[],
        };
        bool IsEditable=AllowEdit&&Kind!=InspectorEditorKind.ReadOnly
            &&(Kind is InspectorEditorKind.Enum or InspectorEditorKind.AssetReference
            ?Candidates.Count>0||DeclaredType?.ToDisplayString() is "CSharpGameEngine.Authoring.TextureAsset" or "CSharpGameEngine.Authoring.ImageAsset"
            :Member.Value.IsEditable);
        return new SceneInspectorMemberRow(new SceneInspectorMember(
            NodeId,
            ComponentTypeFullyQualifiedName,
            Member.MemberName,
            Member.Value.SourceExpressionText,
            IsEditable,
            Kind,
            Member.Value.ConstantValue,
            Kind==InspectorEditorKind.Number?(IsLogicalScalar(DeclaredType)?SpecialType.System_Single:DeclaredType?.SpecialType):null,
            Candidates,
            [..ParentPath,Member.MemberName],
            AngleUnit:Angle,
            DisplayName:Display,AssetTypeName:DeclaredType?.ToDisplayString(),
            TextureUsage:Member.MemberSymbol?.GetAttributes().FirstOrDefault(A=>A.AttributeClass?.ToDisplayString()=="CSharpGameEngine.Contracts.TextureUsageAttribute")?.ConstructorArguments.FirstOrDefault().Value is int Usage?(CSharpGameEngine.Contracts.TextureUsage)Usage:null));
    }

    /// <summary>
    /// 値が<see cref="SceneValueKind.ObjectCreation"/>で、初期化子(<see cref="SceneValue.Members"/>)か
    /// 位置引数(<see cref="SceneValue.Arguments"/>)のどちらかを持つときだけネストを作る。
    /// 空の<c>new Vector3()</c>は展開する意味が無いので通常の(読み取り専用の)葉行のままにする。
    /// </summary>
    private static SceneInspectorNestedRow?TryBuildNested(
        EditorNodeId NodeId,string ComponentTypeFullyQualifiedName,IReadOnlyList<string>ParentPath,
        string MemberName,string DisplayName,SceneValue Value,bool AllowEdit,SceneAngleUnit?Angle){
        if(Value.Type?.ToDisplayString() is "CSharpGameEngine.Authoring.TextureAsset" or "CSharpGameEngine.Authoring.ImageAsset"||Value.Kind!=SceneValueKind.ObjectCreation||(Value.Members.IsEmpty&&Value.Arguments.IsEmpty)){
            return null;
        }

        IReadOnlyList<string>Path=[..ParentPath,MemberName];
        var ArgumentLabels=ResolveArgumentLabels(Value.Type,Value.Arguments.Length,out var ArgumentParameters);
        bool AllowRufEdit=AllowEdit
            &&(ArgumentLabels.SequenceEqual(["R","U","F"],StringComparer.Ordinal)
                ||Value.Members.Select(static Member=>Member.MemberName).SequenceEqual(["R","U","F"],StringComparer.Ordinal));
        List<SceneInspectorRow>Rows=[];
        foreach(var Member in Value.Members){
            Rows.Add(BuildRow(NodeId,ComponentTypeFullyQualifiedName,Path,Member,AllowRufEdit,Angle));
        }

        for(var Index=0;Index<Value.Arguments.Length;Index++){
            var Argument=Value.Arguments[Index];
            SceneComponentMember Synthetic=new(ArgumentLabels[Index],MemberSymbol:null,Argument,Argument.Location);
            Rows.Add(BuildRow(NodeId,ComponentTypeFullyQualifiedName,Path,Synthetic,AllowRufEdit,
                Angle??(ArgumentParameters.Count>Index?SceneJsonSceneAnalyzer.AngleUnitOf(ArgumentParameters[Index]):null),
                ArgumentParameters.Count>Index?SceneJsonSceneAnalyzer.KeyOf(ArgumentParameters[Index]):null));
        }

        return new SceneInspectorNestedRow(
            NodeId,
            ComponentTypeFullyQualifiedName,
            Path,
            DisplayName,
            Value.Type is { }Type?SceneInspectorTypeName.Simplify(SceneTypePredicates.ToFullyQualifiedName(Type)):MemberName,
            Rows);
    }

    /// <summary>
    /// <see cref="SceneValueReader"/>は位置引数のパラメータ名を保持しない(<c>NameColon</c>付きの
    /// 名前付き引数以外は解決していない)ため、ここで<see cref="SceneValue.Type"/>のコンストラクタを
    /// 引数の個数だけで探す。1つに絞れなければ(オーバーロードが複数、または型が未解決)、
    /// <c>[0]</c>のような合成ラベルへ縮退する。
    /// </summary>
    private static IReadOnlyList<string>ResolveArgumentLabels(ITypeSymbol?Type,int ArgumentCount,out IReadOnlyList<IParameterSymbol>Parameters){
        Parameters=[];
        if(ArgumentCount==0){
            return [];
        }

        if(Type is INamedTypeSymbol Named){
            var Matches=Named.Constructors.Where(Constructor=>Constructor.Parameters.Length==ArgumentCount).ToArray();
            if(Matches.Length==1){
                Parameters=Matches[0].Parameters;
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
    /// <summary>
    /// The engine's authored real number (Q64.64), edited as a float: scene JSON holds it as a number read through float
    /// (ADR-0070), so a float literal loses nothing the document keeps. Character movement, the spring arm and every RUF
    /// vector of a Transform are LogicalScalar.
    /// </summary>
    private static bool IsLogicalScalar(ITypeSymbol?Type)=>
        Type is not null&&SceneTypePredicates.ToFullyQualifiedName(Type)=="global::CSharpGameEngine.Numerics.LogicalScalar";

    private static InspectorEditorKind DetermineEditorKind(SceneValue Value,ITypeSymbol?DeclaredType){
        if(DeclaredType?.ToDisplayString() is "CSharpGameEngine.Authoring.TextureAsset" or "CSharpGameEngine.Authoring.ImageAsset"||Value.Kind==SceneValueKind.StaticMemberRef&&Value.ReferencedSymbol?.ContainingType is not null){
            return InspectorEditorKind.AssetReference;
        }
        if(!Value.IsEditable){
            return InspectorEditorKind.ReadOnly;
        }
        if(DeclaredType is { TypeKind:TypeKind.Enum }){
            return InspectorEditorKind.Enum;
        }
        if(IsLogicalScalar(DeclaredType)){
            return InspectorEditorKind.Number;
        }

        return DeclaredType?.SpecialType switch {
            SpecialType.System_Boolean=>InspectorEditorKind.Boolean,
            SpecialType.System_String=>InspectorEditorKind.Text,
            SpecialType.System_SByte or SpecialType.System_Byte
                or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Int32 or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64
                or SpecialType.System_Single=>InspectorEditorKind.Number,
            _=>InspectorEditorKind.ReadOnly,
        };
    }

    private static IReadOnlyList<InspectorChoiceCandidate>EnumerateEnumCandidates(ITypeSymbol?Type){
        if(Type is not { TypeKind:TypeKind.Enum }){
            return [];
        }

        List<InspectorChoiceCandidate>Candidates=[];
        foreach(var Member in Type.GetMembers()){
            if(Member is IFieldSymbol { IsConst:true }Field){
                Candidates.Add(new InspectorChoiceCandidate(Field.Name,SceneEmitExpressionWriter.WriteSymbolReference(Field)));
            }
        }

        return Candidates;
    }

    private static IReadOnlyList<InspectorChoiceCandidate>EnumerateAssetCandidates(
        SceneValue Value,ITypeSymbol?DeclaredType,IAssemblySymbol?Assembly){
        if(DeclaredType is null){ return []; }
        var Container=Value.ReferencedSymbol?.ContainingType;
        var All=Container is null&&Assembly is not null;
        var Symbols=new List<ISymbol>();
        void Collect(INamedTypeSymbol Type){ Symbols.AddRange(Type.GetMembers());foreach(var Child in Type.GetTypeMembers()){ Collect(Child); } }
        if(Container is not null){ Symbols.AddRange(Container.GetMembers()); }
        else if(Assembly is not null){ foreach(var Root in Assembly.GlobalNamespace.GetTypeMembers("Assets")){ Collect(Root); } }
        List<InspectorChoiceCandidate>Candidates=[];
        foreach(var Symbol in Symbols){
            ITypeSymbol?CandidateType=Symbol switch {
                IFieldSymbol { IsStatic:true }Field when Field.IsConst||Field.IsReadOnly=>Field.Type,
                IPropertySymbol { IsStatic:true,GetMethod:not null,SetMethod:null }Property=>Property.Type,
                _=>null,
            };
            if(CandidateType is null||!SymbolEqualityComparer.Default.Equals(CandidateType,DeclaredType)){ continue; }
            Candidates.Add(new InspectorChoiceCandidate(All?Symbol.ToDisplayString():Symbol.Name,SceneEmitExpressionWriter.WriteSymbolReference(Symbol)));
        }
        return Candidates.OrderBy(static Candidate=>Candidate.Name,StringComparer.Ordinal).ToArray();
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
                // "NaN" と "Infinity" は解析できますが、その文字列は C# リテラル（"Infinityf"）でも JSON 数値でもありません。
                if(!float.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var SingleValue)
                    ||float.IsNaN(SingleValue)||float.IsInfinity(SingleValue)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=SceneEditValues.SingleLiteral(SingleValue);
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

    /// <summary>数値入力欄で表示する値から、C#ソース専用のfloatサフィックスを除去する。</summary>
    internal static string RemoveFloatSuffix(string Expression)=>
        Expression.EndsWith("f",StringComparison.OrdinalIgnoreCase)
            ?Expression[..^1]
            :Expression;

    internal static string FormatText(string Value)=>SymbolDisplay.FormatLiteral(Value,quote:true);

    /// <summary>ADR-0074：ソースがどの単位で保持されていても、degree で表示する角度の数値。</summary>
    internal static string FormatAngle(string Expression,SceneAngleUnit Unit){
        var Number=RemoveFloatSuffix(Expression);
        return float.TryParse(Number,NumberStyles.Float,CultureInfo.InvariantCulture,out var Value)
            ?SceneJsonAngles.FormatDegrees(Value,Unit)
            :Number;
    }

    /// <summary>ADR-0074：角度行に入力された degree を、メンバーの単位に合わせたソース式に変換します。</summary>
    internal static bool TryFormatAngle(SpecialType Type,string Degrees,SceneAngleUnit Unit,out string Expression){
        Degrees=Degrees.Trim();
        if(Type!=SpecialType.System_Single||!SceneJsonAngles.IsNumber(Degrees)){
            Expression=string.Empty;
            return false;
        }
        return TryFormatNumber(Type,SceneJsonAngles.FromDegrees(Degrees,Unit).ToString("R",CultureInfo.InvariantCulture),out Expression);
    }
}

/// <summary>
/// Inspector行1件の編集確定を<see cref="SceneEditCommitRequest"/>へ組み立てる。
///
/// <para>
/// <see cref="SetComponentMemberOperation.OriginalValueExpression"/>は常に<c>null</c>で渡す。これを渡すと
/// <see cref="SceneEditConflictMerger"/>が差分リベース(<c>Current + (Final - Original)</c>)を行う ——
/// <see cref="PropertyGizmo"/>のドラッグ(相対ジェスチャ)には正しいが、Inspectorのタイプ入力(絶対値指定)
/// には誤りである。ユーザーが「5にする」と打ったなら5になるべきで「+2する」ではない。同じ版への確定は
/// そのまま適用され、古い版への確定は外部の変更を上書きしないよう<see cref="SceneEditConflictMerger"/>が
/// 拒否する(後勝ちにはしない)。
/// </para>
/// </summary>
internal static class SceneInspectorCommitPlanner {
    internal static SceneEditCommitRequest CreateCommit(
        SceneKey SceneKey,long DocumentVersion,SceneInspectorMember Member,string NewValueExpression){
        SetComponentMemberOperation Operation=new(
            SceneKey,Member.NodeId,DocumentVersion,Member.ComponentTypeFullyQualifiedName,
            Member.MemberName,NewValueExpression,MemberPath:[..Member.DefinitionMemberPath]);
        return new SceneEditCommitRequest(
            SceneKey,DocumentVersion,$"Edit {Member.MemberName}",
            Array.Empty<SceneTransformChange>(),SceneTransformMemberMask.None,
            DirectOperations:[Operation]);
    }

    internal static bool TryCreateChoiceCommit(
        SceneKey SceneKey,long DocumentVersion,SceneInspectorMember Member,string SelectedName,
        out SceneEditCommitRequest Request){
        Request=null!;
        if(Member.EditorKind is not(InspectorEditorKind.Enum or InspectorEditorKind.AssetReference)){ return false; }
        if(IsCurrentChoice(Member,SelectedName)){ return false; }
        foreach(var Candidate in Member.Candidates){
            if(!string.Equals(Candidate.Name,SelectedName,StringComparison.Ordinal)){ continue; }
            Request=CreateCommit(SceneKey,DocumentVersion,Member,Candidate.EmitExpression);
            return true;
        }
        return false;
    }

    internal static bool IsCurrentChoice(SceneInspectorMember Member,string SelectedName){
        return string.Equals(GetCurrentChoiceName(Member.SourceExpression),SelectedName,StringComparison.Ordinal);
    }

    internal static string GetCurrentChoiceName(string SourceExpression){
        string Expression=SourceExpression.Trim();
        int Separator=Expression.LastIndexOf('.');
        string CurrentName=Separator>=0?Expression.Substring(Separator+1):Expression;
        if(CurrentName.StartsWith("@",StringComparison.Ordinal)){ CurrentName=CurrentName.Substring(1); }
        return CurrentName;
    }
}
