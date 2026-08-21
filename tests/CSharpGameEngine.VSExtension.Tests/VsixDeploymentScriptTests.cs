namespace CSharpGameEngine.VSExtension.Tests;

public sealed class VsixDeploymentScriptTests {
    [Fact]
    public void ReinstallScript_PreservesSingletonDiscoveryAsArrays(){
        string Script=File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,"TestAssets","Reinstall-CgeFixedVsix.ps1"));

        Assert.Equal(2,CountOccurrences(Script,"$Hives=@(Get-CgeFixedHives)"));
        Assert.Equal(2,CountOccurrences(Script,"$Installed=@(Get-CgeFixedInstallations $Hives)"));
        Assert.Equal(2,CountOccurrences(Script,"$Legacy=@(Get-CgeFixedLegacyDirectories $Hives)"));
    }

    [Fact]
    public void LaunchScript_OnlyRequestsANormalCgeFixedClose(){
        string Script=File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,"TestAssets","Launch-VSExtension.ps1"));

        Assert.Contains("[switch]$CloseCgeFixed",Script);
        Assert.Contains("CloseMainWindow()",Script);
        Assert.DoesNotContain("Stop-Process",Script);
    }

    private static int CountOccurrences(string Text,string Value){
        int Count=0;
        int Index=0;
        while((Index=Text.IndexOf(Value,Index,StringComparison.Ordinal))>=0){
            Count++;
            Index+=Value.Length;
        }
        return Count;
    }
}