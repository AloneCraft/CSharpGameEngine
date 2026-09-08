using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.SceneAnalysis;

/// <summary>
/// <see cref="SceneValue"/>から、生成コードへそのまま貼れる<b>完全修飾済み</b>の式テキストを組み立てる。
///
/// <para>
/// 旧<c>SceneRuntimeEmitter</c>は<c>Set(new T { … })</c>の初期化子テキストを逐語コピーし、解決のために
/// 元ファイルのusingディレクティブを生成ファイルへ一緒にコピーしていた。この方式はusing alias
/// (<c>using T = Ns.Transform;</c>)・別名前空間の同名型・ファイルスコープ名前空間で破綻するため廃止し、
/// シンボルから式を<b>再構築</b>する方式へ変更した(ADR-0040)。生成ファイルにusingは一切出さない。
/// </para>
///
/// <para>
/// 呼び出しは<see cref="SceneValueReader"/>から<b>ボトムアップ</b>に行われる。子の
/// <see cref="SceneValue.EmitExpressionText"/>が確定してから親を組み立てるため、ここでは再帰しない。
/// </para>
/// </summary>
internal static class SceneEmitExpressionWriter {
    /// <summary>解析に失敗した値の代替。生成コードがコンパイルできる形は保つ。</summary>
    internal const string DefaultExpression="default";

    /// <summary>
    /// メンバー参照用の表示形式。<see cref="SymbolDisplayFormat.FullyQualifiedFormat"/>は
    /// <c>memberOptions</c>を持たないため、そのままでは<c>PrimitiveType.Cube</c>が<c>Cube</c>に、
    /// <c>Assets.Floor</c>が<c>Floor</c>になってしまい、生成コードが解決できない名前になる。
    /// </summary>
    private static readonly SymbolDisplayFormat MemberFormat=SymbolDisplayFormat.FullyQualifiedFormat
        .WithMemberOptions(SymbolDisplayMemberOptions.IncludeContainingType);

    internal static string WriteSymbolReference(ISymbol Symbol){
        return Symbol.ToDisplayString(MemberFormat);
    }

    internal static string WriteTypeReference(ITypeSymbol Type){
        return Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    /// <summary><c>new global::Ns.T(a, b) { X = 1, Y = 2 }</c>。引数・メンバーが空なら省略する。</summary>
    internal static string WriteObjectCreation(string TypeFullyQualifiedName,IReadOnlyList<string>Arguments,IReadOnlyList<string>MemberAssignments){
        var Text= $"new {TypeFullyQualifiedName}({string.Join(", ",Arguments)})";

        if(MemberAssignments.Count>0){
            Text+= $" {{ {string.Join(", ",MemberAssignments)} }}";
        }

        return Text;
    }

    internal static string WriteMemberAssignment(string MemberName,string ValueExpression){
        return $"{MemberName} = {ValueExpression}";
    }

    /// <summary>
    /// <c>new global::Ns.T[] { a, b }</c>。コレクション式<c>[ … ]</c>ではなく明示的な配列生成を出すのは、
    /// 生成コードが呼び出し先の型推論に依存しないようにするため。
    /// </summary>
    internal static string WriteArrayCreation(string ElementTypeFullyQualifiedName,IReadOnlyList<string>Elements){
        return Elements.Count==0
            ? $"new {ElementTypeFullyQualifiedName}[0]"
            : $"new {ElementTypeFullyQualifiedName}[] {{ {string.Join(", ",Elements)} }}";
    }

    internal static string WriteInvocation(string MethodFullyQualifiedName,IReadOnlyList<string>Arguments){
        return $"{MethodFullyQualifiedName}({string.Join(", ",Arguments)})";
    }

    /// <summary>名前付き引数を保存する(<c>new Vector3(y: 1)</c>のように順序が位置と一致しない場合があるため)。</summary>
    internal static string WriteArgument(string?ArgumentName,string ValueExpression){
        return ArgumentName is null? ValueExpression: $"{ArgumentName}: {ValueExpression}";
    }

    /// <summary>
    /// 定数値をC#リテラルへ整形する。<b>浮動小数点には必ず接尾辞を付ける</b> — <c>float</c>フィールドへ
    /// <c>0.20943952</c>(=double)を代入するとコンパイルエラーになるため。往復可能な書式指定子は
    /// .NET Framework上でも正しく動く<c>G9</c>/<c>G17</c>を使う(<c>R</c>は.NET Frameworkのfloatで
    /// 往復しないため使わない)。
    /// </summary>
    internal static string WriteConstant(object?Value){
        switch(Value){
            case null:
                return "null";
            case bool BoolValue:
                return BoolValue?"true":"false";
            case string StringValue:
                return SymbolDisplay.FormatLiteral(StringValue,quote:true);
            case char CharValue:
                return SymbolDisplay.FormatLiteral(CharValue,quote:true);
            case float FloatValue:
                return WriteSingle(FloatValue);
            case long LongValue:
                return LongValue.ToString(CultureInfo.InvariantCulture)+"L";
            case ulong UlongValue:
                return UlongValue.ToString(CultureInfo.InvariantCulture)+"UL";
            case uint UintValue:
                return UintValue.ToString(CultureInfo.InvariantCulture)+"U";
            case int IntValue:
                return IntValue.ToString(CultureInfo.InvariantCulture);
            case short ShortValue:
                return ShortValue.ToString(CultureInfo.InvariantCulture);
            case ushort UshortValue:
                return UshortValue.ToString(CultureInfo.InvariantCulture);
            case sbyte SbyteValue:
                return SbyteValue.ToString(CultureInfo.InvariantCulture);
            case byte ByteValue:
                return ByteValue.ToString(CultureInfo.InvariantCulture);
            default:
                return DefaultExpression;
        }
    }

    private static string WriteSingle(float Value){
        if(float.IsNaN(Value)){
            return "global::System.Single.NaN";
        }

        if(float.IsPositiveInfinity(Value)){
            return "global::System.Single.PositiveInfinity";
        }

        if(float.IsNegativeInfinity(Value)){
            return "global::System.Single.NegativeInfinity";
        }

        return Value.ToString("G9",CultureInfo.InvariantCulture)+"f";
    }

}
