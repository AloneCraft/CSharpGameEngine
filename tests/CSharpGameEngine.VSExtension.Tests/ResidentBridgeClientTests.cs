using CSharpGameEngine.VSExtension;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// Exercises <see cref="ResidentBridgeClient.ComputePipeName"/> only (ADR-0023) - the rest of
/// <see cref="ResidentBridgeClient"/> spawns real processes/pipes and is verified manually instead,
/// matching how <c>BridgeExecutableLocatorTests</c> stays real-process-free. Determinism matters
/// specifically because <see cref="object.GetHashCode"/>/<see cref="string.GetHashCode()"/> are
/// randomized per process - a naive hash-based name would never let a second devenv.exe (or the
/// same one restarted) find an already-running server.
/// </summary>
public class ResidentBridgeClientTests
{
    [Fact]
    public void ComputePipeName_SameInputs_ReturnsSameName()
    {
        string First = ResidentBridgeClient.ComputePipeName(@"G:\repo", @"tools\CSharpGameEngine.Import.Gltf.Cli");
        string Second = ResidentBridgeClient.ComputePipeName(@"G:\repo", @"tools\CSharpGameEngine.Import.Gltf.Cli");

        Assert.Equal(First, Second);
    }

    [Fact]
    public void ComputePipeName_DifferentRepoRoot_ReturnsDifferentName()
    {
        string First = ResidentBridgeClient.ComputePipeName(@"G:\repo-one", @"tools\CSharpGameEngine.Import.Gltf.Cli");
        string Second = ResidentBridgeClient.ComputePipeName(@"G:\repo-two", @"tools\CSharpGameEngine.Import.Gltf.Cli");

        Assert.NotEqual(First, Second);
    }

    [Fact]
    public void ComputePipeName_DifferentBridgeProject_ReturnsDifferentName()
    {
        string Gltf = ResidentBridgeClient.ComputePipeName(@"G:\repo", @"tools\CSharpGameEngine.Import.Gltf.Cli");
        string Fbx = ResidentBridgeClient.ComputePipeName(@"G:\repo", @"tools\CSharpGameEngine.Import.Fbx.Cli");

        Assert.NotEqual(Gltf, Fbx);
    }
}
