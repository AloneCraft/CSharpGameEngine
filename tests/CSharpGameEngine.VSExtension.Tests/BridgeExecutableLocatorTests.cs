using System;
using System.IO;
using CSharpGameEngine.VSExtension;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// Exercises <see cref="BridgeExecutableLocator"/>'s path-selection logic directly against temp
/// folders shaped like a real bridge project's <c>bin/</c> output (ADR-0010 addendum: launching a
/// prebuilt apphost <c>.exe</c> instead of <c>dotnet run --project</c>) - never spawns a real
/// dotnet/MSBuild process, so this stays fast and deterministic.
/// </summary>
public sealed class BridgeExecutableLocatorTests : IDisposable
{
    private const string BridgeProjectRelativePath = @"tools\Sample.Bridge.Cli";
    private readonly string RepoRoot;

    public BridgeExecutableLocatorTests()
    {
        RepoRoot = Path.Combine(Path.GetTempPath(), "csge-bridgelocator-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RepoRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(RepoRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void NoBuiltExecutable_ReturnsNull()
    {
        string? Result = BridgeExecutableLocator.FindExecutable(RepoRoot, BridgeProjectRelativePath);

        Assert.Null(Result);
    }

    [Fact]
    public void DebugBuildOnly_IsFound()
    {
        string ExePath = CreateExe("Debug", RidSubfolder: null, DateTime.UtcNow);

        string? Result = BridgeExecutableLocator.FindExecutable(RepoRoot, BridgeProjectRelativePath);

        Assert.Equal(ExePath, Result);
    }

    [Fact]
    public void RuntimeIdentifierSubfolder_IsFound()
    {
        string ExePath = CreateExe("Debug", "win-x64", DateTime.UtcNow);

        string? Result = BridgeExecutableLocator.FindExecutable(RepoRoot, BridgeProjectRelativePath);

        Assert.Equal(ExePath, Result);
    }

    [Fact]
    public void MultipleCandidates_PicksNewestByWriteTime_EvenWhenReleaseIsNewer()
    {
        DateTime Now = DateTime.UtcNow;
        CreateExe("Debug", RidSubfolder: null, Now);
        string NewerReleaseExePath = CreateExe("Release", RidSubfolder: null, Now.AddMinutes(5));

        string? Result = BridgeExecutableLocator.FindExecutable(RepoRoot, BridgeProjectRelativePath);

        Assert.Equal(NewerReleaseExePath, Result);
    }

    [Fact]
    public void MultipleCandidates_PicksNewestByWriteTime_EvenWhenDebugIsNewer()
    {
        DateTime Now = DateTime.UtcNow;
        CreateExe("Release", RidSubfolder: null, Now);
        string NewerDebugExePath = CreateExe("Debug", RidSubfolder: null, Now.AddMinutes(5));

        string? Result = BridgeExecutableLocator.FindExecutable(RepoRoot, BridgeProjectRelativePath);

        Assert.Equal(NewerDebugExePath, Result);
    }

    private string CreateExe(string Configuration, string? RidSubfolder, DateTime WriteTimeUtc)
    {
        string directory = RidSubfolder is null
            ? Path.Combine(RepoRoot, BridgeProjectRelativePath, "bin", Configuration, "net10.0")
            : Path.Combine(RepoRoot, BridgeProjectRelativePath, "bin", Configuration, "net10.0", RidSubfolder);
        Directory.CreateDirectory(directory);

        string ExePath = Path.Combine(directory, "Sample.Bridge.Cli.exe");
        File.WriteAllText(ExePath, "fake exe bytes");
        File.SetLastWriteTimeUtc(ExePath, WriteTimeUtc);

        return ExePath;
    }
}
