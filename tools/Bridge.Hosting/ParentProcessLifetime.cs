using System.Diagnostics;

namespace CSharpGameEngine.Bridge.Hosting;

/// <summary>
/// このプロセスを起動したプロセスが終了したときにトークンをキャンセルする。Visual Studio 拡張機能のヘルパープロセス（プレビューホスト、常駐ブリッジ）は拡張機能のインストールフォルダーから実行されるため、Visual Studio 終了後もプロセスが残ると、次回配置時にフォルダーがロックされる。親とともに終了させる。
/// 親がすでに終了しているか、状態を監視できない場合は直ちにキャンセルする。
/// </summary>
public sealed class ParentProcessLifetime:IDisposable {
    private readonly Process Parent;
    private readonly CancellationTokenSource Cancellation;
    private bool Disposed;

    private ParentProcessLifetime(Process Parent,CancellationTokenSource Cancellation){
        this.Parent=Parent;
        this.Cancellation=Cancellation;
        this.Parent.EnableRaisingEvents=true;
        this.Parent.Exited+=this.OnParentExited;
        if(this.Parent.HasExited){
            this.Cancellation.Cancel();
        }
    }

    /// <summary><paramref name="ParentProcessId"/> が null の場合は null（監視対象なし）。</summary>
    public static ParentProcessLifetime?Attach(int?ParentProcessId,CancellationTokenSource Cancellation){
        ArgumentNullException.ThrowIfNull(Cancellation);
        if(ParentProcessId is null){
            return null;
        }
        Process?Parent=null;
        try{
            Parent=Process.GetProcessById(ParentProcessId.Value);
            return new ParentProcessLifetime(Parent,Cancellation);
        }catch(Exception Error)when(Error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){
            Parent?.Dispose();
            Cancellation.Cancel();
            return null;
        }
    }

    public void Dispose(){
        if(this.Disposed){
            return;
        }
        this.Disposed=true;
        this.Parent.Exited-=this.OnParentExited;
        this.Parent.Dispose();
    }

    private void OnParentExited(object?Sender,EventArgs Args)=>this.Cancellation.Cancel();
}
