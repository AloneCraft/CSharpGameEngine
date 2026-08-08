using System.Numerics;

namespace SoaSystemsDemo;

public struct LocalTransform
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;
}

public struct Velocity
{
    public Vector3 Value;
}

public struct Health
{
    public float Value;
}

// インスタンスフィールドを持たない構造体はタグコンポーネントとして扱われ、SGはこれを
// Query条件にのみ使い、列(ストレージ)を割り当てない。
public readonly struct MovableTag
{
}

public readonly struct DisabledTag
{
}

// frame1とframe2の間に初めて生成するArchetypeを作るための追加コンポーネント。既存のQuery
// キャッシュ(frame1で一度Updateされている)がこの新規Archetypeをframe2で正しく検出できることを
// DemoScenarioで検証する。
public readonly struct BoostTag
{
}

public readonly struct FrameContext
{
    public FrameContext(float deltaTime, ulong frameNumber)
    {
        this.DeltaTime = deltaTime;
        this.FrameNumber = frameNumber;
    }

    public float DeltaTime { get; }

    public ulong FrameNumber { get; }
}
