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

/// <summary>選択中のNode/ComponentをSceneModelから読み出し、UI非依存のInspector行モデルを組み立てる。</summary>
internal static class SceneInspectorModelBuilder {
    internal static IReadOnlyList<SceneInspectorMember>Build(SceneDocumentSnapshot Snapshot,SceneSelection?Selection){
        if(Selection is null
            ||!Snapshot.Key.Equals(Selection.Key)
            ||Selection.PrimaryNodeId is not { } NodeId
            ||Snapshot.Projection?.TryGetNode(NodeId,out SceneProjectionNode Node)!=true){
            return [];
        }

        IEnumerable<SceneComponent>Components=Selection.ComponentType is { } Type
            ?Node.Node.Components.Where(Component=>string.Equals(Component.TypeFullyQualifiedName,Type,StringComparison.Ordinal))
            :Node.Node.Components;
        return Components.SelectMany(Component=>Component.Members.Select(Member=>{
            InspectorEditorKind Kind=DetermineEditorKind(Member.Value);
            return new SceneInspectorMember(
                NodeId,
                Component.TypeFullyQualifiedName,
                Member.MemberName,
                Member.Value.SourceExpressionText,
                Member.Value.IsEditable,
                Kind,
                Member.Value.ConstantValue,
                Kind==InspectorEditorKind.Number?Member.Value.Type?.SpecialType:null,
                EnumerateEnumCandidates(Member.Value.Type));
        })).ToArray();
    }

    /// <summary>
    /// 分解編集(<c>new Vector3(...)</c>等の入れ子)は対象外。読み取り専用理由(定数式/静的参照/ファクトリ)は
    /// <see cref="SceneValue.IsEditable"/>で既に確定しているので、ここでは残りの編集可能種別(Literal/
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
        foreach(ISymbol Member in Type.GetMembers()){
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
                if(!float.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out float SingleValue)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=SingleValue.ToString("R",CultureInfo.InvariantCulture)+"f";
                return true;
            case SpecialType.System_Double:
                if(!double.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double DoubleValue)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=DoubleValue.ToString("R",CultureInfo.InvariantCulture)+"d";
                return true;
            case SpecialType.System_Decimal:
                if(!decimal.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out decimal DecimalValue)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=DecimalValue.ToString(CultureInfo.InvariantCulture)+"m";
                return true;
            case SpecialType.System_Int64:
                if(!long.TryParse(Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out long Int64Value)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=Int64Value.ToString(CultureInfo.InvariantCulture)+"L";
                return true;
            case SpecialType.System_UInt64:
                if(!ulong.TryParse(Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out ulong UInt64Value)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=UInt64Value.ToString(CultureInfo.InvariantCulture)+"UL";
                return true;
            case SpecialType.System_UInt32:
                if(!uint.TryParse(Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out uint UInt32Value)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=UInt32Value.ToString(CultureInfo.InvariantCulture)+"U";
                return true;
            case SpecialType.System_Int32:
                if(!int.TryParse(Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int Int32Value)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=Int32Value.ToString(CultureInfo.InvariantCulture);
                return true;
            case SpecialType.System_Int16:
                if(!short.TryParse(Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out short Int16Value)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=Int16Value.ToString(CultureInfo.InvariantCulture);
                return true;
            case SpecialType.System_SByte:
                if(!sbyte.TryParse(Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out sbyte SByteValue)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=SByteValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case SpecialType.System_UInt16:
                if(!ushort.TryParse(Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out ushort UInt16Value)){
                    Expression=string.Empty;
                    return false;
                }
                Expression=UInt16Value.ToString(CultureInfo.InvariantCulture);
                return true;
            case SpecialType.System_Byte:
                if(!byte.TryParse(Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out byte ByteValue)){
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
