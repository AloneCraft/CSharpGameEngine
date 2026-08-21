using System;
using System.Collections.Generic;
using System.Numerics;
using CSharpGameEngine.Entities;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>
/// Scene-user-compilation Transform. Its absolute position and scale use the precision selected
/// by <c>[assembly: TransformPrecision(...)]</c>; quaternion components use a normalized-range
/// fixed scalar. Coordinates use Unity-compatible RUF (+X right, +Y up, +Z forward).
/// </summary>
/// <remarks>
/// This source is linked into a Scene compilation (rather than the Windows renderer DLL), because
/// the limb width is selected by that compilation. The renderer receives only camera-relative
/// matrices converted to float at the final GPU boundary.
/// </remarks>
[SceneComponentOrder(SceneComponentCategory.Spatial,0)]
public struct Transform {
    public FixedScalar PositionX;
    public FixedScalar PositionY;
    public FixedScalar PositionZ;

    public FixedRotationScalar RotationX;
    public FixedRotationScalar RotationY;
    public FixedRotationScalar RotationZ;
    public FixedRotationScalar RotationW;

    public FixedScalar ScaleX;
    public FixedScalar ScaleY;
    public FixedScalar ScaleZ;

    public Transform(){
        this.RotationW=FixedRotationScalar.One;
        this.ScaleX=FixedScalar.One;
        this.ScaleY=FixedScalar.One;
        this.ScaleZ=FixedScalar.One;
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
    public FixedScalar M11 { get; }
    public FixedScalar M12 { get; }
    public FixedScalar M13 { get; }
    public FixedScalar M21 { get; }
    public FixedScalar M22 { get; }
    public FixedScalar M23 { get; }
    public FixedScalar M31 { get; }
    public FixedScalar M32 { get; }
    public FixedScalar M33 { get; }
    public FixedScalar M41 { get; }
    public FixedScalar M42 { get; }
    public FixedScalar M43 { get; }

    private FixedAffineTransform(
        FixedScalar M11,FixedScalar M12,FixedScalar M13,
        FixedScalar M21,FixedScalar M22,FixedScalar M23,
        FixedScalar M31,FixedScalar M32,FixedScalar M33,
        FixedScalar M41,FixedScalar M42,FixedScalar M43){
        this.M11=M11; this.M12=M12; this.M13=M13;
        this.M21=M21; this.M22=M22; this.M23=M23;
        this.M31=M31; this.M32=M32; this.M33=M33;
        this.M41=M41; this.M42=M42; this.M43=M43;
    }

    public static FixedAffineTransform Identity=>new(
        FixedScalar.One,FixedScalar.Zero,FixedScalar.Zero,
        FixedScalar.Zero,FixedScalar.One,FixedScalar.Zero,
        FixedScalar.Zero,FixedScalar.Zero,FixedScalar.One,
        FixedScalar.Zero,FixedScalar.Zero,FixedScalar.Zero);

    public static FixedTransformStatus TryFromTransform(
        in Transform Transform,out FixedAffineTransform Matrix){
        FixedTransformStatus Status=TryGetNormalizedQuaternion(
            Transform,out FixedScalar X,out FixedScalar Y,out FixedScalar Z,out FixedScalar W);
        if(Status!=FixedTransformStatus.Success){
            Matrix=default;
            return Status;
        }

        if(!TryDoubleProduct(X,X,out FixedScalar TwoXX)
            ||!TryDoubleProduct(Y,Y,out FixedScalar TwoYY)
            ||!TryDoubleProduct(Z,Z,out FixedScalar TwoZZ)
            ||!TryDoubleProduct(X,Y,out FixedScalar TwoXY)
            ||!TryDoubleProduct(X,Z,out FixedScalar TwoXZ)
            ||!TryDoubleProduct(Y,Z,out FixedScalar TwoYZ)
            ||!TryDoubleProduct(X,W,out FixedScalar TwoXW)
            ||!TryDoubleProduct(Y,W,out FixedScalar TwoYW)
            ||!TryDoubleProduct(Z,W,out FixedScalar TwoZW)
            ||!FixedScalar.TrySubtract(FixedScalar.One,TwoYY,out FixedScalar M11)
            ||!FixedScalar.TrySubtract(M11,TwoZZ,out M11)
            ||!FixedScalar.TryAdd(TwoXY,TwoZW,out FixedScalar M12)
            ||!FixedScalar.TrySubtract(TwoXZ,TwoYW,out FixedScalar M13)
            ||!FixedScalar.TrySubtract(TwoXY,TwoZW,out FixedScalar M21)
            ||!FixedScalar.TrySubtract(FixedScalar.One,TwoXX,out FixedScalar M22)
            ||!FixedScalar.TrySubtract(M22,TwoZZ,out M22)
            ||!FixedScalar.TryAdd(TwoYZ,TwoXW,out FixedScalar M23)
            ||!FixedScalar.TryAdd(TwoXZ,TwoYW,out FixedScalar M31)
            ||!FixedScalar.TrySubtract(TwoYZ,TwoXW,out FixedScalar M32)
            ||!FixedScalar.TrySubtract(FixedScalar.One,TwoXX,out FixedScalar M33)
            ||!FixedScalar.TrySubtract(M33,TwoYY,out M33)
            ||!FixedScalar.TryMultiply(Transform.ScaleX,M11,out M11)
            ||!FixedScalar.TryMultiply(Transform.ScaleX,M12,out M12)
            ||!FixedScalar.TryMultiply(Transform.ScaleX,M13,out M13)
            ||!FixedScalar.TryMultiply(Transform.ScaleY,M21,out M21)
            ||!FixedScalar.TryMultiply(Transform.ScaleY,M22,out M22)
            ||!FixedScalar.TryMultiply(Transform.ScaleY,M23,out M23)
            ||!FixedScalar.TryMultiply(Transform.ScaleZ,M31,out M31)
            ||!FixedScalar.TryMultiply(Transform.ScaleZ,M32,out M32)
            ||!FixedScalar.TryMultiply(Transform.ScaleZ,M33,out M33)){
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
        if(!TryDot3(Left.M11,Left.M12,Left.M13,Right.M11,Right.M21,Right.M31,out FixedScalar M11)
            ||!TryDot3(Left.M11,Left.M12,Left.M13,Right.M12,Right.M22,Right.M32,out FixedScalar M12)
            ||!TryDot3(Left.M11,Left.M12,Left.M13,Right.M13,Right.M23,Right.M33,out FixedScalar M13)
            ||!TryDot3(Left.M21,Left.M22,Left.M23,Right.M11,Right.M21,Right.M31,out FixedScalar M21)
            ||!TryDot3(Left.M21,Left.M22,Left.M23,Right.M12,Right.M22,Right.M32,out FixedScalar M22)
            ||!TryDot3(Left.M21,Left.M22,Left.M23,Right.M13,Right.M23,Right.M33,out FixedScalar M23)
            ||!TryDot3(Left.M31,Left.M32,Left.M33,Right.M11,Right.M21,Right.M31,out FixedScalar M31)
            ||!TryDot3(Left.M31,Left.M32,Left.M33,Right.M12,Right.M22,Right.M32,out FixedScalar M32)
            ||!TryDot3(Left.M31,Left.M32,Left.M33,Right.M13,Right.M23,Right.M33,out FixedScalar M33)
            ||!TryDot3(Left.M41,Left.M42,Left.M43,Right.M11,Right.M21,Right.M31,out FixedScalar TranslationX)
            ||!FixedScalar.TryAdd(TranslationX,Right.M41,out TranslationX)
            ||!TryDot3(Left.M41,Left.M42,Left.M43,Right.M12,Right.M22,Right.M32,out FixedScalar TranslationY)
            ||!FixedScalar.TryAdd(TranslationY,Right.M42,out TranslationY)
            ||!TryDot3(Left.M41,Left.M42,Left.M43,Right.M13,Right.M23,Right.M33,out FixedScalar TranslationZ)
            ||!FixedScalar.TryAdd(TranslationZ,Right.M43,out TranslationZ)){
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
        if(!FixedScalar.TrySubtract(this.M41,CameraWorld.M41,out FixedScalar X)
            ||!FixedScalar.TrySubtract(this.M42,CameraWorld.M42,out FixedScalar Y)
            ||!FixedScalar.TrySubtract(this.M43,CameraWorld.M43,out FixedScalar Z)){
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
        out FixedScalar X,out FixedScalar Y,out FixedScalar Z,out FixedScalar W){
        X=Transform.RotationX;
        Y=Transform.RotationY;
        Z=Transform.RotationZ;
        W=Transform.RotationW;
        if(!FixedScalar.TryMultiply(X,X,out FixedScalar XX)
            ||!FixedScalar.TryMultiply(Y,Y,out FixedScalar YY)
            ||!FixedScalar.TryMultiply(Z,Z,out FixedScalar ZZ)
            ||!FixedScalar.TryMultiply(W,W,out FixedScalar WW)
            ||!FixedScalar.TryAdd(XX,YY,out FixedScalar LengthSquared)
            ||!FixedScalar.TryAdd(LengthSquared,ZZ,out LengthSquared)
            ||!FixedScalar.TryAdd(LengthSquared,WW,out LengthSquared)){
            return FixedTransformStatus.NumericOverflow;
        }
        if(LengthSquared.IsZero||!FixedScalar.TrySquareRoot(LengthSquared,out FixedScalar Length)
            ||Length.IsZero){
            return FixedTransformStatus.NonInvertible;
        }
        if(!FixedScalar.TryDivide(X,Length,out X)
            ||!FixedScalar.TryDivide(Y,Length,out Y)
            ||!FixedScalar.TryDivide(Z,Length,out Z)
            ||!FixedScalar.TryDivide(W,Length,out W)){
            return FixedTransformStatus.NumericOverflow;
        }
        return FixedTransformStatus.Success;
    }

    private static bool TryDoubleProduct(
        FixedScalar Left,FixedScalar Right,out FixedScalar Result){
        Result=default;
        return FixedScalar.TryMultiply(Left,Right,out FixedScalar Product)
            &&FixedScalar.TryAdd(Product,Product,out Result);
    }

    private static bool TryDot3(
        FixedScalar LeftX,FixedScalar LeftY,FixedScalar LeftZ,
        FixedScalar RightX,FixedScalar RightY,FixedScalar RightZ,
        out FixedScalar Result){
        Result=default;
        if(!FixedScalar.TryMultiply(LeftX,RightX,out FixedScalar ProductX)
            ||!FixedScalar.TryMultiply(LeftY,RightY,out FixedScalar ProductY)
            ||!FixedScalar.TryMultiply(LeftZ,RightZ,out FixedScalar ProductZ)
            ||!FixedScalar.TryAdd(ProductX,ProductY,out FixedScalar Sum)){
            return false;
        }
        return FixedScalar.TryAdd(Sum,ProductZ,out Result);
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
