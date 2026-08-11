using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <remarks>
/// このプロジェクトはRoslynホストとの互換性のためnetstandard2.0を対象としており(ADR-0014)、
/// <c>record</c>/<c>record struct</c>が要求する<c>System.Runtime.CompilerServices.IsExternalInit</c>を
/// BCLが提供しないため、recordではなく通常のstructとして定義する(既存Model群と同じ理由)。
/// </remarks>

/// <summary>
/// 1つのComponentを表す。<see cref="TupleElementName"/>が<c>null</c>なら「プロパティの戻り値そのもの」が
/// このComponentであることを意味し(単一Component Entity)、値があれば戻り値がTupleでその要素の1つで
/// あることを意味する。
/// </summary>
internal readonly struct StartupSceneComponentModel {
    internal readonly string TypeFullyQualifiedName;
    internal readonly string?TupleElementName;

    internal StartupSceneComponentModel(string typeFullyQualifiedName,string?tupleElementName){
        this.TypeFullyQualifiedName=typeFullyQualifiedName;
        this.TupleElementName=tupleElementName;
    }
}

/// <summary>1つの<c>[SceneEntity]</c>プロパティ = 1つの論理Entityの解析結果。</summary>
internal readonly struct StartupSceneEntityModel {
    internal readonly string PropertyName;
    internal readonly ImmutableArray<StartupSceneComponentModel>Components;
    internal readonly int DeclarationPosition;

    internal StartupSceneEntityModel(string propertyName,ImmutableArray<StartupSceneComponentModel>components,int declarationPosition){
        this.PropertyName=propertyName;
        this.Components=components;
        this.DeclarationPosition=declarationPosition;
    }
}

/// <summary>
/// 1つの<c>[StartupScene]</c>クラスの解析結果。<see cref="Diagnostics"/>が1件でもあれば、このクラス分の
/// 出力(合成System・Entity生成コード)を丸ごとスキップする(<c>GameSystemResult</c>/
/// <c>SceneDefinitionClassResult</c>と同じ全か無かの方針)。
/// </summary>
internal readonly struct StartupSceneClassResult {
    internal readonly string?Namespace;
    internal readonly string ClassName;
    internal readonly int Order;
    internal readonly ImmutableArray<StartupSceneEntityModel>Entities;
    internal readonly ImmutableArray<Diagnostic>Diagnostics;

    /// <summary>
    /// 合成Systemの実行順序タイブレークに使う宣言位置。<see cref="GameSystemResult"/>と同じ規則
    /// (<see cref="GameSystemExecutionOrder"/>参照)。
    /// </summary>
    internal readonly string FilePath;
    internal readonly int DeclarationPosition;

    internal StartupSceneClassResult(
        string?@namespace,
        string className,
        int order,
        ImmutableArray<StartupSceneEntityModel>entities,
        ImmutableArray<Diagnostic>diagnostics,
        string filePath,
        int declarationPosition){
        this.Namespace=@namespace;
        this.ClassName=className;
        this.Order=order;
        this.Entities=entities;
        this.Diagnostics=diagnostics;
        this.FilePath=filePath;
        this.DeclarationPosition=declarationPosition;
    }

    internal string ClassFullyQualifiedName=>this.Namespace is null?this.ClassName: $"{this.Namespace}.{this.ClassName}";

    internal string GlobalQualifiedName=>this.Namespace is null? $"global::{this.ClassName}" : $"global::{this.Namespace}.{this.ClassName}";
}
