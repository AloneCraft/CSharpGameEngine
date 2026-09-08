using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Entities;
using CSharpGameEngine.World;
using EcsWorld=CSharpGameEngine.Entities.World;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>
/// Bridges the generated ECS hierarchy to the scene-selected fixed-point Transform implementation.
/// Each top-level resolution starts with a fresh resolver so structural edits and reparenting made
/// in the previous ECS frame cannot reuse a stale parent chain. The resolver still caches every
/// ancestor during the one resolution, which keeps the affine composition entirely fixed-point.
/// </summary>
internal static class RuntimeWorldTransform {
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
    public static FixedTransformResolveStatus TryResolveWorld(
        Entity Entity,EcsWorld EcsWorld,out FixedAffineTransform World)=>
        TryResolveWorld(Entity,EcsWorld,out World,out _);

    public static FixedTransformResolveStatus TryResolveWorld(
        Entity Entity,EcsWorld EcsWorld,out FixedAffineTransform World,out ReferenceFrameId Frame){
        FixedTransformWorldResolver Resolver=new();
        FixedTransformResolveStatus Status=Resolver.Resolve(
            Entity.Id,
            (int EntityId,out Transform Transform)=>{
                Entity Candidate=new(EntityId);
                if(EcsWorld.TryGetComponent(Candidate,out Transform)){
                    return true;
                }
                // A scene root may be a logical node that owns environment/components but no
                // spatial Transform. Only the actual live scene root gets this identity fallback;
                // an arbitrary live entity without Transform remains a malformed parent.
                SceneInstance OwningScene=EcsWorld.GetOwningScene(Candidate);
                if(EcsWorld.IsAlive(Candidate)&&!OwningScene.IsNone
                    &&EcsWorld.GetSceneRoot(OwningScene).Equals(Candidate)){
                    Transform=new global::CSharpGameEngine.Entities.Transform();
                    return true;
                }
                Transform=default;
                return false;
            },
            (int EntityId,out int ParentEntityId)=>{
                if(EcsWorld.TryGetComponent(new Entity(EntityId),out Parent Parent)){
                    ParentEntityId=Parent.Value.Id;
                    return true;
                }
                ParentEntityId=default;
                return false;
            },out World);
        if(Status!=FixedTransformResolveStatus.Success){ Frame=default;return Status; }
        if(!TryResolveFrame(Entity,EcsWorld,out Frame)){
            World=default;
            return FixedTransformResolveStatus.FrameMismatch;
        }
        return FixedTransformResolveStatus.Success;
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

    private static bool TryResolveFrame(Entity Entity,EcsWorld EcsWorld,out ReferenceFrameId Frame){
        Frame=ReferenceFrameId.Root;
        bool HasFrame=false;
        Entity Current=Entity;
        while(true){
            if(EcsWorld.TryGetComponent(Current,out Transform Transform)){
                if(!HasFrame){ Frame=Transform.Frame;HasFrame=true; }
                else if(Frame!=Transform.Frame){ return false; }
            }
            if(!EcsWorld.TryGetComponent(Current,out Parent Parent)){ return Frame.IsValid; }
            Current=Parent.Value;
        }
    }

    public static FixedTransformStatus ToTransformStatus(FixedTransformResolveStatus Status)=>Status switch {
        FixedTransformResolveStatus.Success=>FixedTransformStatus.Success,
        FixedTransformResolveStatus.NumericOverflow=>FixedTransformStatus.NumericOverflow,
        FixedTransformResolveStatus.NonInvertible=>FixedTransformStatus.NonInvertible,
        FixedTransformResolveStatus.FrameMismatch=>FixedTransformStatus.NonInvertible,
        _=>FixedTransformStatus.NonInvertible,
    };
}
