using System;
using System.Threading;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Runs potentially blocking work on one raw background <see cref="Thread"/> and reports only the
/// newest requested result. Requests arriving while work is in flight replace the pending request,
/// which prevents live editor analysis from starting an unbounded number of bridge processes.
/// </summary>
internal sealed class LatestOnlyThreadWorker<TRequest,TResult>:IDisposable where TResult:class {
    private readonly object Gate=new();
    private readonly Func<TRequest,TResult> Work;
    private readonly Action<Action> Dispatch;
    private readonly Action<TRequest,TResult?,Exception?>Completed;
    private WorkItem?Pending;
    private long LatestVersion;
    private bool Running;
    private bool Disposed;

    internal LatestOnlyThreadWorker(
        Func<TRequest,TResult> Work,
        Action<Action>Dispatch,
        Action<TRequest,TResult?,Exception?>Completed){
        this.Work=Work??throw new ArgumentNullException(nameof(Work));
        this.Dispatch=Dispatch??throw new ArgumentNullException(nameof(Dispatch));
        this.Completed=Completed??throw new ArgumentNullException(nameof(Completed));
    }

    internal void Request(TRequest Request){
        lock(Gate){
            if(Disposed){
                throw new ObjectDisposedException(nameof(LatestOnlyThreadWorker<TRequest,TResult>));
            }

            Pending=new WorkItem(++LatestVersion,Request);
            if(Running){
                return;
            }

            Running=true;
            Thread WorkerThread=new(Run){
                IsBackground=true,
                Name=$"{nameof(LatestOnlyThreadWorker<TRequest,TResult>)}<{typeof(TRequest).Name}>"
            };
            WorkerThread.Start();
        }
    }

    private void Run(){
        while(true){
            WorkItem Item;
            lock(Gate){
                if(Disposed||Pending is null){
                    Running=false;
                    return;
                }

                Item=Pending.Value;
                Pending=null;
            }

            TResult?Result=null;
            Exception?Error=null;
            try {
                Result=Work(Item.Request);
            } catch(Exception Ex){
                Error=Ex;
            }

            try {
                Dispatch(()=>Complete(Item,Result,Error));
            } catch(InvalidOperationException){
                // The WPF Dispatcher can reject a callback while Visual Studio is shutting down.
                // There is no UI left to receive this result, so dropping it is the correct action.
            }
        }
    }

    private void Complete(WorkItem Item,TResult?Result,Exception?Error){
        lock(Gate){
            if(Disposed||Item.Version!=LatestVersion){
                return;
            }
        }

        Completed(Item.Request,Result,Error);
    }

    public void Dispose(){
        lock(Gate){
            if(Disposed){
                return;
            }

            Disposed=true;
            Pending=null;
            LatestVersion++;
        }

        // Deliberately do not Join: an in-flight bridge may take up to its timeout, and Dispose is
        // called from WPF/VS window teardown. The background thread will finish naturally and its
        // completion is rejected by the Disposed/version checks above.
    }

    private readonly record struct WorkItem(long Version,TRequest Request);
}
