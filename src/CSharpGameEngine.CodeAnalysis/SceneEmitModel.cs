using System.Collections.Immutable;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>ImmutableArray&lt;T&gt;</c>の既定の等価性は<b>内部配列の参照比較</b>である。値が同じでも別インスタンスなら
/// 不一致になり、インクリメンタルジェネレーターのキャッシュが毎回ミスして生成段が走り続ける。
/// 生成層のモデルは必ずここを通して比較する(ADR-0040 リスクO-2)。
/// </summary>
internal static class SceneEmitEquality {
    internal static bool SequenceEqual<T>(ImmutableArray<T>Left,ImmutableArray<T>Right)where T:IEquatable<T>{
        if(Left.IsDefault||Right.IsDefault){
            return Left.IsDefault&&Right.IsDefault;
        }

        if(Left.Length!=Right.Length){
            return false;
        }

        for(var Index=0;Index<Left.Length;Index++){
            if(!Left[Index].Equals(Right[Index])){
                return false;
            }
        }

        return true;
    }

    internal static int CombineHashCodes<T>(ImmutableArray<T>Values){
        if(Values.IsDefault){
            return 0;
        }

        unchecked {
            var Hash=17;

            foreach(var Value in Values){
                Hash=(Hash*31)+(Value?.GetHashCode()??0);
            }

            return Hash;
        }
    }

    internal static int Combine(params int[]HashCodes){
        unchecked {
            var Hash=17;

            foreach(var Code in HashCodes){
                Hash=(Hash*31)+Code;
            }

            return Hash;
        }
    }

    internal static int OrdinalHash(string?Value)=>Value is null?0:StringComparer.Ordinal.GetHashCode(Value);
}

/// <summary>ノード1つに載る Component 1つ。生成に必要な情報だけを文字列で持つ。</summary>
internal readonly struct SceneEmitComponent : IEquatable<SceneEmitComponent> {
    internal readonly string TypeFullyQualifiedName;

    /// <summary>完全修飾済みの<c>new T { … }</c>式。usingに依存しない。</summary>
    internal readonly string EmitExpressionText;

    internal SceneEmitComponent(string TypeFullyQualifiedName,string EmitExpressionText){
        this.TypeFullyQualifiedName=TypeFullyQualifiedName;
        this.EmitExpressionText=EmitExpressionText;
    }

    public bool Equals(SceneEmitComponent Other){
        return string.Equals(this.TypeFullyQualifiedName,Other.TypeFullyQualifiedName,StringComparison.Ordinal)
            &&string.Equals(this.EmitExpressionText,Other.EmitExpressionText,StringComparison.Ordinal);
    }

    public override bool Equals(object?Obj)=>Obj is SceneEmitComponent Other&&this.Equals(Other);

    public override int GetHashCode()=>SceneEmitEquality.Combine(
        SceneEmitEquality.OrdinalHash(this.TypeFullyQualifiedName),
        SceneEmitEquality.OrdinalHash(this.EmitExpressionText));
}

/// <summary>Scene のノード1つ。<b>シンボルを一切持たない</b>ので<c>IncrementalValuesProvider</c>を流せる。</summary>
internal readonly struct SceneEmitNode : IEquatable<SceneEmitNode> {
    /// <summary>生成コードのローカル変数名(<c>Node</c>/<c>Node_0</c>/<c>Node_0_1</c>)。<c>NodePath</c>から機械的に導出する。</summary>
    internal readonly string LocalName;

    /// <summary>生成コードのコメント用。<c>Name</c>は識別子とは限らない(空白も日本語も入る)ためローカル名には使えない。</summary>
    internal readonly string PathDisplay;

    internal readonly string?Name;

    internal readonly ImmutableArray<SceneEmitComponent>Components;

    internal readonly ImmutableArray<SceneEmitNode>Children;

    internal SceneEmitNode(
        string LocalName,
        string PathDisplay,
        string?Name,
        ImmutableArray<SceneEmitComponent>Components,
        ImmutableArray<SceneEmitNode>Children){
        this.LocalName=LocalName;
        this.PathDisplay=PathDisplay;
        this.Name=Name;
        this.Components=Components;
        this.Children=Children;
    }

    public bool Equals(SceneEmitNode Other){
        return string.Equals(this.LocalName,Other.LocalName,StringComparison.Ordinal)
            &&string.Equals(this.PathDisplay,Other.PathDisplay,StringComparison.Ordinal)
            &&string.Equals(this.Name,Other.Name,StringComparison.Ordinal)
            &&SceneEmitEquality.SequenceEqual(this.Components,Other.Components)
            &&SceneEmitEquality.SequenceEqual(this.Children,Other.Children);
    }

    public override bool Equals(object?Obj)=>Obj is SceneEmitNode Other&&this.Equals(Other);

    public override int GetHashCode()=>SceneEmitEquality.Combine(
        SceneEmitEquality.OrdinalHash(this.LocalName),
        SceneEmitEquality.OrdinalHash(this.PathDisplay),
        SceneEmitEquality.OrdinalHash(this.Name),
        SceneEmitEquality.CombineHashCodes(this.Components),
        SceneEmitEquality.CombineHashCodes(this.Children));
}

/// <summary>
/// 1つの<c>IScene</c>実装クラスから生成に必要な情報だけを抜き出した値(ADR-0040)。
///
/// <para>
/// <b><c>ISymbol</c>を絶対に含めてはならない。</b>シンボルは<c>Compilation</c>をルート保持し値等価性も
/// 持たないため、<c>IncrementalValuesProvider</c>へ流すとキャッシュが常時無効化されメモリをリークさせる。
/// シンボルを持つ<c>SceneModel</c>は<c>SceneModelBridge.TryAnalyze</c>の中だけで生き、その場でこの型へ
/// 変換されて捨てられる。
/// </para>
/// </summary>
internal readonly struct SceneEmitModel : IEquatable<SceneEmitModel> {
    internal readonly string?Namespace;

    internal readonly string ClassName;

    /// <summary><c>global::</c>接頭辞<b>なし</b>(<c>GameSystemResult.ClassFullyQualifiedName</c>と同じキー空間)。</summary>
    internal readonly string ClassFullyQualifiedName;

    internal readonly SceneEmitNode?Root;

    internal readonly int TotalNodeCount;

    internal readonly bool IsStartupScene;

    internal readonly int StartupOrder;

    /// <summary>診断のあるSceneは丸ごと生成をスキップする(既存の全か無かの方針)。診断の報告は<c>SceneDslAnalyzer</c>の責務。</summary>
    internal readonly bool HasErrors;

    internal readonly string FilePath;

    /// <summary>決定論的な整列キー。<c>Collect()</c>の順序に依存しないため。</summary>
    internal readonly int DeclarationPosition;

    internal SceneEmitModel(
        string?Namespace,
        string ClassName,
        string ClassFullyQualifiedName,
        SceneEmitNode?Root,
        int TotalNodeCount,
        bool IsStartupScene,
        int StartupOrder,
        bool HasErrors,
        string FilePath,
        int DeclarationPosition){
        this.Namespace=Namespace;
        this.ClassName=ClassName;
        this.ClassFullyQualifiedName=ClassFullyQualifiedName;
        this.Root=Root;
        this.TotalNodeCount=TotalNodeCount;
        this.IsStartupScene=IsStartupScene;
        this.StartupOrder=StartupOrder;
        this.HasErrors=HasErrors;
        this.FilePath=FilePath;
        this.DeclarationPosition=DeclarationPosition;
    }

    internal string GlobalQualifiedName=>this.Namespace is null
        ? $"global::{this.ClassName}"
        : $"global::{this.Namespace}.{this.ClassName}";

    /// <summary>生成可能か。診断があるか根ノードが取れなかったSceneは何も出さない。</summary>
    internal bool CanEmit=>!this.HasErrors&&this.Root is not null;

    public bool Equals(SceneEmitModel Other){
        return string.Equals(this.Namespace,Other.Namespace,StringComparison.Ordinal)
            &&string.Equals(this.ClassName,Other.ClassName,StringComparison.Ordinal)
            &&string.Equals(this.ClassFullyQualifiedName,Other.ClassFullyQualifiedName,StringComparison.Ordinal)
            &&Nullable.Equals(this.Root,Other.Root)
            &&this.TotalNodeCount==Other.TotalNodeCount
            &&this.IsStartupScene==Other.IsStartupScene
            &&this.StartupOrder==Other.StartupOrder
            &&this.HasErrors==Other.HasErrors
            &&string.Equals(this.FilePath,Other.FilePath,StringComparison.Ordinal)
            &&this.DeclarationPosition==Other.DeclarationPosition;
    }

    public override bool Equals(object?Obj)=>Obj is SceneEmitModel Other&&this.Equals(Other);

    public override int GetHashCode()=>SceneEmitEquality.Combine(
        SceneEmitEquality.OrdinalHash(this.Namespace),
        SceneEmitEquality.OrdinalHash(this.ClassName),
        this.Root?.GetHashCode()??0,
        this.TotalNodeCount,
        this.IsStartupScene?1:0,
        this.StartupOrder,
        this.HasErrors?1:0,
        SceneEmitEquality.OrdinalHash(this.FilePath),
        this.DeclarationPosition);
}
