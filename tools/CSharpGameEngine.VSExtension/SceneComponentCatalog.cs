using System;
using System.Collections.Generic;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>Shape Palette/Inspector???????????Scene Component???</summary>
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
/// ???Compilation??Scene DSL??????value-type Component??????reflection????
/// Source Generator???Roslyn???????Parent/SceneNodeTag/generic/ref struct???????
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

        // ????default???????????C# required member???object initializer?????
        // ??????????required???????AddComponent operation????????????????
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
