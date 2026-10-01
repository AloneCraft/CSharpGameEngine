namespace CSharpGameEngine.Entities;

public enum SimulationPlaybackState:byte {
    Running,
    Paused,
}

/// <summary>Stable frame phases. Values are persisted by generated execution metadata.</summary>
public enum SystemPhase:byte {
    FrameInput,
    FixedSimulation,
    PhysicsSimulation,
    PostPhysics,
    Presentation,
}

/// <summary>Assigns an ECS system to one deterministic frame phase.</summary>
[AttributeUsage(AttributeTargets.Struct,AllowMultiple=false,Inherited=false)]
public sealed class SystemPhaseAttribute(SystemPhase Phase):Attribute {
    public SystemPhase Phase { get; }=Phase;
}

/// <summary>Marks an <c>in</c> struct parameter as a read-only ECS component access.</summary>
[AttributeUsage(AttributeTargets.Parameter,AllowMultiple=false,Inherited=false)]
public sealed class ReadOnlyComponentAttribute:Attribute {
}

/// <summary>
/// Opts a system into the generated access schedule: scheduled systems of one phase that touch the same
/// component, one of them writing, must be ordered by <see cref="RunsAfterAttribute"/> or
/// <see cref="RunsBeforeAttribute"/> (SYSGEN032). It does not change the execution order.
/// </summary>
[AttributeUsage(AttributeTargets.Struct,AllowMultiple=false,Inherited=false)]
public sealed class ScheduledSystemAttribute:Attribute {
}

/// <summary>
/// ADR-0073: this system runs after <paramref name="SystemType"/> in the same phase. Systems of one phase run
/// in an order that keeps every declared edge; systems with no path between them run by full name (ordinal).
/// A system of an earlier phase is always before, so naming one does nothing; naming a later-phase system is
/// SYSGEN035.
/// </summary>
[AttributeUsage(AttributeTargets.Struct,AllowMultiple=true,Inherited=false)]
public sealed class RunsAfterAttribute(Type SystemType):Attribute {
    public Type SystemType{get;}=SystemType??throw new ArgumentNullException(nameof(SystemType));
}

/// <summary>
/// ADR-0073: this system runs before <paramref name="SystemType"/> in the same phase, the mirror of
/// <see cref="RunsAfterAttribute"/>. Game code needs it to run before an engine system, because engine code
/// cannot name a game's types.
/// </summary>
[AttributeUsage(AttributeTargets.Struct,AllowMultiple=true,Inherited=false)]
public sealed class RunsBeforeAttribute(Type SystemType):Attribute {
    public Type SystemType{get;}=SystemType??throw new ArgumentNullException(nameof(SystemType));
}

/// <summary>A world-level system is invoked once per phase rather than once per matching Entity.</summary>
public interface IWorldSystem:ISystem {
}

/// <summary>Integer simulation time exposed to systems. No wall-clock value enters a fixed update.</summary>
public readonly record struct SimulationTiming(ulong Tick,int TickRate,SystemPhase Phase) {
    public static SimulationTiming Default=>new(0,60,SystemPhase.FixedSimulation);
}
