using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Windows;
using CSharpGameEngine.World;
using RenderCamera=CSharpGameEngine.Graphics.D3D12.Camera;

namespace CSharpGameEngine.Runtime.Windows.Tests;

public sealed class TransformTests {
    [Fact]
    public void NewTransform_IsIdentity(){
        Transform Transform=new();

        Assert.Equal(ReferenceFrameId.Root,Transform.Frame);
        Assert.Equal(LogicalVector3.Zero,Transform.Position);
        Assert.Equal(LogicalQuaternion.Identity,Transform.Rotation);
        Assert.Equal(LogicalVector3.One,Transform.Scale);
    }

    [Fact]
    public void TryToCameraRelativeMatrix_SubtractsFixedOriginBeforeFloatConversion(){
        LogicalScalar LargeOrigin=LogicalScalar.ParseInvariant("8000000000000000000");
        Assert.True(LogicalScalar.TryAdd(
            LargeOrigin,
            LogicalScalar.ParseInvariant("0.125"),
            out LogicalScalar NearbyPosition));

        Transform Camera=new() { Position=new LogicalVector3(LargeOrigin,0f,0f) };
        Transform Object=new() { Position=new LogicalVector3(NearbyPosition,0f,0f) };

        FixedTransformStatus Status=TransformMath.TryToCameraRelativeMatrix(
            Object,Camera,out Matrix4x4 Matrix);

        Assert.Equal(FixedTransformStatus.Success,Status);
        Assert.Equal(0.125f,Matrix.M41);
        Assert.Equal(0f,Matrix.M42);
        Assert.Equal(0f,Matrix.M43);
    }
    [Fact]
    public void FrameRelativeRendererBoundaryStopsAtLcaBeforeRemoteRootOffsetCanOverflow(){
        var Root=new ReferenceFrameId(StableId.FromName(StableId.Parse("5a60c273-cbba-553a-8889-98771b38fa9d"),"root"));
        var Planet=new ReferenceFrameId(StableId.FromName(Root.Value,"planet"));
        var ObjectFrame=new ReferenceFrameId(StableId.FromName(Root.Value,"object"));
        var CameraFrame=new ReferenceFrameId(StableId.FromName(Root.Value,"camera"));
        var Frames=new ReferenceFrameGraph(Root);
        Frames.Add(Planet,Root,new FramePose(new LogicalVector3(LogicalScalar.MaxValue-1,0,0),LogicalQuaternion.Identity,LogicalVector3.One));
        Frames.Add(ObjectFrame,Planet,new FramePose(new LogicalVector3(10,0,0),LogicalQuaternion.Identity,LogicalVector3.One));
        Frames.Add(CameraFrame,Planet,new FramePose(new LogicalVector3(12,0,0),LogicalQuaternion.Identity,LogicalVector3.One));
        Assert.True(Frames.TryCommit(1,out var Snapshot,out var FrameError),FrameError.Message);
        Transform Object=new() { Frame=ObjectFrame,Position=new LogicalVector3(5,0,0) };
        Transform Camera=new() { Frame=CameraFrame,Position=new LogicalVector3(1,0,0) };
        Assert.Equal(FixedTransformStatus.Success,FixedAffineTransform.TryFromTransform(Object,out var ObjectWorld));
        Assert.Equal(FixedTransformStatus.Success,FixedAffineTransform.TryFromTransform(Camera,out var CameraWorld));
        var Status=RuntimeWorldTransform.TryToCameraRelativeMatrix(ObjectWorld,Object.Frame,CameraWorld,Camera.Frame,Snapshot,out var Relative);
        Assert.Equal(FixedTransformStatus.Success,Status);
        Assert.Equal(2f,Relative.M41);
        Assert.Equal(0f,Relative.M42);
        Assert.Equal(0f,Relative.M43);
        Assert.False(Snapshot.TryTransformAffine(ObjectWorld.Logical,Object.Frame,Root,out _));
    }

    [Fact]
    public void RuntimeRenderExtraction_UsesHostOwnedFrameSnapshotAndCameraPose(){
        ReferenceFrameId Root=ReferenceFrameId.Root;
        var Planet=new ReferenceFrameId(StableId.FromName(Root.Value,"runtime-planet"));
        var ObjectFrame=new ReferenceFrameId(StableId.FromName(Root.Value,"runtime-object"));
        var CameraFrame=new ReferenceFrameId(StableId.FromName(Root.Value,"runtime-camera"));
        using EngineHost Host=new();
        Host.ReferenceFrames.Add(Planet,Root,new FramePose(
            new LogicalVector3(LogicalScalar.MaxValue-1,0,0),LogicalQuaternion.Identity,LogicalVector3.One));
        Host.ReferenceFrames.Add(ObjectFrame,Planet,new FramePose(
            new LogicalVector3(10,0,0),LogicalQuaternion.Identity,LogicalVector3.One));
        Host.ReferenceFrames.Add(CameraFrame,Planet,new FramePose(
            new LogicalVector3(12,0,0),LogicalQuaternion.Identity,LogicalVector3.One));
        Assert.True(Host.ReferenceFrames.TryCommit(1,out _,out var Error),Error.Message);

        CSharpGameEngine.Entities.World EcsWorld=new();
        Entity ObjectEntity=EcsWorld.Create(
            new Transform { Frame=ObjectFrame,Position=new LogicalVector3(5,0,0) },
            new PrimitiveMesh());
        Transform CameraTransform=new() { Frame=CameraFrame,Position=new LogicalVector3(1,0,0) };
        Assert.Equal(FixedTransformStatus.Success,
            FixedAffineTransform.TryFromTransform(CameraTransform,out var CameraWorld));
        Host.SetCameraPose(CameraWorld,CameraFrame);

        FixedTransformStatus Status=RuntimeWorldTransform.TryGetCameraRelativeMatrix(
            ObjectEntity,EcsWorld,Host,out Matrix4x4 Relative,out var ResolveStatus);

        Assert.Equal(FixedTransformResolveStatus.Success,ResolveStatus);
        Assert.Equal(FixedTransformStatus.Success,Status);
        Assert.Equal(2f,Relative.M41);
        Assert.Equal(0f,Relative.M42);
        Assert.Equal(0f,Relative.M43);
    }

    [Fact]
    public void RuntimeAudioExtraction_ProducesOnlyListenerRelativeFloatSnapshot(){
        ReferenceFrameId Root=ReferenceFrameId.Root;
        var Planet=new ReferenceFrameId(StableId.FromName(Root.Value,"audio-planet"));
        var SourceFrame=new ReferenceFrameId(StableId.FromName(Root.Value,"audio-source"));
        var ListenerFrame=new ReferenceFrameId(StableId.FromName(Root.Value,"audio-listener"));
        using EngineHost Host=new();
        Host.ReferenceFrames.Add(Planet,Root,new FramePose(
            new LogicalVector3(LogicalScalar.MaxValue-1,0,0),LogicalQuaternion.Identity,LogicalVector3.One));
        Host.ReferenceFrames.Add(SourceFrame,Planet,new FramePose(
            new LogicalVector3(10,0,0),LogicalQuaternion.Identity,LogicalVector3.One));
        Host.ReferenceFrames.Add(ListenerFrame,Planet,new FramePose(
            new LogicalVector3(12,0,0),LogicalQuaternion.Identity,LogicalVector3.One));
        Assert.True(Host.ReferenceFrames.TryCommit(1,out _,out var Error),Error.Message);
        Transform Source=new() { Frame=SourceFrame,Position=new LogicalVector3(5,0,0) };
        Transform Listener=new() { Frame=ListenerFrame,Position=new LogicalVector3(1,0,0) };
        Assert.Equal(FixedTransformStatus.Success,FixedAffineTransform.TryFromTransform(Source,out var SourcePose));
        Assert.Equal(FixedTransformStatus.Success,FixedAffineTransform.TryFromTransform(Listener,out var ListenerPose));
        Host.BeginAudioFrame();
        Host.SetAudioListener(new Entity(1),ListenerPose,ListenerFrame);

        Assert.True(Host.TrySubmitAudioSource(
            new Entity(2),SourcePose,SourceFrame,0.5f,1f,10f,true));

        AudioSourceSnapshot Snapshot=Assert.Single(Host.AudioSources);
        Assert.Equal(new Vector3(2f,0f,0f),Snapshot.ListenerRelativePosition);
        Assert.Equal(0.5f,Snapshot.Volume);
        Assert.Equal(1f,Snapshot.SpatialMix.Pan);
        Assert.Equal(8f/9f,Snapshot.SpatialMix.Attenuation,5);
        Assert.Equal(4f/9f,Snapshot.EffectiveGain,5);
        Assert.True(Snapshot.Loop);
    }
    [Fact]
    public void FixedTransformWorldResolver_ComposesHierarchyBeforeCameraRelativeFloatConversion(){
        LogicalScalar LargeOrigin=LogicalScalar.ParseInvariant("8000000000000000000");
        Transform Parent=new() { Position=new LogicalVector3(LargeOrigin,0f,0f) };
        Transform Child=new() {
            Position=new LogicalVector3(LogicalScalar.ParseInvariant("0.125"),2f,0f),
        };
        FixedTransformWorldResolver Resolver=new();

        FixedTransformResolveStatus ResolveStatus=Resolver.Resolve(
            2,TryGetTransform,TryGetParent,out FixedAffineTransform World);
        FixedTransformStatus MatrixStatus=World.TryToCameraRelativeMatrix(
            Parent,out Matrix4x4 Matrix);

        Assert.Equal(FixedTransformResolveStatus.Success,ResolveStatus);
        Assert.Equal(FixedTransformStatus.Success,MatrixStatus);
        Assert.Equal(0.125f,Matrix.M41);
        Assert.Equal(2f,Matrix.M42);

        bool TryGetTransform(int EntityId,out Transform Result){
            switch(EntityId){
                case 1:
                    Result=Parent;
                    return true;
                case 2:
                    Result=Child;
                    return true;
                default:
                    Result=default;
                    return false;
            }
        }

        static bool TryGetParent(int EntityId,out int ParentEntityId){
            ParentEntityId=EntityId==2?1:default;
            return EntityId==2;
        }
    }

    [Fact]
    public void FixedTransformWorldResolver_UsesParentedCameraWorldBeforeFloatConversion(){
        LogicalScalar LargeOrigin=LogicalScalar.ParseInvariant("8000000000000000000");
        Transform Root=new() { Position=new LogicalVector3(LargeOrigin,0f,0f) };
        Transform Camera=new() { Position=new LogicalVector3(2f,3f,0f) };
        Transform Object=new() { Position=new LogicalVector3(2.125f,4f,0f) };
        FixedTransformWorldResolver Resolver=new();

        Assert.Equal(
            FixedTransformResolveStatus.Success,
            Resolver.Resolve(2,TryGetTransform,TryGetParent,out FixedAffineTransform CameraWorld));
        Assert.Equal(
            FixedTransformResolveStatus.Success,
            Resolver.Resolve(3,TryGetTransform,TryGetParent,out FixedAffineTransform ObjectWorld));

        FixedTransformStatus Status=ObjectWorld.TryToCameraRelativeMatrix(
            CameraWorld,out Matrix4x4 CameraRelative);

        Assert.Equal(FixedTransformStatus.Success,Status);
        Assert.Equal(0.125f,CameraRelative.M41);
        Assert.Equal(1f,CameraRelative.M42);
        Assert.Equal(0f,CameraRelative.M43);

        bool TryGetTransform(int EntityId,out Transform Result){
            switch(EntityId){
                case 1:
                    Result=Root;
                    return true;
                case 2:
                    Result=Camera;
                    return true;
                case 3:
                    Result=Object;
                    return true;
                default:
                    Result=default;
                    return false;
            }
        }

        static bool TryGetParent(int EntityId,out int ParentEntityId){
            ParentEntityId=EntityId is 2 or 3?1:default;
            return EntityId is 2 or 3;
        }
    }

    [Fact]
    public void FixedTransformWorldResolver_UsesParentedCameraLocalPositiveZForViewAtLargeCoordinates(){
        LogicalScalar LargeOrigin=LogicalScalar.ParseInvariant("8000000000000000000");
        Transform Root=new() { Position=new LogicalVector3(LargeOrigin,0f,0f) };
        Transform Parent=new() {
            Rotation=LogicalQuaternion.FromEulerRuf(new LogicalVector3(0f,MathF.PI/2f,0f)),
        };
        Transform Camera=new();
        FixedTransformWorldResolver Resolver=new();

        Assert.Equal(
            FixedTransformResolveStatus.Success,
            Resolver.Resolve(3,TryGetTransform,TryGetParent,out FixedAffineTransform CameraWorld));
        Assert.Equal(
            FixedTransformStatus.Success,
            CameraWorld.TryGetCameraBasis(out Vector3 Forward,out Vector3 Up));
        Assert.Equal(
            FixedTransformStatus.Success,
            CameraWorld.TryToCameraRelativeMatrix(CameraWorld,out Matrix4x4 CameraRelative));

        RenderCamera RenderCamera=RenderCamera.CreateFirstPerson(Vector3.Zero);
        Assert.True(RenderCamera.TrySetWorldOrientation(Forward,Up));

        AssertApprox(1f,RenderCamera.GetForward().X);
        AssertApprox(0f,RenderCamera.GetForward().Y);
        AssertApprox(0f,RenderCamera.GetForward().Z);
        AssertApprox(0f,RenderCamera.GetUp().X);
        AssertApprox(1f,RenderCamera.GetUp().Y);
        AssertApprox(0f,RenderCamera.GetUp().Z);
        Assert.Equal(0f,CameraRelative.M41);
        Assert.Equal(0f,CameraRelative.M42);
        Assert.Equal(0f,CameraRelative.M43);

        bool TryGetTransform(int EntityId,out Transform Result){
            switch(EntityId){
                case 1:
                    Result=Root;
                    return true;
                case 2:
                    Result=Parent;
                    return true;
                case 3:
                    Result=Camera;
                    return true;
                default:
                    Result=default;
                    return false;
            }
        }

        static bool TryGetParent(int EntityId,out int ParentEntityId){
            ParentEntityId=EntityId switch { 2=>1,3=>2,_=>default };
            return EntityId is 2 or 3;
        }
    }

    private static void AssertApprox(float Expected,float Actual,float Tolerance=1e-4f){
        Assert.InRange(Actual,Expected-Tolerance,Expected+Tolerance);
    }
}
