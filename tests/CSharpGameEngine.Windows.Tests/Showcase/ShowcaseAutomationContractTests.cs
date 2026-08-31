using CSharpGameEngine.Showcase.Automation;
namespace CSharpGameEngine.Windows.Tests.Showcase;
public sealed class ShowcaseAutomationContractTests {
    [Fact]
    public void Arguments_ParseDeclaredRunContract(){
        var Parsed=ShowcaseAutomationArguments.TryParse([
            "run","--configuration","Release","--adapter","warp","--locale","ja-JP",
            "--timeout-seconds","180","--evidence-dir","evidence",
        ],out var Options,out var Error);
        Assert.True(Parsed,Error);
        Assert.NotNull(Options);
        Assert.Equal("Release",Options.Configuration);
        Assert.Equal("warp",Options.Adapter);
        Assert.Equal("ja-JP",Options.Locale);
        Assert.Equal(180,Options.TimeoutSeconds);
        Assert.Equal("evidence",Options.EvidenceDirectory);
    }
    [Theory]
    [InlineData("profile","warp","auto")]
    [InlineData("Debug","software","auto")]
    [InlineData("Debug","warp","fr-FR")]
    public void Arguments_RejectUnknownClosedSetValues(string Configuration,string Adapter,string Locale){
        var Parsed=ShowcaseAutomationArguments.TryParse([
            "run","--configuration",Configuration,"--adapter",Adapter,"--locale",Locale,
        ],out var Options,out var Error);
        Assert.False(Parsed);
        Assert.Null(Options);
        Assert.NotEmpty(Error);
    }
    [Fact]
    public void ExitCodes_MatchFeatureRunnerContract(){
        Assert.Equal(0,(int)ShowcaseAutomationExitCode.Success);
        Assert.Equal(1,(int)ShowcaseAutomationExitCode.OracleMismatch);
        Assert.Equal(2,(int)ShowcaseAutomationExitCode.InfrastructureError);
    }
}
