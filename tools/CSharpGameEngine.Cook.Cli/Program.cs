using CSharpGameEngine.Cook.Cli;
if(args.Length==3&&args[0]=="thumbnail-image"){
    try { ImageThumbnailCommand.Run(args[1],args[2]);return 0; }
    catch(Exception Error){ Console.Error.WriteLine(Error.Message);return 1; }
}
if(args.Length>=2&&args[0] is "describe" or "import-worker"){
    try {
        if(args[0]=="describe"&&args.Length==2){ Console.WriteLine(ImportWorker.Describe(args[1]));return 0; }
        if(args[0]=="import-worker"&&args.Length==4){
            using var Stop=new CancellationTokenSource();
            ConsoleCancelEventHandler Cancel=(_,Event)=>{ Event.Cancel=true;Stop.Cancel(); };
            Console.CancelKeyPress+=Cancel;
            if(Console.IsInputRedirected){
                _=Task.Run(async()=>{
                    try { if(await Console.In.ReadLineAsync(Stop.Token)=="cancel"){ Stop.Cancel(); } }
                    catch(Exception Error) when(Error is OperationCanceledException or ObjectDisposedException or IOException){ }
                });
            }
            try { return await ImportWorker.RunAsync(args[1],args[2],args[3],Stop.Token); }
            finally { Console.CancelKeyPress-=Cancel; }
        }
        Console.Error.WriteLine("Usage: describe <module.dll> | import-worker <module.dll> <request.json> <result.json>");
        return 2;
    } catch(Exception Error) when(Error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or BadImageFormatException){
        Console.Error.WriteLine(Error.Message);return 1;
    }
}

// ADR-0079：プラグインの Cook モジュールを Visual Studio とゲームの外で実行する。
//   list <cook-module.dll>
//   cook <cook-module.dll> <input> <output-directory> [--project <directory>] [--cooker <name>] [--verify]
if(args.Length<2||args[0] is not ("list" or "cook")){
    Console.Error.WriteLine("Usage: CSharpGameEngine.Cook.Cli list <cook-module.dll>");
    Console.Error.WriteLine("       CSharpGameEngine.Cook.Cli cook <cook-module.dll> <input> <output-directory> [--project <directory>] [--cooker <name>] [--verify]");
    return 2;
}
try {
    using var Host=CookerHost.Load(args[1]);
    if(args[0]=="list"){
        foreach(var Cooker in Host.Cookers){ Console.WriteLine($"{Cooker.Name}\t{string.Join(" ",Cooker.InputExtensions)}"); }
        return 0;
    }
    if(args.Length<4){
        Console.Error.WriteLine("cook needs <cook-module.dll> <input> <output-directory>.");
        return 2;
    }
    string?Project=null;
    string?Name=null;
    var Verify=false;
    for(var Index=4;Index<args.Length;Index++){
        switch(args[Index]){
            case "--project" when Index+1<args.Length:Project=args[++Index];break;
            case "--cooker" when Index+1<args.Length:Name=args[++Index];break;
            case "--verify":Verify=true;break;
            default:
                Console.Error.WriteLine($"Unknown option: {args[Index]}");
                return 2;
        }
    }
    var Selected=Host.Select(args[2],Name);
    List<string>Messages=[];
    var Outputs=CookerHost.Cook(Selected,args[2],args[3],Project??Path.GetDirectoryName(Path.GetFullPath(args[2]))!,Verify,Messages);
    // クッカー自身のメモ（警告、統計）は標準エラーへ出す。標準出力にはファイル一覧だけを出す。
    foreach(var Message in Messages){ Console.Error.WriteLine(Message); }
    foreach(var Output in Outputs){ Console.WriteLine(Output); }
    return 0;
} catch(CookerHost.CookFailedException Failure){
    foreach(var Message in Failure.Messages){ Console.Error.WriteLine(Message); }
    return 1;
} catch(Exception Error) when(Error is IOException or InvalidOperationException or BadImageFormatException or UnauthorizedAccessException){
    Console.Error.WriteLine(Error.Message);
    return 1;
}
