using System.Collections.Immutable;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
namespace CSharpGameEngine.CodeAnalysis.Tests;
/// <summary>
/// ADR-0070：ジェネレーターは Scene JSON（<c>Scenes\**\*.json</c>）だけから Scene をビルドする。テストでは短く型検査もできる C# Scene DSL で
///  Scene を記述しておく。ジェネレーター実行前に各 Scene クラスを、<c>CSharpGameEngine.SceneJson.Cli convert --delete-source</c>
///  と同じ方法（同じアナライザーとコンバーター）で Scene JSON に変換し、コンパイルからクラスを削除する。コンバーターで表現できない Scene は C# のまま残り、ユーザーが未変換のまま置
/// いた Scene と同様にビルドされない。
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
            // 番号付きパスで Scene の C# 宣言順を維持し、アーキタイプの順序を固定する。
            Scenes.Add(new InMemoryAdditionalText($@"{SceneProjectOptions.ScenesDirectory}{Index++:D3}.{Type.Name}.json",Json));
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
