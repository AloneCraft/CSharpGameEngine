using CSharpGameEngine.Bridge.Hosting;
using CSharpGameEngine.SceneAuthoring.Cli;

if (args is ["--serve", var PipeName])
{
    await BridgeServerHost.RunAsync(PipeName, ScenePreviewBridgeWorker.Run, TimeSpan.FromMinutes(15));
    return 0;
}

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CSharpGameEngine.SceneAuthoring.Cli <request.json> <response.json>");
    Console.Error.WriteLine("       CSharpGameEngine.SceneAuthoring.Cli --serve <pipeName>");
    return 2;
}

(int ExitCode, string Output) = ScenePreviewBridgeWorker.Run(args[0], args[1]);
if (Output.Length > 0)
{
    if (ExitCode == 0)
    {
        Console.WriteLine(Output);
    }
    else
    {
        Console.Error.WriteLine(Output);
    }
}

return ExitCode;
