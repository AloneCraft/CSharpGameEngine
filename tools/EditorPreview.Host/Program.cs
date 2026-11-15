using System.Threading;
using CSharpGameEngine.Bridge.Hosting;
using CSharpGameEngine.EditorPreview.Host;

string? parentProcessText=ReadOption(args,"--parent-process-id");
int parsedParentProcessId=0;
if(parentProcessText is not null
    &&(!int.TryParse(parentProcessText,out parsedParentProcessId)
        ||parsedParentProcessId<=0)){
    return;
}
int? parentProcessId=parentProcessText is null?null:parsedParentProcessId;
using var hostCancellation=new CancellationTokenSource();
using ParentProcessLifetime? parentLifetime=ParentProcessLifetime.Attach(
    parentProcessId,hostCancellation);
Console.CancelKeyPress+=(sender,eventArgs)=>{
    eventArgs.Cancel=true;
    hostCancellation.Cancel();
};

if(args.Length==3&&args[0]=="--thumbnail"){
    try { AssetThumbnailWorker.Run(args[1],args[2]); }
    catch(Exception Error){ Console.Error.WriteLine(Error.Message);Environment.ExitCode=1; }
    return;
}
string? renderServerPipe=ReadOption(args,"--render-server");
if(renderServerPipe is not null){
    try{
        await RenderPreviewHostServer.RunAsync(renderServerPipe,hostCancellation.Token);
    }catch(OperationCanceledException) when(hostCancellation.IsCancellationRequested){
    }
    return;
}

// M0096 (ADR-0047)：唯一のモードはレンダーサーバーであり、エディターワールドもそこで実行する。
Console.Error.WriteLine("usage: CSharpGameEngine.EditorPreview.Host --render-server <pipe> [--parent-process-id <pid>]");

static string? ReadOption(string[] Arguments,string Name){
    for(int Index=0;Index+1<Arguments.Length;Index++){
        if(string.Equals(Arguments[Index],Name,StringComparison.Ordinal)){
            return Arguments[Index+1];
        }
    }
    return null;
}
