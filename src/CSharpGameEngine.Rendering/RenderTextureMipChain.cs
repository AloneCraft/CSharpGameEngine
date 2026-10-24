namespace CSharpGameEngine.Rendering;
/// <summary>How a material texture's colour channels are encoded, which decides the space its mip levels are averaged in (M0110).</summary>
public enum RenderTextureColorSpace:byte {
    /// <summary>Data read as it is (normal and metallic-roughness maps): every channel is averaged as stored.</summary>
    Linear=0,
    /// <summary>sRGB-encoded colour (base colour and emissive maps): RGB is decoded to linear light, averaged and encoded again; alpha is linear.</summary>
    Srgb=1,
}
/// <summary>One level of a <see cref="RenderTextureMipChain"/>: tightly packed RGBA8 rows.</summary>
public sealed class RenderTextureMipLevel {
    private readonly byte[]PixelsStorage;
    internal RenderTextureMipLevel(int Width,int Height,byte[]Pixels){
        this.Width=Width;
        this.Height=Height;
        this.PixelsStorage=Pixels;
    }
    public int Width { get; }
    public int Height { get; }
    public ReadOnlySpan<byte>Pixels=>this.PixelsStorage;
}
/// <summary>
/// The full mip chain of a material texture (M0110), made on the CPU when the texture is uploaded. Deterministic: the result depends only on
/// the texels and the colour space, never on the machine. Each level halves the previous one (rounding down, at least one texel, the D3D12
/// rule), and every texel is the area-weighted box average of the texels it covers, so a dimension that is not a power of two gets fractional
/// weights instead of losing a row. The weights are integers (the overlap lengths in units of one over the destination size) and the
/// averages are rounded integer divisions. Colour is carried at 16 bits per channel from level to level; an sRGB texture's RGB is averaged in
/// linear light through a fixed decoding table and encoded back by the nearest 8-bit value.
/// </summary>
public sealed class RenderTextureMipChain {
    /// <summary>
    /// The IEC 61966-2-1 decoding of each 8-bit code (code / 255 <= 0.04045 ? x / 12.92 : ((x + 0.055) / 1.055)^2.4) times 65535, rounded half
    /// to even. Written out instead of computed so no platform's pow can change a mip (and product code carries no doubles).
    /// </summary>
    private static readonly ushort[]SrgbToLinear16=[
        0,20,40,60,80,99,119,139,159,179,199,219,241,264,288,313,
        340,367,396,427,458,491,526,562,599,637,677,718,761,805,851,898,
        947,997,1048,1101,1156,1212,1270,1330,1391,1453,1517,1583,1651,1720,1790,1863,
        1937,2013,2090,2170,2250,2333,2418,2504,2592,2681,2773,2866,2961,3058,3157,3258,
        3360,3464,3570,3678,3788,3900,4014,4129,4247,4366,4488,4611,4736,4864,4993,5124,
        5257,5392,5530,5669,5810,5953,6099,6246,6395,6547,6700,6856,7014,7174,7335,7500,
        7666,7834,8004,8177,8352,8528,8708,8889,9072,9258,9445,9635,9828,10022,10219,10417,
        10619,10822,11028,11235,11446,11658,11873,12090,12309,12530,12754,12980,13209,13440,13673,13909,
        14146,14387,14629,14874,15122,15371,15623,15878,16135,16394,16656,16920,17187,17456,17727,18001,
        18277,18556,18837,19121,19407,19696,19987,20281,20577,20876,21177,21481,21787,22096,22407,22721,
        23038,23357,23678,24002,24329,24658,24990,25325,25662,26001,26344,26688,27036,27386,27739,28094,
        28452,28813,29176,29542,29911,30282,30656,31033,31412,31794,32179,32567,32957,33350,33745,34143,
        34544,34948,35355,35764,36176,36591,37008,37429,37852,38278,38706,39138,39572,40009,40449,40891,
        41337,41785,42236,42690,43147,43606,44069,44534,45002,45473,45947,46423,46903,47385,47871,48359,
        48850,49344,49841,50341,50844,51349,51858,52369,52884,53401,53921,54445,54971,55500,56032,56567,
        57105,57646,58190,58737,59287,59840,60396,60955,61517,62082,62650,63221,63795,64372,64952,65535,
    ];
    private readonly RenderTextureMipLevel[]LevelsStorage;
    private RenderTextureMipChain(RenderTextureMipLevel[]Levels){
        this.LevelsStorage=Levels;
    }
    public IReadOnlyList<RenderTextureMipLevel>Levels=>this.LevelsStorage;
    /// <summary>The number of levels down to 1 x 1: floor(log2(max(Width, Height))) + 1, computed on integers.</summary>
    public static int LevelCount(int Width,int Height){
        ArgumentOutOfRangeException.ThrowIfLessThan(Width,1);
        ArgumentOutOfRangeException.ThrowIfLessThan(Height,1);
        var Largest=Math.Max(Width,Height);
        var Count=1;
        while(Largest>1){
            Largest>>=1;
            Count++;
        }
        return Count;
    }
    /// <summary>The linear value (0 to 65535) the 8-bit sRGB code decodes to; the table every sRGB average goes through.</summary>
    public static ushort DecodeSrgb(byte Code)=>SrgbToLinear16[Code];
    /// <summary>The 8-bit sRGB code whose decoded value is nearest to <paramref name="Linear"/> (the upper one on a tie).</summary>
    public static byte EncodeSrgb(ushort Linear){
        var Low=0;
        var High=255;
        while(Low<High){
            var Middle=(Low+High)>>1;
            if(2*Linear>=SrgbToLinear16[Middle]+SrgbToLinear16[Middle+1]){
                Low=Middle+1;
            } else {
                High=Middle;
            }
        }
        return (byte)Low;
    }
    /// <summary>A chain of the texture alone, without smaller levels (what the screen layer uploads: its glyphs and images are drawn at their size).</summary>
    public static RenderTextureMipChain TopLevelOnly(RenderTextureResource Texture){
        ArgumentNullException.ThrowIfNull(Texture);
        return new RenderTextureMipChain([new RenderTextureMipLevel(Texture.Width,Texture.Height,Texture.Pixels.ToArray())]);
    }
    /// <summary>Builds every level of <paramref name="Texture"/>; level 0 is the texture itself.</summary>
    public static RenderTextureMipChain Build(RenderTextureResource Texture,RenderTextureColorSpace ColorSpace){
        ArgumentNullException.ThrowIfNull(Texture);
        return Build(Texture.Width,Texture.Height,Texture.Pixels,ColorSpace);
    }
    /// <summary>Builds every level of an RGBA8 image of <paramref name="Width"/> x <paramref name="Height"/> texels.</summary>
    public static RenderTextureMipChain Build(int Width,int Height,ReadOnlySpan<byte>Rgba8Pixels,RenderTextureColorSpace ColorSpace){
        ArgumentOutOfRangeException.ThrowIfLessThan(Width,1);
        ArgumentOutOfRangeException.ThrowIfLessThan(Height,1);
        if(ColorSpace is not(RenderTextureColorSpace.Linear or RenderTextureColorSpace.Srgb)){
            throw new ArgumentOutOfRangeException(nameof(ColorSpace));
        }
        if(Rgba8Pixels.Length!=checked(Width*Height*4)){
            throw new ArgumentException("RGBA8 byte length does not match the texture dimensions.",nameof(Rgba8Pixels));
        }
        var Count=LevelCount(Width,Height);
        var Levels=new RenderTextureMipLevel[Count];
        Levels[0]=new RenderTextureMipLevel(Width,Height,Rgba8Pixels.ToArray());
        if(Count==1){
            return new RenderTextureMipChain(Levels);
        }
        var Srgb=ColorSpace==RenderTextureColorSpace.Srgb;
        var Wide=new ushort[Rgba8Pixels.Length];
        for(var Index=0;Index<Rgba8Pixels.Length;Index++){
            var Value=Rgba8Pixels[Index];
            Wide[Index]=Srgb&&(Index&3)!=3?SrgbToLinear16[Value]:(ushort)(Value*257);
        }
        var SourceWidth=Width;
        var SourceHeight=Height;
        for(var Level=1;Level<Count;Level++){
            var TargetWidth=Math.Max(1,SourceWidth>>1);
            var TargetHeight=Math.Max(1,SourceHeight>>1);
            var Reduced=Reduce(Wide,SourceWidth,SourceHeight,TargetWidth,TargetHeight);
            var Bytes=new byte[Reduced.Length];
            for(var Index=0;Index<Reduced.Length;Index++){
                Bytes[Index]=Srgb&&(Index&3)!=3?EncodeSrgb(Reduced[Index]):(byte)((Reduced[Index]*255+32767)/65535);
            }
            Levels[Level]=new RenderTextureMipLevel(TargetWidth,TargetHeight,Bytes);
            Wide=Reduced;
            SourceWidth=TargetWidth;
            SourceHeight=TargetHeight;
        }
        return new RenderTextureMipChain(Levels);
    }
    /// <summary>The area-weighted box reduction of one 16-bit RGBA level.</summary>
    private static ushort[]Reduce(ushort[]Source,int SourceWidth,int SourceHeight,int TargetWidth,int TargetHeight){
        var Columns=Footprints(SourceWidth,TargetWidth);
        var Rows=Footprints(SourceHeight,TargetHeight);
        var Total=(long)SourceWidth*SourceHeight;
        var Target=new ushort[TargetWidth*TargetHeight*4];
        Span<long>Sums=stackalloc long[4];
        for(var Y=0;Y<TargetHeight;Y++){
            var Row=Rows[Y];
            for(var X=0;X<TargetWidth;X++){
                var Column=Columns[X];
                Sums.Clear();
                for(var J=0;J<Row.Count;J++){
                    var SourceY=Row.First+J;
                    var WeightY=(long)Row.Weights[J];
                    for(var I=0;I<Column.Count;I++){
                        var Weight=WeightY*Column.Weights[I];
                        var Offset=(SourceY*SourceWidth+Column.First+I)*4;
                        Sums[0]+=Weight*Source[Offset];
                        Sums[1]+=Weight*Source[Offset+1];
                        Sums[2]+=Weight*Source[Offset+2];
                        Sums[3]+=Weight*Source[Offset+3];
                    }
                }
                var TargetOffset=(Y*TargetWidth+X)*4;
                for(var Channel=0;Channel<4;Channel++){
                    Target[TargetOffset+Channel]=(ushort)((Sums[Channel]+Total/2)/Total);
                }
            }
        }
        return Target;
    }
    /// <summary>
    /// For each target texel along one axis, the source texels it covers and their overlap lengths, measured in units of 1 / target size so
    /// they are integers: target texel x spans [x * S, (x + 1) * S) and source texel i spans [i * T, (i + 1) * T). The weights of a texel sum to S.
    /// </summary>
    private static Footprint[]Footprints(int SourceSize,int TargetSize){
        var Result=new Footprint[TargetSize];
        for(var X=0;X<TargetSize;X++){
            var Start=(long)X*SourceSize;
            var End=(long)(X+1)*SourceSize;
            var First=(int)(Start/TargetSize);
            var Last=(int)((End-1)/TargetSize);
            var Weights=new int[Last-First+1];
            for(var I=First;I<=Last;I++){
                Weights[I-First]=(int)(Math.Min(End,(long)(I+1)*TargetSize)-Math.Max(Start,(long)I*TargetSize));
            }
            Result[X]=new Footprint(First,Weights);
        }
        return Result;
    }
    private readonly record struct Footprint(int First,int[]Weights) {
        public int Count=>this.Weights.Length;
    }
}
