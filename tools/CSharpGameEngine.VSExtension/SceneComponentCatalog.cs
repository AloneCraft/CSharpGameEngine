using System;
using System.Collections.Generic;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>Shape PaletteやInspectorの「Componentを追加」候補に出すScene Component型の1件。</summary>
internal sealed class SceneComponentCatalogEntry {
    internal SceneComponentCatalogEntry(string FullyQualifiedName,string DisplayName,INamedTypeSymbol Symbol){
        this.FullyQualifiedName=FullyQualifiedName;
        this.DisplayName=DisplayName;
        this.Symbol=Symbol;
    }

    internal string FullyQualifiedName { get; }

    internal string DisplayName { get; }

    internal INamedTypeSymbol Symbol { get; }
}

/// <summary>
/// 現在のCompilationからScene DSLに追加可能なvalue-type Component型を列挙する。reflectionではなく
/// Source Generatorと同じRoslynシンボル判定を使い、Parent/SceneNodeTag等の予約型・generic・ref structを除外する。
/// </summary>
internal static class SceneComponentCatalog {
    internal static IReadOnlyList<SceneComponentCatalogEntry>Build(Compilation Compilation){
        List<SceneComponentCatalogEntry>Entries=[];
        foreach(INamedTypeSymbol Type in EnumerateTypes(Compilation.Assembly.GlobalNamespace)){
            if(!IsAddable(Type)){
                continue;
            }

            string FullyQualifiedName=SceneTypePredicates.ToFullyQualifiedName(Type);
            Entries.Add(new SceneComponentCatalogEntry(FullyQualifiedName,Type.Name,Type));
        }

        return Entries.OrderBy(static Entry=>Entry.DisplayName,StringComparer.Ordinal).ToArray();
    }

    private static bool IsAddable(INamedTypeSymbol Type){
        if(!SceneTypePredicates.IsNonNullableStruct(Type)
            ||Type.IsRefLikeType
            ||Type.IsGenericType
            ||Type.IsImplicitlyDeclared
            ||SceneTypePredicates.IsReservedComponent(SceneTypePredicates.ToFullyQualifiedName(Type))){
            return false;
        }

        // 引数なしのdefault構築で追加できる型だけを許可する。C# required memberはobject initializerでの
        // 明示指定を強制するため、requiredを持つ型はAddComponent operationの対象から除外する。
        foreach(ISymbol Member in Type.GetMembers()){
            if(Member is IPropertySymbol { IsRequired:true }||Member is IFieldSymbol { IsRequired:true }){
                return false;
            }
        }

        return Type.Locations.Any(static Location=>Location.IsInSource)
            ||Type.ContainingNamespace.ToDisplayString().StartsWith("CSharpGameEngine.Runtime.Windows",StringComparison.Ordinal);
    }

    private static IEnumerable<INamedTypeSymbol>EnumerateTypes(INamespaceSymbol Namespace){
        foreach(INamedTypeSymbol Type in Namespace.GetTypeMembers()){
            yield return Type;
        }

        foreach(INamespaceSymbol Child in Namespace.GetNamespaceMembers()){
            foreach(INamedTypeSymbol Type in EnumerateTypes(Child)){
                yield return Type;
            }
        }
    }
}
