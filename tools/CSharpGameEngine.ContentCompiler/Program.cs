using CSharpGameEngine.Content;
using CSharpGameEngine.ContentCompiler;

if(args.Length==3&&string.Equals(args[0],"recover",StringComparison.Ordinal)){
    var Store=new ContentAddressedStore(args[1]);
    var Compiler=new IncrementalContentCompiler(Store,args[2]);
    Console.WriteLine(Compiler.RecoverInterruptedPublications());
    return 0;
}
Console.Error.WriteLine("Usage: CSharpGameEngine.ContentCompiler recover <cas-root> <state-file>");
return 2;
