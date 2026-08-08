using CSharpGameEngine.Entities;

namespace SoaSystemsDemo;

[GameSystem]
public static class MovementSystems
{
    [Update]
    public static void Move(ref LocalTransform transform, in Velocity velocity, in MovableTag _, [Context] in FrameContext frame)
    {
        transform.Position += velocity.Value * frame.DeltaTime;
    }
}
