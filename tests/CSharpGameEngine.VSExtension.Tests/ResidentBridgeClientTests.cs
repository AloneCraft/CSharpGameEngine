using CSharpGameEngine.VSExtension;
using CSharpGameEngine.Contracts;
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
public class ResidentBridgeClientTests {
    [Fact]
    public void ComputePipeName_SameInputs_ReturnsSameName(){
        var First=ResidentBridgeClient.ComputePipeName(@"G:\repo",@"tools\CSharpGameEngine.Import.Gltf.Cli",OwnerProcessId:100);
        var Second=ResidentBridgeClient.ComputePipeName(@"G:\repo",@"tools\CSharpGameEngine.Import.Gltf.Cli",OwnerProcessId:100);

        Assert.Equal(First,Second);
    }

    [Fact]
    public void ComputePipeName_DifferentRepoRoot_ReturnsDifferentName(){
        var First=ResidentBridgeClient.ComputePipeName(@"G:\repo-one",@"tools\CSharpGameEngine.Import.Gltf.Cli",OwnerProcessId:100);
        var Second=ResidentBridgeClient.ComputePipeName(@"G:\repo-two",@"tools\CSharpGameEngine.Import.Gltf.Cli",OwnerProcessId:100);

        Assert.NotEqual(First,Second);
    }

    [Fact]
    public void ComputePipeName_DifferentBridgeProject_ReturnsDifferentName(){
        var Gltf=ResidentBridgeClient.ComputePipeName(@"G:\repo",@"tools\CSharpGameEngine.Import.Gltf.Cli",OwnerProcessId:100);
        var Fbx=ResidentBridgeClient.ComputePipeName(@"G:\repo",@"tools\CSharpGameEngine.Import.Fbx.Cli",OwnerProcessId:100);

        Assert.NotEqual(Gltf,Fbx);
    }

    [Fact]
    public void ComputePipeName_DifferentOwner_ReturnsDifferentName(){
        // Each Visual Studio has its own bridges; another one (another install) never shares them.
        var First=ResidentBridgeClient.ComputePipeName(@"G:\repo",@"tools\CSharpGameEngine.Import.Gltf.Cli",OwnerProcessId:100);
        var Second=ResidentBridgeClient.ComputePipeName(@"G:\repo",@"tools\CSharpGameEngine.Import.Gltf.Cli",OwnerProcessId:200);

        Assert.NotEqual(First,Second);
    }

    [Fact]
    public void ServeArguments_NameTheOwnerTheBridgeEndsWith(){
        Assert.Equal("--serve \"Pipe.Name\" --parent-process-id 4242",ResidentBridgeClient.ServeArguments("Pipe.Name",4242));
    }

    [Theory]
    [InlineData(@"tools\CSharpGameEngine.Import.Gltf.Cli",AuthoringHostOperation.Import)]
    [InlineData(@"tools\CSharpGameEngine.Import.Fbx.Cli",AuthoringHostOperation.Import)]
    [InlineData(@"tools\CSharpGameEngine.Primitive.Cli",AuthoringHostOperation.Preview)]
    [InlineData(@"tools\CSharpGameEngine.Scene.Bake.Cli",AuthoringHostOperation.Bake)]
    public void ClassifyOperation_UsesAuthoringBoundary(string Project,AuthoringHostOperation Expected){
        Assert.Equal(Expected,ResidentBridgeClient.ClassifyOperation(Project));
    }

    [Fact]
    public void ClassifyTransportFailure_BeforeSubmission_IsUnavailable(){
        Assert.Equal(
            ResidentBridgeAttemptStatus.Unavailable,
            ResidentBridgeClient.ClassifyTransportFailure(false,new TimeoutException()));
    }

    [Theory]
    [InlineData(true)]
    public void ClassifyTransportFailure_AfterSubmission_IsTerminal(bool Submitted){
        Assert.Equal(
            ResidentBridgeAttemptStatus.TimedOut,
            ResidentBridgeClient.ClassifyTransportFailure(Submitted,new TimeoutException()));
        Assert.Equal(
            ResidentBridgeAttemptStatus.HostCrashed,
            ResidentBridgeClient.ClassifyTransportFailure(Submitted,new IOException()));
        Assert.Equal(
            ResidentBridgeAttemptStatus.ProtocolMismatch,
            ResidentBridgeClient.ClassifyTransportFailure(Submitted,new InvalidOperationException()));
    }
}
