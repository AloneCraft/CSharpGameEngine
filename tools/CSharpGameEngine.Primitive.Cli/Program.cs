using CSharpGameEngine.Bridge.Hosting;
using CSharpGameEngine.Primitive.Cli;

if (args is ["--serve", var pipeName])
{
    await BridgeServerHost.RunAsync(pipeName, PrimitiveBridgeWorker.Run, TimeSpan.FromMinutes(15));
    return 0;
}

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CSharpGameEngine.Primitive.Cli <PrimitiveName> <output.glb>");
    Console.Error.WriteLine("       CSharpGameEngine.Primitive.Cli --serve <pipeName>");
    return 2;
}

(int exitCode, string output) = PrimitiveBridgeWorker.Run(args[0], args[1]);
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
