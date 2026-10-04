using CSharpGameEngine.Entities;

namespace CSharpGameEngine.Runtime.Physics;

public enum ColliderShape:byte {
    Box,
    Sphere,
    Capsule,
    ConvexAsset,
    StaticTriangleMesh,
}

public enum PhysicsMotionType:byte {
    Dynamic,
    Kinematic,
}

public enum PhysicsCcdMode:byte {
    Discrete,
    ContinuousStatic,
}

public readonly record struct PhysicsLayer(byte Value);

public readonly record struct PhysicsLayerMask(ulong Value) {
    public static PhysicsLayerMask None=>default;
    public static PhysicsLayerMask All=>new(ulong.MaxValue);
    public static PhysicsLayerMask FromLayer(int Layer)=>Layer is >=0 and <64?new(1UL<<Layer):throw new ArgumentOutOfRangeException(nameof(Layer));
    public bool Contains(PhysicsLayer Layer)=>(this.Value&(1UL<<Layer.Value))!=0;
}

public struct PhysicsMaterial() {
    public LogicalScalar Friction = LogicalScalar.FromSingle(0.5f);
    public LogicalScalar Restitution = LogicalScalar.Zero;
}

[SceneComponentOrder(SceneComponentCategory.Spatial,20)]
public struct Collider() {
    public ColliderShape Shape = ColliderShape.Box;
    public LogicalVector3 Center;
    public LogicalVector3 HalfExtents = new(
                                            LogicalScalar.FromSingle(0.5f),
                                            LogicalScalar.FromSingle(0.5f),
                                            LogicalScalar.FromSingle(0.5f));
    public LogicalScalar Radius = LogicalScalar.FromSingle(0.5f);
    public LogicalScalar HalfHeight = LogicalScalar.FromSingle(0.5f);
    public PhysicsMaterial Material = new();
    public PhysicsLayer Layer;
    public PhysicsLayerMask CollidesWith = PhysicsLayerMask.All;
    public string? Asset;
    public bool IsTrigger;
}

[SceneComponentOrder(SceneComponentCategory.Spatial,21)]
public struct RigidBody() {
    public PhysicsMotionType MotionType = PhysicsMotionType.Dynamic;
    public LogicalScalar Mass = LogicalScalar.One;
    public LogicalScalar GravityFactor = LogicalScalar.One;
    public bool AllowSleep = true;
    public PhysicsCcdMode Ccd;
}

public struct PhysicsStableId:IRuntimeComponent<Collider> {
    public ulong Value;
}

[SceneComponentOrder(SceneComponentCategory.Spatial,23)]
public struct PhysicsRotation() {
    public LogicalQuaternion Value = LogicalQuaternion.Identity;
}

[SceneComponentOrder(SceneComponentCategory.Spatial,24)]
public struct PhysicsVelocity {
    public LogicalVector3 Linear;
    public LogicalVector3 Angular;
    public static PhysicsVelocity Zero=>default;
}

[SceneComponentOrder(SceneComponentCategory.Spatial,22)]
public struct Destructible {
    public string FractureAsset;
    public LogicalScalar FractureThreshold;
    public int FragmentBudget;
}

public enum PhysicsCachePlaybackState:byte {
    Stopped,
    Playing,
    HandedOffToRealtime,
    Faulted,
}

[SceneComponentOrder(SceneComponentCategory.Spatial,25)]
public struct PhysicsCachePlayback {
    public string Cache;
    public PhysicsCachePlaybackState State;
    public bool AllowRealtimeHandoff;
}
