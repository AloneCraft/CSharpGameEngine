using CSharpGameEngine.Bridge.Hosting;
namespace CSharpGameEngine.Import.AssetAuthoring.Cli;

/// <summary>`Cge` の下位コマンドの入口。引数は下位コマンドの名前を除いた残り。</summary>
internal static class AssetAuthoringBridgeCommand {
    internal static async Task<int> RunAsync(string[] Arguments){
        if(ImageModelCommands.Run(Arguments) is { }AuthoringExit){ return AuthoringExit; }

        if(BridgeServeCommand.TryParse(Arguments,out var PipeName,out var OwnerProcessId)){
            await BridgeServerHost.RunArgumentsAsync(
                PipeName,(Arguments,Cancellation)=>Arguments.Length==2?AssetAuthoringBridgeWorker.Run(Arguments[0],Arguments[1],Cancellation):(2,"Two request paths are required."),TimeSpan.FromMinutes(15),OwnerProcessId:OwnerProcessId);
            return 0;
        }

        if(Arguments.Length!=2){
            Console.Error.WriteLine(
                "Usage: Cge asset-authoring <request.json> <result.json>");
            Console.Error.WriteLine(
                "       Cge asset-authoring --serve <pipeName>");
            return 2;
        }

        (var ExitCode,var Output)=AssetAuthoringBridgeWorker.Run(Arguments[0],Arguments[1]);
        if(Output.Length>0){
            if(ExitCode==0){ Console.WriteLine(Output); }
            else { Console.Error.WriteLine(Output); }
        }
        return ExitCode;
    }
}
