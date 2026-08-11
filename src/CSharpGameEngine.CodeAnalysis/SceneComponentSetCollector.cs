namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// Scene定義の全ノードが必要とするComponent集合を列挙する(ADR-0040)。
///
/// <para>
/// <see cref="GameSystemIndexAssigner.Assign"/>の<c>AdditionalComponentSets</c>へ、<b>実Systemの後に</b>
/// 登録される。この順序により、Sceneを追加・削除してもSystem由来のArchetype番号は動かない
/// (ADR-0038が確立した保証をそのまま継承する)。Sceneのノード形状がSystemのQuery形状と一致すれば、
/// 集合デデュープによって同じArchetypeへ自動的に収まる。
/// </para>
/// </summary>
internal static class SceneComponentSetCollector {
    internal static List<IReadOnlyList<string>>Collect(SceneEmitModel Scene){
        List<IReadOnlyList<string>>Sets=[];

        if(!Scene.CanEmit||Scene.Root is not { } Root){
            return Sets;
        }

        Walk(Root,IsRoot:true,Sets);
        return Sets;
    }

    private static void Walk(SceneEmitNode Node,bool IsRoot,List<IReadOnlyList<string>>Sets){
        // 実効構成の導出はSceneInstantiateEmitterと同じ1箇所を必ず共有する。ここがズレると
        // 「登録されていないArchetypeをCreateしようとする」という診断困難なビルド破壊になる。
        Sets.Add([.. SceneInstantiateEmitter
            .BuildEffectiveComponents(Node,IsRoot?null:"Parent")
            .Select(static C=>C.TypeFullyQualifiedName)]);

        foreach(SceneEmitNode Child in Node.Children){
            Walk(Child,IsRoot:false,Sets);
        }
    }
}
