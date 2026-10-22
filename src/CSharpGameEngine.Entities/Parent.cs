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

/// <summary>
/// 親Entityが持つComponent <typeparamref name="T"/>の読み取り専用の写し(ADR-0077)。
/// Systemの引数に<c>in Parent&lt;T&gt;</c>と書くと、生成されたループが子EntityのParentから親を引き、
/// 親が生きていて<typeparamref name="T"/>を持つときだけ呼ぶ。親が死んでいる、または<typeparamref name="T"/>を
/// 持たない子はスキップされる(既定値を渡さない)。値は呼び出しごとのコピーなので、親へ書き戻すことはできない。
/// </summary>
public readonly struct Parent<T>(Entity Entity,T Value) where T:struct {
    /// <summary>親Entity本人。親の別のComponentを<c>World.TryGetComponent</c>で読むときに使う。</summary>
    public readonly Entity Entity=Entity;
    public readonly T Value=Value;
}
