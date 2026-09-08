using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class HermeticBuildVerifierTests {
    [Fact]
    public void PlanUsesOneOwnedRootAndRunsRestoreThenDebugAndReleaseBuildTest(){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();

        HermeticBuildPlan Plan=HermeticBuildPlan.Create(RepositoryRoot,"contract-test");

        Assert.Equal([
            "restore","build-Debug","test-Debug","build-Release","test-Release",
        ],Plan.Commands.Select(Command=>Command.Phase));
        Assert.StartsWith(
            Path.Combine(RepositoryRoot,".artifacts","CleanBuild","work"),
            Plan.WorkRoot,StringComparison.OrdinalIgnoreCase);
        Assert.All(Plan.Commands,Command=>{
            Assert.Contains("--artifacts-path",Command.Arguments);
            Assert.Contains(Plan.ArtifactRoot,Command.Arguments);
            Assert.Contains("--disable-build-servers",Command.Arguments);
        });
        HermeticBuildCommand Restore=Plan.Commands[0];
        Assert.Contains("--packages",Restore.Arguments);
        Assert.Contains(Plan.PackageRoot,Restore.Arguments);
        Assert.All(Plan.Commands.Where(Command=>Command.Phase.StartsWith("build",StringComparison.Ordinal)),
            Command=>Assert.Contains("--no-incremental",Command.Arguments));
        Assert.All(Plan.Commands.Where(Command=>Command.Phase.StartsWith("test",StringComparison.Ordinal)),
            Command=>Assert.Contains("--no-build",Command.Arguments));
        Assert.False(Directory.Exists(Plan.WorkRoot));
    }
}
