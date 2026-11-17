using System.Numerics;
using CSharpGameEngine.Rendering.Streaming;
namespace CSharpGameEngine.Rendering;
/// <summary>
/// 仮想シャドウの格子を世界に固定するための位置（ADR-0100 決定 5、M0132）。世界を <see cref="CellSizeMeters"/> の区画に分け、区画の番号と、
/// 描画空間の原点が区画の角からどれだけ離れているか（<see cref="DrawingOriginOffset"/>）を持つ。実行時は描画空間の原点がカメラなので、
/// L5 がカメラの Q64.64 の位置から求めて渡す。描画空間がそのまま世界の位置なら（試験、Editor）、既定の値（区画 0、ずれ 0）のままでよい。
/// </summary>
public readonly record struct VirtualShadowAnchor(long CellX,long CellY,long CellZ,Vector3 DrawingOriginOffset) {
    /// <summary>区画の一辺。区画の中の位置を float で持つ誤差（1,024 m で約 0.06 mm）は、段 0 のテクセル（約 0.5 mm）より小さい。</summary>
    public const float CellSizeMeters=1024f;
}
/// <summary>作るページ 1 つ。<see cref="Projection"/> は描画空間からそのページの正射影の範囲へ写す行列（行ベクトル）。</summary>
public readonly record struct VirtualShadowMapPage(VirtualShadowPageKey Key,int PhysicalIndex,Matrix4x4 Projection);
/// <summary>
/// 仮想シャドウの計画（ADR-0100 決定 1・5、M0132 単位 1）。D3D12 を使わない。
/// <list type="bullet">
/// <item>段は <see cref="LevelCount"/>。段 L は光の向きの平面で幅 8×2^L m の正方形を、128×128 ページ（1 ページ 128×128 テクセル）で覆う。
/// 範囲はカメラを中心に段ごとに動き（ページ表の窓）、ページの番号は世界に固定した光の座標で決まるので、カメラが動いても作ったページは使える。</item>
/// <item>ページの番号 (x, y) は、区画の角からの位置を光の向きの回転で写した座標を、ページの幅で割って切り捨てたもの。段 L+3 のページは (x / 8, y / 8)（切り捨て）。</item>
/// <item>深度は段ごとの範囲（<see cref="DepthRangeMeters"/>）で、中心はカメラの光の深さを範囲の 4 分の 1 で切り捨てた所。中心が変わった段のページは捨てる。</item>
/// <item>全部を捨てるのは、光の向きが <see cref="LightChangeDegrees"/> を超えて変わったときと、区画が変わったとき。</item>
/// <item>物を包む箱（世界に固定した位置）が <see cref="MovementToleranceMeters"/> を超えて動いたら、前と今の箱に重なるページを古くする。形が変わる物（骨のあるメッシュ）は毎フレーム。</item>
/// <item>要求されたページは、選んだ段の 3 段上（粗いページ）も一緒に要求したことにする。作る順は粗い段から。1 フレームに作るのは <c>RenderBudget</c> まで。
/// 要求のうち作れていない（無いか古い）ページが半分を超えるときは、上限を外して 1 フレームで作る。</item>
/// <item>置き場の割り当ては <see cref="PageSlotAllocator"/>（1 ページ 1 枠、使っていないのが最も古いページから追い出す）。</item>
/// </list>
/// </summary>
public sealed class VirtualShadowMapPlanner {
    public const int LevelCount=16;
    public const int PagesPerAxis=128;
    public const int PageTexels=128;
    /// <summary>1 段の仮想の大きさ（テクセル）。</summary>
    public const int VirtualTexels=PagesPerAxis*PageTexels;
    public const int PhysicalPagesPerAxis=64;
    public const int MaximumPhysicalPages=PhysicalPagesPerAxis*PhysicalPagesPerAxis;
    /// <summary>ページ表の長さ（段 × 128 × 128）。値は置き場の番号 + 1（0 なら無い）。</summary>
    public const int TableLength=LevelCount*PagesPerAxis*PagesPerAxis;
    public const int CoarseRequestLevels=3;
    public const int DefaultRenderBudget=64;
    public const float LightChangeDegrees=0.01f;
    public const float MovementToleranceMeters=0.001f;
    private readonly uint[]Table=new uint[TableLength];
    private readonly Matrix4x4[]LevelProjections=new Matrix4x4[LevelCount];
    private readonly int[]MinimumX=new int[LevelCount];
    private readonly int[]MinimumY=new int[LevelCount];
    private readonly float[]DepthCenters=new float[LevelCount];
    private readonly Dictionary<ulong,bool>DirtyPages=[];
    private readonly Dictionary<int,(Vector3 Minimum,Vector3 Maximum,bool CastsShadow)>Previous=[];
    private readonly List<VirtualShadowMapPage>Pages=[];
    private readonly List<PageAllocation>Allocations=[];
    private PageSlotAllocator?Allocator;
    private Matrix4x4 Rotation=Matrix4x4.Identity;
    private Vector3 Light;
    private (long X,long Y,long Z)Cell;
    private bool Started;
    private ulong Frame;
    /// <summary>段 L の幅（メートル）。</summary>
    public static float LevelWidthMeters(int Level)=>8f*MathF.ScaleB(1f,Level);
    /// <summary>段 L の 1 ページの幅（メートル）。</summary>
    public static float PageWidthMeters(int Level)=>LevelWidthMeters(Level)/PagesPerAxis;
    /// <summary>段 L の深度の範囲（メートル）。段の幅の 2 倍、4,096 m 以上。</summary>
    public static float DepthRangeMeters(int Level)=>MathF.Max(4096f,2f*LevelWidthMeters(Level));
    /// <summary>ページ表（段ごとに 128×128、窓の左上から行優先。y は窓の上が 0）。</summary>
    public ReadOnlySpan<uint>PageTable=>this.Table;
    /// <summary>段ごとの、描画空間から窓全体の正射影へ写す行列（行ベクトル）。シェーダーはこれで窓の中の位置（uv）と深度を求める。</summary>
    public ReadOnlySpan<Matrix4x4>Projections=>this.LevelProjections;
    /// <summary>このフレームに作るページ（粗い段から）。</summary>
    public IReadOnlyList<VirtualShadowMapPage>PagesToRender=>this.Pages;
    public VirtualShadowStatistics Statistics { get;private set; }
    /// <summary>置き場の割り当ての報告（このフレームの分）。</summary>
    public StreamingReport Report { get; }=new();
    /// <summary>置き場の数。</summary>
    public int PhysicalPages=>this.Allocator?.Capacity??0;
    /// <summary>全部のページを捨てる（置き場を作り直したときなど）。</summary>
    public void Reset(){
        this.Allocator=null;
        this.DirtyPages.Clear();
        this.Previous.Clear();
        this.Started=false;
    }
    /// <summary>描画空間の位置 <paramref name="Position"/> を含む、段 <paramref name="Level"/> のページ。<see cref="Plan"/> の後に使う（そのフレームの光と格子で求める）。</summary>
    public VirtualShadowPageKey PageOf(int Level,Vector3 Position,VirtualShadowAnchor Anchor)=>KeyOf(Level,this.ToLight(Position,Anchor));
    /// <summary>光の向き <paramref name="TowardLight"/> の回転で求めた、段 <paramref name="Level"/> のページ（計画の前に要求を作る試験と、CPU の答え合わせに使う）。</summary>
    public static VirtualShadowPageKey PageOf(int Level,Vector3 Position,VirtualShadowAnchor Anchor,Vector3 TowardLight)=>KeyOf(Level,Vector3.TransformNormal(Position+Anchor.DrawingOriginOffset,LightRotation(Vector3.Normalize(TowardLight))));
    /// <summary>光の向きの回転（光へ向かう向きを奥に向ける。真上・真下の光では上を z にする）。</summary>
    public static Matrix4x4 LightRotation(Vector3 TowardLight)=>Matrix4x4.CreateLookAt(Vector3.Zero,-TowardLight,MathF.Abs(TowardLight.Y)>0.99f?Vector3.UnitZ:Vector3.UnitY);
    private static VirtualShadowPageKey KeyOf(int Level,Vector3 Light){
        var Width=PageWidthMeters(Level);
        return new(Level,(int)MathF.Floor(Light.X/Width),(int)MathF.Floor(Light.Y/Width));
    }
    /// <summary>ページ表で、ページ <paramref name="Key"/> の場所（窓の外なら -1）。</summary>
    public int TableIndex(VirtualShadowPageKey Key){
        var X=Key.X-this.MinimumX[Key.Level];
        var Y=PagesPerAxis-1-(Key.Y-this.MinimumY[Key.Level]);
        if((uint)X>=PagesPerAxis||(uint)Y>=PagesPerAxis){ return -1; }
        return (Key.Level*PagesPerAxis+Y)*PagesPerAxis+X;
    }
    /// <summary>
    /// 1 フレームの計画。物の動きでページを古くし、要求（と、その粗いページ）に置き場を割り当て、作るページとページ表を決める。
    /// </summary>
    /// <param name="Anchor">格子を世界に固定する位置。</param>
    /// <param name="CameraPosition">カメラの位置（描画空間）。</param>
    /// <param name="TowardLight">光へ向かう向き（描画空間、長さは問わない）。</param>
    /// <param name="Objects">影を落とす物の包む箱（描画空間）。<see cref="VirtualShadowBounds.Id"/> でフレームをまたいで比べる。</param>
    /// <param name="Requests">画面から要求されたページ。</param>
    /// <param name="PhysicalPages">置き場の数（1〜<see cref="MaximumPhysicalPages"/>）。変わったら全部を捨てる。</param>
    /// <param name="RenderBudget">1 フレームに作るページの上限。</param>
    public void Plan(VirtualShadowAnchor Anchor,Vector3 CameraPosition,Vector3 TowardLight,ReadOnlySpan<VirtualShadowBounds>Objects,ReadOnlySpan<VirtualShadowPageKey>Requests,int PhysicalPages,int RenderBudget=DefaultRenderBudget){
        ArgumentOutOfRangeException.ThrowIfLessThan(PhysicalPages,1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(PhysicalPages,MaximumPhysicalPages);
        ArgumentOutOfRangeException.ThrowIfNegative(RenderBudget);
        if(!Finite(CameraPosition)||!Finite(TowardLight)||!Finite(Anchor.DrawingOriginOffset)||TowardLight.LengthSquared()<1e-12f){
            throw new ArgumentException("A finite camera, anchor and nonzero light direction are required.");
        }
        TowardLight=Vector3.Normalize(TowardLight);
        var Cell=(Anchor.CellX,Anchor.CellY,Anchor.CellZ);
        if(!this.Started||this.Allocator is null||this.Allocator.Capacity!=PhysicalPages||Cell!=this.Cell
            ||LightChanged(TowardLight,this.Light)){
            this.Reset();
            this.Allocator=new PageSlotAllocator(PhysicalPages);
            this.Light=TowardLight;
            this.Rotation=LightRotation(TowardLight);
            this.Cell=Cell;
            this.Started=true;
            for(var Level=0;Level<LevelCount;Level++){ this.DepthCenters[Level]=float.NaN; }
        }
        this.Frame++;
        this.Report.Clear();
        this.Pages.Clear();
        Array.Clear(this.Table);
        var Camera=this.ToLight(CameraPosition,Anchor);
        var Invalidated=0;
        for(var Level=0;Level<LevelCount;Level++){
            var Width=PageWidthMeters(Level);
            this.MinimumX[Level]=(int)MathF.Floor(Camera.X/Width)-PagesPerAxis/2;
            this.MinimumY[Level]=(int)MathF.Floor(Camera.Y/Width)-PagesPerAxis/2;
            var Range=DepthRangeMeters(Level);
            var Center=MathF.Floor(Camera.Z/(Range/4f))*(Range/4f);
            if(Center!=this.DepthCenters[Level]){
                if(!float.IsNaN(this.DepthCenters[Level])){ Invalidated+=this.DropLevel(Level); }
                this.DepthCenters[Level]=Center;
            }
            this.LevelProjections[Level]=this.Projection(Anchor,Level,this.MinimumX[Level]*Width,this.MinimumY[Level]*Width,Width*PagesPerAxis);
        }
        Invalidated+=this.InvalidateMoved(Anchor,Objects);
        // 要求を、粗いページも足して重複を除き、粗い段から並べる。
        var Wanted=new SortedSet<VirtualShadowPageKey>(Comparer<VirtualShadowPageKey>.Create(static (A,B)=>
            A.Level!=B.Level?B.Level.CompareTo(A.Level):A.Y!=B.Y?A.Y.CompareTo(B.Y):A.X.CompareTo(B.X)));
        foreach(var Key in Requests){
            if((uint)Key.Level>=LevelCount){ continue; }
            if(this.TableIndex(Key)>=0){ Wanted.Add(Key); }
            var Coarse=Math.Min(Key.Level+CoarseRequestLevels,LevelCount-1);
            if(Coarse!=Key.Level){
                var Shift=Coarse-Key.Level;
                var Parent=new VirtualShadowPageKey(Coarse,Key.X>>Shift,Key.Y>>Shift);
                if(this.TableIndex(Parent)>=0){ Wanted.Add(Parent); }
            }
        }
        var Keys=new ulong[Wanted.Count];
        var Missing=0;
        var Index=0;
        foreach(var Key in Wanted){
            Keys[Index++]=Pack(Key);
            if(!this.Allocator!.TryGetSlot(Pack(Key),out _)||this.DirtyPages.ContainsKey(Pack(Key))){ Missing++; }
        }
        var Budget=Missing*2>Wanted.Count?int.MaxValue:RenderBudget;
        // まず置いてある古いページを作り直し、残りの上限で新しいページを置く（どちらも粗い段から）。
        foreach(var Key in Wanted){
            if(Budget==0){ break; }
            var Packed=Pack(Key);
            if(this.Allocator!.TryGetSlot(Packed,out var Slot)&&this.DirtyPages.Remove(Packed)){
                this.Pages.Add(new(Key,Slot,this.PageProjection(Anchor,Key)));
                Budget--;
            }
        }
        this.Allocations.Clear();
        this.Allocator!.Update(this.Frame,Keys,Budget==int.MaxValue?Keys.Length:Budget,this.Allocations,this.Report);
        foreach(var Allocation in this.Allocations){
            var Key=Unpack(Allocation.Key);
            if(Allocation.EvictedKey is{ }Evicted){ this.DirtyPages.Remove(Evicted); }
            this.DirtyPages.Remove(Allocation.Key);
            this.Pages.Add(new(Key,Allocation.Slot,this.PageProjection(Anchor,Key)));
        }
        // ページ表: 窓の中にある置いたページ（古いものも、作り直すまでは前の影を引く）。
        var Resident=0;
        var Reused=0;
        foreach(var Key in Wanted){
            if(!this.Allocator.TryGetSlot(Pack(Key),out var Slot)){ continue; }
            Resident++;
            this.Table[this.TableIndex(Key)]=(uint)Slot+1;
        }
        var Rendered=this.Pages.Select(static Page=>Page.Key).ToHashSet();
        foreach(var Key in Wanted){
            if(this.Allocator.TryGetSlot(Pack(Key),out _)&&!this.DirtyPages.ContainsKey(Pack(Key))&&!Rendered.Contains(Key)){ Reused++; }
        }
        this.Statistics=new(Wanted.Count,Resident,this.Pages.Count,Reused,Wanted.Count-Resident,Invalidated);
    }
    /// <summary>描画空間の位置を、区画の角からの光の座標へ写す。</summary>
    private Vector3 ToLight(Vector3 Position,VirtualShadowAnchor Anchor)=>Vector3.TransformNormal(Position+Anchor.DrawingOriginOffset,this.Rotation);
    private Matrix4x4 Projection(VirtualShadowAnchor Anchor,int Level,float X,float Y,float Size){
        var Range=DepthRangeMeters(Level);
        var Center=this.DepthCenters[Level];
        return Matrix4x4.CreateTranslation(Anchor.DrawingOriginOffset)*this.Rotation*Matrix4x4.CreateOrthographicOffCenter(X,X+Size,Y,Y+Size,-Center-Range/2f,-Center+Range/2f);
    }
    private Matrix4x4 PageProjection(VirtualShadowAnchor Anchor,VirtualShadowPageKey Key){
        var Width=PageWidthMeters(Key.Level);
        return this.Projection(Anchor,Key.Level,Key.X*Width,Key.Y*Width,Width);
    }
    /// <summary>段の深度の中心が変わったので、その段の置いてあるページを全部古くする。</summary>
    private int DropLevel(int Level){
        var Count=0;
        if(this.Allocator is null){ return 0; }
        foreach(var Packed in this.Allocator.Resident.Keys){
            if(Unpack(Packed).Level!=Level||this.DirtyPages.ContainsKey(Packed)){ continue; }
            this.DirtyPages[Packed]=true;
            Count++;
        }
        return Count;
    }
    /// <summary>世界に固定した箱が動いた物について、前と今の箱に重なる置いてあるページを古くする。</summary>
    private int InvalidateMoved(VirtualShadowAnchor Anchor,ReadOnlySpan<VirtualShadowBounds>Objects){
        List<(Vector3 Minimum,Vector3 Maximum)>Changed=[];
        var Seen=new HashSet<int>();
        foreach(var Object in Objects){
            if(!Finite(Object.Minimum)||!Finite(Object.Maximum)){ continue; }
            Seen.Add(Object.Id);
            var Minimum=Object.Minimum+Anchor.DrawingOriginOffset;
            var Maximum=Object.Maximum+Anchor.DrawingOriginOffset;
            if(this.Previous.TryGetValue(Object.Id,out var Old)){
                var Moved=Object.Deforming||Old.CastsShadow!=Object.CastsShadow||!Near(Old.Minimum,Minimum)||!Near(Old.Maximum,Maximum);
                if(Moved){
                    if(Old.CastsShadow){ Changed.Add((Old.Minimum,Old.Maximum)); }
                    if(Object.CastsShadow){ Changed.Add((Minimum,Maximum)); }
                }
            } else if(Object.CastsShadow){
                Changed.Add((Minimum,Maximum));
            }
            this.Previous[Object.Id]=(Minimum,Maximum,Object.CastsShadow);
        }
        foreach(var Id in this.Previous.Keys.Where(Id=>!Seen.Contains(Id)).ToArray()){
            var Old=this.Previous[Id];
            if(Old.CastsShadow){ Changed.Add((Old.Minimum,Old.Maximum)); }
            this.Previous.Remove(Id);
        }
        if(Changed.Count==0||this.Allocator is null){ return 0; }
        var Count=0;
        var Keys=this.Allocator.Resident.Keys.ToArray();
        foreach(var (Minimum,Maximum) in Changed){
            var (Low,High)=this.LightBox(Minimum,Maximum);
            foreach(var Packed in Keys){
                if(this.DirtyPages.ContainsKey(Packed)){ continue; }
                var Key=Unpack(Packed);
                var Width=PageWidthMeters(Key.Level);
                if(Key.X*Width<=High.X&&(Key.X+1)*Width>=Low.X&&Key.Y*Width<=High.Y&&(Key.Y+1)*Width>=Low.Y){
                    this.DirtyPages[Packed]=true;
                    Count++;
                }
            }
        }
        return Count;
    }
    /// <summary>世界に固定した箱（区画の角から）の 8 隅を光の座標へ写した範囲。</summary>
    private (Vector3 Low,Vector3 High)LightBox(Vector3 Minimum,Vector3 Maximum){
        var Low=new Vector3(float.PositiveInfinity);
        var High=new Vector3(float.NegativeInfinity);
        for(var Corner=0;Corner<8;Corner++){
            var Point=Vector3.TransformNormal(new Vector3((Corner&1)==0?Minimum.X:Maximum.X,(Corner&2)==0?Minimum.Y:Maximum.Y,(Corner&4)==0?Minimum.Z:Maximum.Z),this.Rotation);
            Low=Vector3.Min(Low,Point);
            High=Vector3.Max(High,Point);
        }
        return (Low,High);
    }
    /// <summary>2 つの位置が、許す動き（1 mm と、大きさに応じた float の丸めの分）以内か。</summary>
    private static bool Near(Vector3 A,Vector3 B){
        var Tolerance=MathF.Max(MovementToleranceMeters,MathF.Max(A.Length(),B.Length())*4e-7f);
        return MathF.Abs(A.X-B.X)<=Tolerance&&MathF.Abs(A.Y-B.Y)<=Tolerance&&MathF.Abs(A.Z-B.Z)<=Tolerance;
    }
    /// <summary>ページのキーを 64 ビットにまとめる（段 4 ビット、x と y は 30 ビットずつ、2^29 を足して負を避ける）。</summary>
    public static ulong Pack(VirtualShadowPageKey Key)=>((ulong)(uint)Key.Level<<60)|((ulong)(uint)(Key.X+(1<<29))&0x3FFFFFFFul)<<30|((ulong)(uint)(Key.Y+(1<<29))&0x3FFFFFFFul);
    public static VirtualShadowPageKey Unpack(ulong Packed)=>new((int)(Packed>>60),(int)((Packed>>30)&0x3FFFFFFFul)-(1<<29),(int)(Packed&0x3FFFFFFFul)-(1<<29));
    /// <summary>
    /// 光の向きが <see cref="LightChangeDegrees"/> を超えて変わったか。外積の長さ（角の正弦）で比べる（cos(0.01°) は float では 1 に丸まり、内積では比べられない）。
    /// </summary>
    private static bool LightChanged(Vector3 Current,Vector3 Previous)=>Vector3.Dot(Current,Previous)<=0f||Vector3.Cross(Current,Previous).Length()>MathF.Sin(LightChangeDegrees*MathF.PI/180f);
    private static bool Finite(Vector3 V)=>float.IsFinite(V.X)&&float.IsFinite(V.Y)&&float.IsFinite(V.Z);
}
