using CSharpGameEngine.Bridge.Hosting;
using CSharpGameEngine.Import.Fbx.Cli;

if(BridgeServeCommand.TryParse(args,out var PipeName,out var OwnerProcessId)){
    await BridgeServerHost.RunAsync(PipeName,FbxBridgeWorker.Run,TimeSpan.FromMinutes(15),OwnerProcessId:OwnerProcessId);
    return 0;
}

if(args.Length!=2){
    Console.Error.WriteLine("Usage: CSharpGameEngine.Import.Fbx.Cli <input.fbx> <output.json>");
    Console.Error.WriteLine("       CSharpGameEngine.Import.Fbx.Cli --serve <pipeName>");
    return 2;
}

(var ExitCode,var Output)=FbxBridgeWorker.Run(args[0],args[1]);
if(Output.Length>0){
    if(ExitCode==0){
        Console.WriteLine(Output);
    } else {
        Console.Error.WriteLine(Output);
    }
}

return ExitCode;
