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

/// <summary>Opts a system into the generated deterministic access schedule.</summary>
[AttributeUsage(AttributeTargets.Struct,AllowMultiple=false,Inherited=false)]
public sealed class ScheduledSystemAttribute:Attribute {
}

/// <summary>Declares a deterministic schedule edge from another system to this system.</summary>
[AttributeUsage(AttributeTargets.Struct,AllowMultiple=true,Inherited=false)]
public sealed class RunsAfterAttribute(Type SystemType):Attribute {
    public Type SystemType{get;}=SystemType??throw new ArgumentNullException(nameof(SystemType));
}

/// <summary>A world-level system is invoked once per phase rather than once per matching Entity.</summary>
public interface IWorldSystem:ISystem {
}

/// <summary>Integer simulation time exposed to systems. No wall-clock value enters a fixed update.</summary>
public readonly record struct SimulationTiming(ulong Tick,int TickRate,SystemPhase Phase) {
    public static SimulationTiming Default=>new(0,60,SystemPhase.FixedSimulation);
}
