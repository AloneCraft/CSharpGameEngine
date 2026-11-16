using System.Numerics;
namespace CSharpGameEngine.Rendering;
public enum DirectionalShadowMode { Cascaded,Virtual }
public readonly record struct DirectionalShadowSettings(DirectionalShadowMode Mode,int PageBudget=1024,bool Compare=false,bool SoftShadows=false,float SourceAngularDiameterDegrees=0.5357f,float MaximumSoftShadowRadiusMeters=2f) {
    public bool IsValid=>this.Mode is DirectionalShadowMode.Cascaded or DirectionalShadowMode.Virtual&&this.PageBudget is >=1 and <=1024
        &&float.IsFinite(this.SourceAngularDiameterDegrees)&&this.SourceAngularDiameterDegrees is >=0f and <=10f
        &&float.IsFinite(this.MaximumSoftShadowRadiusMeters)&&this.MaximumSoftShadowRadiusMeters is >0f and <=16f;
}
public readonly record struct VirtualShadowBounds(int Id,Vector3 Minimum,Vector3 Maximum,bool Visible,bool CastsShadow,bool Deforming=false,int GeometryId=0,Matrix4x4 Transform=default);
public readonly record struct VirtualShadowPageKey(int Level,int X,int Y);
public readonly record struct VirtualShadowPage(VirtualShadowPageKey Key,int PhysicalIndex,Matrix4x4 Projection);
public readonly record struct VirtualShadowStatistics(int Requested,int Resident,int Rendered,int Reused,int Missing,int Invalidated);
/// <summary>D3D12 に依存しない CPU 側のページ計画。クリップマップの移動中もワールド格子のキーを維持し、アップロード用テーブルは呼び出し側のフレームスロットに属する。</summary>
public sealed class VirtualDirectionalShadowCache {
    public const int LevelCount=4,VirtualResolution=4096,PageSize=128,PagesPerAxis=32,TableLength=4096;
    public const float DepthRangeMeters=2048f;
    private sealed class Entry(int Slot) { public int Slot=Slot;public long LastUsed;public bool Dirty=true; }
    private readonly Dictionary<VirtualShadowPageKey,Entry>Entries=[];
    private readonly Dictionary<int,VirtualShadowBounds>Previous=[];
    private readonly HashSet<VirtualShadowPageKey>Requests=[];
    private readonly List<VirtualShadowPage>DirtyPages=[];
    private readonly Matrix4x4[]ClipProjections=new Matrix4x4[LevelCount];
    private readonly int[]MinimumX=new int[LevelCount],MinimumY=new int[LevelCount];
    private readonly uint[]TableData=new uint[TableLength];
    private Matrix4x4 Rotation;
    private Vector3 Direction;
    private float DepthCenter;
    private int Budget;
    private float FilterRadius;
    private long Frame;
    public ReadOnlySpan<uint>PageTable=>this.TableData;
    public ReadOnlySpan<Matrix4x4>Projections=>this.ClipProjections;
    public IReadOnlyList<VirtualShadowPage>PagesToRender=>this.DirtyPages;
    public VirtualShadowStatistics Statistics{get;private set;}
    public void SuspendFrame()=>this.Statistics=new(0,this.Entries.Count,0,0,0,0);
    public void Reset(){ this.Entries.Clear();this.Previous.Clear();this.Direction=Vector3.Zero; }
    private static float WorldPageSize(int Level)=>2f*(1<<Level);
    public void Plan(Vector3 CameraPosition,Vector3 TowardLight,ReadOnlySpan<VirtualShadowBounds>Objects,int PageBudget=1024,float FilterRadiusMeters=0f){
        if(PageBudget is <1 or >1024){ throw new ArgumentOutOfRangeException(nameof(PageBudget)); }
        if(!float.IsFinite(FilterRadiusMeters)||FilterRadiusMeters is <0f or >16f){ throw new ArgumentOutOfRangeException(nameof(FilterRadiusMeters)); }
        this.FilterRadius=FilterRadiusMeters;
        if(!Finite(CameraPosition)||!Finite(TowardLight)||TowardLight.LengthSquared()<1e-12f){ throw new ArgumentException("Finite camera and nonzero light direction required."); }
        TowardLight=Vector3.Normalize(TowardLight);
        var Rotation=Matrix4x4.CreateLookAt(Vector3.Zero,-TowardLight,MathF.Abs(TowardLight.Y)>0.99f?Vector3.UnitZ:Vector3.UnitY);
        var Center=Vector3.Transform(CameraPosition,Rotation);
        var DepthCenter=MathF.Floor(Center.Z/256f)*256f;
        if(TowardLight!=this.Direction||DepthCenter!=this.DepthCenter||PageBudget!=this.Budget){ this.Reset(); }
        this.Direction=TowardLight;this.Rotation=Rotation;this.DepthCenter=DepthCenter;this.Budget=PageBudget;this.Frame++;
        this.Requests.Clear();this.DirtyPages.Clear();Array.Clear(this.TableData);
        for(var Level=0;Level<LevelCount;Level++){
            var Size=WorldPageSize(Level);
            this.MinimumX[Level]=(int)MathF.Floor(Center.X/Size)-16;
            this.MinimumY[Level]=(int)MathF.Floor(Center.Y/Size)-16;
            this.ClipProjections[Level]=this.Projection(this.MinimumX[Level]*Size,this.MinimumY[Level]*Size,Size*PagesPerAxis);
        }
        var Invalidated=0;
        var Seen=new HashSet<int>();
        foreach(var Object in Objects){
            Seen.Add(Object.Id);
            if(!Finite(Object.Minimum)||!Finite(Object.Maximum)){ continue; }
            if(this.Previous.TryGetValue(Object.Id,out var Old)&&(Old.Minimum!=Object.Minimum||Old.Maximum!=Object.Maximum||Old.CastsShadow!=Object.CastsShadow||Old.GeometryId!=Object.GeometryId||Old.Transform!=Object.Transform)||Object.Deforming){
                if(Old.CastsShadow){ Invalidated+=this.Invalidate(Old); }
                if(Object.CastsShadow){ Invalidated+=this.Invalidate(Object); }
            } else if(!this.Previous.ContainsKey(Object.Id)&&Object.CastsShadow){ Invalidated+=this.Invalidate(Object); }
            this.Previous[Object.Id]=Object;
            if(Object.Visible){ this.Request(Object); }
        }
        foreach(var Id in this.Previous.Keys.Where(Id=>!Seen.Contains(Id)).ToArray()){
            var Old=this.Previous[Id];if(Old.CastsShadow){ Invalidated+=this.Invalidate(Old); }this.Previous.Remove(Id);
        }
        var Used=new HashSet<int>(this.Entries.Values.Select(Entry=>Entry.Slot));
        var Missing=0;var Reused=0;
        foreach(var Key in this.Requests.OrderBy(Key=>Key.Level).ThenBy(Key=>Key.X).ThenBy(Key=>Key.Y)){
            if(!this.Entries.TryGetValue(Key,out var Entry)){
                var Slot=Enumerable.Range(0,PageBudget).FirstOrDefault(Index=>!Used.Contains(Index),-1);
                if(Slot<0){
                    var Victim=this.Entries.Where(Pair=>!this.Requests.Contains(Pair.Key)).OrderBy(Pair=>Pair.Value.LastUsed).ThenBy(Pair=>Pair.Value.Slot).FirstOrDefault();
                    if(Victim.Value is null){ Missing++;continue; }
                    Slot=Victim.Value.Slot;this.Entries.Remove(Victim.Key);
                }
                Entry=new Entry(Slot);this.Entries.Add(Key,Entry);Used.Add(Slot);
            }
            Entry.LastUsed=this.Frame;
            var X=Key.X-this.MinimumX[Key.Level];var Y=31-(Key.Y-this.MinimumY[Key.Level]);
            this.TableData[Key.Level*1024+Y*32+X]=(uint)Entry.Slot+1;
            if(Entry.Dirty){
                var Size=WorldPageSize(Key.Level);
                this.DirtyPages.Add(new(Key,Entry.Slot,this.Projection(Key.X*Size,Key.Y*Size,Size)));Entry.Dirty=false;
            } else { Reused++; }
        }
        this.Statistics=new(this.Requests.Count,this.Entries.Count,this.DirtyPages.Count,Reused,Missing,Invalidated);
    }
    private Matrix4x4 Projection(float X,float Y,float Size)=>this.Rotation*Matrix4x4.CreateOrthographicOffCenter(X,X+Size,Y,Y+Size,-this.DepthCenter-DepthRangeMeters/2,-this.DepthCenter+DepthRangeMeters/2);
    private static bool Finite(Vector3 V)=>float.IsFinite(V.X)&&float.IsFinite(V.Y)&&float.IsFinite(V.Z);
    private (Vector3 Min,Vector3 Max)LightBounds(VirtualShadowBounds Object){
        var Min=new Vector3(float.PositiveInfinity);var Max=new Vector3(float.NegativeInfinity);
        for(var I=0;I<8;I++){
            var P=Vector3.Transform(new Vector3((I&1)==0?Object.Minimum.X:Object.Maximum.X,(I&2)==0?Object.Minimum.Y:Object.Maximum.Y,(I&4)==0?Object.Minimum.Z:Object.Maximum.Z),this.Rotation);
            Min=Vector3.Min(Min,P);Max=Vector3.Max(Max,P);
        }
        return(Min,Max);
    }
    private int Invalidate(VirtualShadowBounds Object){
        var (Min,Max)=this.LightBounds(Object);var Count=0;
        foreach(var Pair in this.Entries){
            var Size=WorldPageSize(Pair.Key.Level);
            if(Pair.Key.X*Size<=Max.X&&(Pair.Key.X+1)*Size>=Min.X&&Pair.Key.Y*Size<=Max.Y&&(Pair.Key.Y+1)*Size>=Min.Y&&!Pair.Value.Dirty){ Pair.Value.Dirty=true;Count++; }
        }
        return Count;
    }
    private void Request(VirtualShadowBounds Object){
        var (Min,Max)=this.LightBounds(Object);
        for(var Level=0;Level<LevelCount;Level++){
            var Size=WorldPageSize(Level);var OriginX=this.MinimumX[Level];var OriginY=this.MinimumY[Level];
            var Halo=1+(int)MathF.Ceiling(this.FilterRadius/Size);
            var X0=Math.Max(OriginX,(int)MathF.Floor(Min.X/Size)-Halo);var X1=Math.Min(OriginX+31,(int)MathF.Floor(Max.X/Size)+Halo);
            var Y0=Math.Max(OriginY,(int)MathF.Floor(Min.Y/Size)-Halo);var Y1=Math.Min(OriginY+31,(int)MathF.Floor(Max.Y/Size)+Halo);
            var InnerHalo=1+(int)MathF.Ceiling(this.FilterRadius/(Size/2));
            for(var Y=Y0;Y<=Y1;Y++){for(var X=X0;X<=X1;X++){
                // 細かいレベルに完全に含まれるページは、「最初に含まれたレベル」を使うシェーダー検索では選ばれない。
                if(Level>0&&X*Size>=(this.MinimumX[Level-1]+InnerHalo)*(Size/2)&&(X+1)*Size<=(this.MinimumX[Level-1]+32-InnerHalo)*(Size/2)
                    &&Y*Size>=(this.MinimumY[Level-1]+InnerHalo)*(Size/2)&&(Y+1)*Size<=(this.MinimumY[Level-1]+32-InnerHalo)*(Size/2)){ continue; }
                this.Requests.Add(new(Level,X,Y));
            }}
        }
    }
}
