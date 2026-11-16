using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class AuthoringOperationPresentationTests {
    [Fact]
    public void ProgressMustCorrelateAndIncrease(){
        Guid RequestId=Guid.NewGuid();
        AuthoringOperationSnapshot Running=AuthoringOperationSnapshot.Start(
            RequestId,AuthoringHostOperation.Bake,"Preparing","Starting");

        AuthoringOperationSnapshot Progressed=Running.ApplyProgress(new AuthoringHostProgress {
            RequestId=RequestId,Sequence=1,Fraction=0.5f,Phase="Bake",
        },"Halfway");
        AuthoringOperationSnapshot Regressed=Progressed.ApplyProgress(new AuthoringHostProgress {
            RequestId=RequestId,Sequence=2,Fraction=0.25f,Phase="Bake",
        },"Invalid");

        Assert.Equal(AuthoringOperationUiState.Running,Progressed.State);
        Assert.Equal(0.5f,Progressed.Fraction);
        Assert.Equal(AuthoringOperationUiState.Failed,Regressed.State);
        Assert.Equal("UI_PROGRESS_INVALID",Assert.Single(Regressed.Diagnostics).Code);
    }

    [Fact]
    public void SuccessRequiresTerminalProgressAndNoErrorDiagnostic(){
        Guid RequestId=Guid.NewGuid();
        AuthoringOperationSnapshot Running=AuthoringOperationSnapshot.Start(
            RequestId,AuthoringHostOperation.Import,"Import","Starting");
        AuthoringHostResult FalseSuccess=new(){
            RequestId=RequestId,State=AuthoringHostTerminalState.Succeeded,ExitCode=0,
            Output="claimed success",
            Progress=new AuthoringHostProgress {
                RequestId=RequestId,Sequence=1,Fraction=0.9f,Phase="Import",
            },
        };

        AuthoringOperationSnapshot Result=Running.Complete(FalseSuccess);

        Assert.Equal(AuthoringOperationUiState.Failed,Result.State);
        Assert.Equal("UI_FALSE_SUCCESS",Assert.Single(Result.Diagnostics).Code);
    }

    [Fact]
    public void CancellationIsTerminalAndCannotBeReportedAsSuccess(){
        Guid RequestId=Guid.NewGuid();
        AuthoringOperationSnapshot Cancelling=AuthoringOperationSnapshot.Start(
            RequestId,AuthoringHostOperation.Bake,"Bake","Starting")
            .RequestCancellation("Cancelling");
        AuthoringHostResult Cancelled=new(){
            RequestId=RequestId,State=AuthoringHostTerminalState.Cancelled,ExitCode=1,
            Output="Cancelled",
            Diagnostics=[new AuthoringHostDiagnostic {
                Severity=AuthoringHostDiagnosticSeverity.Error,
                Code="HOST_CANCELLED",Message="Cancelled",
            }],
        };

        AuthoringOperationSnapshot Result=Cancelling.Complete(Cancelled);

        Assert.Equal(AuthoringOperationUiState.Cancelled,Result.State);
        Assert.True(Result.IsTerminal);
        Assert.False(Result.CanCancel);
        Assert.Equal("HOST_CANCELLED",Assert.Single(Result.Diagnostics).Code);
    }

    [Fact]
    public void HostDiagnosticsRemainTypedForTheUi(){
        AuthoringOperationSnapshot Running=AuthoringOperationSnapshot.Start(
            Guid.NewGuid(),AuthoringHostOperation.Import,"Import","Starting");
        AuthoringHostDiagnostic[]Diagnostics=[
            new AuthoringHostDiagnostic {
                Severity=AuthoringHostDiagnosticSeverity.Warning,
                Code="IMPORT_WARNING",Message="warning",
            },
            new AuthoringHostDiagnostic {
                Severity=AuthoringHostDiagnosticSeverity.Error,
                Code="IMPORT_INVALID_MESH",Message="mesh is invalid",
            },
        ];

        AuthoringOperationSnapshot Failed=Running.Fail("Import failed",Diagnostics);

        Assert.Equal(AuthoringOperationUiState.Failed,Failed.State);
        Assert.Collection(Failed.Diagnostics,
            Warning=>Assert.Equal("IMPORT_WARNING",Warning.Code),
            Error=>Assert.Equal("IMPORT_INVALID_MESH",Error.Code));
    }

    [Fact]
    public void FailedResultCannotInjectUncorrelatedTerminalProgress(){
        Guid RequestId=Guid.NewGuid();
        AuthoringOperationSnapshot Running=AuthoringOperationSnapshot.Start(
            RequestId,AuthoringHostOperation.Bake,"Bake","Starting");
        AuthoringOperationSnapshot Result=Running.Complete(new AuthoringHostResult {
            RequestId=RequestId,State=AuthoringHostTerminalState.Failed,ExitCode=1,
            Output="failed",
            Progress=new AuthoringHostProgress {
                RequestId=Guid.NewGuid(),Sequence=1,Fraction=0.5f,Phase="Bake",
            },
        });

        Assert.Equal(AuthoringOperationUiState.Failed,Result.State);
        Assert.Contains(Result.Diagnostics,Diagnostic=>
            Diagnostic.Code=="UI_RESULT_PROGRESS_INVALID");
        Assert.Equal(0f,Result.Fraction);
    }
}
