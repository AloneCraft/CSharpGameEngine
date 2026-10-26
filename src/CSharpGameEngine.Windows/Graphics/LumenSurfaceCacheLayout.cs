using System.Numerics;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// サーフェスキャッシュのカードアトラス（ADR-0078/M0111）。<see cref="MinimumCardTexels"/> から <see cref="PageTexels"/> まで、辺の
/// 長さが2の累乗であるカードを、<see cref="PageTexels"/>^2 のページにサイズごとに格納する。割り当てと解放は最悪でも O(ページ数) で、断片化解消は不要。最後のカードが解放さ
/// れたページは空きページへ戻す。
/// </summary>
internal sealed class LumenCardAtlas {
    internal const int PageTexels=128;
    internal const int MinimumCardTexels=8;
    private readonly int PagesPerSide;
    private readonly (int Width,int Height)[]PageClass;
    private readonly bool[][]PageSlots;
    private readonly int[]PageUsed;
    private readonly Stack<int>FreePages=new();
    private readonly Dictionary<(int Width,int Height),List<int>>OpenPages=[];
    internal LumenCardAtlas(int AtlasTexels){
        if(AtlasTexels<PageTexels||AtlasTexels%PageTexels!=0){
            throw new ArgumentOutOfRangeException(nameof(AtlasTexels),"The atlas must be a whole number of pages.");
        }
        this.AtlasTexels=AtlasTexels;
        this.PagesPerSide=AtlasTexels/PageTexels;
        var Pages=this.PagesPerSide*this.PagesPerSide;
        this.PageClass=new (int,int)[Pages];
        this.PageSlots=new bool[Pages][];
        this.PageUsed=new int[Pages];
        for(var Page=Pages-1;Page>=0;Page--){
            this.FreePages.Push(Page);
        }
    }
    internal int AtlasTexels { get; }
    internal int PageCount=>this.PagesPerSide*this.PagesPerSide;
    internal int FreePageCount=>this.FreePages.Count;
    /// <summary>カードサイズを割り当て可能か。両辺がページ内に収まる2の累乗である必要がある。</summary>
    internal static bool IsValidSize(int Width,int Height)=>IsPowerOfTwoInRange(Width)&&IsPowerOfTwoInRange(Height);
    private static bool IsPowerOfTwoInRange(int Value)=>Value>=MinimumCardTexels&&Value<=PageTexels&&(Value&(Value-1))==0;
    /// <summary>幅 <paramref name="Width"/> × 高さ <paramref name="Height"/> のカード用の領域を探す。アトラスが満杯なら false。</summary>
    internal bool TryAllocate(int Width,int Height,out int X,out int Y){
        if(!IsValidSize(Width,Height)){
            throw new ArgumentOutOfRangeException(nameof(Width),"Card sides must be powers of two within a page.");
        }
        X=0;
        Y=0;
        var Key=(Width,Height);
        if(!this.OpenPages.TryGetValue(Key,out var Open)){
            Open=[];
            this.OpenPages[Key]=Open;
        }
        if(Open.Count==0){
            if(this.FreePages.Count==0){
                return false;
            }
            var Fresh=this.FreePages.Pop();
            this.PageClass[Fresh]=Key;
            this.PageSlots[Fresh]=new bool[(PageTexels/Width)*(PageTexels/Height)];
            this.PageUsed[Fresh]=0;
            Open.Add(Fresh);
        }
        var Page=Open[^1];
        var Slots=this.PageSlots[Page];
        var Slot=Array.IndexOf(Slots,false);
        Slots[Slot]=true;
        this.PageUsed[Page]++;
        if(this.PageUsed[Page]==Slots.Length){
            Open.RemoveAt(Open.Count-1);
        }
        var Columns=PageTexels/Width;
        X=Page%this.PagesPerSide*PageTexels+Slot%Columns*Width;
        Y=Page/this.PagesPerSide*PageTexels+Slot/Columns*Height;
        return true;
    }
    /// <summary><see cref="TryAllocate"/> が割り当てたカードを解放する。</summary>
    internal void Free(int X,int Y,int Width,int Height){
        var Page=Y/PageTexels*this.PagesPerSide+X/PageTexels;
        if(this.PageSlots[Page] is not{ }Slots||this.PageClass[Page]!=(Width,Height)){
            throw new InvalidOperationException("The card was not allocated with this size.");
        }
        var Columns=PageTexels/Width;
        var Slot=(Y%PageTexels)/Height*Columns+(X%PageTexels)/Width;
        if(!Slots[Slot]){
            throw new InvalidOperationException("The card was already free.");
        }
        var WasFull=this.PageUsed[Page]==Slots.Length;
        Slots[Slot]=false;
        this.PageUsed[Page]--;
        var Open=this.OpenPages[(Width,Height)];
        if(this.PageUsed[Page]==0){
            Open.Remove(Page);
            this.PageSlots[Page]=null!;
            this.FreePages.Push(Page);
        } else if(WasFull){
            Open.Add(Page);
        }
    }
}
/// <summary>
/// サーフェスキャッシュのカード形状（ADR-0078/M0111）。各メッシュには自身の空間にあるボックスの6面それぞれに1つ、計6枚のカードを割り当てる。面 f のカードは
/// <see cref="Axis"/>(f) 側からボックスを見る。(<see cref="U"/>、<see cref="V"/>) はボックス面に広がり、U x V = Axis となる。メッシュ
/// 空間の点 q は、ボックス中心 c、U、V、Axis 方向の半サイズ hu、hv、hn を使って、クリップ座標 x = (q - c)·U / hu、y = (q - c)·V / hv、深度 z =
///  (hn - (q - c)·Axis) / 2hn に写像する。Lumen.hlsl の CardCoordinates と CardPoint も同じ写像を使い、キャプチャーには
/// <see cref="CaptureProjection"/> を使う。
/// </summary>
internal static class LumenCards {
    internal const int FaceCount=6;
    private static readonly Vector3[]Axes=[Vector3.UnitX,-Vector3.UnitX,Vector3.UnitY,-Vector3.UnitY,Vector3.UnitZ,-Vector3.UnitZ];
    private static readonly Vector3[]Us=[-Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitX,Vector3.UnitX,-Vector3.UnitX];
    private static readonly Vector3[]Vs=[Vector3.UnitY,Vector3.UnitY,-Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitY,Vector3.UnitY];
    internal static Vector3 Axis(int Face)=>Axes[Face];
    internal static Vector3 U(int Face)=>Us[Face];
    internal static Vector3 V(int Face)=>Vs[Face];
    /// <summary>カードが囲むボックス。メッシュの境界を、サイズの1%および各面につき1 mmずつ拡張する。境界面と完全に一致する面も、キャプチャーの深度範囲でクリップされない。平坦な軸にも厚みを持たせる。</summary>
    internal static (Vector3 Minimum,Vector3 Maximum)CardBox(Vector3 BoundsMinimum,Vector3 BoundsMaximum){
        var Margin=(BoundsMaximum-BoundsMinimum)*0.01f+new Vector3(0.001f);
        return (BoundsMinimum-Margin,BoundsMaximum+Margin);
    }
    /// <summary>キャプチャー用のビュープロジェクション（Stage プログラムの乗算と同じ行ベクトル規約）。メッシュ空間からカードのクリップ空間へ変換する。</summary>
    internal static Matrix4x4 CaptureProjection(int Face,Vector3 BoxMinimum,Vector3 BoxMaximum){
        var Centre=(BoxMinimum+BoxMaximum)*0.5f;
        var Half=Vector3.Max((BoxMaximum-BoxMinimum)*0.5f,new Vector3(1e-5f));
        var AxisU=Us[Face];
        var AxisV=Vs[Face];
        var AxisN=Axes[Face];
        var Hu=Vector3.Dot(Vector3.Abs(AxisU),Half);
        var Hv=Vector3.Dot(Vector3.Abs(AxisV),Half);
        var Hn=Vector3.Dot(Vector3.Abs(AxisN),Half);
        return new Matrix4x4(
            AxisU.X/Hu,AxisV.X/Hv,-AxisN.X/(2f*Hn),0f,
            AxisU.Y/Hu,AxisV.Y/Hv,-AxisN.Y/(2f*Hn),0f,
            AxisU.Z/Hu,AxisV.Z/Hv,-AxisN.Z/(2f*Hn),0f,
            -Vector3.Dot(Centre,AxisU)/Hu,-Vector3.Dot(Centre,AxisV)/Hv,(Hn+Vector3.Dot(Centre,AxisN))/(2f*Hn),1f);
    }
    /// <summary>
    /// カードのテクセル密度。<paramref name="TexelsPerMeter"/> と各軸のワールド長の積より大きい、最も近い2の累乗を選び、
    /// <see cref="LumenCardAtlas.MinimumCardTexels"/> から <paramref name="MaximumTexels"/> の範囲に収める。
    /// <paramref name="AxisScale"/> は各ローカル軸に沿うオブジェクトのスケール。
    /// </summary>
    internal static (int Width,int Height)Resolution(int Face,Vector3 BoxMinimum,Vector3 BoxMaximum,Vector3 AxisScale,float TexelsPerMeter,int MaximumTexels){
        var Size=(BoxMaximum-BoxMinimum)*AxisScale;
        var WorldU=Vector3.Dot(Vector3.Abs(Us[Face]),Size);
        var WorldV=Vector3.Dot(Vector3.Abs(Vs[Face]),Size);
        return (Side(WorldU*TexelsPerMeter,MaximumTexels),Side(WorldV*TexelsPerMeter,MaximumTexels));
        static int Side(float Texels,int Maximum){
            if(!float.IsFinite(Texels)||Texels<=LumenCardAtlas.MinimumCardTexels){
                return LumenCardAtlas.MinimumCardTexels;
            }
            var Rounded=(int)BitOperations.RoundUpToPowerOf2((uint)Math.Min(MathF.Ceiling(Texels),Maximum));
            return Math.Clamp(Rounded,LumenCardAtlas.MinimumCardTexels,Maximum);
        }
    }
}
