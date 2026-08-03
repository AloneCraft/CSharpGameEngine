using CSharpGameEngine.Ecs;

namespace UserGameProject;

[EcsArchetype]
public partial struct MovingObject
{
    // 宣言順は自由。EcsArchetypeGeneratorがコンポーネント型の完全修飾名で正規化する。
    public partial ref Velocity Velocity { get; }

    public partial ref Position Position { get; }
}

[EcsArchetype]
public partial struct DamageableObject
{
    public partial ref Health Health { get; }

    public partial ref Position Position { get; }
}

[EcsArchetype]
public partial struct Player
{
    // わざと正規化順とは異なる順番で書いている
    public partial ref PlayerTag PlayerTag { get; }

    public partial ref Velocity Velocity { get; }

    public partial ref Health Health { get; }

    public partial ref Rotation Rotation { get; }

    public partial ref Position Position { get; }
}

/// <summary>
/// 名前付き引数コンストラクタ形式(<c>world.Create&lt;Bullet&gt;(position: ..., velocity: ..., damage: ...)</c>)
/// の実演用アーキタイプ。
/// </summary>
[EcsArchetype]
public partial struct Bullet
{
    public partial ref Velocity Velocity { get; }

    public partial ref Position Position { get; }

    public partial ref Damage Damage { get; }
}

/// <summary>ステージ上の静止オブジェクト(地面など)用の最小アーキタイプ。移動しないので毎フレームは触らない。</summary>
[EcsArchetype]
public partial struct StaticProp
{
    public partial ref Position Position { get; }

    public partial ref Rotation Rotation { get; }
}
