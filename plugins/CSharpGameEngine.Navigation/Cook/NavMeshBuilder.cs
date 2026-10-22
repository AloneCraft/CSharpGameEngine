using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.Navigation.Cook;
/// <summary>
/// 三角形から<see cref="NavMesh"/>を作る(Recast の手順の簡約版)。すべて<see cref="LogicalScalar"/>と整数で計算し、同じ入力から同じ結果になる。
/// 1. ボクセル化: 三角形を水平の格子のセルで切り、セルごとの高さの区間(span)にする。傾きが歩ける三角形の上面は歩ける印を付ける。
/// 2. 歩ける面: 低い出っ張り(登れる段差)を歩ける印に、崖のふち・険しい坂・頭がつかえる所を外す。
/// 3. 開いた空間: 歩ける span ごとに、上の空きの高さと、4方向の隣(段差が登れ、頭が通る所)への接続を作る。
/// 4. 半径ぶん縁から離す(Recast の erode: 2/3 の距離変換)。
/// 5. 多角形: 同じ高さで続くセルを、行ごとに伸ばした長方形にまとめる。
/// 6. 接続: 長方形の境で、実際に通れる区間(接続のあるセルの並び)を portal にする。
/// 多角形は平らな長方形なので坂は段になる(段の高さは<see cref="NavMeshSettings.CellHeight"/>)。Recast の輪郭の単純化・斜めの辺は作らない。
/// </summary>
internal static class NavMeshBuilder {
    /// <summary>格子の幅と奥行きの上限(セル)。ボクセル化の作業領域がセルの数に比例する。</summary>
    internal const int MaximumGridSize=4096;
    private const int MaximumCells=16_000_000;
    private const int NoArea=0;
    private const int Walkable=1;
    private const int TopLimit=int.MaxValue/4;
    private static readonly int[]DirectionX=[-1,0,1,0];
    private static readonly int[]DirectionZ=[0,1,0,-1];
    internal static bool TryBuild(NavMeshSettings Settings,LogicalVector3 Min,LogicalVector3 Max,IReadOnlyList<NavTriangle>Triangles,out NavMesh?Mesh,out string Error){
        Mesh=null;
        try {
            var Builder=new Build(Settings,Min,Max);
            if(!Builder.TryPrepare(out Error)){ return false; }
            foreach(var Triangle in Triangles){ Builder.Rasterize(Triangle); }
            Builder.FilterCoincidentCeilings();
            Builder.FilterLowHangingObstacles();
            Builder.FilterLedges();
            Builder.FilterLowHeightSpans();
            Builder.BuildCompactHeightfield();
            Builder.Erode();
            var Polygons=Builder.BuildPolygons();
            var Portals=Builder.BuildPortals(Polygons);
            return NavMesh.TryCreate(Settings,Min,Builder.Width,Builder.Depth,Polygons,Portals,out Mesh,out Error);
        }
        catch(OverflowException){
            Error="The navigation mesh could not be built: a number left the supported range.";
            return false;
        }
    }
    /// <summary>
    /// 柱の中の高さの区間(段の単位)。<see cref="Up"/>と<see cref="Down"/>は、区間に入った歩ける傾きの三角形のうち、上向きの面と下向きの面(巻き順で決まる外向きの法線)の
    /// 一番高い所(段の 1/<see cref="FineUnits"/>の単位)。閉じた形が床に載っているとき、床の上面とその形の底面が同じ高さで重なる、という見分けに使う。
    /// </summary>
    private struct HeightSpan(int Min,int Max,int Area,int Up,int Down) {
        public int Min=Min;
        public int Max=Max;
        public int Area=Area;
        public int Up=Up;
        public int Down=Down;
    }
    private const int FineUnits=16;
    /// <summary>底面が床の上面と、この細かさ(段の 1/4)以内で重なっていたら、その床は形の内側(下)にある。</summary>
    private const int CoincidentTolerance=FineUnits/4;
    private const int NoFace=int.MinValue;
    private sealed class Build(NavMeshSettings Settings,LogicalVector3 Min,LogicalVector3 Max) {
        private readonly NavMeshSettings Settings=Settings;
        private readonly LogicalVector3 Min=Min;
        private readonly LogicalVector3 Max=Max;
        private List<HeightSpan>?[]Columns=[];
        private int Steps;
        private int WalkableHeight;
        private int WalkableClimb;
        private int WalkableRadius;
        private LogicalScalar WalkableCosineSquared;
        // 開いた空間(歩ける span だけ)
        private int[]CellStart=[];
        private int[]CellCount=[];
        private int[]SpanY=[];
        private int[]SpanClearance=[];
        private int[]SpanArea=[];
        private int[]SpanConnection=[];
        public int Width { get;private set; }
        public int Depth { get;private set; }
        public bool TryPrepare(out string Error){
            Error=string.Empty;
            var Extent=this.Max-this.Min;
            if(Extent.R<=LogicalScalar.Zero||Extent.U<=LogicalScalar.Zero||Extent.F<=LogicalScalar.Zero){
                Error="The navigation volume has no size.";
                return false;
            }
            var CellSize=this.Settings.CellSize;
            var CellHeight=this.Settings.CellHeight;
            var CellColumns=CeilIndex(Extent.R,CellSize);
            var CellRows=CeilIndex(Extent.F,CellSize);
            var CellLayers=CeilIndex(Extent.U,CellHeight);
            if(CellColumns>MaximumGridSize||CellRows>MaximumGridSize||CellColumns*CellRows>MaximumCells||CellLayers>TopLimit/4){
                Error=$"The navigation volume is too large for its cell size ({CellColumns} x {CellRows} cells of {CellSize.ToStringInvariant()} m; the limit is {MaximumGridSize} per side and {MaximumCells} in all). Use a smaller volume or a larger cell size.";
                return false;
            }
            this.Width=(int)Math.Max(CellColumns,1);
            this.Depth=(int)Math.Max(CellRows,1);
            this.Steps=(int)Math.Max(CellLayers,1);
            this.WalkableHeight=(int)Math.Max(CeilIndex(this.Settings.AgentHeight,CellHeight),3);
            this.WalkableClimb=(int)Math.Max(FloorIndex(this.Settings.AgentMaxClimb,CellHeight),0);
            this.WalkableRadius=(int)Math.Max(CeilIndex(this.Settings.AgentRadius,CellSize),0);
            this.Columns=new List<HeightSpan>?[this.Width*this.Depth];
            if(!LogicalMath.TrySinCos(this.Settings.AgentMaxSlopeDegrees*LogicalMath.Pi/LogicalScalar.FromInt64(180),out _,out var Cosine)){
                Error="The maximum slope is not a valid angle.";
                return false;
            }
            this.WalkableCosineSquared=Cosine*Cosine;
            return true;
        }
        private static long FloorIndex(LogicalScalar Value,LogicalScalar Size)=>FloorDivide(Value.Raw,Size.Raw);
        private static long CeilIndex(LogicalScalar Value,LogicalScalar Size)=>-FloorDivide(-Value.Raw,Size.Raw);
        private static long FloorDivide(Int128 Numerator,Int128 Denominator){
            var Quotient=Numerator/Denominator;
            if((Numerator%Denominator)!=0&&((Numerator<0)!=(Denominator<0))){ Quotient--; }
            return Quotient>int.MaxValue?int.MaxValue:Quotient<int.MinValue?int.MinValue:(long)Quotient;
        }
        // ---- 1. ボクセル化 ----
        public void Rasterize(NavTriangle Triangle){
            var Normal=LogicalVector3.Cross(Triangle.B-Triangle.A,Triangle.C-Triangle.A);
            // 歩ける傾きは、向きによらない(物理の床も両面)。上向き・下向きの区別は、閉じた形の内側の床を見分けるためだけに使う。
            var SlopeAllowed=!Normal.LengthSquared.IsZero&&Normal.U*Normal.U>=this.WalkableCosineSquared*Normal.LengthSquared;
            var Area=SlopeAllowed?Walkable:NoArea;
            var FacesUp=SlopeAllowed&&Normal.U>LogicalScalar.Zero;
            var FacesDown=SlopeAllowed&&Normal.U<LogicalScalar.Zero;
            var MinR=Smaller(Smaller(Triangle.A.R,Triangle.B.R),Triangle.C.R);
            var MaxR=Larger(Larger(Triangle.A.R,Triangle.B.R),Triangle.C.R);
            var MinF=Smaller(Smaller(Triangle.A.F,Triangle.B.F),Triangle.C.F);
            var MaxF=Larger(Larger(Triangle.A.F,Triangle.B.F),Triangle.C.F);
            var MinU=Smaller(Smaller(Triangle.A.U,Triangle.B.U),Triangle.C.U);
            var MaxU=Larger(Larger(Triangle.A.U,Triangle.B.U),Triangle.C.U);
            var CellSize=this.Settings.CellSize;
            var CellHeight=this.Settings.CellHeight;
            var GridMaxR=this.Min.R+CellSize*LogicalScalar.FromInt64(this.Width);
            var GridMaxF=this.Min.F+CellSize*LogicalScalar.FromInt64(this.Depth);
            if(MaxR<this.Min.R||MinR>GridMaxR||MaxF<this.Min.F||MinF>GridMaxF||MaxU<this.Min.U||MinU>this.Max.U){ return; }
            var FirstRow=(int)Math.Clamp(FloorIndex(MinF-this.Min.F,CellSize),-1,this.Depth-1);
            var LastRow=(int)Math.Clamp(FloorIndex(MaxF-this.Min.F,CellSize),0,this.Depth-1);
            var Remaining=new LogicalVector3[16];
            var RemainingCount=3;
            Remaining[0]=Triangle.A;
            Remaining[1]=Triangle.B;
            Remaining[2]=Triangle.C;
            var Row=new LogicalVector3[16];
            var Cell=new LogicalVector3[16];
            var Buffer=new LogicalVector3[16];
            for(var Z=FirstRow;Z<=LastRow;Z++){
                var RowEnd=this.Min.F+CellSize*LogicalScalar.FromInt64(Z+1);
                var RowCount=Divide(Remaining,RemainingCount,Row,Buffer,out var OutsideCount,RowEnd,false);
                (Remaining,Buffer)=(Buffer,Remaining);
                RemainingCount=OutsideCount;
                if(RowCount<3||Z<0){ continue; }
                var RowMin=Row[0].R;
                var RowMax=Row[0].R;
                for(var Index=1;Index<RowCount;Index++){
                    RowMin=Smaller(RowMin,Row[Index].R);
                    RowMax=Larger(RowMax,Row[Index].R);
                }
                var FirstColumn=FloorIndex(RowMin-this.Min.R,CellSize);
                var LastColumn=FloorIndex(RowMax-this.Min.R,CellSize);
                if(LastColumn<0||FirstColumn>=this.Width){ continue; }
                var FromColumn=(int)Math.Clamp(FirstColumn,-1,this.Width-1);
                var ToColumn=(int)Math.Clamp(LastColumn,0,this.Width-1);
                var RowRemaining=Row;
                var RowRemainingCount=RowCount;
                var Spare=new LogicalVector3[16];
                for(var X=FromColumn;X<=ToColumn;X++){
                    var ColumnEnd=this.Min.R+CellSize*LogicalScalar.FromInt64(X+1);
                    var CellCount=Divide(RowRemaining,RowRemainingCount,Cell,Spare,out var BeyondCount,ColumnEnd,true);
                    (RowRemaining,Spare)=(Spare,RowRemaining);
                    RowRemainingCount=BeyondCount;
                    if(CellCount<3||X<0){ continue; }
                    var SpanMin=Cell[0].U;
                    var SpanMax=Cell[0].U;
                    for(var Index=1;Index<CellCount;Index++){
                        SpanMin=Smaller(SpanMin,Cell[Index].U);
                        SpanMax=Larger(SpanMax,Cell[Index].U);
                    }
                    SpanMin-=this.Min.U;
                    SpanMax-=this.Min.U;
                    if(SpanMax<LogicalScalar.Zero||SpanMin>this.Max.U-this.Min.U){ continue; }
                    if(SpanMin<LogicalScalar.Zero){ SpanMin=LogicalScalar.Zero; }
                    if(SpanMax>this.Max.U-this.Min.U){ SpanMax=this.Max.U-this.Min.U; }
                    var Bottom=(int)Math.Clamp(FloorIndex(SpanMin,CellHeight),0,this.Steps);
                    var Top=(int)Math.Clamp(CeilIndex(SpanMax,CellHeight),Bottom+1,this.Steps+1);
                    var Fine=(int)Math.Clamp(FloorDivide(SpanMax.Raw*FineUnits,CellHeight.Raw),0,int.MaxValue/2);
                    this.AddSpan(X,Z,Bottom,Top,Area,FacesUp?Fine:NoFace,FacesDown?Fine:NoFace);
                }
            }
        }
        /// <summary>
        /// 多角形を、軸(<paramref name="Horizontal"/>なら R、でなければ F)が<paramref name="Plane"/>以下の側と、より大きい側に分ける(Recast の dividePoly)。
        /// 以下の側は<paramref name="Near"/>に、大きい側は<paramref name="Far"/>に書く。平面の上の頂点は両方に入る。戻り値は<paramref name="Near"/>の頂点数。
        /// </summary>
        private static int Divide(LogicalVector3[]Input,int InputCount,LogicalVector3[]Near,LogicalVector3[]Far,out int FarCount,LogicalScalar Plane,bool Horizontal){
            Span<LogicalScalar>Distance=stackalloc LogicalScalar[InputCount];
            for(var Index=0;Index<InputCount;Index++){ Distance[Index]=Plane-(Horizontal?Input[Index].R:Input[Index].F); }
            var NearCount=0;
            FarCount=0;
            for(int Index=0,Previous=InputCount-1;Index<InputCount;Previous=Index,Index++){
                var PreviousInside=Distance[Previous]>=LogicalScalar.Zero;
                var CurrentInside=Distance[Index]>=LogicalScalar.Zero;
                if(PreviousInside!=CurrentInside){
                    var Fraction=Distance[Previous]/(Distance[Previous]-Distance[Index]);
                    var Crossing=Input[Previous]+(Input[Index]-Input[Previous])*Fraction;
                    Near[NearCount++]=Crossing;
                    Far[FarCount++]=Crossing;
                    if(Distance[Index]>LogicalScalar.Zero){ Near[NearCount++]=Input[Index]; }
                    else if(Distance[Index]<LogicalScalar.Zero){ Far[FarCount++]=Input[Index]; }
                } else {
                    if(Distance[Index]>=LogicalScalar.Zero){
                        Near[NearCount++]=Input[Index];
                        if(Distance[Index]!=LogicalScalar.Zero){ continue; }
                    }
                    Far[FarCount++]=Input[Index];
                }
            }
            return NearCount;
        }
        private static LogicalScalar Smaller(LogicalScalar A,LogicalScalar B)=>A<B?A:B;
        private static LogicalScalar Larger(LogicalScalar A,LogicalScalar B)=>A>B?A:B;
        /// <summary>柱に span を足す。重なる span は1つにまとめ、上端が登れる段差以内なら歩ける印を引き継ぐ(Recast の rcAddSpan)。</summary>
        private void AddSpan(int X,int Z,int Bottom,int Top,int Area,int Up,int Down){
            var Index=X+Z*this.Width;
            var Column=this.Columns[Index]??=[];
            var Merged=new HeightSpan(Bottom,Top,Area,Up,Down);
            var Insert=0;
            for(var Position=0;Position<Column.Count;){
                var Existing=Column[Position];
                if(Existing.Max<Merged.Min){
                    Position++;
                    Insert=Position;
                    continue;
                }
                if(Existing.Min>Merged.Max){ break; }
                if(Existing.Min<Merged.Min){ Merged.Min=Existing.Min; }
                if(Existing.Max>Merged.Max){ Merged.Max=Existing.Max; }
                if(Math.Abs(Merged.Max-Existing.Max)<=this.WalkableClimb){ Merged.Area=Math.Max(Merged.Area,Existing.Area); }
                Merged.Up=Math.Max(Merged.Up,Existing.Up);
                Merged.Down=Math.Max(Merged.Down,Existing.Down);
                Column.RemoveAt(Position);
            }
            Column.Insert(Insert,Merged);
        }
        // ---- 2. 歩ける面の絞り込み ----
        /// <summary>
        /// 閉じた形(箱、壁)が床に載っていると、その底面(下向き)が床の上面(上向き)と同じ高さで重なり、形の内側にも床の面が残る。内側は行き着けない島で、
        /// 立っていられる場所でもないので、下向きの面が上向きの歩ける面と同じ高さ(または上)にある span は歩けないことにする。
        /// 薄い板は、底面が上面より下にあるので歩ける。
        /// </summary>
        public void FilterCoincidentCeilings(){
            foreach(var Column in this.Columns){
                if(Column is null){ continue; }
                for(var Index=0;Index<Column.Count;Index++){
                    var Span=Column[Index];
                    if(Span.Area==NoArea||Span.Up==NoFace||Span.Down==NoFace||Span.Down<Span.Up-CoincidentTolerance){ continue; }
                    Span.Area=NoArea;
                    Column[Index]=Span;
                }
            }
        }
        public void FilterLowHangingObstacles(){
            foreach(var Column in this.Columns){
                if(Column is null){ continue; }
                var PreviousWalkable=false;
                var PreviousArea=NoArea;
                var PreviousMax=0;
                for(var Index=0;Index<Column.Count;Index++){
                    var Span=Column[Index];
                    var Walks=Span.Area!=NoArea;
                    // 歩ける span のすぐ上にあって、登れる高さしか違わない歩けない span は、歩ける(低い出っ張り)。
                    if(!Walks&&PreviousWalkable&&Math.Abs(Span.Max-PreviousMax)<=this.WalkableClimb){
                        Span.Area=PreviousArea;
                        Column[Index]=Span;
                    }
                    PreviousWalkable=Walks;
                    PreviousArea=Span.Area;
                    PreviousMax=Span.Max;
                }
            }
        }
        public void FilterLedges(){
            // 隣の柱の判定は、この関数が書き換える前の値で行う(順序に依存しない)。
            var Remove=new bool[this.Columns.Length];
            List<(int Column,int Span)>Ledges=[];
            for(var Z=0;Z<this.Depth;Z++){
                for(var X=0;X<this.Width;X++){
                    var Column=this.Columns[X+Z*this.Width];
                    if(Column is null){ continue; }
                    for(var Index=0;Index<Column.Count;Index++){
                        var Span=Column[Index];
                        if(Span.Area==NoArea){ continue; }
                        var Bottom=Span.Max;
                        var Top=Index+1<Column.Count?Column[Index+1].Min:TopLimit;
                        var LowestNeighbour=TopLimit;
                        var AccessibleMin=Span.Max;
                        var AccessibleMax=Span.Max;
                        for(var Direction=0;Direction<4;Direction++){
                            var NeighbourX=X+DirectionX[Direction];
                            var NeighbourZ=Z+DirectionZ[Direction];
                            if(NeighbourX<0||NeighbourZ<0||NeighbourX>=this.Width||NeighbourZ>=this.Depth){
                                LowestNeighbour=Math.Min(LowestNeighbour,-this.WalkableClimb-Bottom);
                                continue;
                            }
                            var Neighbour=this.Columns[NeighbourX+NeighbourZ*this.Width];
                            // 無限の下から最初の span まで。
                            var NeighbourBottom=-this.WalkableClimb;
                            var NeighbourTop=Neighbour is { Count:>0 }?Neighbour[0].Min:TopLimit;
                            if(Math.Min(Top,NeighbourTop)-Math.Max(Bottom,NeighbourBottom)>this.WalkableHeight){
                                LowestNeighbour=Math.Min(LowestNeighbour,NeighbourBottom-Bottom);
                            }
                            if(Neighbour is null){ continue; }
                            for(var Other=0;Other<Neighbour.Count;Other++){
                                NeighbourBottom=Neighbour[Other].Max;
                                NeighbourTop=Other+1<Neighbour.Count?Neighbour[Other+1].Min:TopLimit;
                                if(Math.Min(Top,NeighbourTop)-Math.Max(Bottom,NeighbourBottom)>this.WalkableHeight){
                                    LowestNeighbour=Math.Min(LowestNeighbour,NeighbourBottom-Bottom);
                                    if(Math.Abs(NeighbourBottom-Bottom)<=this.WalkableClimb){
                                        AccessibleMin=Math.Min(AccessibleMin,NeighbourBottom);
                                        AccessibleMax=Math.Max(AccessibleMax,NeighbourBottom);
                                    }
                                }
                            }
                        }
                        // 一番低い隣へ登れない高さだけ落ちるなら崖のふち。隣どうしの高さの差が大きすぎるなら険しい坂。
                        if(LowestNeighbour<-this.WalkableClimb||AccessibleMax-AccessibleMin>this.WalkableClimb){ Ledges.Add((X+Z*this.Width,Index)); }
                    }
                }
            }
            foreach(var (ColumnIndex,SpanIndex) in Ledges){
                var Column=this.Columns[ColumnIndex]!;
                var Span=Column[SpanIndex];
                Span.Area=NoArea;
                Column[SpanIndex]=Span;
            }
        }
        public void FilterLowHeightSpans(){
            foreach(var Column in this.Columns){
                if(Column is null){ continue; }
                for(var Index=0;Index<Column.Count;Index++){
                    var Span=Column[Index];
                    var Top=Index+1<Column.Count?Column[Index+1].Min:TopLimit;
                    if(Top-Span.Max<this.WalkableHeight){
                        Span.Area=NoArea;
                        Column[Index]=Span;
                    }
                }
            }
        }
        // ---- 3. 開いた空間 ----
        public void BuildCompactHeightfield(){
            this.CellStart=new int[this.Width*this.Depth];
            this.CellCount=new int[this.Width*this.Depth];
            List<int>Floors=[];
            List<int>Clearances=[];
            List<int>Areas=[];
            for(var Cell=0;Cell<this.Columns.Length;Cell++){
                this.CellStart[Cell]=Floors.Count;
                var Column=this.Columns[Cell];
                if(Column is null){ continue; }
                for(var Index=0;Index<Column.Count;Index++){
                    var Span=Column[Index];
                    if(Span.Area==NoArea){ continue; }
                    var Top=Index+1<Column.Count?Column[Index+1].Min:TopLimit;
                    Floors.Add(Span.Max);
                    Clearances.Add(Top-Span.Max);
                    Areas.Add(Span.Area);
                    this.CellCount[Cell]++;
                }
            }
            this.SpanY=[.. Floors];
            this.SpanClearance=[.. Clearances];
            this.SpanArea=[.. Areas];
            this.SpanConnection=new int[Floors.Count*4];
            Array.Fill(this.SpanConnection,-1);
            for(var Z=0;Z<this.Depth;Z++){
                for(var X=0;X<this.Width;X++){
                    var Cell=X+Z*this.Width;
                    for(var Index=this.CellStart[Cell];Index<this.CellStart[Cell]+this.CellCount[Cell];Index++){
                        for(var Direction=0;Direction<4;Direction++){
                            var NeighbourX=X+DirectionX[Direction];
                            var NeighbourZ=Z+DirectionZ[Direction];
                            if(NeighbourX<0||NeighbourZ<0||NeighbourX>=this.Width||NeighbourZ>=this.Depth){ continue; }
                            var NeighbourCell=NeighbourX+NeighbourZ*this.Width;
                            for(var Other=this.CellStart[NeighbourCell];Other<this.CellStart[NeighbourCell]+this.CellCount[NeighbourCell];Other++){
                                var Bottom=Math.Max(this.SpanY[Index],this.SpanY[Other]);
                                var Top=Math.Min(this.SpanY[Index]+this.SpanClearance[Index],this.SpanY[Other]+this.SpanClearance[Other]);
                                if(Top-Bottom>=this.WalkableHeight&&Math.Abs(this.SpanY[Other]-this.SpanY[Index])<=this.WalkableClimb){
                                    this.SpanConnection[Index*4+Direction]=Other;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }
        // ---- 4. 半径ぶん縁から離す ----
        /// <summary>
        /// 縁(4方向のどれかに接続の無い span)からの距離を、直交 2・斜め 3(= 半セルを1とした 2/3 の距離変換)で求め、半径に届かない span を外す(Recast の rcErodeWalkableArea)。
        /// 縁の span 自身も外れる。
        /// </summary>
        public void Erode(){
            if(this.WalkableRadius<=0){ return; }
            var Count=this.SpanY.Length;
            var Distance=new int[Count];
            Array.Fill(Distance,255);
            for(var Index=0;Index<Count;Index++){
                if(this.SpanArea[Index]==NoArea){
                    Distance[Index]=0;
                    continue;
                }
                var Neighbours=0;
                for(var Direction=0;Direction<4;Direction++){
                    var Other=this.SpanConnection[Index*4+Direction];
                    if(Other>=0&&this.SpanArea[Other]!=NoArea){ Neighbours++; }
                }
                if(Neighbours!=4){ Distance[Index]=0; }
            }
            for(var Z=0;Z<this.Depth;Z++){
                for(var X=0;X<this.Width;X++){
                    var Cell=X+Z*this.Width;
                    for(var Index=this.CellStart[Cell];Index<this.CellStart[Cell]+this.CellCount[Cell];Index++){
                        this.Relax(Distance,Index,0,3);
                        this.Relax(Distance,Index,3,2);
                    }
                }
            }
            for(var Z=this.Depth-1;Z>=0;Z--){
                for(var X=this.Width-1;X>=0;X--){
                    var Cell=X+Z*this.Width;
                    for(var Index=this.CellStart[Cell]+this.CellCount[Cell]-1;Index>=this.CellStart[Cell];Index--){
                        this.Relax(Distance,Index,2,1);
                        this.Relax(Distance,Index,1,0);
                    }
                }
            }
            var Threshold=this.WalkableRadius*2;
            for(var Index=0;Index<Count;Index++){
                if(Distance[Index]<Threshold){ this.SpanArea[Index]=NoArea; }
            }
        }
        /// <summary>方向<paramref name="Direction"/>の隣(+2)と、その隣の<paramref name="DiagonalDirection"/>の隣(+3)から、距離を縮める。</summary>
        private void Relax(int[]Distance,int Index,int Direction,int DiagonalDirection){
            var Other=this.SpanConnection[Index*4+Direction];
            if(Other<0){ return; }
            Distance[Index]=Math.Min(Distance[Index],Math.Min(Distance[Other]+2,255));
            var Diagonal=this.SpanConnection[Other*4+DiagonalDirection];
            if(Diagonal>=0){ Distance[Index]=Math.Min(Distance[Index],Math.Min(Distance[Diagonal]+3,255)); }
        }
        // ---- 5. 多角形 ----
        public List<NavPolygon>BuildPolygons(){
            var Polygons=new List<NavPolygon>();
            this.PolygonOfSpan=new int[this.SpanY.Length];
            Array.Fill(this.PolygonOfSpan,-1);
            for(var Z=0;Z<this.Depth;Z++){
                for(var X=0;X<this.Width;X++){
                    var Cell=X+Z*this.Width;
                    for(var Index=this.CellStart[Cell];Index<this.CellStart[Cell]+this.CellCount[Cell];Index++){
                        if(this.SpanArea[Index]==NoArea||this.PolygonOfSpan[Index]>=0){ continue; }
                        var Floor=this.SpanY[Index];
                        // まず +R へ、同じ高さの未割り当ての span が続く限り伸ばし、次に +F へ、その幅ぜんぶで続く限り伸ばす。
                        var EndX=X;
                        while(EndX+1<this.Width&&this.FindFree(EndX+1,Z,Floor)>=0){ EndX++; }
                        var EndZ=Z;
                        while(EndZ+1<this.Depth){
                            var All=true;
                            for(var Column=X;Column<=EndX&&All;Column++){ All=this.FindFree(Column,EndZ+1,Floor)>=0; }
                            if(!All){ break; }
                            EndZ++;
                        }
                        var Polygon=Polygons.Count;
                        for(var Row=Z;Row<=EndZ;Row++){
                            for(var Column=X;Column<=EndX;Column++){ this.PolygonOfSpan[this.FindFree(Column,Row,Floor)]=Polygon; }
                        }
                        Polygons.Add(new NavPolygon(X,Z,EndX+1,EndZ+1,Floor));
                    }
                }
            }
            return Polygons;
        }
        private int[]PolygonOfSpan=[];
        /// <summary>そのセルの、高さ<paramref name="Floor"/>の歩ける未割り当ての span。無ければ -1。</summary>
        private int FindFree(int X,int Z,int Floor){
            var Cell=X+Z*this.Width;
            for(var Index=this.CellStart[Cell];Index<this.CellStart[Cell]+this.CellCount[Cell];Index++){
                if(this.SpanY[Index]==Floor&&this.SpanArea[Index]!=NoArea&&this.PolygonOfSpan[Index]<0){ return Index; }
            }
            return -1;
        }
        private int SpanOf(int X,int Z,int Polygon){
            var Cell=X+Z*this.Width;
            for(var Index=this.CellStart[Cell];Index<this.CellStart[Cell]+this.CellCount[Cell];Index++){
                if(this.PolygonOfSpan[Index]==Polygon){ return Index; }
            }
            return -1;
        }
        // ---- 6. 接続 ----
        /// <summary>
        /// 長方形の +R の辺と +F の辺(反対側の辺は隣が数える)を歩き、接続のあるセルの並びのうち同じ隣の多角形に続く区間を portal にする。
        /// </summary>
        public List<NavPortal>BuildPortals(List<NavPolygon>Polygons){
            var Portals=new List<NavPortal>();
            for(var Index=0;Index<Polygons.Count;Index++){
                var Polygon=Polygons[Index];
                var Run=-1;
                var RunStart=0;
                for(var Z=Polygon.MinZ;Z<=Polygon.MaxZ;Z++){
                    var Next=Z<Polygon.MaxZ?this.NeighbourPolygon(Polygon.MaxX-1,Z,Index,2,Polygons,Polygon.MaxX,true):-1;
                    if(Next==Run&&Next>=0){ continue; }
                    if(Run>=0){ Portals.Add(new NavPortal(Index,Run,Polygon.MaxX,RunStart,Polygon.MaxX,Z)); }
                    Run=Next;
                    RunStart=Z;
                }
                Run=-1;
                RunStart=0;
                for(var X=Polygon.MinX;X<=Polygon.MaxX;X++){
                    var Next=X<Polygon.MaxX?this.NeighbourPolygon(X,Polygon.MaxZ-1,Index,1,Polygons,Polygon.MaxZ,false):-1;
                    if(Next==Run&&Next>=0){ continue; }
                    if(Run>=0){ Portals.Add(new NavPortal(Index,Run,RunStart,Polygon.MaxZ,X,Polygon.MaxZ)); }
                    Run=Next;
                    RunStart=X;
                }
            }
            return Portals;
        }
        /// <summary>
        /// 長方形<paramref name="Polygon"/>の端のセル(<paramref name="X"/>,<paramref name="Z"/>)から<paramref name="Direction"/>へ接続した隣の多角形。無い、または隣が境の線から始まらない
        /// 長方形(同じ柱の別の層が重なる特殊な場合)なら -1。
        /// </summary>
        private int NeighbourPolygon(int X,int Z,int Polygon,int Direction,List<NavPolygon>Polygons,int BorderLine,bool Right){
            var Span=this.SpanOf(X,Z,Polygon);
            if(Span<0){ return -1; }
            var Other=this.SpanConnection[Span*4+Direction];
            if(Other<0){ return -1; }
            var Found=this.PolygonOfSpan[Other];
            if(Found<0||Found==Polygon){ return -1; }
            var Rectangle=Polygons[Found];
            return (Right?Rectangle.MinX:Rectangle.MinZ)==BorderLine?Found:-1;
        }
    }
}
