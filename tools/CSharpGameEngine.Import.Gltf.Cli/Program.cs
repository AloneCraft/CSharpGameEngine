using CSharpGameEngine.Bridge.Hosting;
using CSharpGameEngine.Import.Gltf.Cli;

if(args is["--serve", var PipeName]){
    await BridgeServerHost.RunAsync(PipeName,GltfBridgeWorker.Run,TimeSpan.FromMinutes(15));
    return 0;
}

if(args.Length!=2){
    Console.Error.WriteLine("Usage: CSharpGameEngine.Import.Gltf.Cli <input.glb|input.gltf> <output.json>");
    Console.Error.WriteLine("       CSharpGameEngine.Import.Gltf.Cli --serve <pipeName>");
    return 2;
}

(var ExitCode,var Output)=GltfBridgeWorker.Run(args[0],args[1]);
if(Output.Length>0){
    if(ExitCode==0){
        Console.WriteLine(Output);
    } else {
        Console.Error.WriteLine(Output);
    }
}

return ExitCode;
