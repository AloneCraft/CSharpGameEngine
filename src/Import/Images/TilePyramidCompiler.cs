using CSharpGameEngine.Content.Tiles;
namespace CSharpGameEngine.Import.Images;
/// <summary>タイルの階層の作り方の設定。</summary>
public sealed record TilePyramidCompileOptions {
    public int TileSize { get;init; }=256;
    public TileSampleEncoding Encoding { get;init; }=TileSampleEncoding.Float32;
    public TileProjection Projection { get;init; }=TileProjection.Equirectangular;
    /// <summary>範囲（正距円筒なら度。既定は全球）。</summary>
    public float West { get;init; }=-180f;
    public float East { get;init; }=180f;
    public float North { get;init; }=90f;
    public float South { get;init; }=-90f;
    /// <summary>1 回に読む行の数（帯の高さ）。メモリは 帯の高さ × 幅 × チャンネル × 4 バイトほど。</summary>
    public int RowsPerRead { get;init; }=64;
}
/// <summary>前処理の結果の数。</summary>
public readonly record struct TilePyramidCompileReport(int Levels,int Tiles,long PackBytes,long SourceSamples);
/// <summary>
/// 大きな画像をタイルの階層にする前処理（ADR-0100 決定 7、M0134）。入力は行の帯ごとに少しずつ読み、全体をメモリに載せない。
/// 段 0 は元の解像度、段 L+1 は段 L の 2×2 の平均（値の無い所は混ぜない。4 つとも無ければ無い）。奇数の端は端の行・列を繰り返す。
/// 各段は TileSize 行ずつの帯だけを持ち、帯がそろうたびにタイルを書き出す。メモリは段の数 × 帯の大きさで、元の画像の大きさによらない。
/// タイルはキーの順（段、y、x）でまとめファイルへ書き、最後に索引を書く。同じ入力から同じバイト列になる。
/// </summary>
public static class TilePyramidCompiler {
    public static TilePyramidCompileReport Compile(IRasterSource Source,string IndexPath,TilePyramidCompileOptions?Options=null){
        ArgumentNullException.ThrowIfNull(Source);
        Options??=new TilePyramidCompileOptions();
        if(Options.TileSize<16||Options.TileSize>4096||Options.RowsPerRead<=0){ throw new ArgumentOutOfRangeException(nameof(Options),"The tile size must lie between 16 and 4096 and the rows per read must be positive."); }
        var LevelCount=1;
        while(((long)Source.Width+(1L<<(LevelCount-1))-1>>(LevelCount-1))>Options.TileSize||((long)Source.Height+(1L<<(LevelCount-1))-1>>(LevelCount-1))>Options.TileSize){ LevelCount++; }
        var Header=new TilePyramidHeader(Source.Width,Source.Height,Source.Channels,Options.TileSize,LevelCount,Options.Encoding,Options.Projection,
            Options.West,Options.East,Options.North,Options.South,Source.NoData,Source.Unit);
        var Directory=Path.GetDirectoryName(Path.GetFullPath(IndexPath));
        if(!string.IsNullOrEmpty(Directory)){ System.IO.Directory.CreateDirectory(Directory); }
        var PackPath=Path.ChangeExtension(IndexPath,TilePyramid.PackExtension);
        // 段ごとのタイルを、まとめファイルとは別の一時ファイルに溜め、最後にキーの順で並べ直す（段 0 から順に帯が出るが、段をまたぐと順が混ざるため）。
        var Levels=new LevelState[LevelCount];
        for(var Level=0;Level<LevelCount;Level++){ Levels[Level]=new LevelState(Header,Level,Path.ChangeExtension(IndexPath,$".level{Level}.tmp")); }
        try {
            var Band=new float[(long)Source.Width*Options.RowsPerRead*Source.Channels];
            var RowValues=Source.Width*Source.Channels;
            for(var Row=0;Row<Source.Height;Row+=Options.RowsPerRead){
                var Rows=Math.Min(Options.RowsPerRead,Source.Height-Row);
                Source.ReadRows(Row,Rows,Band.AsSpan(0,Rows*RowValues));
                for(var Index=0;Index<Rows;Index++){ Push(Levels,0,Band.AsSpan(Index*RowValues,RowValues)); }
            }
            for(var Level=0;Level<LevelCount;Level++){ Finish(Levels,Level); }
            // まとめファイル: 段の順、段の中は帯の順（y）、帯の中は x の順。
            var Entries=new List<(TileKey,long,int)>();
            using(var Pack=File.Create(PackPath)){
                foreach(var State in Levels){
                    State.Spill.Position=0;
                    foreach(var (Key,Length) in State.Written){
                        var Bytes=new byte[Length];
                        State.Spill.ReadExactly(Bytes);
                        Entries.Add((Key,Pack.Position,Length));
                        Pack.Write(Bytes);
                    }
                }
            }
            TilePyramid.WriteIndex(IndexPath,Header,Entries);
            return new TilePyramidCompileReport(LevelCount,Entries.Count,new FileInfo(PackPath).Length,(long)Source.Width*Source.Height*Source.Channels);
        } finally {
            foreach(var State in Levels){ State.Dispose(); }
        }
    }
    /// <summary>段 1 つの帯と、下の段から来た行の半分。</summary>
    private sealed class LevelState:IDisposable {
        internal readonly TilePyramidHeader Header;
        internal readonly int Level;
        internal readonly int Width;
        internal readonly int Height;
        internal readonly float[]Band;
        internal int BandRows;
        internal int BandIndex;
        internal int RowsReceived;
        /// <summary>この段の 2 行を 1 行にする前の、上の行（上の段へ送る前に待つ）。</summary>
        internal float[]?PendingRow;
        internal readonly FileStream Spill;
        internal readonly List<(TileKey Key,int Length)>Written=[];
        internal LevelState(TilePyramidHeader Header,int Level,string SpillPath){
            this.Header=Header;
            this.Level=Level;
            this.Width=Header.LevelWidth(Level);
            this.Height=Header.LevelHeight(Level);
            this.Band=new float[(long)this.Width*Header.TileSize*Header.Channels];
            this.Spill=new FileStream(SpillPath,FileMode.Create,FileAccess.ReadWrite,FileShare.None,1<<16,FileOptions.DeleteOnClose);
        }
        public void Dispose()=>this.Spill.Dispose();
    }
    /// <summary>段 <paramref name="Level"/> に 1 行を足す。帯がそろえばタイルを書き、2 行そろえば上の段へ 1 行を送る。</summary>
    private static void Push(LevelState[]Levels,int Level,ReadOnlySpan<float>Row){
        var State=Levels[Level];
        if(State.RowsReceived>=State.Height){ return; }
        var Channels=State.Header.Channels;
        Row.CopyTo(State.Band.AsSpan(State.BandRows*State.Width*Channels,State.Width*Channels));
        State.BandRows++;
        State.RowsReceived++;
        if(State.BandRows==State.Header.TileSize){ WriteBand(State); }
        if(Level+1>=Levels.Length){ return; }
        if(State.PendingRow is null){
            State.PendingRow=Row.ToArray();
            // 奇数の高さの最後の行は、自分を繰り返して上へ送る。
            if(State.RowsReceived==State.Height){ SendUp(Levels,Level,State.PendingRow,State.PendingRow); }
        } else {
            SendUp(Levels,Level,State.PendingRow,Row);
        }
    }
    private static void SendUp(LevelState[]Levels,int Level,float[]Upper,ReadOnlySpan<float>Lower){
        var State=Levels[Level];
        var Above=Levels[Level+1];
        var Channels=State.Header.Channels;
        var NoData=State.Header.NoData;
        var Result=new float[Above.Width*Channels];
        for(var X=0;X<Above.Width;X++){
            var X0=Math.Min(2*X,State.Width-1);
            var X1=Math.Min(2*X+1,State.Width-1);
            for(var Channel=0;Channel<Channels;Channel++){
                var Sum=0f;
                var Count=0;
                foreach(var Value in (ReadOnlySpan<float>)[Upper[X0*Channels+Channel],Upper[X1*Channels+Channel],Lower[X0*Channels+Channel],Lower[X1*Channels+Channel]]){
                    if(float.IsNaN(Value)||NoData is{ }Missing&&Value==Missing){ continue; }
                    Sum+=Value;
                    Count++;
                }
                Result[X*Channels+Channel]=Count>0?Sum/Count:NoData??float.NaN;
            }
        }
        State.PendingRow=null;
        Push(Levels,Level+1,Result);
    }
    /// <summary>段の残りの帯（TileSize 行に満たない最後の帯）を書く。</summary>
    private static void Finish(LevelState[]Levels,int Level){
        var State=Levels[Level];
        if(State.BandRows>0){ WriteBand(State); }
    }
    /// <summary>帯をタイルに切って書く（帯の下と右の外は端の値を繰り返す）。</summary>
    private static void WriteBand(LevelState State){
        var Size=State.Header.TileSize;
        var Channels=State.Header.Channels;
        var Tile=new float[Size*Size*Channels];
        var Across=State.Header.TilesAcross(State.Level);
        for(var TileX=0;TileX<Across;TileX++){
            for(var Y=0;Y<Size;Y++){
                var SourceY=Math.Min(Y,State.BandRows-1);
                for(var X=0;X<Size;X++){
                    var SourceX=Math.Min(TileX*Size+X,State.Width-1);
                    State.Band.AsSpan((SourceY*State.Width+SourceX)*Channels,Channels).CopyTo(Tile.AsSpan((Y*Size+X)*Channels,Channels));
                }
            }
            var Bytes=TilePyramid.EncodeTile(Tile,State.Header.Encoding);
            State.Spill.Write(Bytes);
            State.Written.Add((new TileKey(State.Level,TileX,State.BandIndex),Bytes.Length));
        }
        State.BandIndex++;
        State.BandRows=0;
    }
}
