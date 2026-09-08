using System.Collections.Generic;
using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>Result of a fixed Transform operation that cannot silently wrap or lose a GPU pose.</summary>
public enum FixedTransformStatus {
    Success,
    NumericOverflow,
    NonInvertible,
    FloatConversionOverflow,
}

/// <summary>
/// Row-vector TRS helpers. A child world matrix is always <c>local * parentWorld</c>, matching
/// System.Numerics and the editor contract.
/// </summary>
public static class TransformMath {
    /// <summary>
    /// Subtracts the fixed camera world origin before converting the TRS to float. This is the
    /// only conversion used for object, light, picking and gizmo render coordinates.
    /// </summary>
    public static FixedTransformStatus TryToCameraRelativeMatrix(
        in Transform Transform,in Transform Camera,out Matrix4x4 Matrix){
        FixedTransformStatus Status=FixedAffineTransform.TryFromTransform(
            Transform,out FixedAffineTransform Affine);
        if(Status!=FixedTransformStatus.Success){
            Matrix=default;
            return Status;
        }
        return Affine.TryToCameraRelativeMatrix(Camera,out Matrix);
    }
}

/// <summary>
/// Fixed-point affine matrix for hierarchy evaluation. It deliberately retains all affine terms
/// until camera-origin subtraction, so a large absolute translation never crosses a float boundary.
/// </summary>
public readonly struct FixedAffineTransform {
    private readonly LogicalAffineTransform Value;
    private FixedAffineTransform(LogicalAffineTransform Value){ this.Value=Value; }
    public LogicalScalar M11=>this.Value.RAxis.R;
    public LogicalScalar M12=>this.Value.RAxis.U;
    public LogicalScalar M13=>this.Value.RAxis.F;
    public LogicalScalar M21=>this.Value.UAxis.R;
    public LogicalScalar M22=>this.Value.UAxis.U;
    public LogicalScalar M23=>this.Value.UAxis.F;
    public LogicalScalar M31=>this.Value.FAxis.R;
    public LogicalScalar M32=>this.Value.FAxis.U;
    public LogicalScalar M33=>this.Value.FAxis.F;
    public LogicalScalar M41=>this.Value.Translation.R;
    public LogicalScalar M42=>this.Value.Translation.U;
    public LogicalScalar M43=>this.Value.Translation.F;
    public LogicalAffineTransform Logical=>this.Value;
    public static FixedAffineTransform Identity=>new(LogicalAffineTransform.Identity);
    public static FixedAffineTransform FromLogical(LogicalAffineTransform Value)=>new(Value);

    public static FixedTransformStatus TryFromTransform(
        in Transform Transform,out FixedAffineTransform Matrix){
        if(!Transform.Rotation.TryNormalize(out _)){
            Matrix=default;
            return FixedTransformStatus.NonInvertible;
        }
        if(!LogicalAffineTransform.TryCreateTrs(Transform.Position,Transform.Rotation,Transform.Scale,out var Logical)){
            Matrix=default;
            return FixedTransformStatus.NumericOverflow;
        }
        Matrix=new FixedAffineTransform(Logical);
        return FixedTransformStatus.Success;
    }

    public static FixedTransformStatus TryMultiply(
        in FixedAffineTransform Left,in FixedAffineTransform Right,out FixedAffineTransform Result){
        if(!LogicalAffineTransform.TryMultiply(Left.Value,Right.Value,out var Logical)){
            Result=default;
            return FixedTransformStatus.NumericOverflow;
        }
        Result=new FixedAffineTransform(Logical);
        return FixedTransformStatus.Success;
    }

    public FixedTransformStatus TryToCameraRelativeMatrix(
        in Transform Camera,out Matrix4x4 Matrix){
        FixedTransformStatus CameraStatus=TryFromTransform(Camera,out FixedAffineTransform CameraWorld);
        if(CameraStatus!=FixedTransformStatus.Success){
            Matrix=default;
            return CameraStatus;
        }
        return this.TryToCameraRelativeMatrix(CameraWorld,out Matrix);
    }

    /// <summary>
    /// Subtracts the already-composed fixed camera world origin before the first float conversion.
    /// This overload is the hierarchy-aware renderer boundary: both the object and camera may have
    /// arbitrary parents, but their absolute translations never enter a floating-point value.
    /// </summary>
    public FixedTransformStatus TryToCameraRelativeMatrix(
        in FixedAffineTransform CameraWorld,out Matrix4x4 Matrix){
        if(!LogicalScalar.TrySubtract(this.Value.Translation.R,CameraWorld.Value.Translation.R,out LogicalScalar X)
            ||!LogicalScalar.TrySubtract(this.Value.Translation.U,CameraWorld.Value.Translation.U,out LogicalScalar Y)
            ||!LogicalScalar.TrySubtract(this.Value.Translation.F,CameraWorld.Value.Translation.F,out LogicalScalar Z)){
            Matrix=default;
            return FixedTransformStatus.NumericOverflow;
        }
        if(!this.Value.RAxis.TryToSingle(out Vector3 RAxis)
            ||!this.Value.UAxis.TryToSingle(out Vector3 UAxis)
            ||!this.Value.FAxis.TryToSingle(out Vector3 FAxis)
            ||!X.TryToSingle(out float M41)||!Y.TryToSingle(out float M42)||!Z.TryToSingle(out float M43)){
            Matrix=default;
            return FixedTransformStatus.FloatConversionOverflow;
        }
        Matrix=new Matrix4x4(
            RAxis.X,RAxis.Y,RAxis.Z,0f,
            UAxis.X,UAxis.Y,UAxis.Z,0f,
            FAxis.X,FAxis.Y,FAxis.Z,0f,
            M41,M42,M43,1f);
        return FixedTransformStatus.Success;
    }

    /// <summary>
    /// Extracts the resolved world directions of a Transform's local +Z (forward) and local +Y
    /// (up) axes. These row-vector basis rows stay fixed-point until the final camera view
    /// boundary; callers normalize them after conversion to remove any inherited scale.
    /// </summary>
    public FixedTransformStatus TryGetCameraBasis(out Vector3 Forward,out Vector3 Up){
        if(!this.Value.FAxis.TryToSingle(out Forward)||!this.Value.UAxis.TryToSingle(out Up)){
            Forward=default;
            Up=default;
            return FixedTransformStatus.FloatConversionOverflow;
        }
        float ForwardLengthSquared=Forward.LengthSquared();
        float UpLengthSquared=Up.LengthSquared();
        if(!float.IsFinite(Forward.X)||!float.IsFinite(Forward.Y)||!float.IsFinite(Forward.Z)
            ||!float.IsFinite(Up.X)||!float.IsFinite(Up.Y)||!float.IsFinite(Up.Z)
            ||!float.IsFinite(ForwardLengthSquared)||!float.IsFinite(UpLengthSquared)
            ||ForwardLengthSquared<=1e-12f||UpLengthSquared<=1e-12f){
            Forward=default;
            Up=default;
            return FixedTransformStatus.NonInvertible;
        }

        return FixedTransformStatus.Success;
    }
}

/// <summary>Fixed hierarchy resolution status, including arithmetic failure states.</summary>
public enum FixedTransformResolveStatus {
    Success,
    MissingTransform,
    DeadParent,
    Cycle,
    NumericOverflow,
    NonInvertible,
    FrameMismatch,
}

/// <summary>
/// Evaluates parent-child affine composition entirely in the configured fixed precision. Call
/// <see cref="FixedAffineTransform.TryToCameraRelativeMatrix"/> only after resolution.
/// </summary>
public sealed class FixedTransformWorldResolver {
    private readonly Dictionary<int,FixedAffineTransform>WorldByEntity=[];
    private readonly Dictionary<int,FixedTransformResolveStatus>StatusByEntity=[];
    private readonly HashSet<int>Resolving=[];

    public void BeginFrame(){
        this.WorldByEntity.Clear();
        this.StatusByEntity.Clear();
        this.Resolving.Clear();
    }

    public FixedTransformResolveStatus Resolve(
        int EntityId,
        TryGetLocalTransform GetTransform,
        TryGetParentEntity GetParent,
        out FixedAffineTransform World){
        if(this.WorldByEntity.TryGetValue(EntityId,out World)){
            return FixedTransformResolveStatus.Success;
        }
        if(this.StatusByEntity.TryGetValue(EntityId,out FixedTransformResolveStatus PreviousStatus)){
            World=default;
            return PreviousStatus;
        }
        if(!this.Resolving.Add(EntityId)){
            World=default;
            return this.StatusByEntity[EntityId]=FixedTransformResolveStatus.Cycle;
        }

        try {
            if(!GetTransform(EntityId,out Transform Local)){
                World=default;
                return this.StatusByEntity[EntityId]=FixedTransformResolveStatus.MissingTransform;
            }
            FixedTransformStatus LocalStatus=FixedAffineTransform.TryFromTransform(Local,out FixedAffineTransform LocalWorld);
            if(LocalStatus!=FixedTransformStatus.Success){
                World=default;
                return this.StatusByEntity[EntityId]=LocalStatus==FixedTransformStatus.NonInvertible
                    ?FixedTransformResolveStatus.NonInvertible
                    :FixedTransformResolveStatus.NumericOverflow;
            }
            if(!GetParent(EntityId,out int ParentEntityId)){
                this.WorldByEntity.Add(EntityId,World=LocalWorld);
                return FixedTransformResolveStatus.Success;
            }

            FixedTransformResolveStatus ParentStatus=this.Resolve(
                ParentEntityId,GetTransform,GetParent,out FixedAffineTransform ParentWorld);
            if(ParentStatus!=FixedTransformResolveStatus.Success){
                World=default;
                return this.StatusByEntity[EntityId]=ParentStatus==FixedTransformResolveStatus.MissingTransform
                    ?FixedTransformResolveStatus.DeadParent
                    :ParentStatus;
            }

            FixedTransformStatus ComposeStatus=FixedAffineTransform.TryMultiply(
                LocalWorld,ParentWorld,out World);
            if(ComposeStatus!=FixedTransformStatus.Success){
                World=default;
                return this.StatusByEntity[EntityId]=FixedTransformResolveStatus.NumericOverflow;
            }
            this.WorldByEntity.Add(EntityId,World);
            return FixedTransformResolveStatus.Success;
        } finally {
            this.Resolving.Remove(EntityId);
        }
    }
}

public delegate bool TryGetLocalTransform(int EntityId,out Transform Transform);
public delegate bool TryGetParentEntity(int EntityId,out int ParentEntityId);
