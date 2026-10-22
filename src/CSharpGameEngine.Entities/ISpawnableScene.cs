using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.Entities;
/// <summary>
/// Source Generatorが、根ノードに <see cref="Transform"/> を持つ Scene 定義へ実装する(ADR-0078/M0105。<b>ユーザーは書かない</b>)。
/// 実行中に Scene を位置つきで生成できる Scene の目印で、<c>Spawner.Spawn&lt;TScene&gt;()</c> の型制約になる。
/// 根に Transform が無い Scene は実装しないので、<c>Spawn</c> に渡すとコンパイルエラーになる。
/// </summary>
public interface ISpawnableScene:ISceneDefinition {
    /// <summary>
    /// System の中から安全な、構造変更キュー経由の生成。根ノードの位置を <paramref name="Position"/> に置き換える
    /// (回転と拡縮は Scene に書いた値のまま)。子ノードは根からの相対の位置になる。
    /// </summary>
    static abstract SceneInstance InstantiateDeferredAt(ref World World,LogicalVector3 Position);
    /// <summary>同じで、根ノードの回転も <paramref name="Rotation"/> に置き換える。</summary>
    static abstract SceneInstance InstantiateDeferredAt(ref World World,LogicalVector3 Position,LogicalQuaternion Rotation);
}
/// <summary>生成コードが根ノードの Transform を置き換えるための関数。Scene に書いた Transform を、実行時に渡された位置(と回転)で上書きする。</summary>
public static class SceneSpawn {
    public static Transform At(Transform Authored,LogicalVector3 Position){
        Authored.Position=Position;
        return Authored;
    }
    public static Transform At(Transform Authored,LogicalVector3 Position,LogicalQuaternion Rotation){
        Authored.Position=Position;
        Authored.Rotation=Rotation;
        return Authored;
    }
}
