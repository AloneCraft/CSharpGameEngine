using CSharpGameEngine.Bridge.Hosting;
using CSharpGameEngine.Import.AssetAuthoring.Cli;

if(args is["--serve",var PipeName]){
    await BridgeServerHost.RunAsync(
        PipeName,AssetAuthoringBridgeWorker.Run,TimeSpan.FromMinutes(15));
    return 0;
}

if(args.Length!=2){
    Console.Error.WriteLine(
        "Usage: CSharpGameEngine.Import.AssetAuthoring.Cli <request.json> <result.json>");
    Console.Error.WriteLine(
        "       CSharpGameEngine.Import.AssetAuthoring.Cli --serve <pipeName>");
    return 2;
}

(var ExitCode,var Output)=AssetAuthoringBridgeWorker.Run(args[0],args[1]);
if(Output.Length>0){
    if(ExitCode==0){ Console.WriteLine(Output); }
    else { Console.Error.WriteLine(Output); }
}
return ExitCode;
