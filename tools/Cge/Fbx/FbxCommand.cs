using CSharpGameEngine.Bridge.Hosting;
namespace CSharpGameEngine.Import.Fbx.Cli;

/// <summary>`Cge` の下位コマンドの入口。引数は下位コマンドの名前を除いた残り。</summary>
internal static class FbxCommand {
    internal static async Task<int> RunAsync(string[] Arguments){
        if(BridgeServeCommand.TryParse(Arguments,out var PipeName,out var OwnerProcessId)){
            await BridgeServerHost.RunAsync(PipeName,FbxBridgeWorker.Run,TimeSpan.FromMinutes(15),OwnerProcessId:OwnerProcessId);
            return 0;
        }

        if(Arguments.Length!=2){
            Console.Error.WriteLine("Usage: Cge fbx <input.fbx> <output.json>");
            Console.Error.WriteLine("       Cge fbx --serve <pipeName>");
            return 2;
        }

        (var ExitCode,var Output)=FbxBridgeWorker.Run(Arguments[0],Arguments[1]);
        if(Output.Length>0){
            if(ExitCode==0){
                Console.WriteLine(Output);
            } else {
                Console.Error.WriteLine(Output);
            }
        }

        return ExitCode;
    }
}
