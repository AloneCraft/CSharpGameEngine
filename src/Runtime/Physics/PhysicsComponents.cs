using CSharpGameEngine.Entities;

namespace CSharpGameEngine.Runtime.Physics;

public enum ColliderShape:byte {
    Box,
    Sphere,
    Capsule,
    ConvexAsset,
    StaticTriangleMesh,
    /// <summary><see cref="PhysicsShapeAssetKind.Compound"/> のアセット。複数の凸形状の子要素が、1つのボディとして動く（ADR-0078/M0113、破壊された破壊可能オブジェクトの破片など）。</summary>
    Compound,
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
    /// <summary>
    /// UE の LinearDamping（1/s、ADR-0078/M0109）。各ティックで線速度に max(0, 1 - LinearDamping × Δt) を掛ける。0 なら速度を保つ。ゲーム
    /// 実行中にシステムが値を変更でき、ボディは次回の同期時にその値を反映する。
    /// </summary>
    public LogicalScalar LinearDamping;
    /// <summary>UE の AngularDamping（1/s、ADR-0078/M0109）。角速度に同様の減衰を適用する（車両は空中で3、地上で0に設定する）。</summary>
    public LogicalScalar AngularDamping;
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
