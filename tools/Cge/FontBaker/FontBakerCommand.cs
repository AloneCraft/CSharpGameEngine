namespace CSharpGameEngine.FontBaker;

/// <summary>`Cge` の下位コマンドの入口。引数は下位コマンドの名前を除いた残り。</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
internal static class FontBakerCommand {
    internal static int Run(string[] Arguments){
        // ADR-0078/M0104：Cge font <font.ttf> <output.cgefont> [--size 64] [--spread 8] [--supersample 4] [--chars <text>]
        if(Arguments.Length<2||Arguments[0].StartsWith("--",StringComparison.Ordinal)){
            Console.Error.WriteLine("Usage: Cge font <font.ttf> <output.cgefont> [--size 64] [--spread 8] [--supersample 4] [--chars <text>]");
            Console.Error.WriteLine("  --chars: the characters to bake instead of ASCII, Latin-1 and common punctuation.");
            return 2;
        }
        int Size=FontBaking.DefaultSize;
        int Spread=FontBaking.DefaultSpread;
        int Supersample=FontBaking.DefaultSupersample;
        IReadOnlyList<int>CodePoints=FontBaking.DefaultCodePoints();
        for(int Index=2;Index<Arguments.Length;Index++){
            if(Index+1>=Arguments.Length){
                Console.Error.WriteLine($"Missing value for {Arguments[Index]}.");
                return 2;
            }
            string Value=Arguments[Index+1];
            switch(Arguments[Index]){
                case "--size" when int.TryParse(Value,out int Parsed)&&Parsed>=8:Size=Parsed;break;
                case "--spread" when int.TryParse(Value,out int Parsed)&&Parsed>=1:Spread=Parsed;break;
                case "--supersample" when int.TryParse(Value,out int Parsed)&&Parsed>=1:Supersample=Parsed;break;
                case "--chars" when Value.Length>0:CodePoints=[.. Value.EnumerateRunes().Select(static Rune=>Rune.Value),0x20];break;
                default:
                    Console.Error.WriteLine($"Unknown or invalid option: {Arguments[Index]} {Value}");
                    return 2;
            }
            Index++;
        }
        try {
            var Font=FontBaking.Bake(Arguments[0],CodePoints,Size,Spread,Supersample);
            byte[]Bytes=Font.ToBytes();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Arguments[1]))!);
            File.WriteAllBytes(Arguments[1],Bytes);
            Console.WriteLine($"{Arguments[1]}: {Font.Glyphs.Count} glyphs, atlas {Font.AtlasWidth}x{Font.AtlasHeight}, {Bytes.Length} bytes");
            return 0;
        } catch(Exception Error) when(Error is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or OutOfMemoryException){
            Console.Error.WriteLine(Error.Message);
            return 1;
        }
    }
}
