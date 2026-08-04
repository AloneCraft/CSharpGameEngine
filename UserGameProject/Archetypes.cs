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

    public partial ref VerticalMotion VerticalMotion { get; }

    public partial ref CameraRig CameraRig { get; }

    public partial ref InputState InputState { get; }
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

/// <summary>
/// UEレベルから移植した静止プロップ(mobility=static)。Static/Movableの区別はコンポーネントの
/// フラグではなく、どちらのアーキタイプでスポーンするかで表現する(アーキタイプECSの流儀 - 動く
/// 可能性のあるオブジェクトだけが<see cref="Velocity"/>列のコストを払う)。SceneLoaderが
/// RenderObjectRefでStageRendererのオブジェクトindexと1:1に紐付ける。
/// </summary>
[EcsArchetype]
public partial struct StageProp
{
    public partial ref Position Position { get; }

    public partial ref Rotation Rotation { get; }

    public partial ref Scale Scale { get; }

    public partial ref RenderObjectRef RenderObjectRef { get; }
}

/// <summary>StageProp + Velocity。Systems/MovingPropSystem.csが毎フレーム上下動/回転させる、可動物体の実証用アーキタイプ。</summary>
[EcsArchetype]
public partial struct MovableStageProp
{
    public partial ref Position Position { get; }

    public partial ref Rotation Rotation { get; }

    public partial ref Scale Scale { get; }

    public partial ref RenderObjectRef RenderObjectRef { get; }

    public partial ref Velocity Velocity { get; }
}

/// <summary>単一のディレクショナルライト(太陽)。シングルトンとして1体だけスポーンする想定。</summary>
[EcsArchetype]
public partial struct SunLight
{
    public partial ref DirectionalLightData DirectionalLightData { get; }
}

/// <summary>フラットなアンビエント項(SkyLightの近似)。シングルトン。</summary>
[EcsArchetype]
public partial struct AmbientLight
{
    public partial ref AmbientLightData AmbientLightData { get; }
}

/// <summary>簡易Exponential Height Fogのパラメータ。シングルトン。</summary>
[EcsArchetype]
public partial struct FogVolume
{
    public partial ref FogData FogData { get; }
}
