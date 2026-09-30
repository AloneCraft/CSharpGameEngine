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
        foreach(var Attribute in Symbol.GetAttributes()){
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

    /// <summary>
    /// A type the user can add to a scene node: the Component palette, the scene JSON Schema and the
    /// scene JSON check all use this one rule (ADR-0070). Non-generic check only; the palette closes
    /// generic definitions separately.
    /// </summary>
    internal static bool IsAddableComponent(INamedTypeSymbol Type,Compilation Compilation){
        if(!IsNonNullableStruct(Type)
            ||Type.IsRefLikeType
            ||Type.IsImplicitlyDeclared
            ||IsReservedComponent(ToFullyQualifiedName(Type))
            ||IsEngineInfrastructure(ToFullyQualifiedName(Type))
            ||ImplementsSystemInterface(Type)
            // エンジンが実行時に埋めるComponent。作者が宣言するものではなく、随伴先を宣言すれば
            // SGが自動付与する(SCENE119)。パレットへ出すと「追加しても中身が永久に空」になる。
            ||ImplementsRuntimeComponentInterface(Type,Compilation)){
            return false;
        }

        // 引数なしのdefault構築で追加できる型だけを許可する。C# required memberはobject initializerでの
        // 明示指定を強制するため、requiredを持つ型はAddComponent operationの対象から除外する。
        foreach(var Member in Type.GetMembers()){
            if(Member is IPropertySymbol { IsRequired:true }||Member is IFieldSymbol { IsRequired:true }){
                return false;
            }
        }

        // The engine compiles many helper value types (queries, reports, events) into every game. Its
        // scene components carry the palette attributes; a game's own structs are always candidates.
        var Namespace=Type.ContainingNamespace?.ToDisplayString()??string.Empty;
        return !(Namespace=="CSharpGameEngine"||Namespace.StartsWith("CSharpGameEngine.",StringComparison.Ordinal))
            ||HasAttribute(Type,"CSharpGameEngine.Entities.SceneComponentOrderAttribute")
            ||HasAttribute(Type,"CSharpGameEngine.Entities.SceneComponentIconAttribute");
    }

    /// <summary>
    /// Engine value types compiled into every game that are the ECS itself, not per-entity data:
    /// they satisfy the struct shape but adding them to a node is meaningless.
    /// </summary>
    internal static bool IsEngineInfrastructure(string TypeFullyQualifiedName)=>
        TypeFullyQualifiedName is "global::CSharpGameEngine.Entities.World"
            or "global::CSharpGameEngine.Entities.State"
            or "global::CSharpGameEngine.Entities.Bootstrap"
            or "global::CSharpGameEngine.Entities.Entity"
            or "global::CSharpGameEngine.Entities.SceneInstance";

    /// <summary>この型が<c>IScene</c>を実装しているか。</summary>
    internal static bool ImplementsSceneInterface(INamedTypeSymbol Type){
        foreach(var Interface in Type.AllInterfaces){
            if(Interface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)==SceneDslWellKnownNames.SceneInterfaceFullyQualifiedName){
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// この型が<c>IRuntimeComponent</c>(generic版を含む)を実装しているか。エンジンが実行時に埋める
    /// Componentなので、作者がScene DSLへ書くことはできず、Componentパレットの候補にも出さない。
    ///
    /// <para>
    /// <paramref name="Compilation"/>から印のシンボルを引いて比較する。完全修飾名の<b>文字列比較は
    /// 使えない</b> — generic版は実装側で型引数が埋まる(<c>IRuntimeComponent&lt;DirectionalLight&gt;</c>)ため、
    /// 定義側の名前とは一致しない。<c>OriginalDefinition</c>を<c>SymbolEqualityComparer</c>で比較する。
    /// </para>
    /// </summary>
    internal static bool ImplementsRuntimeComponentInterface(INamedTypeSymbol Type,Compilation Compilation){
        INamedTypeSymbol?Marker=Compilation.GetTypeByMetadataName(
            SceneDslWellKnownNames.RuntimeComponentInterfaceMetadataName);
        if(Marker is null){
            return false;
        }

        foreach(var Interface in Type.AllInterfaces){
            if(SymbolEqualityComparer.Default.Equals(Interface.OriginalDefinition,Marker)){
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// この型が<c>IRuntimeComponent&lt;TAuthored&gt;</c>を実装しているなら、その<c>TAuthored</c>を返す。
    /// 随伴付与(<c>SceneInstantiateEmitter.BuildEffectiveComponents</c>)の対応表を作るのに使う。
    /// 非genericの<c>IRuntimeComponent</c>だけを実装している場合は<see langword="false"/>
    /// (単独で生成されるだけで、随伴先を持たない実行時Component)。
    /// </summary>
    internal static bool TryGetAuthoredComponentType(
        INamedTypeSymbol Type,Compilation Compilation,out INamedTypeSymbol AuthoredType){
        AuthoredType=null!;
        INamedTypeSymbol?Marker=Compilation.GetTypeByMetadataName(
            SceneDslWellKnownNames.RuntimeComponentOfInterfaceMetadataName);
        if(Marker is null){
            return false;
        }

        foreach(var Interface in Type.AllInterfaces){
            if(SymbolEqualityComparer.Default.Equals(Interface.OriginalDefinition,Marker)
                &&Interface.TypeArguments.Length==1
                &&Interface.TypeArguments[0]is INamedTypeSymbol Authored){
                AuthoredType=Authored;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// この型が<c>ISystem</c>を実装しているか。ECSの<c>[GameSystem]</c>構造体は非nullable structという
    /// 形の上では<see cref="IsNonNullableStruct"/>を満たしてしまうが、Scene Componentとしては無意味
    /// (フィールドを持たず、per-Entityの状態ではない)なので、Componentパレット等の候補列挙から除く。
    /// </summary>
    internal static bool ImplementsSystemInterface(INamedTypeSymbol Type){
        foreach(var Interface in Type.AllInterfaces){
            if(Interface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)==SceneDslWellKnownNames.SystemInterfaceFullyQualifiedName){
                return true;
            }
        }

        return false;
    }
}
