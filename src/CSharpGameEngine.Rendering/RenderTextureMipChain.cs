namespace CSharpGameEngine.Rendering;
/// <summary>マテリアルテクスチャの色チャンネルの符号化形式。ミップレベルの平均を計算する色空間が決まる（M0110）。</summary>
public enum RenderTextureColorSpace:byte {
    /// <summary>そのまま読み取るデータ（法線マップ、メタリック・ラフネスマップ）。各チャンネルは保存された値のまま平均する。</summary>
    Linear=0,
    /// <summary>sRGB 符号化色（ベースカラーと発光マップ）。RGB はリニア光にデコードして平均した後、再符号化する。アルファはリニア値として扱う。</summary>
    Srgb=1,
}
/// <summary><see cref="RenderTextureMipChain"/> のミップレベル1つ。RGBA8 の行を隙間なく格納する。</summary>
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
/// マテリアルテクスチャの全ミップチェーン（M0110）。テクスチャのアップロード時に CPU 上で作成する。結果はテクセルと色空間だけで決まり、実行マシンに依存しない。各レベルは直前の半分にする（端数は
/// 切り捨て、最低1テクセル。D3D12 の規則）。各テクセルは、その領域に含まれるテクセルの面積加重平均を求めるため、2の累乗でない寸法でも行を捨てずに小数の重みで処理する。重みは整数（出力サイズの
/// 逆数単位で表した重なり長）とし、平均は整数除算で丸める。レベル間では各色チャンネルを16ビットで保持する。sRGB テクスチャの RGB は固定デコードテーブルを使ってリニア光で平均し、最も近い8
/// ビット値で再符号化する。
/// </summary>
public sealed class RenderTextureMipChain {
    /// <summary>
    /// 各8ビット符号値を IEC 61966-2-1 に従ってデコードした値。式は (code / 255 = 0.04045 ? x / 12.92 : ((x + 0.055) / 1.055)^2.4
    /// )。65535を掛け、最近接の偶数へ丸める。どのプラットフォームの pow 関数でもミップ結果が変わらないよう、実行時計算ではなく値を記述しておく（製品コードで double も使わない）。
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
    public static RenderTextureMipChain FromImported(CSharpGameEngine.Content.TextureOutputData Texture){
        _=CSharpGameEngine.Content.ImageArtifactCodec.WriteTexture(Texture);
        return new(Texture.Levels.Select(Level=>new RenderTextureMipLevel(Level.Width,Level.Height,Level.Pixels.ToArray())).ToArray());
    }
    public IReadOnlyList<RenderTextureMipLevel>Levels=>this.LevelsStorage;
    /// <summary>整数演算で求める、1×1までのレベル数。floor(log2(max(Width, Height))) + 1。</summary>
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
    /// <summary>8ビット sRGB 符号値をデコードしたリニア値（0～65535）。sRGB 平均はすべてこのテーブルを通す。</summary>
    public static ushort DecodeSrgb(byte Code)=>SrgbToLinear16[Code];
    /// <summary><paramref name="Linear"/> に最も近いデコード済み値を持つ8ビット sRGB 符号値。同距離なら大きい方。</summary>
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
    /// <summary>小さいミップレベルを持たないテクスチャ単独のチェーン（スクリーンレイヤーのアップロード対象。グリフや画像を元のサイズで描画する）。</summary>
    public static RenderTextureMipChain TopLevelOnly(RenderTextureResource Texture){
        ArgumentNullException.ThrowIfNull(Texture);
        return new RenderTextureMipChain([new RenderTextureMipLevel(Texture.Width,Texture.Height,Texture.Pixels.ToArray())]);
    }
    /// <summary><paramref name="Texture"/> の全レベルを生成する。レベル0はテクスチャ自体。</summary>
    public static RenderTextureMipChain Build(RenderTextureResource Texture,RenderTextureColorSpace ColorSpace){
        ArgumentNullException.ThrowIfNull(Texture);
        return Build(Texture.Width,Texture.Height,Texture.Pixels,ColorSpace);
    }
    /// <summary>幅 <paramref name="Width"/>、高さ <paramref name="Height"/> の RGBA8 画像について全レベルを生成する。</summary>
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
    /// <summary>16ビット RGBA レベル1つを面積加重で縮小する。</summary>
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
    /// 1つの軸に沿う各出力テクセルについて、覆う入力テクセルと重なり長を求める。長さは出力サイズの逆数を単位とする整数。出力テクセル x は [x * S, (x + 1) * S)、入力テクセル i は
    /// [i * T, (i + 1) * T) の範囲を占める。各入力テクセルの重みの合計は S。
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
