namespace CSharpGameEngine.Entities;

/// <summary>
/// ECSコンポーネントに付ける。ランタイムで導出される状態をWorldスナップショットに永続化してはならないことを示す。
/// 宣言コントラクトがシリアライズやランタイム依存関係に依存しないよう、スナップショットコーデックとは分離している。
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class EcsSnapshotTransientAttribute:Attribute {
}
