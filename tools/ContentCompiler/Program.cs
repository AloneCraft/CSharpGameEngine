using CSharpGameEngine.Content;
using CSharpGameEngine.ContentCompiler;
using var Stop=new CancellationTokenSource();
ConsoleCancelEventHandler Cancel=(_,Event)=>{ Event.Cancel=true;Stop.Cancel(); };
Console.CancelKeyPress+=Cancel;
try {
    var ImportExit=await ModelImportCommands.TryRunAsync(args,Stop.Token);
    if(ImportExit is{ }Exit){ return Exit; }
} finally { Console.CancelKeyPress-=Cancel; }

if(args.Length==3&&string.Equals(args[0],"recover",StringComparison.Ordinal)){
    var Store=new ContentAddressedStore(args[1]);
    var Compiler=new IncrementalContentCompiler(Store,args[2]);
    Console.WriteLine(Compiler.RecoverInterruptedPublications());
    return 0;
}
Console.Error.WriteLine("Usage: CSharpGameEngine.ContentCompiler recover <cas-root> <state-file>");
Console.Error.WriteLine("       inspect <file.cgex> | explain <source> --cook <Cook.Cli.dll> --module <module.dll>");
Console.Error.WriteLine("       import-ir <source> --project <directory> --cook <Cook.Cli.dll> --module <module.dll> --out <file.cgex> [--importer <identity>] [--asset <guid>] [--option <name=value>] [--verify]");
return 2;
