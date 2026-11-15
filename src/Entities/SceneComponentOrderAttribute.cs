using System;

namespace CSharpGameEngine.Entities;

/// <summary>
/// シーンコンポーネントを表示・正規化する際に使う、安定した機能分類。
/// 数値はアセット作成コントラクトの一部である。相対順を変更すると、コンポーネント型に変更がなくてもコレクションが書き換わる。
/// </summary>
public enum SceneComponentCategory {
    /// <summary>空間姿勢、階層構造、その他の座標空間データ。</summary>
    Spatial=0,

    /// <summary>カメラとビュー／投影データ。</summary>
    Camera=1,

    /// <summary>照明と環境データ。</summary>
    LightingEnvironment=2,

    /// <summary>メッシュ、マテリアル、その他の描画データ。</summary>
    Rendering=3,

    /// <summary>物理ボディ、コライダー、拘束。</summary>
    Physics=4,

    /// <summary>音源、リスナー、ルーティングのデータ。</summary>
    Audio=5,

    /// <summary>ゲーム固有のアセット作成動作。</summary>
    Gameplay=6,

    /// <summary>生成されたランタイム用の補助コンポーネント、または一時コンポーネント。</summary>
    Runtime=7,
}

/// <summary>
/// コンポーネントのシーン内順序キーを決定的に指定する。
/// この属性のないコンポーネントは<see cref="SceneComponentCategory.Gameplay"/>として正規化し、
/// 既存のゲームコンポーネントに安定した後方互換の既定値を適用する。
/// </summary>
[AttributeUsage(AttributeTargets.Struct,AllowMultiple=false,Inherited=false)]
public sealed class SceneComponentOrderAttribute(SceneComponentCategory Category,int Order):Attribute {
    /// <summary>分類と、その分類内での明示的な順序を設定する。</summary>
    /// <summary><see cref="Order"/>より先に比較する機能分類。</summary>
    public SceneComponentCategory Category { get; }=Category;

    /// <summary><see cref="Category"/>内での安定した順序値。</summary>
    public int Order { get; }=Order;
}
