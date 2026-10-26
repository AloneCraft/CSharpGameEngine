namespace CSharpGameEngine.Rendering.Backend;
/// <summary>A provider failure with an explicit optional recovery classification; unknown failures do not request recovery.</summary>
public class RenderBackendFailureException:Exception {
    public RenderBackendFailureException(string Operation,int NativeCode,string Message,RenderRecoveryReason?RecoveryReason=null,Exception?InnerException=null)
        :base(Message,InnerException){
        this.Operation=Operation;
        this.HResult=NativeCode;
        this.RecoveryReason=RecoveryReason;
    }
    public string Operation { get; }
    public int NativeCode=>this.HResult;
    public RenderRecoveryReason?RecoveryReason { get; }
}
