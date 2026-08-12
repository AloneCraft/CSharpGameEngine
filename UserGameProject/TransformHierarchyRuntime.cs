using System.Numerics;

namespace UserGameProject;

/// <summary>
/// Per-frame shared transform lookup used by transform systems. It is a World Resource rather
/// than entity state: each entity stores only its local transform and optional Parent component.
/// </summary>
public sealed class TransformHierarchyRuntime {
    private readonly Dictionary<int,Matrix4x4> WorldByEntityId=[];

    public void Set(int EntityId,Matrix4x4 World)=>this.WorldByEntityId[EntityId]=World;

    public bool TryGet(int EntityId,out Matrix4x4 World)=>this.WorldByEntityId.TryGetValue(EntityId,out World);
}
