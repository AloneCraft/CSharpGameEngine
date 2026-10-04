using System.Xml.Linq;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// Visual Studio runs design-time builds (CompileDesignTime) in the background. They run
/// MarkupCompilePass1 without Pass2 and produce no BAML, package or deployment, so a check of build
/// outputs there fails spuriously and shows in the Error List. That is how the WPF BAML check reported
/// "resource generation is incomplete" on every F5.
/// </summary>
public sealed class VsixBuildTargetTests {
    [Fact]
    public void ProjectTargetsThatFailTheBuild_NeverRunInDesignTimeBuilds(){
        XDocument Project=XDocument.Load(Path.Combine(RepositoryRoot(),"tools","CSharpGameEngine.VSExtension","CSharpGameEngine.VSExtension.csproj"));
        var Failing=Project.Descendants("Target")
            .Where(static Target=>Target.Descendants("Error").Any())
            .ToList();

        Assert.NotEmpty(Failing);
        Assert.All(Failing,static Target=>Assert.True(
            ((string?)Target.Attribute("Condition")??string.Empty).Replace(" ",string.Empty).Contains("'$(DesignTimeBuild)'!='true'"),
            $"Target {(string?)Target.Attribute("Name")} can fail a design-time build."));
    }

    private static string RepositoryRoot(){
        DirectoryInfo?Directory=new(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ Directory=Directory.Parent; }
        return Directory?.FullName??throw new DirectoryNotFoundException();
    }
}
