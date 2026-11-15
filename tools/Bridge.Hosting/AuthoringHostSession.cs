using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Bridge.Hosting;

public sealed class AuthoringHostCrashedException:Exception {
    public AuthoringHostCrashedException(string Message):base(Message){ }
}

public readonly record struct AuthoringHostExecutionResult(int ExitCode,string Output);

public static class AuthoringHostSession {
    public static async Task<AuthoringHostResult>RunAsync(
        AuthoringHostRequest Request,
        Func<AuthoringHostRequest,Action<AuthoringHostProgress>,CancellationToken,Task<AuthoringHostExecutionResult>>Executor,
        CancellationToken CancellationToken=default){
        if(Request is null){ throw new ArgumentNullException(nameof(Request)); }
        if(Executor is null){ throw new ArgumentNullException(nameof(Executor)); }

        var ValidationError=AuthoringHostProtocol.Validate(Request);
        if(ValidationError is not null){
            var State=Request.ProtocolVersion==AuthoringHostProtocol.CurrentVersion
                ?AuthoringHostTerminalState.Failed
                :AuthoringHostTerminalState.VersionMismatch;
            return Failure(Request,State,"HOST_REQUEST_INVALID",ValidationError);
        }

        object ProgressGate=new();
        AuthoringHostProgress?LastProgress=null;
        string?ProgressError=null;
        var Closed=false;
        void Report(AuthoringHostProgress Progress){
            lock(ProgressGate){
                if(Closed){ return; }
                var Error=ValidateProgress(Request,LastProgress,Progress);
                if(Error is not null){
                    ProgressError=Error;
                    return;
                }
                LastProgress=Progress;
            }
        }

        using var TimeoutCts=new CancellationTokenSource(Request.TimeoutMilliseconds);
        using var ExecutionCts=CancellationTokenSource.CreateLinkedTokenSource(CancellationToken,TimeoutCts.Token);
        try {
            var ExecutionTask=Executor(Request,Report,ExecutionCts.Token);
            var Execution=await ExecutionTask.WaitAsync(ExecutionCts.Token);
            lock(ProgressGate){ Closed=true; }
            if(ProgressError is not null){
                return Failure(Request,AuthoringHostTerminalState.Failed,"HOST_PROGRESS_INVALID",ProgressError,LastProgress);
            }
            if(Execution.ExitCode!=0){
                return Failure(Request,AuthoringHostTerminalState.Failed,"HOST_OPERATION_FAILED",Execution.Output,LastProgress,Execution.ExitCode);
            }
            var FinalProgress=CompleteProgress(Request,LastProgress);
            return new AuthoringHostResult {
                RequestId=Request.RequestId,
                State=AuthoringHostTerminalState.Succeeded,
                ExitCode=0,
                Output=Execution.Output??string.Empty,
                Progress=FinalProgress,
            };
        } catch(OperationCanceledException) when (CancellationToken.IsCancellationRequested){
            lock(ProgressGate){ Closed=true; }
            return Failure(Request,AuthoringHostTerminalState.Cancelled,"HOST_CANCELLED","The authoring request was cancelled.",LastProgress);
        } catch(OperationCanceledException) when (TimeoutCts.IsCancellationRequested){
            lock(ProgressGate){ Closed=true; }
            return Failure(Request,AuthoringHostTerminalState.TimedOut,"HOST_TIMED_OUT","The authoring host request timed out.",LastProgress);
        } catch(AuthoringHostCrashedException Ex){
            lock(ProgressGate){ Closed=true; }
            return Failure(Request,AuthoringHostTerminalState.HostCrashed,"HOST_CRASHED",Ex.Message,LastProgress);
        } catch(Exception Ex){
            lock(ProgressGate){ Closed=true; }
            return Failure(Request,AuthoringHostTerminalState.Failed,"HOST_EXCEPTION",Ex.Message,LastProgress);
        }
    }

    private static string?ValidateProgress(AuthoringHostRequest Request,AuthoringHostProgress?Previous,AuthoringHostProgress Progress){
        if(Progress is null){ return "Progress is required."; }
        if(Progress.ProtocolVersion!=AuthoringHostProtocol.CurrentVersion){ return "Progress protocol version does not match."; }
        if(Progress.RequestId!=Request.RequestId){ return "Progress request ID does not match."; }
        if(Progress.Sequence==0||(Previous is not null&&Progress.Sequence<=Previous.Sequence)){ return "Progress sequence must increase."; }
        if(!float.IsFinite(Progress.Fraction)||Progress.Fraction<0f||Progress.Fraction>1f){ return "Progress fraction must be finite and between zero and one."; }
        if(Previous is not null&&Progress.Fraction<Previous.Fraction){ return "Progress fraction must not decrease."; }
        if(string.IsNullOrWhiteSpace(Progress.Phase)){ return "Progress phase is required."; }
        return null;
    }

    private static AuthoringHostProgress CompleteProgress(AuthoringHostRequest Request,AuthoringHostProgress?Previous)=>new(){
        RequestId=Request.RequestId,
        Sequence=Previous is null?1:Previous.Sequence+1,
        Fraction=1f,
        Phase="Completed",
    };

    private static AuthoringHostResult Failure(
        AuthoringHostRequest Request,
        AuthoringHostTerminalState State,
        string Code,
        string Message,
        AuthoringHostProgress?Progress=null,
        int ExitCode=1)=>new(){
            RequestId=Request.RequestId,
            State=State,
            ExitCode=ExitCode,
            Output=Message??string.Empty,
            Progress=Progress,
            Diagnostics=[new AuthoringHostDiagnostic {
                Severity=AuthoringHostDiagnosticSeverity.Error,
                Code=Code,
                Message=Message??string.Empty,
            }],
        };
}
