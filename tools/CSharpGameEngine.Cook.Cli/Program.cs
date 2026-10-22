using CSharpGameEngine.Cook.Cli;

// ADR-0079: runs a plugin's Cook module outside Visual Studio and the game.
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
    var Outputs=CookerHost.Cook(Selected,args[2],args[3],Project??Path.GetDirectoryName(Path.GetFullPath(args[2]))!,Verify);
    foreach(var Output in Outputs){ Console.WriteLine(Output); }
    return 0;
} catch(Exception Error) when(Error is IOException or InvalidOperationException or BadImageFormatException or UnauthorizedAccessException){
    Console.Error.WriteLine(Error.Message);
    return 1;
}
