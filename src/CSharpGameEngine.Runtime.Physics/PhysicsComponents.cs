using CSharpGameEngine.Entities;

namespace CSharpGameEngine.Runtime.Physics;

public enum ColliderShape:byte {
    Box,
    Sphere,
    Capsule,
    ConvexAsset,
    Compound,
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
    public PhysicsScalar Friction = PhysicsScalar.FromDouble(0.5);
    public PhysicsScalar Restitution = PhysicsScalar.Zero;
}

[SceneComponentOrder(SceneComponentCategory.Spatial,20)]
public struct Collider() {
    public ColliderShape Shape = ColliderShape.Box;
    public PhysicsVector3 Center;
    public PhysicsVector3 HalfExtents = new(
                                            PhysicsScalar.FromDouble(0.5),
                                            PhysicsScalar.FromDouble(0.5),
                                            PhysicsScalar.FromDouble(0.5));
    public PhysicsScalar Radius = PhysicsScalar.FromDouble(0.5);
    public PhysicsScalar HalfHeight = PhysicsScalar.FromDouble(0.5);
    public PhysicsMaterial Material = new();
    public PhysicsLayer Layer;
    public PhysicsLayerMask CollidesWith = PhysicsLayerMask.All;
    public string? Asset;
    public bool IsTrigger;
}

[SceneComponentOrder(SceneComponentCategory.Spatial,21)]
public struct RigidBody() {
    public PhysicsMotionType MotionType = PhysicsMotionType.Dynamic;
    public PhysicsScalar Mass = PhysicsScalar.One;
    public PhysicsScalar GravityFactor = PhysicsScalar.One;
    public bool AllowSleep = true;
    public PhysicsCcdMode Ccd;
}

public struct PhysicsStableId:IRuntimeComponent<Collider> {
    public ulong Value;
}

public struct PhysicsRotation() {
    public PhysicsQuaternion Value = PhysicsQuaternion.Identity;
}

public struct PhysicsVelocity {
    public PhysicsVector3 Linear;
    public PhysicsVector3 Angular;
    public static PhysicsVelocity Zero=>default;
}

[SceneComponentOrder(SceneComponentCategory.Spatial,22)]
public struct Destructible {
    public string FractureAsset;
    public PhysicsScalar FractureThreshold;
    public int FragmentBudget;
}

public enum PhysicsCachePlaybackState:byte {
    Stopped,
    Playing,
    HandedOffToRealtime,
    Faulted,
}

public struct PhysicsCachePlayback {
    public string Cache;
    public PhysicsCachePlaybackState State;
    public bool AllowRealtimeHandoff;
}
