using System;
using System.IO;
using CSharpGameEngine.VSExtension;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// Exercises <see cref="ProjectOutputAssemblyResolver"/>'s pure path-combination and glob-fallback
/// logic directly (mirrors <see cref="BridgeExecutableLocatorTests"/>'s style) - never touches
/// <see cref="EnvDTE.Project"/>, which requires a running VS host (<see cref="ProjectOutputAssemblyResolver.TryResolve"/>
/// itself is not covered here for that reason).
/// </summary>
public sealed class ProjectOutputAssemblyResolverTests : IDisposable
{
    private readonly string _projectDirectory;

    public ProjectOutputAssemblyResolverTests()
    {
        _projectDirectory = Path.Combine(Path.GetTempPath(), "csge-outputresolver-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_projectDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void CombineOutputPath_JoinsProjectDirectoryOutputPathAndFileName()
    {
        string result = ProjectOutputAssemblyResolver.CombineOutputPath(@"G:\repo\UserGameProject", @"bin\Debug\net10.0-windows10.0.19041.0\win-x64\", "UserGameProject.dll");

        Assert.Equal(Path.GetFullPath(@"G:\repo\UserGameProject\bin\Debug\net10.0-windows10.0.19041.0\win-x64\UserGameProject.dll"), result);
    }

    [Fact]
    public void FindNewestByGlob_NoBuiltAssembly_ReturnsNull()
    {
        string? result = ProjectOutputAssemblyResolver.FindNewestByGlob(_projectDirectory, "Sample.dll");

        Assert.Null(result);
    }

    [Fact]
    public void FindNewestByGlob_SingleCandidate_IsFound()
    {
        string expected = CreateAssembly("Debug", "net10.0", DateTime.UtcNow);

        string? result = ProjectOutputAssemblyResolver.FindNewestByGlob(_projectDirectory, "Sample.dll");

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FindNewestByGlob_MultipleCandidates_PicksNewestByWriteTime()
    {
        DateTime now = DateTime.UtcNow;
        CreateAssembly("Debug", "net10.0", now);
        string newest = CreateAssembly("Debug", "net10.0-windows10.0.19041.0\\win-x64", now.AddMinutes(5));

        string? result = ProjectOutputAssemblyResolver.FindNewestByGlob(_projectDirectory, "Sample.dll");

        Assert.Equal(newest, result);
    }

    private string CreateAssembly(string configuration, string tfmSubpath, DateTime writeTimeUtc)
    {
        string directory = Path.Combine(_projectDirectory, "bin", configuration, tfmSubpath);
        Directory.CreateDirectory(directory);

        string assemblyPath = Path.Combine(directory, "Sample.dll");
        File.WriteAllText(assemblyPath, "fake dll bytes");
        File.SetLastWriteTimeUtc(assemblyPath, writeTimeUtc);

        return assemblyPath;
    }
}
