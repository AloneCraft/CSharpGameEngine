using CSharpGameEngine.FontBaker;

// ADR-0078/M0104：CSharpGameEngine.FontBaker <font.ttf> <output.cgefont> [--size 64] [--spread 8] [--supersample 4] [--chars <text>]
if(args.Length<2||args[0].StartsWith("--",StringComparison.Ordinal)){
    Console.Error.WriteLine("Usage: CSharpGameEngine.FontBaker <font.ttf> <output.cgefont> [--size 64] [--spread 8] [--supersample 4] [--chars <text>]");
    Console.Error.WriteLine("  --chars: the characters to bake instead of ASCII, Latin-1 and common punctuation.");
    return 2;
}
int Size=FontBaking.DefaultSize;
int Spread=FontBaking.DefaultSpread;
int Supersample=FontBaking.DefaultSupersample;
IReadOnlyList<int>CodePoints=FontBaking.DefaultCodePoints();
for(int Index=2;Index<args.Length;Index++){
    if(Index+1>=args.Length){
        Console.Error.WriteLine($"Missing value for {args[Index]}.");
        return 2;
    }
    string Value=args[Index+1];
    switch(args[Index]){
        case "--size" when int.TryParse(Value,out int Parsed)&&Parsed>=8:Size=Parsed;break;
        case "--spread" when int.TryParse(Value,out int Parsed)&&Parsed>=1:Spread=Parsed;break;
        case "--supersample" when int.TryParse(Value,out int Parsed)&&Parsed>=1:Supersample=Parsed;break;
        case "--chars" when Value.Length>0:CodePoints=[.. Value.EnumerateRunes().Select(static Rune=>Rune.Value),0x20];break;
        default:
            Console.Error.WriteLine($"Unknown or invalid option: {args[Index]} {Value}");
            return 2;
    }
    Index++;
}
try {
    var Font=FontBaking.Bake(args[0],CodePoints,Size,Spread,Supersample);
    byte[]Bytes=Font.ToBytes();
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    File.WriteAllBytes(args[1],Bytes);
    Console.WriteLine($"{args[1]}: {Font.Glyphs.Count} glyphs, atlas {Font.AtlasWidth}x{Font.AtlasHeight}, {Bytes.Length} bytes");
    return 0;
} catch(Exception Error) when(Error is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or OutOfMemoryException){
    Console.Error.WriteLine(Error.Message);
    return 1;
}
