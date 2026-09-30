using System.Reflection;
using System.Runtime.Loader;
using System.IO.Pipes;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
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

string? renderServerPipe=ReadOption(args,"--render-server");
if(renderServerPipe is not null){
    try{
        await RenderPreviewHostServer.RunAsync(renderServerPipe,hostCancellation.Token);
    }catch(OperationCanceledException) when(hostCancellation.IsCancellationRequested){
    }
    return;
}

if(args.Length==0){
    return;
}
string? assemblyPath=ReadOption(args,"--assembly");
string? sceneName=ReadOption(args,"--scene");
if(string.IsNullOrWhiteSpace(assemblyPath)||!File.Exists(assemblyPath)){
    return;
}
if(hostCancellation.IsCancellationRequested){
    return;
}
AssemblyLoadContext.Default.Resolving+=(context,name)=>{
    string candidate=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(assemblyPath))??string.Empty,$"{name.Name}.dll");
    return File.Exists(candidate)?context.LoadFromAssemblyPath(candidate):null;
};
Assembly assembly;
try{
    assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
}catch{
    return;
}
var bridgeType=assembly.GetType("CSharpGameEngine.Generated.GeneratedEditorPreviewBridge",throwOnError:false);
var initialize=bridgeType?.GetMethod("Initialize",BindingFlags.Static|BindingFlags.NonPublic);
var tickChange=bridgeType?.GetMethod("TickOnChange",BindingFlags.Static|BindingFlags.NonPublic);
var tickRealtime=bridgeType?.GetMethod("TickRealtime",BindingFlags.Static|BindingFlags.NonPublic);
var shutdown=bridgeType?.GetMethod("Shutdown",BindingFlags.Static|BindingFlags.NonPublic);
if(initialize is null||tickChange is null||tickRealtime is null||shutdown is null){
    return;
}
try{
    if(initialize.Invoke(null,[sceneName]) is not true){
        return;
    }
}catch{
    return;
}
var controlPipeName=$"CSharpGameEngine.EditorPreview.{Environment.ProcessId}";
var controlTask=Task.Run(async()=>{
    while(!hostCancellation.IsCancellationRequested){
        try{
            using var pipe=new NamedPipeServerStream(controlPipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(hostCancellation.Token);
            using var reader=new StreamReader(pipe,Encoding.UTF8,leaveOpen:true);
            using var writer=new StreamWriter(pipe,Encoding.UTF8,leaveOpen:true){AutoFlush=true};
            var command=await reader.ReadLineAsync(hostCancellation.Token);
            if(command=="shutdown"){
                hostCancellation.Cancel();
                await writer.WriteLineAsync("ok");
            }else if(command=="tick"){
                tickChange.Invoke(null,null);
                await writer.WriteLineAsync("ok");
            }else if(command=="ping"){
                await writer.WriteLineAsync("ready");
            }else{
                await writer.WriteLineAsync("ignored");
            }
        }catch(OperationCanceledException){
            break;
        }catch{
            if(!hostCancellation.IsCancellationRequested) await Task.Delay(100,hostCancellation.Token);
        }
    }
},hostCancellation.Token);
try{
    while(!hostCancellation.IsCancellationRequested){
        tickRealtime.Invoke(null,null);
        await Task.Delay(TimeSpan.FromTicks(TimeSpan.TicksPerSecond/15),hostCancellation.Token);
    }
}catch(OperationCanceledException){
}finally{
    try{ shutdown.Invoke(null,null); }catch{}
    hostCancellation.Cancel();
    try{ await controlTask; }catch{}
}

static string? ReadOption(string[] Arguments,string Name){
    for(int Index=0;Index+1<Arguments.Length;Index++){
        if(string.Equals(Arguments[Index],Name,StringComparison.Ordinal)){
            return Arguments[Index+1];
        }
    }
    return null;
}
