namespace CSharpGameEngine.SceneAnalysis;

/// <summary>
/// Scene DSLが参照するランタイム型の名前。<b>型そのものへは決して参照しない</b> —
/// CSharpGameEngine.SceneModelからCSharpGameEngine.Entities/Coreへのアセンブリ参照を発生させないため
/// (ADR-0001、既存の全ジェネレータと同じ方針)。
/// </summary>
internal static class SceneDslWellKnownNames {
    /// <summary><c>Compilation.GetTypeByMetadataName</c>用(<c>global::</c>接頭辞を付けない)。</summary>
    internal const string SceneInterfaceMetadataName="CSharpGameEngine.Entities.IScene";

    /// <summary><c>SymbolDisplayFormat.FullyQualifiedFormat</c>の出力と比較する用。</summary>
    internal const string SceneInterfaceFullyQualifiedName="global::CSharpGameEngine.Entities.IScene";

    internal const string NodeMetadataName="CSharpGameEngine.Entities.Node";

    internal const string NodeFullyQualifiedName="global::CSharpGameEngine.Entities.Node";

    /// <summary>非RootノードへSGが自動付与する。ユーザーが明示するとSCENE115。</summary>
    internal const string ParentFullyQualifiedName="global::CSharpGameEngine.Entities.Parent";

    /// <summary>Component数0のノードへSGが自動付与する(Archetypeは最低1Componentを要求する)。</summary>
    internal const string SceneNodeTagFullyQualifiedName="global::CSharpGameEngine.Entities.SceneNodeTag";

    /// <summary>
    /// 宣言的subsetの明示的な脱出口(ADR-0040)。CSharpGameEngine.WindowsのCamera.CreateFirstPersonのような
    /// 既存staticファクトリを許可するために使う。EntitiesではなくCoreに置くのは、Windowsが
    /// Entitiesを参照していないため。
    /// </summary>
    internal const string SceneFactoryAttributeMetadataName="CSharpGameEngine.Authoring.SceneFactoryAttribute";

    /// <summary>静的Asset参照(<c>Assets.Floor</c>)を許可する静的クラスに付ける。</summary>
    internal const string SceneAssetsAttributeMetadataName="CSharpGameEngine.Authoring.SceneAssetsAttribute";

    internal const string StartupSceneAttributeMetadataName="CSharpGameEngine.Entities.StartupSceneAttribute";

    internal const string RootPropertyName="Root";

    internal const string NameMemberName="Name";

    internal const string ComponentsMemberName="Components";

    internal const string ChildrenMemberName="Children";

    /// <summary>
    /// Nodeの入れ子とオブジェクト初期化子の再帰上限。生成コードや悪意ある入力でスタックオーバーフローを
    /// 起こさないための安全弁であり、実用上のScene階層がこれを超えることはない。
    /// </summary>
    internal const int MaxRecursionDepth=64;
}
