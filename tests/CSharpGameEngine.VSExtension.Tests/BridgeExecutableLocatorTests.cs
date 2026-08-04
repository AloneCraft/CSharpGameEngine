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
    private readonly string _repoRoot;

    public BridgeExecutableLocatorTests()
    {
        _repoRoot = Path.Combine(Path.GetTempPath(), "csge-bridgelocator-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repoRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_repoRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void NoBuiltExecutable_ReturnsNull()
    {
        string? result = BridgeExecutableLocator.FindExecutable(_repoRoot, BridgeProjectRelativePath);

        Assert.Null(result);
    }

    [Fact]
    public void DebugBuildOnly_IsFound()
    {
        string exePath = CreateExe("Debug", ridSubfolder: null, DateTime.UtcNow);

        string? result = BridgeExecutableLocator.FindExecutable(_repoRoot, BridgeProjectRelativePath);

        Assert.Equal(exePath, result);
    }

    [Fact]
    public void RuntimeIdentifierSubfolder_IsFound()
    {
        string exePath = CreateExe("Debug", "win-x64", DateTime.UtcNow);

        string? result = BridgeExecutableLocator.FindExecutable(_repoRoot, BridgeProjectRelativePath);

        Assert.Equal(exePath, result);
    }

    [Fact]
    public void MultipleCandidates_PicksNewestByWriteTime_EvenWhenReleaseIsNewer()
    {
        DateTime now = DateTime.UtcNow;
        CreateExe("Debug", ridSubfolder: null, now);
        string newerReleaseExePath = CreateExe("Release", ridSubfolder: null, now.AddMinutes(5));

        string? result = BridgeExecutableLocator.FindExecutable(_repoRoot, BridgeProjectRelativePath);

        Assert.Equal(newerReleaseExePath, result);
    }

    [Fact]
    public void MultipleCandidates_PicksNewestByWriteTime_EvenWhenDebugIsNewer()
    {
        DateTime now = DateTime.UtcNow;
        CreateExe("Release", ridSubfolder: null, now);
        string newerDebugExePath = CreateExe("Debug", ridSubfolder: null, now.AddMinutes(5));

        string? result = BridgeExecutableLocator.FindExecutable(_repoRoot, BridgeProjectRelativePath);

        Assert.Equal(newerDebugExePath, result);
    }

    private string CreateExe(string configuration, string? ridSubfolder, DateTime writeTimeUtc)
    {
        string directory = ridSubfolder is null
            ? Path.Combine(_repoRoot, BridgeProjectRelativePath, "bin", configuration, "net10.0")
            : Path.Combine(_repoRoot, BridgeProjectRelativePath, "bin", configuration, "net10.0", ridSubfolder);
        Directory.CreateDirectory(directory);

        string exePath = Path.Combine(directory, "Sample.Bridge.Cli.exe");
        File.WriteAllText(exePath, "fake exe bytes");
        File.SetLastWriteTimeUtc(exePath, writeTimeUtc);

        return exePath;
    }
}
