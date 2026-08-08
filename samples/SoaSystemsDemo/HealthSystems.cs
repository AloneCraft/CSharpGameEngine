using CSharpGameEngine.Entities;

namespace SoaSystemsDemo;

[GameSystem]
public static class HealthSystems
{
    [Update]
    public static void Regenerate(ref Health health, [Context] in FrameContext frame)
    {
        health.Value += 2.0f * frame.DeltaTime;
    }

    [Update]
    public static void DisableDeadEntity(in Health health, [CurrentEntity] Entity entity, [Commands] EntityCommandBuffer commands)
    {
        if (health.Value > 0f)
        {
            return;
        }

        commands.Add<DisabledTag>(entity);
        commands.Remove<MovableTag>(entity);
    }
}
