using System.Numerics;

namespace UserGameProject;

/// <summary>
/// Per-frame shared transform lookup used by transform systems. It is a World Resource rather
/// than entity state: each entity stores only its local transform and optional Parent component.
/// </summary>
public sealed class TransformHierarchyRuntime {
    private readonly Dictionary<int,Matrix4x4> worldByEntityId=[];

    public void Set(int entityId,Matrix4x4 world)=>this.worldByEntityId[entityId]=world;

    public bool TryGet(int entityId,out Matrix4x4 world)=>this.worldByEntityId.TryGetValue(entityId,out world);
}
