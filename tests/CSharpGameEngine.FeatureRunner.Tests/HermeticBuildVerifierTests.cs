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
        // ビルド中の入れ子の復元（ビルドが実行するツール）も、同じ隔離フォルダーを使わなければならない。
        Assert.Equal(Plan.PackageRoot,Plan.Environment["NUGET_PACKAGES"]);
        // ルートは空の状態で始める。Rebuild では、各プロジェクトが自身の用途でビルドしたツールまで消去してしまう (AUD-004)。
        Assert.All(Plan.Commands.Where(Command=>Command.Phase.StartsWith("build",StringComparison.Ordinal)),
            Command=>Assert.DoesNotContain("--no-incremental",Command.Arguments));
        Assert.All(Plan.Commands.Where(Command=>Command.Phase.StartsWith("test",StringComparison.Ordinal)),
            Command=>Assert.Contains("--no-build",Command.Arguments));
        Assert.False(Directory.Exists(Plan.WorkRoot));
    }

    [Fact]
    public void DefaultRunIdKeepsTheArtifactRootShallow(){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();

        HermeticBuildPlan Plan=HermeticBuildPlan.Create(RepositoryRoot);

        // VSIX は Bridges\<project>\<project>.runtimeconfig.json を成果物ルートから約 110 文字下に圧縮する。
        // 32 文字の実行 ID によって、チェックアウトの深さがわずか 40 文字でも MAX_PATH を超えた。
        Assert.True(Plan.ArtifactRoot.Length-Plan.RepositoryRoot.Length<=50,Plan.ArtifactRoot);
    }
}
