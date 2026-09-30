using CSharpGameEngine.Bridge.Hosting;
using CSharpGameEngine.Primitive.Cli;

if(BridgeServeCommand.TryParse(args,out var PipeName,out var OwnerProcessId)){
    await BridgeServerHost.RunAsync(PipeName,PrimitiveBridgeWorker.Run,TimeSpan.FromMinutes(15),OwnerProcessId:OwnerProcessId);
    return 0;
}

if(args.Length!=2){
    Console.Error.WriteLine("Usage: CSharpGameEngine.Primitive.Cli <PrimitiveName> <output.glb>");
    Console.Error.WriteLine("       CSharpGameEngine.Primitive.Cli --serve <pipeName>");
    return 2;
}

(var ExitCode,var Output)=PrimitiveBridgeWorker.Run(args[0],args[1]);
if(Output.Length>0){
    if(ExitCode==0){
        Console.WriteLine(Output);
    } else {
        Console.Error.WriteLine(Output);
    }
}

return ExitCode;
