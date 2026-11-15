using System;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// シーンのコンパイルからコンポーネント型を解決します。
/// パレットカタログにはアクティブなプロジェクトのソースコンポーネントだけが表示されますが、Inspector には CSharpGameEngine.Windows.Transform など、参照プロジェクトで宣言されたコンポーネントも含まれます。
/// </summary>
internal static class ComponentTypeResolver {
    internal static INamedTypeSymbol?Resolve(Compilation Compilation,string FullyQualifiedName){
        foreach(SceneComponentCatalogEntry Candidate in SceneComponentCatalog.Build(Compilation)){
            if(string.Equals(Candidate.FullyQualifiedName,FullyQualifiedName,StringComparison.Ordinal)){
                return Candidate.Symbol;
            }
        }

        return Compilation.GetTypeByMetadataName(ToMetadataName(FullyQualifiedName));
    }

    internal static string ToMetadataName(string FullyQualifiedName){
        const string GlobalPrefix="global::";
        return FullyQualifiedName.StartsWith(GlobalPrefix,StringComparison.Ordinal)
            ?FullyQualifiedName.Substring(GlobalPrefix.Length)
            :FullyQualifiedName;
    }
}