using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// An Entity that ends itself (ADR-0078/M0105): the time left runs down by one fixed tick per tick and at zero the Entity is destroyed.
/// On the root of a Scene it ends the whole Scene, children included, which is how a spawned marker or burst cleans up after itself;
/// on any other Entity it ends that Entity alone. Counted in fixed ticks, so it is as deterministic as the rest of the simulation.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,20)]
public struct Lifetime {
    /// <summary>Seconds left. At zero or less the Entity is destroyed at the next fixed tick.</summary>
    public LogicalScalar Seconds;
}
/// <summary>ADR-0078/M0105: counts every <see cref="Lifetime"/> down and destroys the Entities that ran out.</summary>
[SystemPhase(SystemPhase.FixedSimulation)]
internal struct LifetimeExpire:ISystem {
    public void Update(Entity Entity,ref Lifetime Lifetime,ref State State){
        var Step=LogicalScalar.One/LogicalScalar.FromInt64(State.FixedTickRate);
        if(LogicalScalar.TrySubtract(Lifetime.Seconds,Step,out var Left)&&Left>LogicalScalar.Zero){
            Lifetime.Seconds=Left;
            return;
        }
        Lifetime.Seconds=LogicalScalar.Zero;
        var Scene=State.World.GetOwningScene(Entity);
        if(!Scene.IsNone&&State.World.GetSceneRoot(Scene)==Entity){
            State.World.UnloadSceneDeferred(Scene,ref State);
        } else {
            State.World.DestroyDeferred(Entity,ref State);
        }
    }
}
