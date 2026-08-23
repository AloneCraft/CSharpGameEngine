using System.Reflection;
using System.Runtime.Loader;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

if(args.Length==0){
    return;
}
string? assemblyPath=null;
string? sceneName=null;
for(var index=0;index<args.Length;index++){
    if(args[index]=="--assembly"&&index+1<args.Length) assemblyPath=args[++index];
    else if(args[index]=="--scene"&&index+1<args.Length) sceneName=args[++index];
}
if(string.IsNullOrWhiteSpace(assemblyPath)||!File.Exists(assemblyPath)){
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
using var cancellation=new CancellationTokenSource();
Console.CancelKeyPress+=(sender,eventArgs)=>{ eventArgs.Cancel=true; cancellation.Cancel(); };
var controlPipeName=$"CSharpGameEngine.EditorPreview.{Environment.ProcessId}";
var controlTask=Task.Run(async()=>{
    while(!cancellation.IsCancellationRequested){
        try{
            using var pipe=new NamedPipeServerStream(controlPipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(cancellation.Token);
            using var reader=new StreamReader(pipe,Encoding.UTF8,leaveOpen:true);
            using var writer=new StreamWriter(pipe,Encoding.UTF8,leaveOpen:true){AutoFlush=true};
            var command=await reader.ReadLineAsync(cancellation.Token);
            if(command=="shutdown"){
                cancellation.Cancel();
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
            if(!cancellation.IsCancellationRequested) await Task.Delay(100,cancellation.Token);
        }
    }
},cancellation.Token);
try{
    while(!cancellation.IsCancellationRequested){
        tickRealtime.Invoke(null,null);
        await Task.Delay(TimeSpan.FromMilliseconds(1000.0/15.0),cancellation.Token);
    }
}catch(OperationCanceledException){
}finally{
    try{ shutdown.Invoke(null,null); }catch{}
    cancellation.Cancel();
    try{ await controlTask; }catch{}
}
