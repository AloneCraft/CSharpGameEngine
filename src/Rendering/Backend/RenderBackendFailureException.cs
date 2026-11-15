namespace CSharpGameEngine.Rendering.Backend;
/// <summary>回復方法の分類を明示したプロバイダー障害。未知の障害は回復を要求しない。</summary>
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
