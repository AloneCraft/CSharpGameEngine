using System.IO;
using System;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class ScenePhysicsPanelLayoutTests {
    [Fact]
    public void PhysicsControlsUseRightSidebarAndViewportColumn(){
        string Xaml=File.ReadAllText(Path.Combine(VsixBuildIdentity.RepoRoot,"tools","CSharpGameEngine.VSExtension","SceneDesignerToolWindowControl.xaml"));
        Assert.Contains("x:Name=\"PhysicsSidebarGrid\"",Xaml,StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PhysicsPanelToggle\"",Xaml,StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PhysicsPanelBorder\"",Xaml,StringComparison.Ordinal);
        Assert.Contains("Width=\"280\"",Xaml,StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RenderHostGrid\" Grid.Column=\"0\"",Xaml,StringComparison.Ordinal);
        Assert.DoesNotContain("<Border Grid.Row=\"1\" Padding=\"8,3\"",Xaml,StringComparison.Ordinal);
    }

    [Fact]
    public void ViewportKeyRoutingReservesNForPhysicsPanel(){
        string Code=File.ReadAllText(Path.Combine(VsixBuildIdentity.RepoRoot,"tools","CSharpGameEngine.VSExtension","SceneDesignerToolWindowControl.xaml.cs"));
        Assert.Contains("if(Args.Key==Key.N)",Code,StringComparison.Ordinal);
        Assert.Contains("SetPhysicsPanelVisible",Code,StringComparison.Ordinal);
        Assert.Contains("ReadShowPhysicsPanel",Code,StringComparison.Ordinal);
        Assert.Contains("WriteShowPhysicsPanel",Code,StringComparison.Ordinal);
    }

    [Fact]
    public void PhysicsPanelExposesRealBakeAndProtocolV2DebugOptions(){
        string Root=VsixBuildIdentity.RepoRoot;
        string Xaml=File.ReadAllText(Path.Combine(Root,"tools","CSharpGameEngine.VSExtension","SceneDesignerToolWindowControl.xaml"));
        string Code=File.ReadAllText(Path.Combine(Root,"tools","CSharpGameEngine.VSExtension","SceneDesignerToolWindowControl.xaml.cs"));

        Assert.Contains("x:Name=\"PhysicsBakeSelectedButton\"",Xaml,StringComparison.Ordinal);
        Assert.Contains("Click=\"OnPhysicsBakeSelected\"",Xaml,StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PhysicsBakeAllButton\"",Xaml,StringComparison.Ordinal);
        Assert.Contains("Click=\"OnPhysicsBakeAll\"",Xaml,StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PhysicsContactsToggle\"",Xaml,StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PhysicsTreeToggle\"",Xaml,StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PhysicsBondsToggle\"",Xaml,StringComparison.Ordinal);
        Assert.Contains("PhysicsSceneBakeService.Bake",Code,StringComparison.Ordinal);
        Assert.Contains("PrepareForBakeAsync(Entries,Cancellation.Token)",Code,StringComparison.Ordinal);
        Assert.DoesNotContain("PhysicsBakeSelectedDeferredToolTip}\"",Xaml,StringComparison.Ordinal);
    }
}
