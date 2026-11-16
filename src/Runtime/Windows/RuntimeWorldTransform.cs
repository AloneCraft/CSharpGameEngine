using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Entities;
using CSharpGameEngine.World;
using EcsWorld=CSharpGameEngine.Entities.World;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>固定小数点ワールド変換のうち、カメラと座標枠を必要とするカメラ相対部分。ワールド変換自体の解決処理は移植可能であり、リゾルバーとともに置く（Entities の FixedTransform.cs、M0096 G）。</summary>
internal static partial class RuntimeWorldTransform {
    public static FixedTransformStatus TryToCameraRelativeMatrix(
        in FixedAffineTransform ObjectWorld,ReferenceFrameId ObjectFrame,
        in FixedAffineTransform CameraWorld,ReferenceFrameId CameraFrame,
        ReferenceFrameSnapshot Frames,out Matrix4x4 Matrix){
        ArgumentNullException.ThrowIfNull(Frames);
        if(!Frames.TryTransformAffine(ObjectWorld.Logical,ObjectFrame,CameraFrame,out var ObjectInCameraFrame)){
            Matrix=default;
            return FixedTransformStatus.NumericOverflow;
        }
        return FixedAffineTransform.FromLogical(ObjectInCameraFrame).TryToCameraRelativeMatrix(CameraWorld,out Matrix);
    }
    /// <summary>Entity の固定小数点による完全なワールド変換を解決し、完全な固定小数点カメラワールド原点を差し引いた後、相対アフィン行列を一度だけ float に変換する。</summary>
    public static FixedTransformStatus TryGetCameraRelativeMatrix(
        Entity Entity,EcsWorld EcsWorld,EngineHost Host,
        out Matrix4x4 Matrix,
        out FixedTransformResolveStatus ResolveStatus){
        ArgumentNullException.ThrowIfNull(Host);
        if(!Host.TryGetCameraPose(out FixedAffineTransform CameraWorld,out ReferenceFrameId CameraFrame)){
            Matrix=default;
            ResolveStatus=FixedTransformResolveStatus.MissingTransform;
            return FixedTransformStatus.FloatConversionOverflow;
        }

        ResolveStatus=TryResolveWorld(Entity,EcsWorld,out FixedAffineTransform World,out ReferenceFrameId ObjectFrame);
        if(ResolveStatus!=FixedTransformResolveStatus.Success){
            Matrix=default;
            return ToTransformStatus(ResolveStatus);
        }
        if(!Host.FrameSnapshot.Contains(ObjectFrame)||!Host.FrameSnapshot.Contains(CameraFrame)){
            Matrix=default;
            ResolveStatus=FixedTransformResolveStatus.FrameMismatch;
            return FixedTransformStatus.NonInvertible;
        }
        return TryToCameraRelativeMatrix(
            World,ObjectFrame,CameraWorld,CameraFrame,Host.FrameSnapshot,out Matrix);
    }

    public static FixedTransformStatus TryGetCameraRelativePose(
        Entity Entity,EcsWorld EcsWorld,EngineHost Host,
        out Vector3 Position,
        out Vector3 Forward,
        out FixedTransformResolveStatus ResolveStatus){
        FixedTransformStatus Status=TryGetCameraRelativeMatrix(
            Entity,EcsWorld,Host,out Matrix4x4 Matrix,out ResolveStatus);
        if(Status!=FixedTransformStatus.Success){
            Position=default;
            Forward=default;
            return Status;
        }

        Position=Matrix.Translation;
        Vector3 WorldForward=Vector3.TransformNormal(Vector3.UnitZ,Matrix);
        if(!float.IsFinite(WorldForward.X)||!float.IsFinite(WorldForward.Y)
            ||!float.IsFinite(WorldForward.Z)||WorldForward.LengthSquared()<=1e-12f){
            Position=default;
            Forward=default;
            return FixedTransformStatus.NonInvertible;
        }

        Forward=Vector3.Normalize(WorldForward);
        return FixedTransformStatus.Success;
    }

    public static FixedTransformStatus ToTransformStatus(FixedTransformResolveStatus Status)=>Status switch {
        FixedTransformResolveStatus.Success=>FixedTransformStatus.Success,
        FixedTransformResolveStatus.NumericOverflow=>FixedTransformStatus.NumericOverflow,
        FixedTransformResolveStatus.NonInvertible=>FixedTransformStatus.NonInvertible,
        FixedTransformResolveStatus.FrameMismatch=>FixedTransformStatus.NonInvertible,
        _=>FixedTransformStatus.NonInvertible,
    };
}
