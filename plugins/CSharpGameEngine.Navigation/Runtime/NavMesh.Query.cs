using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.Navigation;
/// <summary>
/// <see cref="NavMesh.TryFindRoute"/>の結果。<see cref="Points"/>は出発点の次から、目的地(またはそこに一番近い行ける点)までの折れ線の頂点で、
/// 高さは各多角形の床。<see cref="IsPartial"/>は目的地に着けず、行ける中で一番近い点までの経路であることを示す。
/// </summary>
public sealed class NavRoute(IReadOnlyList<LogicalVector3>Points,bool IsPartial) {
    public IReadOnlyList<LogicalVector3>Points { get; }=Points;
    public bool IsPartial { get; }=IsPartial;
}
public sealed partial class NavMesh {
    /// <summary>1セルを分けて座標を持つ細かさ。経路の計算は、この単位の整数だけで行う。</summary>
    private const int SubUnits=16;
    private const int BucketShift=4;
    /// <summary>座標がこの範囲(サブ単位)の外なら端に寄せる。二乗しても桁あふれしないための上限。</summary>
    private const long CoordinateLimit=1L<<29;
    private int[][]PortalsOfPolygon=[];
    private int[][]BucketPolygons=[];
    private int BucketColumns;
    private int BucketRows;
    private int HeightSubUnitsPerStep;
    private int VerticalRangeSteps;
    private int ProjectionRadiusSubUnits;
    private void BuildQueryTables(){
        var Counts=new int[this.PolygonArray.Length];
        foreach(var Portal in this.PortalArray){
            Counts[Portal.PolygonA]++;
            Counts[Portal.PolygonB]++;
        }
        this.PortalsOfPolygon=new int[this.PolygonArray.Length][];
        for(var Index=0;Index<Counts.Length;Index++){ this.PortalsOfPolygon[Index]=new int[Counts[Index]]; }
        Array.Clear(Counts);
        for(var Index=0;Index<this.PortalArray.Length;Index++){
            var Portal=this.PortalArray[Index];
            this.PortalsOfPolygon[Portal.PolygonA][Counts[Portal.PolygonA]++]=Index;
            this.PortalsOfPolygon[Portal.PolygonB][Counts[Portal.PolygonB]++]=Index;
        }
        this.BucketColumns=((this.GridWidth-1)>>BucketShift)+1;
        this.BucketRows=((this.GridDepth-1)>>BucketShift)+1;
        var Lists=new List<int>?[this.BucketColumns*this.BucketRows];
        for(var Index=0;Index<this.PolygonArray.Length;Index++){
            var Polygon=this.PolygonArray[Index];
            for(var Row=Polygon.MinZ>>BucketShift;Row<=(Polygon.MaxZ-1)>>BucketShift;Row++){
                for(var Column=Polygon.MinX>>BucketShift;Column<=(Polygon.MaxX-1)>>BucketShift;Column++){
                    (Lists[Row*this.BucketColumns+Column]??=[]).Add(Index);
                }
            }
        }
        this.BucketPolygons=new int[Lists.Length][];
        for(var Index=0;Index<Lists.Length;Index++){ this.BucketPolygons[Index]=Lists[Index] is { }List?[.. List]:[]; }
        // 段の高さを、水平のサブ単位に直した値(整数)。床の高さの差を、水平の距離と同じ物差しで測るために使う。
        this.HeightSubUnitsPerStep=LogicalScalar.TryDivide(this.CellHeight*LogicalScalar.FromInt64(SubUnits),this.CellSize,out var Ratio)&&Ratio.TryToScaledInt64(0,out var Rounded)?(int)Math.Clamp(Rounded,1,SubUnits*64):1;
        this.VerticalRangeSteps=LogicalScalar.TryDivide(this.Settings.AgentHeight,this.CellHeight,out var Steps)&&Steps.TryToScaledInt64(0,out var StepCount)?(int)Math.Clamp(StepCount+1,1,1_000_000):1;
        var Radius=this.Settings.AgentRadius*LogicalScalar.FromInt64(2)+this.CellSize;
        this.ProjectionRadiusSubUnits=LogicalScalar.TryDivide(Radius*LogicalScalar.FromInt64(SubUnits),this.CellSize,out var Cells)&&Cells.TryToScaledInt64(0,out var CellCount)?(int)Math.Clamp(CellCount+1,SubUnits,SubUnits*256):SubUnits;
    }
    /// <summary>世界の座標を、原点からのサブ単位(切り捨て)にする。</summary>
    private long ToSubUnits(LogicalScalar Value,LogicalScalar OriginValue){
        var Numerator=(Value.Raw-OriginValue.Raw)*SubUnits;
        return Math.Clamp(FloorDivide(Numerator,this.CellSize.Raw),-CoordinateLimit,CoordinateLimit);
    }
    private long ToSteps(LogicalScalar Value)=>Math.Clamp(FloorDivide(Value.Raw-this.Origin.U.Raw,this.CellHeight.Raw),-CoordinateLimit,CoordinateLimit);
    private static long FloorDivide(Int128 Numerator,Int128 Denominator){
        var Quotient=Numerator/Denominator;
        if((Numerator%Denominator)!=0&&((Numerator<0)!=(Denominator<0))){ Quotient--; }
        return Quotient>long.MaxValue?long.MaxValue:Quotient<long.MinValue?long.MinValue:(long)Quotient;
    }
    private LogicalScalar FromSubUnits(long Value,LogicalScalar OriginValue)=>LogicalScalar.FromRaw(OriginValue.Raw+this.CellSize.Raw*Value/SubUnits);
    private LogicalVector3 ToWorld(long X,long Z,int Y)=>new(this.FromSubUnits(X,this.Origin.R),LogicalScalar.FromRaw(this.Origin.U.Raw+this.CellHeight.Raw*Y),this.FromSubUnits(Z,this.Origin.F));
    /// <summary>
    /// 床の点<paramref name="Point"/>(足の位置)に一番近い、多角形の中の点を探す。点が多角形の中にあれば、その中で床の高さが一番近いものを選ぶ。
    /// 外なら、水平に<see cref="ProjectionRadius"/>以内で床が歩く者の背丈の範囲に入る、一番近い多角形を選ぶ。無ければ false。
    /// </summary>
    public bool TryLocate(LogicalVector3 Point,out int Polygon,out LogicalVector3 Projected){
        Polygon=-1;
        Projected=default;
        var X=this.ToSubUnits(Point.R,this.Origin.R);
        var Z=this.ToSubUnits(Point.F,this.Origin.F);
        var Step=this.ToSteps(Point.U);
        if(!this.Nearest(X,Z,Step,this.ProjectionRadiusSubUnits,out Polygon)){ return false; }
        var Found=this.PolygonArray[Polygon];
        Projected=this.ToWorld(Math.Clamp(X,(long)Found.MinX*SubUnits,(long)Found.MaxX*SubUnits),Math.Clamp(Z,(long)Found.MinZ*SubUnits,(long)Found.MaxZ*SubUnits),Found.Y);
        return true;
    }
    /// <summary>水平にこの距離(m)以内の多角形だけを、<see cref="TryLocate"/>と経路の出発点・目的地の置き場にする。</summary>
    public LogicalScalar ProjectionRadius=>FromRatio(this.ProjectionRadiusSubUnits,this.CellSize);
    private static LogicalScalar FromRatio(int SubUnitCount,LogicalScalar CellSize)=>LogicalScalar.FromRaw(CellSize.Raw*SubUnitCount/SubUnits);
    private bool Nearest(long X,long Z,long Step,int RadiusSubUnits,out int Best){
        Best=-1;
        var BestDistance=long.MaxValue;
        var RadiusSquared=(long)RadiusSubUnits*RadiusSubUnits;
        var Cells=(RadiusSubUnits/SubUnits)+1;
        var CellX=X>>4;
        var CellZ=Z>>4;
        var FromColumn=(int)Math.Clamp((CellX-Cells)>>BucketShift,0,this.BucketColumns-1);
        var ToColumn=(int)Math.Clamp((CellX+Cells)>>BucketShift,0,this.BucketColumns-1);
        var FromRow=(int)Math.Clamp((CellZ-Cells)>>BucketShift,0,this.BucketRows-1);
        var ToRow=(int)Math.Clamp((CellZ+Cells)>>BucketShift,0,this.BucketRows-1);
        // 検索範囲が格子の外に完全に出ているときは、端の区画だけを見ることになるので、水平の距離で弾く。
        for(var Row=FromRow;Row<=ToRow;Row++){
            for(var Column=FromColumn;Column<=ToColumn;Column++){
                foreach(var Candidate in this.BucketPolygons[Row*this.BucketColumns+Column]){
                    var Polygon=this.PolygonArray[Candidate];
                    var Rise=Math.Abs(Polygon.Y-Step);
                    if(Rise>this.VerticalRangeSteps){ continue; }
                    var Horizontal=HorizontalDistanceSquared(Polygon,X,Z);
                    if(Horizontal>RadiusSquared){ continue; }
                    var Vertical=Rise*this.HeightSubUnitsPerStep;
                    var Distance=Horizontal+Vertical*Vertical;
                    if(Distance<BestDistance||(Distance==BestDistance&&Candidate<Best)){
                        BestDistance=Distance;
                        Best=Candidate;
                    }
                }
            }
        }
        return Best>=0;
    }
    private static long HorizontalDistanceSquared(NavPolygon Polygon,long X,long Z){
        var Dx=Math.Max(Math.Max((long)Polygon.MinX*SubUnits-X,X-(long)Polygon.MaxX*SubUnits),0);
        var Dz=Math.Max(Math.Max((long)Polygon.MinZ*SubUnits-Z,Z-(long)Polygon.MaxZ*SubUnits),0);
        return Dx*Dx+Dz*Dz;
    }
    private long DistanceToPointSquared(NavPolygon Polygon,long X,long Z,long Step){
        var Vertical=Math.Abs(Polygon.Y-Step)*this.HeightSubUnitsPerStep;
        return HorizontalDistanceSquared(Polygon,X,Z)+Vertical*Vertical;
    }
    private static long IntegerSquareRoot(long Value){
        if(Value<=0){ return 0; }
        var Remainder=(ulong)Value;
        ulong Result=0;
        var Bit=1UL<<62;
        while(Bit>Remainder){ Bit>>=2; }
        while(Bit!=0){
            if(Remainder>=Result+Bit){
                Remainder-=Result+Bit;
                Result=(Result>>1)+Bit;
            } else {
                Result>>=1;
            }
            Bit>>=2;
        }
        return (long)Result;
    }
    private static long Distance(long Ax,long Az,long Bx,long Bz){
        var Dx=Ax-Bx;
        var Dz=Az-Bz;
        return IntegerSquareRoot(Dx*Dx+Dz*Dz);
    }
    /// <summary>
    /// <paramref name="Start"/>から<paramref name="Goal"/>(どちらも足の位置)への経路を探す。多角形の隣接を A* で探し、通る境を引っ張って(funnel)折れ線にする。
    /// 出発点が多角形の近く(<see cref="ProjectionRadius"/>)に無ければ false。目的地が近くに無い、または着けないときは、行ける多角形のうち目的地に一番近い点までの
    /// 経路(<see cref="NavRoute.IsPartial"/>)を返す。整数だけで解くので、同じ入力から同じ経路になる。
    /// </summary>
    public bool TryFindRoute(LogicalVector3 Start,LogicalVector3 Goal,out NavRoute Route){
        Route=new NavRoute([],false);
        var StartX=this.ToSubUnits(Start.R,this.Origin.R);
        var StartZ=this.ToSubUnits(Start.F,this.Origin.F);
        if(!this.Nearest(StartX,StartZ,this.ToSteps(Start.U),this.ProjectionRadiusSubUnits,out var StartPolygon)){ return false; }
        var GoalX=this.ToSubUnits(Goal.R,this.Origin.R);
        var GoalZ=this.ToSubUnits(Goal.F,this.Origin.F);
        var GoalStep=this.ToSteps(Goal.U);
        var GoalPolygon=this.Nearest(GoalX,GoalZ,GoalStep,this.ProjectionRadiusSubUnits,out var Located)?Located:-1;
        var Begin=this.Clamp(StartPolygon,StartX,StartZ);
        var Count=this.PolygonArray.Length;
        var Cost=new long[Count];
        var Parent=new int[Count];
        var ParentPortal=new int[Count];
        var PositionX=new long[Count];
        var PositionZ=new long[Count];
        var Closed=new bool[Count];
        var Seen=new bool[Count];
        Array.Fill(Parent,-1);
        Array.Fill(ParentPortal,-1);
        var Open=new MinHeap();
        Seen[StartPolygon]=true;
        PositionX[StartPolygon]=Begin.X;
        PositionZ[StartPolygon]=Begin.Z;
        var GoalAim=GoalPolygon>=0?this.Clamp(GoalPolygon,GoalX,GoalZ):(X:GoalX,Z:GoalZ);
        Open.Push(Distance(Begin.X,Begin.Z,GoalAim.X,GoalAim.Z),StartPolygon);
        var Closest=StartPolygon;
        var ClosestDistance=long.MaxValue;
        var Reached=false;
        while(Open.TryPop(out var Current)){
            if(Closed[Current]){ continue; }
            Closed[Current]=true;
            var CurrentDistance=this.DistanceToPointSquared(this.PolygonArray[Current],GoalX,GoalZ,GoalStep);
            if(CurrentDistance<ClosestDistance){
                ClosestDistance=CurrentDistance;
                Closest=Current;
            }
            if(Current==GoalPolygon){
                Reached=true;
                break;
            }
            foreach(var PortalIndex in this.PortalsOfPolygon[Current]){
                var Portal=this.PortalArray[PortalIndex];
                var Other=Portal.PolygonA==Current?Portal.PolygonB:Portal.PolygonA;
                if(Closed[Other]){ continue; }
                // 境のうち、今の位置から目的地への最短の通り方に一番近い点を通るとして、その点までの費用を測る。目的地の多角形に入るときは、目的地までの残りも足す。
                // (境の真ん中だと、大きな長方形どうしでは遠い側を回る経路を選んでしまう。)
                var (MiddleX,MiddleZ)=Crossing(Portal,PositionX[Current],PositionZ[Current],GoalAim.X,GoalAim.Z);
                var Entry=Cost[Current]+Distance(PositionX[Current],PositionZ[Current],MiddleX,MiddleZ);
                long NextCost;
                long Heuristic;
                long NextX=MiddleX;
                long NextZ=MiddleZ;
                if(Other==GoalPolygon){
                    NextCost=Entry+Distance(MiddleX,MiddleZ,GoalAim.X,GoalAim.Z);
                    Heuristic=0;
                    NextX=GoalAim.X;
                    NextZ=GoalAim.Z;
                } else {
                    NextCost=Entry;
                    Heuristic=Distance(MiddleX,MiddleZ,GoalAim.X,GoalAim.Z);
                }
                if(Seen[Other]&&NextCost>=Cost[Other]){ continue; }
                Seen[Other]=true;
                Cost[Other]=NextCost;
                Parent[Other]=Current;
                ParentPortal[Other]=PortalIndex;
                PositionX[Other]=NextX;
                PositionZ[Other]=NextZ;
                Open.Push(NextCost+Heuristic,Other);
            }
        }
        var End=Reached?GoalPolygon:Closest;
        var EndPoint=Reached?GoalAim:this.Clamp(End,GoalX,GoalZ);
        // 終わりから出発へ辿って、多角形の列(回廊)と、通る境を集める。
        List<int>Corridor=[];
        List<int>Gates=[];
        for(var Node=End;Node>=0;Node=Parent[Node]){
            Corridor.Add(Node);
            if(Parent[Node]>=0){ Gates.Add(ParentPortal[Node]); }
        }
        Corridor.Reverse();
        Gates.Reverse();
        Route=new NavRoute(this.Straighten(Corridor,Gates,Begin,EndPoint),!Reached);
        return true;
    }
    /// <summary>
    /// 点<paramref name="Px"/>,<paramref name="Pz"/>から境を通って点<paramref name="Gx"/>,<paramref name="Gz"/>へ行く最短の折れ線が、境を横切る点(境の線分の上)。
    /// 両方が境の同じ側にあるときは、目的地を境の線について折り返した点への直線と交わる所(光の反射と同じ)。サブ単位の整数だけで求める。
    /// </summary>
    private static (long X,long Z)Crossing(NavPortal Portal,long Px,long Pz,long Gx,long Gz){
        var Vertical=Portal.X0==Portal.X1;
        // 境の線に垂直な軸を「縦」、沿う軸を「横」として同じ式で解く。
        var Line=(Vertical?(long)Portal.X0:Portal.Z0)*SubUnits;
        var From=Vertical?Px:Pz;
        var FromAlong=Vertical?Pz:Px;
        var To=Vertical?Gx:Gz;
        var ToAlong=Vertical?Gz:Gx;
        var Low=(Vertical?(long)Portal.Z0:Portal.X0)*SubUnits;
        var High=(Vertical?(long)Portal.Z1:Portal.X1)*SubUnits;
        var SideFrom=From-Line;
        var SideTo=To-Line;
        var Mirrored=(SideFrom>=0&&SideTo>=0)||(SideFrom<=0&&SideTo<=0)?2*Line-To:To;
        var Along=FromAlong;
        var Run=Mirrored-From;
        if(Run!=0){ Along=FromAlong+(ToAlong-FromAlong)*(Line-From)/Run; }
        Along=Math.Clamp(Along,Low,High);
        return Vertical?(Line,Along):(Along,Line);
    }
    private (long X,long Z)Clamp(int Polygon,long X,long Z){
        var Rectangle=this.PolygonArray[Polygon];
        return (Math.Clamp(X,(long)Rectangle.MinX*SubUnits,(long)Rectangle.MaxX*SubUnits),Math.Clamp(Z,(long)Rectangle.MinZ*SubUnits,(long)Rectangle.MaxZ*SubUnits));
    }
    /// <summary>
    /// 回廊の境を、出発点から目的地へ引っ張って、角だけの折れ線にする(Mikko Mononen の simple stupid funnel)。座標はサブ単位の整数なので、面積の符号の判定は厳密。
    /// 面積の符号は Detour の dtTriArea2D と同じで、軸は R が右、F が上(数学の向き。+R へ進むとき、左は +F の側)。歩く向きに対して左・右の端を境から決める。
    /// </summary>
    private List<LogicalVector3> Straighten(List<int>Corridor,List<int>Gates,(long X,long Z)Begin,(long X,long Z)End){
        List<LogicalVector3>Points=[];
        var Last=Corridor[^1];
        if(Gates.Count==0){
            AddPoint(Points,this.ToWorld(End.X,End.Z,this.PolygonArray[Last].Y));
            return Points;
        }
        var Total=Gates.Count+2;
        var LeftX=new long[Total];
        var LeftZ=new long[Total];
        var RightX=new long[Total];
        var RightZ=new long[Total];
        var Height=new int[Total];
        LeftX[0]=RightX[0]=Begin.X;
        LeftZ[0]=RightZ[0]=Begin.Z;
        Height[0]=this.PolygonArray[Corridor[0]].Y;
        for(var Index=0;Index<Gates.Count;Index++){
            var Portal=this.PortalArray[Gates[Index]];
            var Entered=this.PolygonArray[Corridor[Index+1]];
            long X0=(long)Portal.X0*SubUnits;
            long Z0=(long)Portal.Z0*SubUnits;
            long X1=(long)Portal.X1*SubUnits;
            long Z1=(long)Portal.Z1*SubUnits;
            bool FirstIsLeft;
            if(Portal.X0==Portal.X1){
                // +R へ進むとき、左は F の大きい側(2つ目の端)。-R へ進むなら逆。
                FirstIsLeft=Entered.MinX!=Portal.X0;
            } else {
                // +F へ進むとき、左は R の小さい側(1つ目の端)。-F へ進むなら逆。
                FirstIsLeft=Entered.MinZ==Portal.Z0;
            }
            (LeftX[Index+1],LeftZ[Index+1],RightX[Index+1],RightZ[Index+1])=FirstIsLeft?(X0,Z0,X1,Z1):(X1,Z1,X0,Z0);
            Height[Index+1]=Entered.Y;
        }
        LeftX[Total-1]=RightX[Total-1]=End.X;
        LeftZ[Total-1]=RightZ[Total-1]=End.Z;
        Height[Total-1]=this.PolygonArray[Last].Y;
        long ApexX=Begin.X;
        long ApexZ=Begin.Z;
        long CurrentLeftX=ApexX;
        long CurrentLeftZ=ApexZ;
        long CurrentRightX=ApexX;
        long CurrentRightZ=ApexZ;
        var LeftIndex=0;
        var RightIndex=0;
        var Guard=0;
        var Limit=64+8*Total*Total;
        for(var Index=1;Index<Total;Index++){
            if(++Guard>Limit){ break; }
            if(Area(ApexX,ApexZ,CurrentRightX,CurrentRightZ,RightX[Index],RightZ[Index])<=0){
                if((ApexX==CurrentRightX&&ApexZ==CurrentRightZ)||Area(ApexX,ApexZ,CurrentLeftX,CurrentLeftZ,RightX[Index],RightZ[Index])>0){
                    CurrentRightX=RightX[Index];
                    CurrentRightZ=RightZ[Index];
                    RightIndex=Index;
                } else {
                    AddPoint(Points,this.ToWorld(CurrentLeftX,CurrentLeftZ,Height[LeftIndex]));
                    ApexX=CurrentLeftX;
                    ApexZ=CurrentLeftZ;
                    var Restart=LeftIndex;
                    CurrentRightX=CurrentLeftX;
                    CurrentRightZ=CurrentLeftZ;
                    RightIndex=Restart;
                    Index=Restart;
                    continue;
                }
            }
            if(Area(ApexX,ApexZ,CurrentLeftX,CurrentLeftZ,LeftX[Index],LeftZ[Index])>=0){
                if((ApexX==CurrentLeftX&&ApexZ==CurrentLeftZ)||Area(ApexX,ApexZ,CurrentRightX,CurrentRightZ,LeftX[Index],LeftZ[Index])<0){
                    CurrentLeftX=LeftX[Index];
                    CurrentLeftZ=LeftZ[Index];
                    LeftIndex=Index;
                } else {
                    AddPoint(Points,this.ToWorld(CurrentRightX,CurrentRightZ,Height[RightIndex]));
                    ApexX=CurrentRightX;
                    ApexZ=CurrentRightZ;
                    var Restart=RightIndex;
                    CurrentLeftX=CurrentRightX;
                    CurrentLeftZ=CurrentRightZ;
                    LeftIndex=Restart;
                    Index=Restart;
                    continue;
                }
            }
        }
        AddPoint(Points,this.ToWorld(End.X,End.Z,Height[Total-1]));
        return Points;
    }
    /// <summary>同じ点が続くときは足さない(最後の境が折れ線の最後の点と重なる場合)。</summary>
    private static void AddPoint(List<LogicalVector3>Points,LogicalVector3 Point){
        if(Points.Count==0||Points[^1]!=Point){ Points.Add(Point); }
    }
    /// <summary>三角形(a,b,c)の面積の2倍の符号つきの値(Detour の dtTriArea2D と同じ)。</summary>
    private static long Area(long Ax,long Az,long Bx,long Bz,long Cx,long Cz)=>(Cx-Ax)*(Bz-Az)-(Bx-Ax)*(Cz-Az);
    /// <summary>(費用, 多角形の番号)の小さい順に取り出す二分ヒープ。費用が同じなら番号の小さい方が先で、順序は決定的。</summary>
    private sealed class MinHeap {
        private long[]Keys=new long[64];
        private int[]Values=new int[64];
        private int Count;
        public void Push(long Key,int Value){
            if(this.Count==this.Keys.Length){
                Array.Resize(ref this.Keys,this.Count*2);
                Array.Resize(ref this.Values,this.Count*2);
            }
            var Index=this.Count++;
            while(Index>0){
                var Parent=(Index-1)/2;
                if(!Less(Key,Value,this.Keys[Parent],this.Values[Parent])){ break; }
                this.Keys[Index]=this.Keys[Parent];
                this.Values[Index]=this.Values[Parent];
                Index=Parent;
            }
            this.Keys[Index]=Key;
            this.Values[Index]=Value;
        }
        public bool TryPop(out int Value){
            Value=-1;
            if(this.Count==0){ return false; }
            Value=this.Values[0];
            var Key=this.Keys[--this.Count];
            var Last=this.Values[this.Count];
            var Index=0;
            while(true){
                var Child=Index*2+1;
                if(Child>=this.Count){ break; }
                if(Child+1<this.Count&&Less(this.Keys[Child+1],this.Values[Child+1],this.Keys[Child],this.Values[Child])){ Child++; }
                if(!Less(this.Keys[Child],this.Values[Child],Key,Last)){ break; }
                this.Keys[Index]=this.Keys[Child];
                this.Values[Index]=this.Values[Child];
                Index=Child;
            }
            if(this.Count>0){
                this.Keys[Index]=Key;
                this.Values[Index]=Last;
            }
            return true;
        }
        private static bool Less(long KeyA,int ValueA,long KeyB,int ValueB)=>KeyA<KeyB||(KeyA==KeyB&&ValueA<ValueB);
    }
}
