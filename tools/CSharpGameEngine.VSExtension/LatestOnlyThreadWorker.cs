using System;
using System.Threading;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// 時間のかかる可能性がある処理を1本の通常のバックグラウンド <see cref="Thread"/> で実行し、最後に要求された結果だけを報告します。処理中に届いた要求は保留中の要求を置き換えます。これにより、ライブエディター解析が bridge プロセスを無制限に起動することを防ぎます。
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
        lock(this.Gate){
            if(this.Disposed){
                throw new ObjectDisposedException(nameof(LatestOnlyThreadWorker<TRequest,TResult>));
            }

            this.Pending=new WorkItem(++this.LatestVersion,Request);
            if(this.Running){
                return;
            }

            this.Running=true;
            Thread WorkerThread=new(this.Run){
                                                 IsBackground=true,
                                                 Name=$"{nameof(LatestOnlyThreadWorker<TRequest,TResult>)}<{typeof(TRequest).Name}>"
                                             };
            WorkerThread.Start();
        }
    }

    private void Run(){
        while(true){
            WorkItem Item;
            lock(this.Gate){
                if(this.Disposed|| this.Pending is null){
                    this.Running=false;
                    return;
                }

                Item= this.Pending.Value;
                this.Pending=null;
            }

            TResult?Result=null;
            Exception?Error=null;
            try {
                Result= this.Work(Item.Request);
            } catch(Exception Ex){
                Error=Ex;
            }

            try {
                this.Dispatch(()=> this.Complete(Item,Result,Error));
            } catch(InvalidOperationException){
                // Visual Studio のシャットダウン中、WPF Dispatcher がコールバックを拒否することがあります。
                // 結果を受け取る UI はもう存在しないため、破棄するのが正しい対応です。
            }
        }
    }

    private void Complete(WorkItem Item,TResult?Result,Exception?Error){
        lock(this.Gate){
            if(this.Disposed||Item.Version!= this.LatestVersion){
                return;
            }
        }

        this.Completed(Item.Request,Result,Error);
    }

    public void Dispose(){
        lock(this.Gate){
            if(this.Disposed){
                return;
            }

            this.Disposed=true;
            this.Pending=null;
            this.LatestVersion++;
        }

        // 意図的に Join しません。処理中の bridge はタイムアウトまで最大限時間がかかる可能性があり、Dispose は
        // WPF/VS のウィンドウ終了処理から呼ばれます。バックグラウンドスレッドは自然に終了し、完了通知は
        // 上記の Disposed/version チェックで拒否されます。
    }

    private readonly record struct WorkItem(long Version,TRequest Request);
}
