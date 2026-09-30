using System.Diagnostics;

namespace CSharpGameEngine.Bridge.Hosting;

/// <summary>
/// Cancels a token when the process that started this one exits. The Visual Studio extension's
/// helper processes (the preview host, the resident bridges) run from the extension's install folder,
/// so one outliving its Visual Studio locks that folder against the next deployment; they end with it.
/// A parent that is already gone, or cannot be observed, cancels at once.
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

    /// <summary>Null (and nothing to watch) when <paramref name="ParentProcessId"/> is null.</summary>
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
