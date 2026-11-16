using System.Buffers.Binary;
using System.Text;

namespace CSharpGameEngine.Rendering;

/// <summary><see cref="FontAtlas"/> のグリフ1つ。平面矩形はベースライン上のペン位置を基準にした em 単位のグリフ四角形で、Y 軸は上向き（距離場の余白を含む）。UV 矩形はアトラス内の同じ四角形で、V 軸は下向き。</summary>
public readonly record struct FontGlyph(
    int CodePoint,float Advance,
    float PlaneLeft,float PlaneBottom,float PlaneRight,float PlaneTop,
    float U0,float V0,float U1,float V1){
    /// <summary>グリフに描画内容があるか（空白にも送り幅はあるが、四角形はない）。</summary>
    public bool HasQuad=>this.PlaneRight>this.PlaneLeft&&this.PlaneTop>this.PlaneBottom;
}

/// <summary>符号付き距離場アトラスに焼き付けたフォント（ADR-0078/M0104）。グリフ表、垂直方向のメトリクス、8ビット画像1枚を保持する。各グリフの輪郭は値128で、その内側ほど大きく、外側ほど小さい値になる。輪郭の両側を <see cref="SpreadEm"/> 分まで格納するため、1つのアトラスで任意のサイズのフォントを描画できる。ファイル形式（リトルエンディアン）：
/// <code>
/// char[8] "CGEFONT1"; int32 width; int32 height; float32 ascent; float32 descent; float32 lineGap; float32 spreadEm; int32 glyphCount;
/// glyphCount × { int32 codePoint; float32 advance; float32 planeLeft, planeBottom, planeRight, planeTop; float32 u0, v0, u1, v1 }  (ascending codePoint)
/// width × height bytes of distance
/// </code>
/// メトリクスは em に対する割合で表す。descent は下向きを正とする。</summary>
public sealed class FontAtlas {
    public const string Magic="CGEFONT1";
    public const int MaximumGlyphs=65_536;
    public const int MaximumAtlasDimension=8192;
    private const int HeaderSize=8+4+4+4+4+4+4+4;
    private const int GlyphSize=4+4+(4*4)+(4*4);

    private readonly FontGlyph[]GlyphTable;
    private readonly byte[]DistanceStorage;
    private RenderTextureResource?TextureResource;

    public FontAtlas(int Width,int Height,float Ascent,float Descent,float LineGap,float SpreadEm,IEnumerable<FontGlyph>Glyphs,byte[]Distance){
        ArgumentNullException.ThrowIfNull(Glyphs);
        ArgumentNullException.ThrowIfNull(Distance);
        if(Width is <1 or >MaximumAtlasDimension){ throw new ArgumentOutOfRangeException(nameof(Width)); }
        if(Height is <1 or >MaximumAtlasDimension){ throw new ArgumentOutOfRangeException(nameof(Height)); }
        if(Distance.Length!=checked(Width*Height)){ throw new ArgumentException("The distance image must be width × height bytes.",nameof(Distance)); }
        if(!float.IsFinite(Ascent)||!float.IsFinite(Descent)||!float.IsFinite(LineGap)||!float.IsFinite(SpreadEm)||Ascent<=0f||Descent<0f||LineGap<0f||SpreadEm<=0f){
            throw new ArgumentException("The font metrics must be finite, with a positive ascent and spread.");
        }
        this.GlyphTable=[.. Glyphs.OrderBy(static Glyph=>Glyph.CodePoint)];
        if(this.GlyphTable.Length is 0 or >MaximumGlyphs){ throw new ArgumentException($"A font needs 1 to {MaximumGlyphs} glyphs."); }
        for(int Index=0;Index<this.GlyphTable.Length;Index++){
            FontGlyph Glyph=this.GlyphTable[Index];
            if(Glyph.CodePoint is <0 or >0x10FFFF){ throw new ArgumentException($"Code point {Glyph.CodePoint} is not valid."); }
            if(Index>0&&this.GlyphTable[Index-1].CodePoint==Glyph.CodePoint){ throw new ArgumentException($"Code point {Glyph.CodePoint} appears twice."); }
            if(!AllFinite(Glyph)||Glyph.Advance<0f){ throw new ArgumentException($"Glyph {Glyph.CodePoint} has invalid metrics."); }
            if(Glyph.U0<0f||Glyph.V0<0f||Glyph.U1>1f||Glyph.V1>1f||Glyph.U1<Glyph.U0||Glyph.V1<Glyph.V0){ throw new ArgumentException($"Glyph {Glyph.CodePoint} has texture coordinates outside the atlas."); }
        }
        this.AtlasWidth=Width;
        this.AtlasHeight=Height;
        this.Ascent=Ascent;
        this.Descent=Descent;
        this.LineGap=LineGap;
        this.SpreadEm=SpreadEm;
        this.DistanceStorage=Distance;
    }

    public int AtlasWidth { get; }
    public int AtlasHeight { get; }
    public float Ascent { get; }
    /// <summary>ベースラインから行の下端までの距離（正の値）。</summary>
    public float Descent { get; }
    public float LineGap { get; }
    /// <summary>輪郭の両側に距離場が広がる幅（em 単位）。</summary>
    public float SpreadEm { get; }
    /// <summary>2つのベースライン間の距離（em 単位）。</summary>
    public float LineHeight=>this.Ascent+this.Descent+this.LineGap;
    public IReadOnlyList<FontGlyph>Glyphs=>this.GlyphTable;
    public ReadOnlySpan<byte>Distance=>this.DistanceStorage;

    public bool TryGetGlyph(int CodePoint,out FontGlyph Glyph){
        int Low=0;
        int High=this.GlyphTable.Length-1;
        while(Low<=High){
            int Middle=Low+((High-Low)/2);
            int Compared=this.GlyphTable[Middle].CodePoint.CompareTo(CodePoint);
            if(Compared==0){
                Glyph=this.GlyphTable[Middle];
                return true;
            }
            if(Compared<0){ Low=Middle+1; } else { High=Middle-1; }
        }
        Glyph=default;
        return false;
    }

    /// <summary><see cref="RenderScreenShading.DistanceField"/> バッチ用テクスチャとしてのアトラス。白地に距離をアルファ値で格納する。</summary>
    public RenderTextureResource Texture{
        get{
            if(this.TextureResource is { }Existing){ return Existing; }
            byte[]Rgba=new byte[this.DistanceStorage.Length*4];
            for(int Index=0;Index<this.DistanceStorage.Length;Index++){
                Rgba[Index*4]=255;
                Rgba[(Index*4)+1]=255;
                Rgba[(Index*4)+2]=255;
                Rgba[(Index*4)+3]=this.DistanceStorage[Index];
            }
            return this.TextureResource=RenderArtifactCodec.CreateProceduralTexture(this.AtlasWidth,this.AtlasHeight,Rgba);
        }
    }

    public byte[]ToBytes(){
        byte[]Bytes=new byte[checked(HeaderSize+(this.GlyphTable.Length*GlyphSize)+this.DistanceStorage.Length)];
        Span<byte>Output=Bytes;
        Encoding.ASCII.GetBytes(Magic,Output);
        BinaryPrimitives.WriteInt32LittleEndian(Output[8..],this.AtlasWidth);
        BinaryPrimitives.WriteInt32LittleEndian(Output[12..],this.AtlasHeight);
        BinaryPrimitives.WriteSingleLittleEndian(Output[16..],this.Ascent);
        BinaryPrimitives.WriteSingleLittleEndian(Output[20..],this.Descent);
        BinaryPrimitives.WriteSingleLittleEndian(Output[24..],this.LineGap);
        BinaryPrimitives.WriteSingleLittleEndian(Output[28..],this.SpreadEm);
        BinaryPrimitives.WriteInt32LittleEndian(Output[32..],this.GlyphTable.Length);
        int Offset=HeaderSize;
        foreach(FontGlyph Glyph in this.GlyphTable){
            BinaryPrimitives.WriteInt32LittleEndian(Output[Offset..],Glyph.CodePoint);
            float[]Values=[Glyph.Advance,Glyph.PlaneLeft,Glyph.PlaneBottom,Glyph.PlaneRight,Glyph.PlaneTop,Glyph.U0,Glyph.V0,Glyph.U1,Glyph.V1];
            for(int Index=0;Index<Values.Length;Index++){
                BinaryPrimitives.WriteSingleLittleEndian(Output[(Offset+4+(Index*4))..],Values[Index]);
            }
            Offset+=GlyphSize;
        }
        this.DistanceStorage.CopyTo(Output[Offset..]);
        return Bytes;
    }

    /// <summary>焼き付け済みフォントを読み込む。有効なファイル形式でない場合は <see cref="FormatException"/> をスローする。</summary>
    public static FontAtlas Parse(ReadOnlySpan<byte>Bytes){
        if(Bytes.Length<HeaderSize||!Bytes[..8].SequenceEqual(Encoding.ASCII.GetBytes(Magic))){
            throw new FormatException("Not a baked font (CGEFONT1).");
        }
        int Width=BinaryPrimitives.ReadInt32LittleEndian(Bytes[8..]);
        int Height=BinaryPrimitives.ReadInt32LittleEndian(Bytes[12..]);
        float Ascent=BinaryPrimitives.ReadSingleLittleEndian(Bytes[16..]);
        float Descent=BinaryPrimitives.ReadSingleLittleEndian(Bytes[20..]);
        float LineGap=BinaryPrimitives.ReadSingleLittleEndian(Bytes[24..]);
        float Spread=BinaryPrimitives.ReadSingleLittleEndian(Bytes[28..]);
        int Count=BinaryPrimitives.ReadInt32LittleEndian(Bytes[32..]);
        if(Width is <1 or >MaximumAtlasDimension||Height is <1 or >MaximumAtlasDimension||Count is <1 or >MaximumGlyphs){
            throw new FormatException("The baked font has an invalid size.");
        }
        long Expected=HeaderSize+((long)Count*GlyphSize)+((long)Width*Height);
        if(Bytes.Length!=Expected){
            throw new FormatException($"The baked font is {Bytes.Length} bytes; its header says {Expected}.");
        }
        List<FontGlyph>Glyphs=new(Count);
        int Offset=HeaderSize;
        for(int Index=0;Index<Count;Index++){
            ReadOnlySpan<byte>Record=Bytes[Offset..];
            Glyphs.Add(new FontGlyph(
                BinaryPrimitives.ReadInt32LittleEndian(Record),
                BinaryPrimitives.ReadSingleLittleEndian(Record[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(Record[8..]),BinaryPrimitives.ReadSingleLittleEndian(Record[12..]),
                BinaryPrimitives.ReadSingleLittleEndian(Record[16..]),BinaryPrimitives.ReadSingleLittleEndian(Record[20..]),
                BinaryPrimitives.ReadSingleLittleEndian(Record[24..]),BinaryPrimitives.ReadSingleLittleEndian(Record[28..]),
                BinaryPrimitives.ReadSingleLittleEndian(Record[32..]),BinaryPrimitives.ReadSingleLittleEndian(Record[36..])));
            Offset+=GlyphSize;
        }
        for(int Index=1;Index<Glyphs.Count;Index++){
            if(Glyphs[Index].CodePoint<=Glyphs[Index-1].CodePoint){ throw new FormatException("The baked font's glyphs are not in ascending code point order."); }
        }
        try {
            return new FontAtlas(Width,Height,Ascent,Descent,LineGap,Spread,Glyphs,Bytes[Offset..].ToArray());
        } catch(ArgumentException Error){
            throw new FormatException("The baked font is not valid: "+Error.Message,Error);
        }
    }

    private static bool AllFinite(in FontGlyph Glyph)=>
        float.IsFinite(Glyph.Advance)&&float.IsFinite(Glyph.PlaneLeft)&&float.IsFinite(Glyph.PlaneBottom)&&float.IsFinite(Glyph.PlaneRight)&&float.IsFinite(Glyph.PlaneTop)
        &&float.IsFinite(Glyph.U0)&&float.IsFinite(Glyph.V0)&&float.IsFinite(Glyph.U1)&&float.IsFinite(Glyph.V1);
}
