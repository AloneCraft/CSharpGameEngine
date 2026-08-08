using CSharpGameEngine.Bridge.Hosting;
using CSharpGameEngine.SceneAuthoring.Cli;

if (args is ["--serve", var pipeName])
{
    await BridgeServerHost.RunAsync(pipeName, ScenePreviewBridgeWorker.Run, TimeSpan.FromMinutes(15));
    return 0;
}

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CSharpGameEngine.SceneAuthoring.Cli <request.json> <response.json>");
    Console.Error.WriteLine("       CSharpGameEngine.SceneAuthoring.Cli --serve <pipeName>");
    return 2;
}

(int exitCode, string output) = ScenePreviewBridgeWorker.Run(args[0], args[1]);
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
