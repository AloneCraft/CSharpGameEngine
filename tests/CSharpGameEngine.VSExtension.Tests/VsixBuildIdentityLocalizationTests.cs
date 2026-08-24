using System.Globalization;
namespace CSharpGameEngine.VSExtension.Tests;
[Collection(VsixLocalizationTestCollection.Name)]
public sealed class VsixBuildIdentityLocalizationTests {
    [Fact]
    public void DisplayText_RemainsInvariantWhileToolTipTracksCapturedCulture(){
        CultureInfo Original=VsixLocalization.UICulture;
        try {
            VsixLocalization.Capture(CultureInfo.GetCultureInfo("en-US"));
            string DisplayText=VsixBuildIdentity.DisplayText;
            Assert.Contains("UI culture: en-US",VsixBuildIdentity.ToolTipText);
            VsixLocalization.Capture(CultureInfo.GetCultureInfo("ja-JP"));
            Assert.Equal(DisplayText,VsixBuildIdentity.DisplayText);
            Assert.Contains("UI culture: ja-JP",VsixBuildIdentity.ToolTipText);
            Assert.Contains("リビジョン:",VsixBuildIdentity.ToolTipText);
            Assert.Contains("uiCulture='ja-JP'",VsixBuildIdentity.ActivityLogText);
        } finally {
            VsixLocalization.Capture(Original);
        }
    }
}
