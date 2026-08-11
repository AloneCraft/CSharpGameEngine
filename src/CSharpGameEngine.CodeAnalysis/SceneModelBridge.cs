using System.Collections.Immutable;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>IScene</c>実装クラスを見つけて<see cref="SceneEmitModel"/>へ落とす、解析層と生成層の唯一の境界
/// (ADR-0040)。
///
/// <para>
/// <b>シンボルを持つ<see cref="SceneModel"/>はこのクラスの中だけで生き、外へ出さない。</b>
/// <c>ISymbol</c>は<c>Compilation</c>をルート保持し値等価性も持たないため、
/// <c>IncrementalValuesProvider</c>へ流すとキャッシュが常時無効化されメモリをリークさせる。
/// </para>
///
/// <para>
/// <b>診断は一切報告しない。</b>SCENE1xxを報告するのは<see cref="SceneDslAnalyzer"/>だけであり、
/// ここでは<see cref="SceneEmitModel.HasErrors"/>として生成可否の判定にのみ使う(二重スクイグルの防止)。
/// </para>
/// </summary>
internal static class SceneModelBridge {
    /// <summary>
    /// <c>IScene</c>には属性が無いため<c>ForAttributeWithMetadataName</c>が使えず、
    /// <c>ISystem</c>と同じく安価な構文プレディケートで絞ってから意味解析で確認する。
    /// </summary>
    internal static bool IsSceneCandidate(SyntaxNode Node){
        return Node is ClassDeclarationSyntax { BaseList.Types.Count:>0 };
    }

    internal static SceneEmitModel?TryAnalyze(GeneratorSyntaxContext Ctx,CancellationToken CancellationToken){
        if(Ctx.SemanticModel.GetDeclaredSymbol(Ctx.Node,CancellationToken) is not INamedTypeSymbol Symbol
            ||!SceneTypePredicates.ImplementsSceneInterface(Symbol)){
            return null;
        }

        SceneModel Model=SceneDeclarationAnalyzer.Analyze(Symbol,Ctx.SemanticModel.Compilation,CancellationToken);
        return ToEmitModel(Model);
    }

    private static SceneEmitModel ToEmitModel(SceneModel Model){
        return new SceneEmitModel(
            Model.Namespace,
            Model.ClassName,
            Model.ClassFullyQualifiedName,
            Model.Root is { } Root?ToEmitNode(Root):null,
            Model.TotalNodeCount,
            Model.IsStartupScene,
            Model.StartupOrder,
            Model.HasErrors,
            Model.FilePath,
            Model.DeclarationPosition);
    }

    private static SceneEmitNode ToEmitNode(SceneNode Node){
        ImmutableArray<SceneEmitComponent>.Builder Components=ImmutableArray.CreateBuilder<SceneEmitComponent>(Node.Components.Length);

        foreach(SceneComponent Component in Node.Components){
            Components.Add(new SceneEmitComponent(Component.TypeFullyQualifiedName,Component.EmitExpressionText));
        }

        ImmutableArray<SceneEmitNode>.Builder Children=ImmutableArray.CreateBuilder<SceneEmitNode>(Node.Children.Length);

        foreach(SceneNode Child in Node.Children){
            Children.Add(ToEmitNode(Child));
        }

        return new SceneEmitNode(
            BuildLocalName(Node.Path),
            Node.Path.ToDisplayString(),
            Node.Name,
            Components.ToImmutable(),
            Children.ToImmutable());
    }

    /// <summary>
    /// <c>NodePath</c>から生成コードのローカル変数名を作る(<c>Node</c>/<c>Node_0</c>/<c>Node_0_1</c>)。
    /// <c>Name</c>を使わないのは、識別子である保証がまったく無いため(省略可能で、空白も日本語も入る)。
    /// </summary>
    internal static string BuildLocalName(NodePath Path){
        if(Path.IsRoot){
            return "Node";
        }

        return "Node_"+string.Join("_",Path.Indices);
    }
}
