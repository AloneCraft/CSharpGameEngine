namespace CSharpGameEngine.Entities;

/// <summary>
/// <c>World</c>上のSceneインスタンスへの軽量ハンドル(ADR-0040)。同じScene定義を何度でも
/// instantiateでき、それぞれ独立した<see cref="SceneInstance"/>になる。
///
/// <para>
/// <b><see cref="Entity"/>と違って世代(<see cref="Version"/>)を持つ。</b>Entityは案Aにより
/// IDを再利用しないので世代が不要だが、Sceneスロットは再利用する — エディターでのリロードを繰り返しても
/// スロットが無限に増えないようにするため。再利用する以上、古いハンドルが新しいSceneを指してしまう
/// ABA問題を防ぐ世代が必要になる。世代はEntityでの「再利用しない」という選択と同じ論理の裏返しである。
/// </para>
/// </summary>
public readonly record struct SceneInstance(int Slot,int Version) {
    /// <summary>どのSceneにも属さないことを表す。<c>default(SceneInstance)</c>も同様に無効(世代は1から始まる)。</summary>
    public static SceneInstance None=>new(-1,0);

    public bool IsNone=>this.Slot<0;
}
