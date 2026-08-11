using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.SceneAnalysis;

/// <summary>
/// Scene DSLの型判定。<see cref="IsNonNullableStruct"/>はComponentの妥当性を決める唯一の述語であり、
/// <c>GameSystemAnalyzer</c>(System側の<c>ref</c>パラメータ判定)もここへ委譲する — 2箇所に同じ規則を
/// 書くと「Systemでは通るがSceneでは通らない」というズレが生まれるため。
/// </summary>
internal static class SceneTypePredicates {
    /// <summary>
    /// Componentになれる型かどうか: 非nullableのstructであること。ADR-0033の構造的推論と同一規則で、
    /// 参照型フィールドを持つstructは許可される(制約はComponent<b>型</b>に対するものであってフィールドではない)。
    /// </summary>
    internal static bool IsNonNullableStruct(ITypeSymbol Type){
        if(Type.TypeKind!=TypeKind.Struct){
            return false;
        }

        return Type is not INamedTypeSymbol { ConstructedFrom.SpecialType:SpecialType.System_Nullable_T };
    }

    /// <summary>
    /// 型が解決できていない(ユーザーが入力中・別のコンパイルエラーが既に出ている)状態。
    /// この場合はScene DSLの診断を<b>出してはならない</b> — 入力途中の識別子に赤線を重ねないため。
    /// </summary>
    internal static bool IsUnresolved(ITypeSymbol?Type){
        return Type is null||Type.TypeKind==TypeKind.Error||Type is IErrorTypeSymbol;
    }

    internal static string ToFullyQualifiedName(ITypeSymbol Type){
        return Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    internal static string ToFullyQualifiedName(ISymbol Symbol){
        return Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    /// <summary>指定のメタデータ名の属性が付いているか(継承は辿らない)。</summary>
    internal static bool HasAttribute(ISymbol Symbol,string AttributeMetadataName){
        foreach(AttributeData Attribute in Symbol.GetAttributes()){
            if(Attribute.AttributeClass is { } AttributeClass
                &&AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)== $"global::{AttributeMetadataName}"){
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Scene DSLが自動付与する予約Component。ユーザーが<c>Components</c>へ明示するとSCENE115になる。
    /// </summary>
    internal static bool IsReservedComponent(string TypeFullyQualifiedName){
        return TypeFullyQualifiedName==SceneDslWellKnownNames.ParentFullyQualifiedName
            ||TypeFullyQualifiedName==SceneDslWellKnownNames.SceneNodeTagFullyQualifiedName;
    }

    /// <summary>この型が<c>IScene</c>を実装しているか。</summary>
    internal static bool ImplementsSceneInterface(INamedTypeSymbol Type){
        foreach(INamedTypeSymbol Interface in Type.AllInterfaces){
            if(Interface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)==SceneDslWellKnownNames.SceneInterfaceFullyQualifiedName){
                return true;
            }
        }

        return false;
    }
}
