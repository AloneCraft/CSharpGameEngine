using System;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpGameEngine.VSExtension;

internal static class SceneSubsystemStartupRetry {
    internal static Task<bool>WaitAsync(
        Func<Task<bool>>TryInitialize,int MaximumAttempts,TimeSpan Delay,
        CancellationToken CancellationToken){
        return WaitAsync(
            TryInitialize,MaximumAttempts,Delay,Task.Delay,CancellationToken);
    }

    internal static async Task<bool>WaitAsync(
        Func<Task<bool>>TryInitialize,int MaximumAttempts,TimeSpan Delay,
        Func<TimeSpan,CancellationToken,Task>Wait,
        CancellationToken CancellationToken){
        if(TryInitialize is null){
            throw new ArgumentNullException(nameof(TryInitialize));
        }
        if(Wait is null){
            throw new ArgumentNullException(nameof(Wait));
        }
        if(MaximumAttempts<=0){
            throw new ArgumentOutOfRangeException(nameof(MaximumAttempts));
        }
        if(Delay<TimeSpan.Zero){
            throw new ArgumentOutOfRangeException(nameof(Delay));
        }
        for(var Attempt=0;Attempt<MaximumAttempts;Attempt++){
            CancellationToken.ThrowIfCancellationRequested();
            if(await TryInitialize()){
                return true;
            }
            if(Attempt+1<MaximumAttempts){
                await Wait(Delay,CancellationToken);
            }
        }
        return false;
    }
}
