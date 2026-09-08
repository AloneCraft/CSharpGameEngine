namespace CSharpGameEngine.Entities;

/// <summary>
/// <c>World</c>上のSceneインスタンスへの軽量ハンドル(ADR-0040)。同じScene定義を何度でも
/// instantiateでき、それぞれ独立した<see cref="SceneInstance"/>になる。
///
/// <para>
/// <see cref="Entity"/>と同様に世代(<see cref="Version"/>)を持つ。Sceneスロットを再利用しても、
/// 古いハンドルが新しいSceneを指すABA問題を防ぐ。
/// </para>
/// </summary>
public readonly record struct SceneInstance(int Slot,int Version) {
    /// <summary>どのSceneにも属さないことを表す。<c>default(SceneInstance)</c>も同様に無効(世代は1から始まる)。</summary>
    public static SceneInstance None=>new(-1,0);

    public bool IsNone=>this.Slot<0;
}
