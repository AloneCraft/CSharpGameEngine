using System.Numerics;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// The surface cache's card atlas (ADR-0078/M0111): rectangles whose sides are powers of two from <see cref="MinimumCardTexels"/> to
/// <see cref="PageTexels"/>, packed in pages of <see cref="PageTexels"/>^2 that each hold one size. Allocation and release are O(pages) at worst
/// and need no defragmentation: a page whose last card goes back to the free pages.
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
    /// <summary>Whether a card size can be allocated at all: both sides powers of two within the page.</summary>
    internal static bool IsValidSize(int Width,int Height)=>IsPowerOfTwoInRange(Width)&&IsPowerOfTwoInRange(Height);
    private static bool IsPowerOfTwoInRange(int Value)=>Value>=MinimumCardTexels&&Value<=PageTexels&&(Value&(Value-1))==0;
    /// <summary>Finds room for a <paramref name="Width"/> x <paramref name="Height"/> card; false when the atlas is full.</summary>
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
    /// <summary>Gives back a card that <see cref="TryAllocate"/> returned.</summary>
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
/// The geometry of the surface cache's cards (ADR-0078/M0111): each mesh gets six cards, one per side of its box in its own space. Card f looks at
/// the box from the side of <see cref="Axis"/>(f); (<see cref="U"/>, <see cref="V"/>) span it with U x V = Axis. A point q of the mesh's space maps
/// to clip x = (q - c)·U / hu, y = (q - c)·V / hv and depth z = (hn - (q - c)·Axis) / 2hn, where c is the box centre and hu, hv, hn its half sizes
/// along U, V and Axis. Lumen.hlsl (CardCoordinates, CardPoint) uses the same mapping, and the capture rasterizes with <see cref="CaptureProjection"/>.
/// </summary>
internal static class LumenCards {
    internal const int FaceCount=6;
    private static readonly Vector3[]Axes=[Vector3.UnitX,-Vector3.UnitX,Vector3.UnitY,-Vector3.UnitY,Vector3.UnitZ,-Vector3.UnitZ];
    private static readonly Vector3[]Us=[-Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitX,Vector3.UnitX,-Vector3.UnitX];
    private static readonly Vector3[]Vs=[Vector3.UnitY,Vector3.UnitY,-Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitY,Vector3.UnitY];
    internal static Vector3 Axis(int Face)=>Axes[Face];
    internal static Vector3 U(int Face)=>Us[Face];
    internal static Vector3 V(int Face)=>Vs[Face];
    /// <summary>
    /// The cards' box: the mesh's bounds grown by 1 % of their size and 1 mm on every side, so a face lying exactly on the bounds is not clipped by
    /// the capture's depth range. A flat axis still gets a thickness.
    /// </summary>
    internal static (Vector3 Minimum,Vector3 Maximum)CardBox(Vector3 BoundsMinimum,Vector3 BoundsMaximum){
        var Margin=(BoundsMaximum-BoundsMinimum)*0.01f+new Vector3(0.001f);
        return (BoundsMinimum-Margin,BoundsMaximum+Margin);
    }
    /// <summary>The capture's view-projection (row vectors, as the Stage program multiplies): the mesh's space to the card's clip space.</summary>
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
    /// A card's texels: each side the power of two nearest above its world length times <paramref name="TexelsPerMeter"/>, from
    /// <see cref="LumenCardAtlas.MinimumCardTexels"/> to <paramref name="MaximumTexels"/>. <paramref name="AxisScale"/> is the object's scale along
    /// each local axis.
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
