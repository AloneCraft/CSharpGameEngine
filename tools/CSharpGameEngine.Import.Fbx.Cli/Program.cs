using CSharpGameEngine.Bridge.Hosting;
using CSharpGameEngine.Import.Fbx.Cli;

if (args is ["--serve", var pipeName])
{
    await BridgeServerHost.RunAsync(pipeName, FbxBridgeWorker.Run, TimeSpan.FromMinutes(15));
    return 0;
}

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CSharpGameEngine.Import.Fbx.Cli <input.fbx> <output.json>");
    Console.Error.WriteLine("       CSharpGameEngine.Import.Fbx.Cli --serve <pipeName>");
    return 2;
}

(int exitCode, string output) = FbxBridgeWorker.Run(args[0], args[1]);
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
