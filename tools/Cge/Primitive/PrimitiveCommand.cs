using CSharpGameEngine.Bridge.Hosting;
namespace CSharpGameEngine.Primitive.Cli;

/// <summary>`Cge` の下位コマンドの入口。引数は下位コマンドの名前を除いた残り。</summary>
internal static class PrimitiveCommand {
    internal static async Task<int> RunAsync(string[] Arguments){
        if(BridgeServeCommand.TryParse(Arguments,out var PipeName,out var OwnerProcessId)){
            await BridgeServerHost.RunAsync(PipeName,PrimitiveBridgeWorker.Run,TimeSpan.FromMinutes(15),OwnerProcessId:OwnerProcessId);
            return 0;
        }

        if(Arguments.Length!=2){
            Console.Error.WriteLine("Usage: Cge primitive <PrimitiveName> <output.glb>");
            Console.Error.WriteLine("       Cge primitive --serve <pipeName>");
            return 2;
        }

        (var ExitCode,var Output)=PrimitiveBridgeWorker.Run(Arguments[0],Arguments[1]);
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
