using CSharpGameEngine.Bridge.Hosting;
using CSharpGameEngine.Import.Gltf.Cli;

if (args is ["--serve", var pipeName])
{
    await BridgeServerHost.RunAsync(pipeName, GltfBridgeWorker.Run, TimeSpan.FromMinutes(15));
    return 0;
}

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CSharpGameEngine.Import.Gltf.Cli <input.glb|input.gltf> <output.json>");
    Console.Error.WriteLine("       CSharpGameEngine.Import.Gltf.Cli --serve <pipeName>");
    return 2;
}

(int exitCode, string output) = GltfBridgeWorker.Run(args[0], args[1]);
if (output.Length > 0)
{
    if (exitCode == 0)
    {
        Console.WriteLine(output);
    }
    else
    {
        Console.Error.WriteLine(output);
    }
}

return exitCode;
