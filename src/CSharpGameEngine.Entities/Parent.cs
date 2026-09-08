namespace CSharpGameEngine.Entities;

/// <summary>
/// 親子関係を表す汎用Component(Unity DOTSの<c>Unity.Transforms.Parent</c>と同じ位置づけ)。
/// このECS自体はHierarchy/親子の概念を持たないため、Source GeneratorがScene DSLの
/// <c>Node.Children</c>の入れ子構造から自動的にこのComponentを子Entityへ付与する
/// (ADR-0040)。ユーザーが<c>Components</c>へ直接書くことはできない — SCENE115で弾かれる。
///
/// <para>
/// <b>ここに入るのは生のEntityハンドルである。</b>親を含むSceneを<c>UnloadScene</c>すると、
/// 他Scene/World直属の子が持つ<see cref="Value"/>は死んだEntityを指したまま残る。親子を辿る
/// Systemは必ず<c>World.IsAlive(Parent.Value)</c>でガードすること(ADR-0040 既知の制約)。
/// </para>
/// </summary>
[EcsSnapshotTransient]
public struct Parent {
    public Entity Value;
}
