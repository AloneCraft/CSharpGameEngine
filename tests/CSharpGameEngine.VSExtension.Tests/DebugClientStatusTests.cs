using System;
using CSharpGameEngine.VSExtension;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class DebugClientStatusTests {
    [Fact]
    public void DefaultStatus_IsNotConnected(){
        Assert.Equal(DebugStatusReason.None,default(DebugClientStatus).Reason);
    }
    [Fact]
    public void FromSuccessfulWireToken_PreservesTokenWithoutUsingItAsDisplayReason(){
        DebugClientStatus Result=DebugClientStatus.FromWireToken("ok",DebugStatusReason.SnapshotUnavailable);
        Assert.Equal(DebugStatusReason.Connected,Result.Reason);
        Assert.Equal("ok",Result.WireToken);
    }
    [Fact]
    public void FromUnsuccessfulWireToken_UsesTypedFallbackAndPreservesInvariantToken(){
        DebugClientStatus Result=DebugClientStatus.FromWireToken("not-ready",DebugStatusReason.SnapshotUnavailable);
        Assert.Equal(DebugStatusReason.SnapshotUnavailable,Result.Reason);
        Assert.Equal("not-ready",Result.WireToken);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FromMissingWireToken_UsesInvalidResponse(string? WireToken){
        DebugClientStatus Result=DebugClientStatus.FromWireToken(WireToken,DebugStatusReason.SnapshotUnavailable);
        Assert.Equal(DebugStatusReason.InvalidResponse,Result.Reason);
        Assert.Null(Result.WireToken);
    }
    [Fact]
    public void TransportFailure_DoesNotExposeWireToken(){
        DebugClientStatus Result=DebugClientStatus.TransportFailure(DebugStatusReason.PipeUnavailable);
        Assert.Equal(DebugStatusReason.PipeUnavailable,Result.Reason);
        Assert.Null(Result.WireToken);
    }
    [Fact]
    public void DebugClients_ExposeTypedStatusInsteadOfDisplayString(){
        Type ByRefStatus=typeof(DebugClientStatus).MakeByRefType();
        Assert.Contains(typeof(SpatialDebugClient).GetMethods(
            System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic),
            Method=>Method.Name=="TryGetSnapshot"&&Method.GetParameters()[3].ParameterType==ByRefStatus);
        Assert.Contains(typeof(PhysicsDebugClient).GetMethods(
            System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic),
            Method=>Method.Name=="TryGetSnapshot"&&Method.GetParameters()[3].ParameterType==ByRefStatus);
        Assert.Contains(typeof(PhysicsDebugClient).GetMethods(
            System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic),
            Method=>Method.Name=="TryControl"&&Method.GetParameters()[3].ParameterType==ByRefStatus);
    }
}
