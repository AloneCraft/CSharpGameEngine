using CSharpGameEngine.Content;
namespace CSharpGameEngine.ContentCompiler;

/// <summary>`Cge` の下位コマンドの入口。引数は下位コマンドの名前を除いた残り。</summary>
internal static class ContentCommand {
    internal static async Task<int> RunAsync(string[] Arguments){
        using var Stop=new CancellationTokenSource();
        ConsoleCancelEventHandler Cancel=(_,Event)=>{ Event.Cancel=true;Stop.Cancel(); };
        Console.CancelKeyPress+=Cancel;
        try {
            var ImportExit=await ModelImportCommands.TryRunAsync(Arguments,Stop.Token);
            if(ImportExit is{ }Exit){ return Exit; }
        } finally { Console.CancelKeyPress-=Cancel; }

        if(TileCommands.TryRun(Arguments) is{ }TileExit){ return TileExit; }
        if(Arguments.Length==3&&string.Equals(Arguments[0],"recover",StringComparison.Ordinal)){
            var Store=new ContentAddressedStore(Arguments[1]);
            var Compiler=new IncrementalContentCompiler(Store,Arguments[2]);
            Console.WriteLine(Compiler.RecoverInterruptedPublications());
            return 0;
        }
        Console.Error.WriteLine("Usage: Cge content recover <cas-root> <state-file>");
        Console.Error.WriteLine(TileCommands.Usage);
        Console.Error.WriteLine("       inspect <file.cgex> | explain <source> --cook <Cook.Cli.dll> --module <module.dll>");
        Console.Error.WriteLine("       import-ir <source> --project <directory> --cook <Cook.Cli.dll> --module <module.dll> --out <file.cgex> [--importer <identity>] [--asset <guid>] [--option <name=value>] [--verify]");
        return 2;
    }
}
