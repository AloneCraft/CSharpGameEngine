using System.Globalization;
namespace CSharpGameEngine.VSExtension.Tests;
[Collection(VsixLocalizationTestCollection.Name)]
public sealed class DebugStatusTextTests {
    [Theory]
    [InlineData("en-US","Physics: pipe unavailable")]
    [InlineData("ja-JP","Physics: Pipeを利用できません")]
    [InlineData("fr-FR","Physics: pipe unavailable")]
    public void PhysicsReason_UsesRequestedCultureWithEnglishFallback(string CultureName,string Expected){
        DebugClientStatus Status=DebugClientStatus.TransportFailure(DebugStatusReason.PipeUnavailable);
        Assert.Equal(Expected,DebugStatusText.Physics(Status,CultureInfo.GetCultureInfo(CultureName)));
    }
    [Theory]
    [InlineData("en-US","Global Spatial Octree: waiting for snapshot")]
    [InlineData("ja-JP","Global Spatial Octree: スナップショット待ち")]
    [InlineData("fr-FR","Global Spatial Octree: waiting for snapshot")]
    public void SpatialReason_UsesRequestedCultureWithEnglishFallback(string CultureName,string Expected){
        DebugClientStatus Status=new(DebugStatusReason.SnapshotUnavailable,"not-ready");
        string Text=DebugStatusText.Spatial(Status,CultureInfo.GetCultureInfo(CultureName));
        Assert.Equal(Expected,Text);
        Assert.DoesNotContain(Status.WireToken!,Text,StringComparison.Ordinal);
    }
    [Fact]
    public void SnapshotFormats_KeepRuntimeIdentifiersAndCounts(){
        CultureInfo Original=VsixLocalization.UICulture;
        try {
            VsixLocalization.Capture(CultureInfo.GetCultureInfo("ja-JP"));
            Assert.Equal("Physics: Runtime | PID 1234 | tick 56 | 7 Body | 除外=2",
                DebugStatusText.PhysicsRuntimeSnapshot(1234,56,7,2));
            Assert.Equal("Global Spatial Octree: Runtime | PID 1234 | 8ノード | 切り詰め=True | 除外=3",
                DebugStatusText.SpatialRuntimeSnapshot(1234,8,true,3));
        } finally {
            VsixLocalization.Capture(Original);
        }
    }
}
