using System.Collections.Immutable;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
namespace CSharpGameEngine.CodeAnalysis.Tests;
/// <summary>
/// ADR-0070: the generator builds scenes only from <c>*.scene.json</c>. Tests keep writing their scenes in
/// the C# scene DSL because it is short and type-checked; before the generator runs, each scene class is
/// turned into its scene JSON exactly as <c>CSharpGameEngine.SceneJson.Cli convert --delete-source</c>
/// does (the same analyzer and converter), and the class is removed from the compilation. A scene the
/// converter cannot represent stays as C# and is therefore not built, like a user's unconverted scene.
/// </summary>
internal static class SceneTestJson {
    internal static (CSharpCompilation Compilation,ImmutableArray<AdditionalText>Scenes)Move(CSharpCompilation Compilation){
        List<AdditionalText>Scenes=[];
        Dictionary<SyntaxTree,List<SyntaxNode>>Declarations=[];
        var Index=0;
        foreach(var Type in EnumerateTypes(Compilation.Assembly.GlobalNamespace)){
            if(!SceneTypePredicates.ImplementsSceneInterface(Type)){ continue; }
            var Model=SceneDeclarationAnalyzer.Analyze(Type,Compilation,CancellationToken.None);
            if(Model.HasErrors||SceneJsonConverter.Convert(Model,null).Text is not { }Json){ continue; }
            // Numbered paths keep the scenes in their C# declaration order, which fixes archetype order.
            Scenes.Add(new InMemoryAdditionalText($"/Scenes/{Index++:D3}.{Type.Name}.scene.json",Json));
            foreach(var Reference in Type.DeclaringSyntaxReferences){
                if(!Declarations.TryGetValue(Reference.SyntaxTree,out var Nodes)){ Declarations[Reference.SyntaxTree]=Nodes=[]; }
                Nodes.Add(Reference.GetSyntax());
            }
        }
        foreach(var Pair in Declarations){
            var Root=Pair.Key.GetRoot().RemoveNodes(Pair.Value,SyntaxRemoveOptions.KeepNoTrivia)!;
            Compilation=Compilation.ReplaceSyntaxTree(Pair.Key,Pair.Key.WithRootAndOptions(Root,Pair.Key.Options));
        }
        return (Compilation,[.. Scenes]);
    }
    private static IEnumerable<INamedTypeSymbol>EnumerateTypes(INamespaceSymbol Namespace){
        foreach(var Member in Namespace.GetMembers()){
            if(Member is INamespaceSymbol Child){
                foreach(var Type in EnumerateTypes(Child)){ yield return Type; }
            } else if(Member is INamedTypeSymbol Type){
                yield return Type;
                foreach(var Nested in Type.GetTypeMembers()){ yield return Nested; }
            }
        }
    }
}
