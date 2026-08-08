namespace CSharpGameEngine.SceneAuthoring.Cli.Tests;

/// <summary>
/// Locates another project's already-built assembly under the repo tree from a running test
/// (walking up from <see cref="AppContext.BaseDirectory"/> to the <c>CSharpGameEngine.slnx</c>
/// marker, mirroring <c>BridgeExecutableLocator</c>'s own "newest by write time across
/// Debug/Release" tie-break). Used by <see cref="SceneRuntimeInvokerTests"/> to point at
/// <c>samples/SceneAuthoringDemo</c>'s real build output - the project reference with
/// <c>ReferenceOutputAssembly="false"</c> in this test project's csproj guarantees that dll exists
/// by the time tests run.
/// </summary>
internal static class RepoBuildOutputLocator
{
    internal static string FindProjectAssembly(string projectRelativeDirectory, string assemblyFileName)
    {
        string repoRoot = FindRepoRoot();
        string binRoot = Path.Combine(repoRoot, projectRelativeDirectory, "bin");

        string[] candidates =
        [
            Path.Combine(binRoot, "Debug", "net10.0", assemblyFileName),
            Path.Combine(binRoot, "Release", "net10.0", assemblyFileName),
        ];

        string? found = candidates
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        return found ?? throw new InvalidOperationException(
            $"'{assemblyFileName}' が見つかりませんでした({string.Join(", ", candidates)})。'dotnet build {projectRelativeDirectory}' を先に実行してください。");
    }

    internal static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGameEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"'CSharpGameEngine.slnx' を辿って探索しましたが、リポジトリルートが見つかりませんでした(起点: {AppContext.BaseDirectory})。");
    }
}
