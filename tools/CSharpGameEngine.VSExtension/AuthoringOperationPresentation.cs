using System;
using System.Collections.Generic;
using System.Linq;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension;

internal enum AuthoringOperationUiState:byte {
    Running,
    Cancelling,
    Succeeded,
    Cancelled,
    Failed,
}

/// <summary>
/// Immutable UI projection of one versioned authoring-host request. Execution remains owned by the
/// host; this type only validates and presents correlated progress, terminal state, and diagnostics.
/// </summary>
internal sealed class AuthoringOperationSnapshot {
    private AuthoringOperationSnapshot(
        Guid RequestId,AuthoringHostOperation Operation,AuthoringOperationUiState State,
        ulong Sequence,float Fraction,string Phase,string Summary,
        IReadOnlyList<AuthoringHostDiagnostic>Diagnostics){
        this.RequestId=RequestId;
        this.Operation=Operation;
        this.State=State;
        this.Sequence=Sequence;
        this.Fraction=Fraction;
        this.Phase=Phase;
        this.Summary=Summary;
        this.Diagnostics=Diagnostics;
    }

    internal Guid RequestId { get; }
    internal AuthoringHostOperation Operation { get; }
    internal AuthoringOperationUiState State { get; }
    internal ulong Sequence { get; }
    internal float Fraction { get; }
    internal string Phase { get; }
    internal string Summary { get; }
    internal IReadOnlyList<AuthoringHostDiagnostic>Diagnostics { get; }
    internal bool IsTerminal=>this.State is AuthoringOperationUiState.Succeeded
        or AuthoringOperationUiState.Cancelled or AuthoringOperationUiState.Failed;
    internal bool CanCancel=>this.State==AuthoringOperationUiState.Running;

    internal static AuthoringOperationSnapshot Start(
        Guid RequestId,AuthoringHostOperation Operation,string Phase,string Summary){
        if(RequestId==Guid.Empty){ throw new ArgumentException(nameof(RequestId)); }
        if(string.IsNullOrWhiteSpace(Phase)){ throw new ArgumentException(nameof(Phase)); }
        return new(
            RequestId,Operation,AuthoringOperationUiState.Running,0,0f,Phase,Summary,[]);
    }

    internal AuthoringOperationSnapshot ApplyProgress(AuthoringHostProgress Progress,string Summary){
        string?Error=ValidateProgress(Progress);
        if(Error is not null){
            return this.Fail("UI_PROGRESS_INVALID",Error);
        }
        return new(
            this.RequestId,this.Operation,this.State,Progress.Sequence,Progress.Fraction,
            Progress.Phase,Summary,this.Diagnostics);
    }

    internal AuthoringOperationSnapshot RequestCancellation(string Summary)=>this.CanCancel
        ?new(
            this.RequestId,this.Operation,AuthoringOperationUiState.Cancelling,this.Sequence,
            this.Fraction,this.Phase,Summary,this.Diagnostics)
        :this;

    internal AuthoringOperationSnapshot Complete(AuthoringHostResult Result){
        if(Result is null){ return this.Fail("UI_RESULT_MISSING","Authoring host result is missing."); }
        if(Result.ProtocolVersion!=AuthoringHostProtocol.CurrentVersion){
            return this.Fail("UI_PROTOCOL_MISMATCH","Authoring host result protocol does not match.");
        }
        if(Result.RequestId!=this.RequestId){
            return this.Fail("UI_REQUEST_MISMATCH","Authoring host result request ID does not match.");
        }

        AuthoringHostDiagnostic[]Diagnostics=CloneDiagnostics(Result.Diagnostics);
        string?ProgressError=ValidateTerminalProgress(Result.Progress);
        if(ProgressError is not null){
            return this.Fail("UI_RESULT_PROGRESS_INVALID",ProgressError,Diagnostics);
        }
        bool HasError=Diagnostics.Any(static Diagnostic=>
            Diagnostic.Severity==AuthoringHostDiagnosticSeverity.Error);
        bool ValidSuccess=Result.State==AuthoringHostTerminalState.Succeeded
            &&Result.ExitCode==0&&!HasError&&Result.Progress is { Fraction:1f }Final
            &&Final.ProtocolVersion==AuthoringHostProtocol.CurrentVersion
            &&Final.RequestId==this.RequestId&&Final.Sequence>this.Sequence;
        if(Result.State==AuthoringHostTerminalState.Succeeded&&!ValidSuccess){
            return this.Fail(
                "UI_FALSE_SUCCESS",
                "A successful authoring result requires exit code zero, terminal progress, and no error diagnostic.",
                Diagnostics);
        }

        AuthoringOperationUiState State=Result.State switch {
            AuthoringHostTerminalState.Succeeded=>AuthoringOperationUiState.Succeeded,
            AuthoringHostTerminalState.Cancelled=>AuthoringOperationUiState.Cancelled,
            _=>AuthoringOperationUiState.Failed,
        };
        AuthoringHostProgress?Progress=Result.Progress;
        return new(
            this.RequestId,this.Operation,State,Progress?.Sequence??this.Sequence,
            Progress?.Fraction??this.Fraction,Progress?.Phase??this.Phase,
            Result.Output??string.Empty,Diagnostics);
    }

    internal AuthoringOperationSnapshot Fail(string Code,string Message)=>
        this.Fail(Code,Message,[]);

    internal AuthoringOperationSnapshot Fail(
        string Summary,IReadOnlyList<AuthoringHostDiagnostic>?Diagnostics){
        AuthoringHostDiagnostic[]Cloned=Diagnostics is null
            ?[]
            :[..Diagnostics.Select(CloneDiagnostic)];
        if(!Cloned.Any(static Diagnostic=>
            Diagnostic.Severity==AuthoringHostDiagnosticSeverity.Error)){
            return this.Fail("UI_OPERATION_FAILED",Summary,Cloned);
        }
        return new(
            this.RequestId,this.Operation,AuthoringOperationUiState.Failed,this.Sequence,
            this.Fraction,this.Phase,Summary??string.Empty,Cloned);
    }

    private AuthoringOperationSnapshot Fail(
        string Code,string Message,IReadOnlyList<AuthoringHostDiagnostic>Existing){
        List<AuthoringHostDiagnostic>Diagnostics=[..Existing.Select(CloneDiagnostic)];
        Diagnostics.Add(new AuthoringHostDiagnostic {
            Severity=AuthoringHostDiagnosticSeverity.Error,
            Code=Code??string.Empty,
            Message=Message??string.Empty,
        });
        return new(
            this.RequestId,this.Operation,AuthoringOperationUiState.Failed,this.Sequence,
            this.Fraction,this.Phase,Message??string.Empty,Diagnostics);
    }

    private string?ValidateProgress(AuthoringHostProgress Progress){
        if(Progress is null){ return "Progress is missing."; }
        if(this.State!=AuthoringOperationUiState.Running){ return "Progress arrived after the request stopped running."; }
        if(Progress.ProtocolVersion!=AuthoringHostProtocol.CurrentVersion){ return "Progress protocol does not match."; }
        if(Progress.RequestId!=this.RequestId){ return "Progress request ID does not match."; }
        if(Progress.Sequence<=this.Sequence){ return "Progress sequence must increase."; }
        if(float.IsNaN(Progress.Fraction)||float.IsInfinity(Progress.Fraction)
            ||Progress.Fraction<this.Fraction||Progress.Fraction>1f){
            return "Progress fraction must be finite, monotonic, and no greater than one.";
        }
        if(string.IsNullOrWhiteSpace(Progress.Phase)){ return "Progress phase is required."; }
        return null;
    }

    private string?ValidateTerminalProgress(AuthoringHostProgress?Progress){
        if(Progress is null){ return null; }
        if(Progress.ProtocolVersion!=AuthoringHostProtocol.CurrentVersion){
            return "Terminal progress protocol does not match.";
        }
        if(Progress.RequestId!=this.RequestId){
            return "Terminal progress request ID does not match.";
        }
        if(Progress.Sequence<this.Sequence){
            return "Terminal progress sequence cannot move backwards.";
        }
        if(float.IsNaN(Progress.Fraction)||float.IsInfinity(Progress.Fraction)
            ||Progress.Fraction<this.Fraction||Progress.Fraction>1f){
            return "Terminal progress fraction must be finite, monotonic, and no greater than one.";
        }
        if(string.IsNullOrWhiteSpace(Progress.Phase)){
            return "Terminal progress phase is required.";
        }
        return null;
    }

    private static AuthoringHostDiagnostic[]CloneDiagnostics(
        AuthoringHostDiagnostic[]?Diagnostics)=>Diagnostics is null
        ?[]
        :[..Diagnostics.Select(CloneDiagnostic)];

    private static AuthoringHostDiagnostic CloneDiagnostic(AuthoringHostDiagnostic Diagnostic)=>new(){
        Severity=Diagnostic?.Severity??AuthoringHostDiagnosticSeverity.Error,
        Code=Diagnostic?.Code??string.Empty,
        Message=Diagnostic?.Message??string.Empty,
    };
}
