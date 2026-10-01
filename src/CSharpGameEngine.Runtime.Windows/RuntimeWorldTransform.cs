using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Entities;
using CSharpGameEngine.World;
using EcsWorld=CSharpGameEngine.Entities.World;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>
/// The camera-relative half of the fixed-point world transform, which needs the host's camera and frames. The world
/// resolution itself is portable and lives with the resolver (Entities' FixedTransform.cs, M0096 G).
/// </summary>
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
    /// <summary>
    /// Resolves an entity's complete fixed world transform, subtracts the complete fixed camera
    /// world origin, then converts the relative affine matrix to float exactly once.
    /// </summary>
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
