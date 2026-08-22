using System;
using System.Collections.Generic;
using System.Numerics;
using CSharpGameEngine.Entities;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>
/// Scene-user-compilation Transform. Its absolute position and scale use the precision selected
/// by <c>the Runtime-owned Logical precision library</c>; quaternion components use a normalized-range
/// fixed scalar. Coordinates use Unity-compatible RUF (+X right, +Y up, +Z forward).
/// </summary>
/// <remarks>
/// This source is linked into a Scene compilation (rather than the Windows renderer DLL), because
/// the limb width is selected by that compilation. The renderer receives only camera-relative
/// matrices converted to float at the final GPU boundary.
/// </remarks>
[SceneComponentOrder(SceneComponentCategory.Spatial,0)]
public struct Transform {
    public Logical.Scalar PositionX;
    public Logical.Scalar PositionY;
    public Logical.Scalar PositionZ;

    public Logical.Scalar RotationX;
    public Logical.Scalar RotationY;
    public Logical.Scalar RotationZ;
    public Logical.Scalar RotationW;

    public Logical.Scalar ScaleX;
    public Logical.Scalar ScaleY;
    public Logical.Scalar ScaleZ;

    public Transform(){
        this.RotationW=Logical.Scalar.One;
        this.ScaleX=Logical.Scalar.One;
        this.ScaleY=Logical.Scalar.One;
        this.ScaleZ=Logical.Scalar.One;
    }
}

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
    public static Matrix4x4 ToMatrix(in Transform Transform)=>
        Matrix4x4.CreateScale(Transform.ScaleX,Transform.ScaleY,Transform.ScaleZ)
        *Matrix4x4.CreateFromQuaternion(new Quaternion(
            Transform.RotationX,Transform.RotationY,Transform.RotationZ,Transform.RotationW))
        *Matrix4x4.CreateTranslation(
            Transform.PositionX,Transform.PositionY,Transform.PositionZ);

    public static Matrix4x4 ToWorld(in Transform Local,in Matrix4x4 ParentWorld)=>
        ToMatrix(Local)*ParentWorld;

    /// <summary>
    /// Recalculates local TRS for reparenting. A non-invertible parent or non-decomposable affine
    /// transform is reported rather than overwriting the last valid pose.
    /// </summary>
    public static bool TryCalculateLocal(
        in Matrix4x4 World,in Matrix4x4 NewParentWorld,out Transform Local){
        if(!Matrix4x4.Invert(NewParentWorld,out Matrix4x4 InverseParent)
            ||!Matrix4x4.Decompose(World*InverseParent,
                out Vector3 Scale,out Quaternion Rotation,out Vector3 Position)){
            Local=default;
            return false;
        }

        Local=new Transform {
            PositionX=Position.X,
            PositionY=Position.Y,
            PositionZ=Position.Z,
            RotationX=Rotation.X,
            RotationY=Rotation.Y,
            RotationZ=Rotation.Z,
            RotationW=Rotation.W,
            ScaleX=Scale.X,
            ScaleY=Scale.Y,
            ScaleZ=Scale.Z,
        };
        return true;
    }

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
    public Logical.Scalar M11 { get; }
    public Logical.Scalar M12 { get; }
    public Logical.Scalar M13 { get; }
    public Logical.Scalar M21 { get; }
    public Logical.Scalar M22 { get; }
    public Logical.Scalar M23 { get; }
    public Logical.Scalar M31 { get; }
    public Logical.Scalar M32 { get; }
    public Logical.Scalar M33 { get; }
    public Logical.Scalar M41 { get; }
    public Logical.Scalar M42 { get; }
    public Logical.Scalar M43 { get; }

    private FixedAffineTransform(
        Logical.Scalar M11,Logical.Scalar M12,Logical.Scalar M13,
        Logical.Scalar M21,Logical.Scalar M22,Logical.Scalar M23,
        Logical.Scalar M31,Logical.Scalar M32,Logical.Scalar M33,
        Logical.Scalar M41,Logical.Scalar M42,Logical.Scalar M43){
        this.M11=M11; this.M12=M12; this.M13=M13;
        this.M21=M21; this.M22=M22; this.M23=M23;
        this.M31=M31; this.M32=M32; this.M33=M33;
        this.M41=M41; this.M42=M42; this.M43=M43;
    }

    public static FixedAffineTransform Identity=>new(
        Logical.Scalar.One,Logical.Scalar.Zero,Logical.Scalar.Zero,
        Logical.Scalar.Zero,Logical.Scalar.One,Logical.Scalar.Zero,
        Logical.Scalar.Zero,Logical.Scalar.Zero,Logical.Scalar.One,
        Logical.Scalar.Zero,Logical.Scalar.Zero,Logical.Scalar.Zero);

    public static FixedTransformStatus TryFromTransform(
        in Transform Transform,out FixedAffineTransform Matrix){
        FixedTransformStatus Status=TryGetNormalizedQuaternion(
            Transform,out Logical.Scalar X,out Logical.Scalar Y,out Logical.Scalar Z,out Logical.Scalar W);
        if(Status!=FixedTransformStatus.Success){
            Matrix=default;
            return Status;
        }

        if(!TryDoubleProduct(X,X,out Logical.Scalar TwoXX)
            ||!TryDoubleProduct(Y,Y,out Logical.Scalar TwoYY)
            ||!TryDoubleProduct(Z,Z,out Logical.Scalar TwoZZ)
            ||!TryDoubleProduct(X,Y,out Logical.Scalar TwoXY)
            ||!TryDoubleProduct(X,Z,out Logical.Scalar TwoXZ)
            ||!TryDoubleProduct(Y,Z,out Logical.Scalar TwoYZ)
            ||!TryDoubleProduct(X,W,out Logical.Scalar TwoXW)
            ||!TryDoubleProduct(Y,W,out Logical.Scalar TwoYW)
            ||!TryDoubleProduct(Z,W,out Logical.Scalar TwoZW)
            ||!Logical.Scalar.TrySubtract(Logical.Scalar.One,TwoYY,out Logical.Scalar M11)
            ||!Logical.Scalar.TrySubtract(M11,TwoZZ,out M11)
            ||!Logical.Scalar.TryAdd(TwoXY,TwoZW,out Logical.Scalar M12)
            ||!Logical.Scalar.TrySubtract(TwoXZ,TwoYW,out Logical.Scalar M13)
            ||!Logical.Scalar.TrySubtract(TwoXY,TwoZW,out Logical.Scalar M21)
            ||!Logical.Scalar.TrySubtract(Logical.Scalar.One,TwoXX,out Logical.Scalar M22)
            ||!Logical.Scalar.TrySubtract(M22,TwoZZ,out M22)
            ||!Logical.Scalar.TryAdd(TwoYZ,TwoXW,out Logical.Scalar M23)
            ||!Logical.Scalar.TryAdd(TwoXZ,TwoYW,out Logical.Scalar M31)
            ||!Logical.Scalar.TrySubtract(TwoYZ,TwoXW,out Logical.Scalar M32)
            ||!Logical.Scalar.TrySubtract(Logical.Scalar.One,TwoXX,out Logical.Scalar M33)
            ||!Logical.Scalar.TrySubtract(M33,TwoYY,out M33)
            ||!Logical.Scalar.TryMultiply(Transform.ScaleX,M11,out M11)
            ||!Logical.Scalar.TryMultiply(Transform.ScaleX,M12,out M12)
            ||!Logical.Scalar.TryMultiply(Transform.ScaleX,M13,out M13)
            ||!Logical.Scalar.TryMultiply(Transform.ScaleY,M21,out M21)
            ||!Logical.Scalar.TryMultiply(Transform.ScaleY,M22,out M22)
            ||!Logical.Scalar.TryMultiply(Transform.ScaleY,M23,out M23)
            ||!Logical.Scalar.TryMultiply(Transform.ScaleZ,M31,out M31)
            ||!Logical.Scalar.TryMultiply(Transform.ScaleZ,M32,out M32)
            ||!Logical.Scalar.TryMultiply(Transform.ScaleZ,M33,out M33)){
            Matrix=default;
            return FixedTransformStatus.NumericOverflow;
        }

        Matrix=new FixedAffineTransform(
            M11,M12,M13,M21,M22,M23,M31,M32,M33,
            Transform.PositionX,Transform.PositionY,Transform.PositionZ);
        return FixedTransformStatus.Success;
    }

    public static FixedTransformStatus TryMultiply(
        in FixedAffineTransform Left,in FixedAffineTransform Right,out FixedAffineTransform Result){
        if(!TryDot3(Left.M11,Left.M12,Left.M13,Right.M11,Right.M21,Right.M31,out Logical.Scalar M11)
            ||!TryDot3(Left.M11,Left.M12,Left.M13,Right.M12,Right.M22,Right.M32,out Logical.Scalar M12)
            ||!TryDot3(Left.M11,Left.M12,Left.M13,Right.M13,Right.M23,Right.M33,out Logical.Scalar M13)
            ||!TryDot3(Left.M21,Left.M22,Left.M23,Right.M11,Right.M21,Right.M31,out Logical.Scalar M21)
            ||!TryDot3(Left.M21,Left.M22,Left.M23,Right.M12,Right.M22,Right.M32,out Logical.Scalar M22)
            ||!TryDot3(Left.M21,Left.M22,Left.M23,Right.M13,Right.M23,Right.M33,out Logical.Scalar M23)
            ||!TryDot3(Left.M31,Left.M32,Left.M33,Right.M11,Right.M21,Right.M31,out Logical.Scalar M31)
            ||!TryDot3(Left.M31,Left.M32,Left.M33,Right.M12,Right.M22,Right.M32,out Logical.Scalar M32)
            ||!TryDot3(Left.M31,Left.M32,Left.M33,Right.M13,Right.M23,Right.M33,out Logical.Scalar M33)
            ||!TryDot3(Left.M41,Left.M42,Left.M43,Right.M11,Right.M21,Right.M31,out Logical.Scalar TranslationX)
            ||!Logical.Scalar.TryAdd(TranslationX,Right.M41,out TranslationX)
            ||!TryDot3(Left.M41,Left.M42,Left.M43,Right.M12,Right.M22,Right.M32,out Logical.Scalar TranslationY)
            ||!Logical.Scalar.TryAdd(TranslationY,Right.M42,out TranslationY)
            ||!TryDot3(Left.M41,Left.M42,Left.M43,Right.M13,Right.M23,Right.M33,out Logical.Scalar TranslationZ)
            ||!Logical.Scalar.TryAdd(TranslationZ,Right.M43,out TranslationZ)){
            Result=default;
            return FixedTransformStatus.NumericOverflow;
        }

        Result=new FixedAffineTransform(
            M11,M12,M13,M21,M22,M23,M31,M32,M33,
            TranslationX,TranslationY,TranslationZ);
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
        if(!Logical.Scalar.TrySubtract(this.M41,CameraWorld.M41,out Logical.Scalar X)
            ||!Logical.Scalar.TrySubtract(this.M42,CameraWorld.M42,out Logical.Scalar Y)
            ||!Logical.Scalar.TrySubtract(this.M43,CameraWorld.M43,out Logical.Scalar Z)){
            Matrix=default;
            return FixedTransformStatus.NumericOverflow;
        }

        if(!this.M11.TryToSingle(out float M11)||!this.M12.TryToSingle(out float M12)||!this.M13.TryToSingle(out float M13)
            ||!this.M21.TryToSingle(out float M21)||!this.M22.TryToSingle(out float M22)||!this.M23.TryToSingle(out float M23)
            ||!this.M31.TryToSingle(out float M31)||!this.M32.TryToSingle(out float M32)||!this.M33.TryToSingle(out float M33)
            ||!X.TryToSingle(out float M41)||!Y.TryToSingle(out float M42)||!Z.TryToSingle(out float M43)){
            Matrix=default;
            return FixedTransformStatus.FloatConversionOverflow;
        }

        Matrix=new Matrix4x4(
            M11,M12,M13,0f,
            M21,M22,M23,0f,
            M31,M32,M33,0f,
            M41,M42,M43,1f);
        return FixedTransformStatus.Success;
    }

    /// <summary>
    /// Extracts the resolved world directions of a Transform's local +Z (forward) and local +Y
    /// (up) axes. These row-vector basis rows stay fixed-point until the final camera view
    /// boundary; callers normalize them after conversion to remove any inherited scale.
    /// </summary>
    public FixedTransformStatus TryGetCameraBasis(out Vector3 Forward,out Vector3 Up){
        if(!this.M31.TryToSingle(out float ForwardX)
            ||!this.M32.TryToSingle(out float ForwardY)
            ||!this.M33.TryToSingle(out float ForwardZ)
            ||!this.M21.TryToSingle(out float UpX)
            ||!this.M22.TryToSingle(out float UpY)
            ||!this.M23.TryToSingle(out float UpZ)){
            Forward=default;
            Up=default;
            return FixedTransformStatus.FloatConversionOverflow;
        }

        Forward=new Vector3(ForwardX,ForwardY,ForwardZ);
        Up=new Vector3(UpX,UpY,UpZ);
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

    private static FixedTransformStatus TryGetNormalizedQuaternion(
        in Transform Transform,
        out Logical.Scalar X,out Logical.Scalar Y,out Logical.Scalar Z,out Logical.Scalar W){
        X=Transform.RotationX;
        Y=Transform.RotationY;
        Z=Transform.RotationZ;
        W=Transform.RotationW;
        if(!Logical.Scalar.TryMultiply(X,X,out Logical.Scalar XX)
            ||!Logical.Scalar.TryMultiply(Y,Y,out Logical.Scalar YY)
            ||!Logical.Scalar.TryMultiply(Z,Z,out Logical.Scalar ZZ)
            ||!Logical.Scalar.TryMultiply(W,W,out Logical.Scalar WW)
            ||!Logical.Scalar.TryAdd(XX,YY,out Logical.Scalar LengthSquared)
            ||!Logical.Scalar.TryAdd(LengthSquared,ZZ,out LengthSquared)
            ||!Logical.Scalar.TryAdd(LengthSquared,WW,out LengthSquared)){
            return FixedTransformStatus.NumericOverflow;
        }
        if(LengthSquared.IsZero||!Logical.Scalar.TrySquareRoot(LengthSquared,out Logical.Scalar Length)
            ||Length.IsZero){
            return FixedTransformStatus.NonInvertible;
        }
        if(!Logical.Scalar.TryDivide(X,Length,out X)
            ||!Logical.Scalar.TryDivide(Y,Length,out Y)
            ||!Logical.Scalar.TryDivide(Z,Length,out Z)
            ||!Logical.Scalar.TryDivide(W,Length,out W)){
            return FixedTransformStatus.NumericOverflow;
        }
        return FixedTransformStatus.Success;
    }

    private static bool TryDoubleProduct(
        Logical.Scalar Left,Logical.Scalar Right,out Logical.Scalar Result){
        Result=default;
        return Logical.Scalar.TryMultiply(Left,Right,out Logical.Scalar Product)
            &&Logical.Scalar.TryAdd(Product,Product,out Result);
    }

    private static bool TryDot3(
        Logical.Scalar LeftX,Logical.Scalar LeftY,Logical.Scalar LeftZ,
        Logical.Scalar RightX,Logical.Scalar RightY,Logical.Scalar RightZ,
        out Logical.Scalar Result){
        Result=default;
        if(!Logical.Scalar.TryMultiply(LeftX,RightX,out Logical.Scalar ProductX)
            ||!Logical.Scalar.TryMultiply(LeftY,RightY,out Logical.Scalar ProductY)
            ||!Logical.Scalar.TryMultiply(LeftZ,RightZ,out Logical.Scalar ProductZ)
            ||!Logical.Scalar.TryAdd(ProductX,ProductY,out Logical.Scalar Sum)){
            return false;
        }
        return Logical.Scalar.TryAdd(Sum,ProductZ,out Result);
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

/// <summary>Parent hierarchy resolution status.</summary>
public enum TransformResolveStatus {
    Success,
    MissingTransform,
    DeadParent,
    Cycle,
}

public delegate bool TryGetLocalTransform(int EntityId,out Transform Transform);
public delegate bool TryGetParentEntity(int EntityId,out int ParentEntityId);

/// <summary>
/// Caches row-vector parent composition for a frame. The caller retains the last valid rendered
/// matrix if this reports a non-success status.
/// </summary>
public sealed class TransformWorldResolver {
    private readonly Dictionary<int,Matrix4x4>WorldByEntity=[];
    private readonly Dictionary<int,TransformResolveStatus>StatusByEntity=[];
    private readonly HashSet<int>Resolving=[];

    public void BeginFrame(){
        this.WorldByEntity.Clear();
        this.StatusByEntity.Clear();
        this.Resolving.Clear();
    }

    public TransformResolveStatus Resolve(
        int EntityId,
        TryGetLocalTransform GetTransform,
        TryGetParentEntity GetParent,
        out Matrix4x4 World){
        if(this.WorldByEntity.TryGetValue(EntityId,out World)){
            return TransformResolveStatus.Success;
        }
        if(this.StatusByEntity.TryGetValue(EntityId,out TransformResolveStatus PreviousStatus)){
            World=default;
            return PreviousStatus;
        }
        if(!this.Resolving.Add(EntityId)){
            World=default;
            return this.StatusByEntity[EntityId]=TransformResolveStatus.Cycle;
        }

        try {
            if(!GetTransform(EntityId,out Transform Local)){
                World=default;
                return this.StatusByEntity[EntityId]=TransformResolveStatus.MissingTransform;
            }

            Matrix4x4 LocalWorld=TransformMath.ToMatrix(Local);
            if(!GetParent(EntityId,out int ParentEntityId)){
                this.WorldByEntity.Add(EntityId,World=LocalWorld);
                return TransformResolveStatus.Success;
            }

            TransformResolveStatus ParentStatus=this.Resolve(
                ParentEntityId,GetTransform,GetParent,out Matrix4x4 ParentWorld);
            if(ParentStatus!=TransformResolveStatus.Success){
                World=default;
                return this.StatusByEntity[EntityId]=ParentStatus==TransformResolveStatus.MissingTransform
                    ?TransformResolveStatus.DeadParent
                    :ParentStatus;
            }

            this.WorldByEntity.Add(EntityId,World=TransformMath.ToWorld(Local,ParentWorld));
            return TransformResolveStatus.Success;
        } finally {
            this.Resolving.Remove(EntityId);
        }
    }
}
