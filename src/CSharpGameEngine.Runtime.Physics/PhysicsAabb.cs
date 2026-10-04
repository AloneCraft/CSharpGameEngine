namespace CSharpGameEngine.Runtime.Physics;

/// <summary>An axis-aligned box in fixed point. It does not use the ECS, so the cooking tools compile it too (M0096 G).</summary>
public readonly record struct PhysicsAabb(LogicalVector3 Min,LogicalVector3 Max) {
    public static PhysicsAabb FromCenterHalfExtents(in LogicalVector3 Center,in LogicalVector3 HalfExtents)=>new(Center-HalfExtents,Center+HalfExtents);
    public bool Intersects(in PhysicsAabb Other)=>this.Min.R<=Other.Max.R&&this.Max.R>=Other.Min.R
        &&this.Min.U<=Other.Max.U&&this.Max.U>=Other.Min.U&&this.Min.F<=Other.Max.F&&this.Max.F>=Other.Min.F;
    public bool Contains(in PhysicsAabb Other)=>this.Min.R<=Other.Min.R&&this.Min.U<=Other.Min.U&&this.Min.F<=Other.Min.F
        &&this.Max.R>=Other.Max.R&&this.Max.U>=Other.Max.U&&this.Max.F>=Other.Max.F;
}
