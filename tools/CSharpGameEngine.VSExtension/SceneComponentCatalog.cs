using System;
using System.Collections.Generic;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>Shape PaletteやInspectorの「Componentを追加」候補に出すScene Component型の1件。</summary>
internal sealed class SceneComponentCatalogEntry {
    internal SceneComponentCatalogEntry(
        string FullyQualifiedName,string DisplayName,string CreationExpression,INamedTypeSymbol Symbol,
        SceneComponentOrderKey OrderKey){
        this.FullyQualifiedName=FullyQualifiedName;
        this.DisplayName=DisplayName;
        this.CreationExpression=CreationExpression;
        this.Symbol=Symbol;
        this.OrderKey=OrderKey;
    }

    internal string FullyQualifiedName { get; }

    internal string DisplayName { get; }

    /// <summary>
    /// このComponentを追加するときソースへ書くobject creation式。書き込み可能メンバーを明示的に
    /// 列挙してあるので、追加した直後からInspectorで全項目を編集できる
    /// (<see cref="SceneComponentInitializerPolicy"/>)。シンボルが生きているここで組み立て、
    /// 以降は文字列として持ち回る。
    /// </summary>
    internal string CreationExpression { get; }

    internal INamedTypeSymbol Symbol { get; }

    internal SceneComponentOrderKey OrderKey { get; }
}

/// <summary>
/// 現在のCompilationからScene DSLに追加可能なvalue-type Component型を列挙する。reflectionではなく
/// Source Generatorと同じRoslynシンボル判定を使い、Parent/SceneNodeTag等の予約型・ref structを除外する。
///
/// <para>
/// <b>generic型は無条件除外ではない。</b> 型引数を1つでも埋められなければ `new Foo&lt;T&gt;()` という
/// 妥当なobject creation式を作れないため、以前は<c>IsGenericType</c>を理由に一律除外していた。
/// しかし型引数に<c>where T : System.Enum</c>や<c>where T : ISomeInterface</c>のような制約が
/// 付いていれば、Compilationの中から制約を満たす具体型を機械的に列挙でき、
/// 「ユーザーが1手で完成させられる閉じたinstantiation」を安全に組み立てられる
/// (<see cref="EnumerateClosedInstantiations"/>)。制約が無い、または広すぎる制約
/// (<c>struct</c>/<c>class</c>/<c>new()</c>単体)の場合は候補が定まらないため、
/// 従来どおり除外する — これは「型引数を埋める手段が無い」場合の安全側の縮退であって、
/// 一律除外という結論そのものは変わっていない。
/// </para>
/// </summary>
internal static class SceneComponentCatalog {
    /// <summary>
    /// 1つのgeneric Component型が生成しうるinstantiation数の上限。列挙可能な型引数候補が多い場合
    /// (enum制約でenumが大量にある等)、組み合わせ爆発でパレットが埋まるのを防ぐ。
    /// </summary>
    private const int MaxInstantiationsPerType=50;

    internal static IReadOnlyList<SceneComponentCatalogEntry>Build(Compilation Compilation){
        List<SceneComponentCatalogEntry>Entries=[];
        foreach(var Type in EnumerateTypes(Compilation.Assembly.GlobalNamespace)){
            if(!IsAddable(Type,Compilation)){
                continue;
            }

            if(!Type.IsGenericType){
                Entries.Add(CreateEntry(Type));
                continue;
            }

            foreach(var Closed in EnumerateClosedInstantiations(Type,Compilation)){
                Entries.Add(CreateEntry(Closed));
            }
        }

        return Entries.OrderBy(static Entry=>Entry.OrderKey.Category).ThenBy(static Entry=>Entry.OrderKey.Order).ThenBy(static Entry=>Entry.OrderKey.FullyQualifiedName,StringComparer.Ordinal).ToArray();
    }

    private static SceneComponentCatalogEntry CreateEntry(INamedTypeSymbol Type){
        var FullyQualifiedName=SceneTypePredicates.ToFullyQualifiedName(Type);
        // Type.Name/型引数のNameは非修飾の短い名前(例: "PoisonKind")。パレット表示は
        // "StatusEffect<PoisonKind>"のように読める形にしたいので、完全修飾名(FullyQualifiedName)とは
        // 別に組み立てる。
        var DisplayName=Type.IsGenericType
                            ?$"{Type.Name}<{string.Join(", ",Type.TypeArguments.Select(static Argument=>Argument.Name))}>"
                            :Type.Name;
        return new SceneComponentCatalogEntry(
            FullyQualifiedName,DisplayName,
            SceneComponentInitializerPolicy.CreateExpression(Type,FullyQualifiedName),Type,
            SceneComponentOrdering.GetSortKey(Type,FullyQualifiedName));
    }

    private static bool IsAddable(INamedTypeSymbol Type,Compilation Compilation){
        if(!SceneTypePredicates.IsNonNullableStruct(Type)
            ||Type.IsRefLikeType
            ||Type.IsImplicitlyDeclared
            ||SceneTypePredicates.IsReservedComponent(SceneTypePredicates.ToFullyQualifiedName(Type))
            ||SceneTypePredicates.ImplementsSystemInterface(Type)
            // エンジンが実行時に埋めるComponent。作者が宣言するものではなく、随伴先を宣言すれば
            // SGが自動付与する(SCENE119)。パレットへ出すと「追加しても中身が永久に空」になる。
            ||SceneTypePredicates.ImplementsRuntimeComponentInterface(Type,Compilation)){
            return false;
        }

        // 引数なしのdefault構築で追加できる型だけを許可する。C# required memberはobject initializerでの
        // 明示指定を強制するため、requiredを持つ型はAddComponent operationの対象から除外する。
        // (generic型の閉じたinstantiationも同じ型定義のメンバーを共有するため、この判定は型引数を
        // 構築する前の型定義に対して行ってよい。)
        foreach(var Member in Type.GetMembers()){
            if(Member is IPropertySymbol { IsRequired:true }||Member is IFieldSymbol { IsRequired:true }){
                return false;
            }
        }

        return Type.Locations.Any(static Location=>Location.IsInSource)
            ||Type.ContainingNamespace.ToDisplayString().StartsWith("CSharpGameEngine.Runtime.Windows",StringComparison.Ordinal);
    }

    /// <summary>
    /// <paramref name="OpenType"/>(generic Component型の定義)を、型引数を埋めて構築できる
    /// 具体的な閉じたinstantiationへすべて展開する。型引数のどれか1つでも候補が見つからなければ
    /// (制約が無い、または広すぎる)、空を返す — その型はパレットに一切出さない。
    /// </summary>
    private static IReadOnlyList<INamedTypeSymbol>EnumerateClosedInstantiations(
        INamedTypeSymbol OpenType,Compilation Compilation){
        if(OpenType.TypeParameters.Length==0){
            // Outer<T>.Inner のような、自身は非genericだが宣言先がgenericなネスト型。
            // Constructで埋める型引数が無いため対象外(以前からの一律除外と同じ扱い)。
            return [];
        }

        List<IReadOnlyList<ITypeSymbol>>PerParameterCandidates=[];
        foreach(var Parameter in OpenType.TypeParameters){
            var Candidates=EnumerateCandidateTypeArguments(Parameter,Compilation);
            if(Candidates.Count==0){
                return [];
            }

            PerParameterCandidates.Add(Candidates);
        }

        List<INamedTypeSymbol>Results=[];
        foreach(var Arguments in CartesianProduct(PerParameterCandidates)){
            Results.Add(OpenType.Construct(Arguments));
            if(Results.Count>=MaxInstantiationsPerType){
                break;
            }
        }

        return Results;
    }

    /// <summary>
    /// 1つの型引数(<paramref name="Parameter"/>)が取りうる具体型をCompilationから機械的に列挙する。
    ///
    /// <para>
    /// 対応する制約は2種類だけ: <c>where T : System.Enum</c>(全enum型)と、
    /// インターフェース/基底クラス制約(それを実装/継承する全型、<c>Compilation.ClassifyCommonConversion</c>で判定)。
    /// 制約が無い、または<c>struct</c>/<c>class</c>/<c>new()</c>のような広すぎる制約しか無い場合は
    /// 候補が定まらないため空を返す — Compilation内の無関係な型まで全部候補になってしまうことを避ける。
    /// </para>
    ///
    /// <para>
    /// 候補はソース宣言型のみを対象にする(<see cref="EnumerateTypes"/>は参照アセンブリまでは辿らない)。
    /// generic Componentへ渡すenum/マーカー型はユーザーが同じプロジェクト内で書くのが通常のため。
    /// </para>
    /// </summary>
    private static IReadOnlyList<ITypeSymbol>EnumerateCandidateTypeArguments(
        ITypeParameterSymbol Parameter,Compilation Compilation){
        // 実機確認: where T : System.Enum 単体では HasValueTypeConstraint は true にならない
        // (struct を明示併記した場合だけ true になる)。enum制約の有無は ConstraintTypes だけで判定する。
        var WantsEnum=Parameter.ConstraintTypes.Any(static Constraint=>Constraint.SpecialType==SpecialType.System_Enum);
        ITypeSymbol[]NominalConstraints=[..Parameter.ConstraintTypes.Where(
            static Constraint=>Constraint.SpecialType is not(SpecialType.System_Enum or SpecialType.System_Object))];

        if(!WantsEnum&&NominalConstraints.Length==0){
            return [];
        }

        List<ITypeSymbol>Candidates=[];
        foreach(var Candidate in EnumerateTypes(Compilation.Assembly.GlobalNamespace)){
            if(WantsEnum){
                if(Candidate.TypeKind==TypeKind.Enum){
                    Candidates.Add(Candidate);
                }

                continue;
            }

            // 抽象型・インターフェース自身は制約を恒等変換で満たしてしまうが(where T : IFoo に対する
            // IFoo自身)、Component型の引数としては何も特定しない無意味な候補なので除外する。
            // 「Componentの型引数として意味があるのは具体的な実装型だけ」という判断。
            if(Candidate.TypeKind!=TypeKind.Interface&&!Candidate.IsAbstract
                &&NominalConstraints.All(Constraint=>Compilation.ClassifyCommonConversion(Candidate,Constraint).IsImplicit)){
                Candidates.Add(Candidate);
            }
        }

        return Candidates;
    }

    private static IEnumerable<ITypeSymbol[]>CartesianProduct(IReadOnlyList<IReadOnlyList<ITypeSymbol>>Sequences)=>
        CartesianProduct(Sequences,0,new ITypeSymbol[Sequences.Count]);

    private static IEnumerable<ITypeSymbol[]>CartesianProduct(
        IReadOnlyList<IReadOnlyList<ITypeSymbol>>Sequences,int Index,ITypeSymbol[]Accumulator){
        if(Index==Sequences.Count){
            yield return (ITypeSymbol[])Accumulator.Clone();
            yield break;
        }

        foreach(var Item in Sequences[Index]){
            Accumulator[Index]=Item;
            foreach(var Rest in CartesianProduct(Sequences,Index+1,Accumulator)){
                yield return Rest;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol>EnumerateTypes(INamespaceSymbol Namespace){
        foreach(var Type in Namespace.GetTypeMembers()){
            yield return Type;
        }

        foreach(var Child in Namespace.GetNamespaceMembers()){
            foreach(var Type in EnumerateTypes(Child)){
                yield return Type;
            }
        }
    }
}
