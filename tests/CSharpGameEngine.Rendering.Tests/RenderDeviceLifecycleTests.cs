using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;

namespace CSharpGameEngine.Rendering.Tests;

public sealed class RenderDeviceLifecycleTests {
    [Fact]
    public void CreatedLifecycle_IsReadyWithGenerationOneAndCanonicalArtifactClosure(){
        AssetArtifactReference[]Artifacts=[Artifact("shader"),Artifact("mesh")];

        using RenderDeviceLifecycle Lifecycle=new(Artifacts);

        Assert.Equal(RenderDeviceState.Ready,Lifecycle.Snapshot.State);
        Assert.Equal(1u,Lifecycle.Snapshot.Generation);
        Assert.Equal(0ul,Lifecycle.Snapshot.CompletedFence);
        Assert.Equal(Artifacts,Lifecycle.RequiredArtifacts);
        Assert.Null(Lifecycle.Snapshot.LastFailure);
        Lifecycle.EnsureCanSubmit();
    }

    [Fact]
    public void SuspendAndBeginRecovery_RejectSubmissionUntilExactClosureIsRebuilt(){
        AssetArtifactReference[]Artifacts=[Artifact("shader"),Artifact("mesh")];
        using RenderDeviceLifecycle Lifecycle=new(Artifacts);

        Lifecycle.Suspend(17);

        Assert.Equal(RenderDeviceState.Suspended,Lifecycle.Snapshot.State);
        Assert.Equal(17ul,Lifecycle.Snapshot.CompletedFence);
        Assert.Throws<RenderDeviceUnavailableException>(Lifecycle.EnsureCanSubmit);

        Lifecycle.BeginRecovery(RenderRecoveryReason.Resume,17);

        Assert.Equal(RenderDeviceState.Recovering,Lifecycle.Snapshot.State);
        Assert.Throws<RenderDeviceUnavailableException>(Lifecycle.EnsureCanSubmit);
        Assert.Throws<ArgumentException>(()=>Lifecycle.CompleteRecovery([Artifacts[0]],18));
        Assert.Equal(RenderDeviceState.Recovering,Lifecycle.Snapshot.State);

        Lifecycle.CompleteRecovery([Artifacts[1],Artifacts[0]],18);

        Assert.Equal(RenderDeviceState.Ready,Lifecycle.Snapshot.State);
        Assert.Equal(2u,Lifecycle.Snapshot.Generation);
        Assert.Equal(18ul,Lifecycle.Snapshot.CompletedFence);
        Assert.Equal(RenderRecoveryReason.Resume,Lifecycle.Snapshot.LastRecoveryReason);
        Assert.Equal(Artifacts,Lifecycle.Snapshot.RebuiltArtifacts);
        Lifecycle.EnsureCanSubmit();
    }

    [Fact]
    public void DeviceLossFromReady_BeginsRecoveryWithoutDiscardingLastKnownGoodClosure(){
        AssetArtifactReference[]Artifacts=[Artifact("shader"),Artifact("material")];
        using RenderDeviceLifecycle Lifecycle=new(Artifacts);

        Lifecycle.BeginRecovery(RenderRecoveryReason.DeviceRemoved,23);

        Assert.Equal(RenderDeviceState.Recovering,Lifecycle.Snapshot.State);
        Assert.Equal(1u,Lifecycle.Snapshot.Generation);
        Assert.Equal(Artifacts,Lifecycle.RequiredArtifacts);
        Assert.Empty(Lifecycle.Snapshot.RebuiltArtifacts);

        RenderDeviceFailure Failure=new(
            RenderDeviceFailureKind.ArtifactRebuildFailed,
            NativeCode:unchecked((int)0x887A0005),
            "Rebuilding the material artifact failed.");
        Lifecycle.Fault(Failure);

        Assert.Equal(RenderDeviceState.Faulted,Lifecycle.Snapshot.State);
        Assert.Equal(Failure,Lifecycle.Snapshot.LastFailure);
        Assert.Equal(Artifacts,Lifecycle.RequiredArtifacts);
        RenderDeviceUnavailableException Exception=
            Assert.Throws<RenderDeviceUnavailableException>(Lifecycle.EnsureCanSubmit);
        Assert.Equal(RenderDeviceState.Faulted,Exception.State);
        Assert.Equal(Failure,Exception.Failure);
    }

    [Fact]
    public void CompleteRecovery_RejectsDuplicateUnknownOrInvalidArtifactIdentity(){
        AssetArtifactReference Shader=Artifact("shader");
        AssetArtifactReference Mesh=Artifact("mesh");
        using RenderDeviceLifecycle Lifecycle=new([Shader,Mesh]);
        Lifecycle.BeginRecovery(RenderRecoveryReason.DeviceReset,0);

        Assert.Throws<ArgumentException>(()=>Lifecycle.CompleteRecovery([Shader,Shader],0));
        Assert.Throws<ArgumentException>(()=>Lifecycle.CompleteRecovery([Shader,Artifact("other")],0));
        Assert.Throws<ArgumentException>(()=>Lifecycle.CompleteRecovery([Shader,default],0));
        Assert.Equal(RenderDeviceState.Recovering,Lifecycle.Snapshot.State);
    }

    [Fact]
    public void IllegalTransitions_AreRejectedWithoutChangingState(){
        using RenderDeviceLifecycle Lifecycle=new([Artifact("shader")]);

        Assert.Throws<InvalidOperationException>(
            ()=>Lifecycle.CompleteRecovery(Lifecycle.RequiredArtifacts,0));
        Assert.Throws<InvalidOperationException>(
            ()=>Lifecycle.BeginRecovery(RenderRecoveryReason.Resume,0));
        Assert.Equal(RenderDeviceState.Ready,Lifecycle.Snapshot.State);

        Lifecycle.Suspend(1);
        Assert.Throws<InvalidOperationException>(()=>Lifecycle.Suspend(1));
        Assert.Throws<InvalidOperationException>(
            ()=>Lifecycle.Fault(new RenderDeviceFailure(
                RenderDeviceFailureKind.NativeFailure,0,"failure")));
        Assert.Equal(RenderDeviceState.Suspended,Lifecycle.Snapshot.State);
    }

    [Fact]
    public void Disposal_IsIdempotentTerminalAndRejectsAllFurtherOperations(){
        RenderDeviceLifecycle Lifecycle=new([Artifact("shader")]);

        Lifecycle.Dispose();
        Lifecycle.Dispose();

        Assert.Equal(RenderDeviceState.Disposed,Lifecycle.Snapshot.State);
        Assert.Throws<ObjectDisposedException>(Lifecycle.EnsureCanSubmit);
        Assert.Throws<ObjectDisposedException>(()=>Lifecycle.Suspend(0));
        Assert.Throws<ObjectDisposedException>(
            ()=>Lifecycle.BeginRecovery(RenderRecoveryReason.DeviceRemoved,0));
        Assert.Throws<ObjectDisposedException>(
            ()=>Lifecycle.CompleteRecovery(Lifecycle.RequiredArtifacts,0));
        Assert.Throws<ObjectDisposedException>(
            ()=>Lifecycle.Fault(new RenderDeviceFailure(
                RenderDeviceFailureKind.NativeFailure,0,"failure")));
    }

    [Fact]
    public void Constructor_RejectsEmptyInvalidOrDuplicateArtifactClosure(){
        Assert.Throws<ArgumentException>(()=>new RenderDeviceLifecycle([]));
        Assert.Throws<ArgumentException>(()=>new RenderDeviceLifecycle([default]));
        AssetArtifactReference ArtifactReference=Artifact("same");
        Assert.Throws<ArgumentException>(
            ()=>new RenderDeviceLifecycle([ArtifactReference,ArtifactReference]));
    }

    [Fact]
    public void ReplaceRequiredArtifacts_CommitsOnlyACompleteValidatedReadyClosure(){
        AssetArtifactReference Initial=Artifact("initial");
        AssetArtifactReference Replacement=Artifact("replacement");
        using RenderDeviceLifecycle Lifecycle=new([Initial]);

        Lifecycle.ReplaceRequiredArtifacts([Replacement]);
        Assert.Equal([Replacement],Lifecycle.RequiredArtifacts);

        Assert.Throws<ArgumentException>(
            ()=>Lifecycle.ReplaceRequiredArtifacts([default]));
        Assert.Equal([Replacement],Lifecycle.RequiredArtifacts);

        Lifecycle.BeginRecovery(RenderRecoveryReason.DeviceRemoved,0);
        Assert.Throws<InvalidOperationException>(
            ()=>Lifecycle.ReplaceRequiredArtifacts([Initial]));
        Assert.Equal([Replacement],Lifecycle.RequiredArtifacts);
    }

    private static AssetArtifactReference Artifact(string Name){
        AssetId AssetId=new(StableId.FromName(
            StableId.Parse("b5f95db4-7b46-5dd5-8f1c-3bc34f2b4f66"),Name));
        Hash256 ContentHash=Hash256.Compute(System.Text.Encoding.UTF8.GetBytes(Name));
        return new AssetArtifactReference(new AssetKey(AssetId,null),ContentHash);
    }
}
