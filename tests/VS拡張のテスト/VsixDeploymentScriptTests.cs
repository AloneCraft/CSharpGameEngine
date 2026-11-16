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
        Assert.Contains("-t:Rebuild",Script);
        Assert.Contains("CloseMainWindow()",Script);
        Assert.DoesNotContain("Stop-Process",Script);
        Assert.Contains("-requires Microsoft.VisualStudio.Component.CoreEditor",Script);
        Assert.Contains("Get-VisualStudioInstallPath",Script);
        Assert.Contains("Common7\\IDE\\devenv.exe",Script);
        Assert.DoesNotContain("-latest -products",Script);
    }

    [Fact]
    public void ReinstallScript_VerifiesBamlAndPackageBeforeDeployment(){
        string Script=File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,"TestAssets","Reinstall-CgeFixedVsix.ps1"));

        Assert.Contains("Get-EmbeddedWpfBamlNames",Script);
        Assert.Contains("Assert-WpfBamlResources",Script);
        Assert.Contains("Get-VsixAssemblyHash",Script);
        Assert.Contains("-t:Rebuild",Script);
        Assert.Contains("VSIX package DLL SHA-256 does not match build output",Script);
    }

    [Fact]
    public void ReinstallScript_AcceptsAnIsolatedRootSuffixAndDefaultsToCgeFixed(){
        string Script=File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,"TestAssets","Reinstall-CgeFixedVsix.ps1"));

        Assert.Contains("[string]$RootSuffix='CgeFixed'",Script);
        Assert.Contains("/rootSuffix:$RootSuffix",Script);
        Assert.DoesNotContain("$RootSuffix='CgeFixed'\r\n$ScriptDirectory",Script);
    }

[Fact]
    public void ReinstallScript_SelectsAVisualStudioInstallerFromAllRunnableCandidates(){
        string Script=File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,"TestAssets","Reinstall-CgeFixedVsix.ps1"));

        Assert.Contains("$InstallPaths=@(& $VsWhere -products",Script);
        Assert.Contains("foreach($InstallPath in $InstallPaths)",Script);
        Assert.DoesNotContain("-latest -products",Script);
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
