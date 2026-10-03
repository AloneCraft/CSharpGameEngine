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
        // Nested restores during the build (tools the build runs) must use the same isolated folder.
        Assert.Equal(Plan.PackageRoot,Plan.Environment["NUGET_PACKAGES"]);
        Assert.All(Plan.Commands.Where(Command=>Command.Phase.StartsWith("build",StringComparison.Ordinal)),
            Command=>Assert.Contains("--no-incremental",Command.Arguments));
        Assert.All(Plan.Commands.Where(Command=>Command.Phase.StartsWith("test",StringComparison.Ordinal)),
            Command=>Assert.Contains("--no-build",Command.Arguments));
        Assert.False(Directory.Exists(Plan.WorkRoot));
    }

    [Fact]
    public void DefaultRunIdKeepsTheArtifactRootShallow(){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();

        HermeticBuildPlan Plan=HermeticBuildPlan.Create(RepositoryRoot);

        // The VSIX zips Bridges\<project>\<project>.runtimeconfig.json about 110 characters below the artifacts root;
        // a 32-character run ID pushed it past MAX_PATH for a checkout only 40 characters deep.
        Assert.True(Plan.ArtifactRoot.Length-Plan.RepositoryRoot.Length<=50,Plan.ArtifactRoot);
    }
}
