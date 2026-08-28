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

/// <summary>A world-level system is invoked once per phase rather than once per matching Entity.</summary>
public interface IWorldSystem:ISystem {
}

/// <summary>Integer simulation time exposed to systems. No wall-clock value enters a fixed update.</summary>
public readonly record struct SimulationTiming(ulong Tick,int TickRate,SystemPhase Phase) {
    public static SimulationTiming Default=>new(0,60,SystemPhase.FixedSimulation);
}
