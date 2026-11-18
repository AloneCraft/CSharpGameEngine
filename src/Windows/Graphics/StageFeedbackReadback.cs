using TerraFX.Interop.DirectX;
using static TerraFX.Interop.DirectX.D3D12_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.Windows.Windows;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// GPU からの読み戻しの輪（ADR-0100 決定 1、M0129 単位 5）。フレームの枠ごとに読み戻し用の置き場を 1 つ持つ。あるフレームが自分の枠へ写した値は、
/// その枠がまた回ってきたとき（描画器はフレームの枠を使い直す前に、その枠のフェンスを待つ）に、GPU を待たずに読める。
/// 時間の計測（影、主パス）と、後の段階の要求（仮想ジオメトリ、仮想シャドウ、仮想テクスチャ）が共有する。
/// </summary>
internal sealed unsafe class StageFeedbackReadback:IDisposable {
    private readonly bool[]Written;
    private ID3D12Resource*Buffer;
    /// <param name="Device">置き場を作るデバイス。</param>
    /// <param name="FrameCount">フレームの枠の数。</param>
    /// <param name="SlotBytes">枠 1 つの大きさ（バイト）。</param>
    /// <param name="Name">失敗したときの説明に使う名前。</param>
    internal StageFeedbackReadback(GraphicsDevice Device,uint FrameCount,uint SlotBytes,string Name){
        ArgumentNullException.ThrowIfNull(Device);
        ArgumentOutOfRangeException.ThrowIfZero(FrameCount);
        ArgumentOutOfRangeException.ThrowIfZero(SlotBytes);
        this.FrameCount=FrameCount;
        this.SlotBytes=SlotBytes;
        this.Written=new bool[FrameCount];
        D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_READBACK);
        var Desc=D3D12_RESOURCE_DESC.Buffer(checked((ulong)FrameCount*SlotBytes));
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,D3D12_RESOURCE_STATE_COPY_DEST,null,__uuidof<ID3D12Resource>(),(void**)&Resource),
            "ID3D12Device::CreateCommittedResource",$"creating the {Name} readback ring");
        this.Buffer=Resource;
    }
    internal uint FrameCount { get; }
    internal uint SlotBytes { get; }
    /// <summary>写し先の置き場。<see cref="SlotOffset"/> の位置へ、<see cref="SlotBytes"/> 以内で写す。</summary>
    internal ID3D12Resource*Resource=>this.Buffer;
    internal ulong SlotOffset(uint Frame){
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(Frame,this.FrameCount);
        return (ulong)Frame*this.SlotBytes;
    }
    /// <summary>このフレームの枠へ写す命令を記録したことを覚える。読めるのは、その枠のフェンスが済んでから。</summary>
    internal void MarkWritten(uint Frame){
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(Frame,this.FrameCount);
        this.Written[Frame]=true;
    }
    /// <summary>まだ読む値の無い状態に戻す（計測をやめた、置き場の中身が古くなった、など）。</summary>
    internal void Invalidate()=>Array.Clear(this.Written);
    /// <summary>
    /// 枠の先頭から <paramref name="Destination"/> の分だけを読む。その枠へまだ何も写していなければ false。呼び出し側は、その枠を写したフレームの
    /// フェンスが済んでいること（描画器がその枠を使い直す前、または GPU の完了を待った後）を守る。
    /// </summary>
    internal bool TryRead<T>(uint Frame,Span<T>Destination) where T:unmanaged {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(Frame,this.FrameCount);
        var Bytes=checked((uint)(Destination.Length*sizeof(T)));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Bytes,this.SlotBytes);
        if(!this.Written[Frame]||this.Buffer==null){ return false; }
        var Begin=(nuint)this.SlotOffset(Frame);
        D3D12_RANGE Range=new(){ Begin=Begin,End=Begin+Bytes };
        byte*Mapped;
        D3D12Diagnostics.EnsureSuccess(this.Buffer->Map(0,&Range,(void**)&Mapped),"ID3D12Resource::Map","reading a readback ring slot");
        try {
            new ReadOnlySpan<T>(Mapped+Begin,Destination.Length).CopyTo(Destination);
        } finally {
            D3D12_RANGE NoWrite=new();
            this.Buffer->Unmap(0,&NoWrite);
        }
        return true;
    }
    public void Dispose(){
        if(this.Buffer!=null){
            _=this.Buffer->Release();
            this.Buffer=null;
        }
    }
}
