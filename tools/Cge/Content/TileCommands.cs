using System.Globalization;
using CSharpGameEngine.Content.Tiles;
using CSharpGameEngine.Import.Images;
namespace CSharpGameEngine.ContentCompiler;
/// <summary>
/// <c>Cge content compile-tiles</c>（ADR-0100 決定 7、M0134）。大きな画像（TIFF、PDS の IMG、ラベルの無い生の画像）を、少しずつ読んでタイルの階層にする。
/// </summary>
internal static class TileCommands {
    internal const string Usage="       compile-tiles <input> <output.cgetiles> [--raw <width>x<height>:<int16|uint16|uint8|int32|uint32|float32>:<le|be>[:<channels>]] [--scale <s>] [--offset <o>]\n"
        +"                     [--tile <size>] [--encoding <float32|unorm16|unorm8>] [--projection <equirectangular|planar>] [--bounds <west>,<east>,<north>,<south>]";
    /// <summary>下位コマンドが compile-tiles なら実行して終わりの値を返す。違えば null。</summary>
    internal static int?TryRun(string[]Arguments){
        if(Arguments.Length<3||!string.Equals(Arguments[0],"compile-tiles",StringComparison.Ordinal)){ return null; }
        try {
            var Input=Arguments[1];
            var Output=Arguments[2];
            string?Raw=null;
            var Scale=1f;
            var Offset=0f;
            var Options=new TilePyramidCompileOptions();
            for(var Index=3;Index<Arguments.Length;Index++){
                var Value=Index+1<Arguments.Length?Arguments[Index+1]:throw new ArgumentException($"'{Arguments[Index]}' needs a value.");
                switch(Arguments[Index]){
                    case "--raw":Raw=Value;break;
                    case "--scale":Scale=float.Parse(Value,CultureInfo.InvariantCulture);break;
                    case "--offset":Offset=float.Parse(Value,CultureInfo.InvariantCulture);break;
                    case "--tile":Options=Options with { TileSize=int.Parse(Value,CultureInfo.InvariantCulture) };break;
                    case "--encoding":Options=Options with { Encoding=Value switch { "float32"=>TileSampleEncoding.Float32,"unorm16"=>TileSampleEncoding.UNorm16,"unorm8"=>TileSampleEncoding.UNorm8,_=>throw new ArgumentException($"Unknown encoding '{Value}'.") } };break;
                    case "--projection":Options=Options with { Projection=Value switch { "equirectangular"=>TileProjection.Equirectangular,"planar"=>TileProjection.Planar,_=>throw new ArgumentException($"Unknown projection '{Value}'.") } };break;
                    case "--bounds":{
                        var Parts=Value.Split(',').Select(Part=>float.Parse(Part,CultureInfo.InvariantCulture)).ToArray();
                        if(Parts.Length!=4){ throw new ArgumentException("--bounds needs west,east,north,south."); }
                        Options=Options with { West=Parts[0],East=Parts[1],North=Parts[2],South=Parts[3] };
                        break;
                    }
                    default:throw new ArgumentException($"Unknown option '{Arguments[Index]}'.");
                }
                Index++;
            }
            using IRasterSource Source=Open(Input,Raw,Scale,Offset);
            var Watch=System.Diagnostics.Stopwatch.StartNew();
            var Report=TilePyramidCompiler.Compile(Source,Output,Options);
            Console.WriteLine($"{Source.Width}x{Source.Height}x{Source.Channels} -> {Report.Levels} levels, {Report.Tiles} tiles, {Report.PackBytes} pack bytes in {Watch.ElapsedMilliseconds} ms");
            return 0;
        } catch(Exception Exception) when(Exception is ArgumentException or InvalidDataException or NotSupportedException or IOException or FormatException) {
            Console.Error.WriteLine(Exception.Message);
            return 1;
        }
    }
    private static IRasterSource Open(string Input,string?Raw,float Scale,float Offset){
        if(Raw is not null){
            var Parts=Raw.Split(':');
            var Size=Parts[0].Split('x');
            if(Parts.Length<3||Size.Length!=2){ throw new ArgumentException("--raw needs <width>x<height>:<type>:<le|be>[:<channels>]."); }
            var Type=Parts[1] switch {
                "int16"=>RasterSampleType.Int16,"uint16"=>RasterSampleType.UInt16,"uint8"=>RasterSampleType.UInt8,
                "int32"=>RasterSampleType.Int32,"uint32"=>RasterSampleType.UInt32,"float32"=>RasterSampleType.Float32,
                _=>throw new ArgumentException($"Unknown sample type '{Parts[1]}'."),
            };
            var Channels=Parts.Length>3?int.Parse(Parts[3],CultureInfo.InvariantCulture):1;
            return new RawRasterSource(Input,new RawRasterDescription(int.Parse(Size[0],CultureInfo.InvariantCulture),int.Parse(Size[1],CultureInfo.InvariantCulture),Channels,Type,Parts[2]=="le",Scale,Offset));
        }
        var Extension=Path.GetExtension(Input).ToLowerInvariant();
        if(Extension is ".tif" or ".tiff"){ return new TiffRasterSource(Input,Scale,Offset); }
        return RawRasterSource.OpenPds(Input);
    }
}
