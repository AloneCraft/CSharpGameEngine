namespace CSharpGameEngine.Entities;

/// <summary>
/// Marks an ECS Component as runtime-derived state that must not be persisted in a World snapshot.
/// Kept separate from the snapshot codec so the declaration contract has no serialization or
/// runtime dependency closure.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class EcsSnapshotTransientAttribute:Attribute {
}
