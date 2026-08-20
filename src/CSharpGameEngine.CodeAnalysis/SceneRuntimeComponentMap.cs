using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>IRuntimeComponent&lt;TAuthored&gt;</c>の実装1件。作者Component型と、それに随伴して自動付与される
/// 実行時Component型の対応を<b>文字列だけ</b>で表す。
///
/// <para>
/// シンボルを持たないのは<see cref="SceneEmitModel"/>と同じ理由 — <c>IncrementalValuesProvider</c>へ
/// 流す値がCompilationをルート保持すると、キーストロークのたびにキャッシュが無効化される。
/// </para>
/// </summary>
internal readonly struct SceneRuntimeComponentPair {
    internal SceneRuntimeComponentPair(
        string AuthoredFullyQualifiedName,string RuntimeFullyQualifiedName,Location?Location){
        this.AuthoredFullyQualifiedName=AuthoredFullyQualifiedName;
        this.RuntimeFullyQualifiedName=RuntimeFullyQualifiedName;
        this.Location=Location;
    }

    internal string AuthoredFullyQualifiedName { get; }

    internal string RuntimeFullyQualifiedName { get; }

    /// <summary>SYSGEN027(同一作者型を2つの実行時Componentが主張)の報告位置。</summary>
    internal Location?Location { get; }
}

/// <summary>
/// 「この作者Componentにはこの実行時Componentが随伴する」の索引。
/// <c>SceneInstantiateEmitter.BuildEffectiveComponents</c>と<c>SceneComponentSetCollector.Collect</c>の
/// <b>両方が同じインスタンスを受け取る</b>ことで、Archetypeの登録と生成が同じ集合を導出する不変条件を保つ。
/// </summary>
internal sealed class SceneRuntimeComponentMap {
    internal static readonly SceneRuntimeComponentMap Empty=new(new Dictionary<string,string>());

    private readonly Dictionary<string,string>RuntimeByAuthored;

    private SceneRuntimeComponentMap(Dictionary<string,string>RuntimeByAuthored){
        this.RuntimeByAuthored=RuntimeByAuthored;
    }

    /// <summary>
    /// 収集したペアから索引を組む。同じ作者型を2つ以上の実行時Componentが主張した場合は
    /// どちらを付与するかが非決定になるので、その分の診断を返して<b>両方とも採用しない</b>。
    /// </summary>
    internal static SceneRuntimeComponentMap Build(
        ImmutableArray<SceneRuntimeComponentPair>Pairs,out ImmutableArray<Diagnostic>Diagnostics){
        // Collect()の順序に依存しないよう必ず整列してから畳む(決定論性テストの要求)。
        List<SceneRuntimeComponentPair>Sorted=[..Pairs];
        Sorted.Sort(static(A,B)=>{
            var ByAuthored=string.CompareOrdinal(A.AuthoredFullyQualifiedName,B.AuthoredFullyQualifiedName);
            return ByAuthored!=0?ByAuthored:string.CompareOrdinal(A.RuntimeFullyQualifiedName,B.RuntimeFullyQualifiedName);
        });

        Dictionary<string,string>Result=[];
        HashSet<string>Conflicted=[];
        var DiagnosticsBuilder=ImmutableArray.CreateBuilder<Diagnostic>();
        foreach(var Pair in Sorted){
            if(Result.TryGetValue(Pair.AuthoredFullyQualifiedName,out string?Existing)){
                if(!string.Equals(Existing,Pair.RuntimeFullyQualifiedName,System.StringComparison.Ordinal)){
                    Conflicted.Add(Pair.AuthoredFullyQualifiedName);
                    DiagnosticsBuilder.Add(Diagnostic.Create(
                        GameSystemDiagnostics.DuplicateRuntimeComponent,
                        Pair.Location,
                        Pair.AuthoredFullyQualifiedName,Existing,Pair.RuntimeFullyQualifiedName));
                }

                continue;
            }

            Result[Pair.AuthoredFullyQualifiedName]=Pair.RuntimeFullyQualifiedName;
        }

        foreach(var Authored in Conflicted){
            Result.Remove(Authored);
        }

        Diagnostics=DiagnosticsBuilder.ToImmutable();
        return new SceneRuntimeComponentMap(Result);
    }

    /// <summary>随伴する実行時Componentの完全修飾名。無ければ<see langword="null"/>。</summary>
    internal string?TryGetRuntimeComponent(string AuthoredFullyQualifiedName)=>
        this.RuntimeByAuthored.TryGetValue(AuthoredFullyQualifiedName,out string?Runtime)?Runtime:null;
}

/// <summary>
/// <c>IRuntimeComponent&lt;TAuthored&gt;</c>を実装するstructを構文候補から拾い、
/// <see cref="SceneRuntimeComponentPair"/>へ縮約する。<c>ISystem</c>と同じく属性が無いため
/// <c>ForAttributeWithMetadataName</c>は使えず、base listを持つ型宣言を安価に絞ってから意味解析で確認する。
/// </summary>
internal static class SceneRuntimeComponentCollector {
    internal static bool IsCandidate(SyntaxNode Node)=>
        Node is StructDeclarationSyntax { BaseList.Types.Count:>0 };

    internal static SceneRuntimeComponentPair?TryAnalyze(GeneratorSyntaxContext Context){
        if(Context.Node is not StructDeclarationSyntax Declaration
            ||Context.SemanticModel.GetDeclaredSymbol(Declaration)is not INamedTypeSymbol Type){
            return null;
        }

        return SceneTypePredicates.TryGetAuthoredComponentType(Type,Context.SemanticModel.Compilation,out var Authored)
            ?new SceneRuntimeComponentPair(
                SceneTypePredicates.ToFullyQualifiedName(Authored),
                SceneTypePredicates.ToFullyQualifiedName(Type),
                Declaration.Identifier.GetLocation())
            :null;
    }
}
