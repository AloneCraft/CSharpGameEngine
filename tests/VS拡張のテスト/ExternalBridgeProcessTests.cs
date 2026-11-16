using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class ExternalBridgeProcessTests {
    [Fact]
    public void SuccessfulResultRequiresZeroExitAndNoErrorDiagnostic(){
        AuthoringHostResult Result=new(){
            State=AuthoringHostTerminalState.Succeeded,ExitCode=0,
            Diagnostics=[new AuthoringHostDiagnostic {
                Severity=AuthoringHostDiagnosticSeverity.Warning,
                Code="IMPORT_WARNING",Message="warning",
            }],
        };

        Assert.True(ExternalBridgeProcess.IsSuccessfulResult(Result));
        Result.ExitCode=1;
        Assert.False(ExternalBridgeProcess.IsSuccessfulResult(Result));
    }

    [Fact]
    public void ErrorDiagnosticCannotBeFlattenedIntoResidentSuccess(){
        AuthoringHostResult Result=new(){
            State=AuthoringHostTerminalState.Succeeded,ExitCode=0,
            Diagnostics=[new AuthoringHostDiagnostic {
                Severity=AuthoringHostDiagnosticSeverity.Error,
                Code="IMPORT_INVALID",Message="invalid",
            }],
        };

        Assert.False(ExternalBridgeProcess.IsSuccessfulResult(Result));
    }
}
