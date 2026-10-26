using CSharpGameEngine.VSExtension;
using CSharpGameEngine.Contracts;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// <see cref="ResidentBridgeClient.ComputePipeName"/> のみを検証する (ADR-0023)。<see cref="ResidentBridgeClient"/> の他の部分は実プロセスとパイプを起動するため、<c>BridgeExecutableLocatorTests</c> が実プロセスを使わないのと同様に、手作業で検証する。特に <see cref="object.GetHashCode"/>/<see cref="string.GetHashCode()"/> はプロセスごとにランダム化されるため、決定性が重要である。単純なハッシュ名では 2 つ目の devenv.exe（または再起動した同じ devenv.exe）が稼働中サーバーを見つけられない。
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
        // 各 Visual Studio は専用のブリッジを持ち、別のインストール環境とは共有しない。
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
