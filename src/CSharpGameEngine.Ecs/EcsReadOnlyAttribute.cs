namespace CSharpGameEngine.Ecs;

/// <summary>
/// アーキタイプ内の<c>ref readonly</c>プロパティに付ける読み取り専用マーカー。
/// エンティティのコンポーネント構成(アーキタイプキー)には一切影響しない。
/// Query/System基盤がまだ存在しない現時点では、EcsArchetypeGeneratorが
/// <c>ref readonly</c>宣言との整合性(ECSGEN004/ECSGEN005)を検証する以外の効果はない。
/// 将来のQuery/View機能が、通常のリフレクションでこの属性を再取得して利用できるよう
/// 生成コード側の宣言(ユーザー記述側)にそのまま残す。
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class EcsReadOnlyAttribute : Attribute
{
}
