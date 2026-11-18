using CSharpGameEngine.Rendering;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DirectX.D3D12_TEXTURE_COPY_TYPE;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// アップロードの輪と、捨てるのを遅らせる仕組み（ADR-0100 決定 1、M0129 単位 5）。テクスチャを足すときは、送る命令をその場で実行して GPU を待つのではなく、
/// 次のフレームの最初（<c>SceneUpload</c>）に記録する。送るのに使った一時の置き場や、広げる前の材質の表・記述子の置き場は、それを使ったかもしれない
/// フレームのフェンスが済んでから放す。こうして、材質を足しても GPU を待たない。
/// </summary>
public sealed unsafe partial class StageRenderer {
    /// <summary>次のフレームで送るテクスチャ 1 枚。送り終えるまで、テクスチャは写し先の状態のまま。</summary>
    private readonly record struct PendingTextureUpload(nint Texture,nint Staging,D3D12_PLACED_SUBRESOURCE_FOOTPRINT[]Footprints);
    private readonly List<PendingTextureUpload>PendingTextureUploads=[];
    /// <summary>次のフレームで送る、画像の一部（書き換えられる画像のページ）。</summary>
    private readonly record struct PendingRegionUpload(nint Texture,nint Staging,D3D12_PLACED_SUBRESOURCE_FOOTPRINT Footprint,uint X,uint Y);
    private readonly List<PendingRegionUpload>PendingRegionUploads=[];
    /// <summary>
    /// 書き換えられる画像の一部を、一時の置き場へ書き、次のフレームの最初に送る（M0135）。画像がまだ描画器に無ければ何もしない
    /// （材質を作るときに、その時点の中身で作る）。GPU は待たない。
    /// </summary>
    public void UpdateTextureRegion(RenderTextureResource Texture,int X,int Y,int Width,int Height,ReadOnlySpan<byte>Rgba8){
        this.ThrowIfDisposed();
        if(!Texture.IsDynamic){ throw new InvalidOperationException("Only a dynamic texture can be updated."); }
        if(X<0||Y<0||Width<1||Height<1||X+Width>Texture.Width||Y+Height>Texture.Height||Rgba8.Length!=Width*Height*4){ throw new ArgumentOutOfRangeException(nameof(X),"The region must lie inside the texture and match the RGBA8 length."); }
        if(!this.ResidentTextures.TryGetValue((Texture.ContentHash,Texture.PreparedColorSpace??RenderTextureColorSpace.Srgb),out var Resident)){ return; }
        var Desc=((ID3D12Resource*)Resident)->GetDesc();
        var RowPitch=((uint)Width*4u+255u)&~255u;
        var Footprint=new D3D12_PLACED_SUBRESOURCE_FOOTPRINT { Offset=0,Footprint=new D3D12_SUBRESOURCE_FOOTPRINT { Format=Desc.Format,Width=(uint)Width,Height=(uint)Height,Depth=1,RowPitch=RowPitch } };
        var Size=(ulong)RowPitch*(uint)Height;
        D3D12_HEAP_PROPERTIES UploadHeap=new(D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_UPLOAD);
        var BufferDesc=D3D12_RESOURCE_DESC.Buffer(Size);
        ID3D12Resource*Staging;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&UploadHeap,D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_GENERIC_READ,null,TerraFX.Interop.Windows.Windows.__uuidof<ID3D12Resource>(),(void**)&Staging),
            "ID3D12Device::CreateCommittedResource","creating a texture region upload buffer");
        D3D12_RANGE NoRead=new();
        byte*Mapped;
        D3D12Diagnostics.EnsureSuccess(Staging->Map(0,&NoRead,(void**)&Mapped),"ID3D12Resource::Map","mapping a texture region upload buffer");
        for(var Row=0;Row<Height;Row++){ Rgba8.Slice(Row*Width*4,Width*4).CopyTo(new Span<byte>(Mapped+(ulong)Row*RowPitch,Width*4)); }
        Staging->Unmap(0,null);
        this.PendingRegionUploads.Add(new PendingRegionUpload(Resident,(nint)Staging,Footprint,(uint)X,(uint)Y));
    }
    /// <summary>
    /// 次に GPU へ送るフレーム（記録中なら今のフレーム）が済んだら放す資源。フェンスの値は、そのフレームを送るとき（<see cref="MoveToNextFrame"/>）に決める。
    /// 記録の途中で GPU の完了を待つ経路（仮想影の置き場を初めて作るときなど）はフェンスの値を進めるので、記録の時点で値を付けると、まだ送っていない
    /// フレームが使う資源を早く放してしまう。
    /// </summary>
    private readonly List<nint>RetiringWithNextFrame=[];
    /// <summary>放す資源と、放してよくなるフェンスの値。</summary>
    private readonly List<(ulong Fence,nint Resource)>RetiredResources=[];
    /// <summary>試験用: GPU の完了を待った回数。材質を足しても増えないことを確かめる。</summary>
    internal int GpuIdleWaitCountForTesting { get;private set; }
    /// <summary>試験用: まだ送っていないテクスチャの数と、まだ放していない資源の数。</summary>
    internal (int PendingUploads,int Retired)UploadRingForTesting=>(this.PendingTextureUploads.Count,this.RetiringWithNextFrame.Count+this.RetiredResources.Count);
    /// <summary>
    /// 資源を、今記録しているフレーム（記録していなければ次に記録するフレーム）が済んだら放す。そのフレームまでのどの命令が使っていてもよい。
    /// </summary>
    private void Retire(void*Resource){
        if(Resource==null){ return; }
        this.RetiringWithNextFrame.Add((nint)Resource);
    }
    /// <summary>フレームを GPU へ送り、その終わりに知らせるフェンスの値が決まったときに呼ぶ。このフレームまでに放すと決めた資源に、その値を付ける。</summary>
    private void SealRetirements(ulong SignaledFence){
        foreach(var Resource in this.RetiringWithNextFrame){ this.RetiredResources.Add((SignaledFence,Resource)); }
        this.RetiringWithNextFrame.Clear();
    }
    /// <summary>フェンスが済んだ資源を放す。フレームの最初に呼ぶ。</summary>
    private void ReleaseCompletedRetirements(){
        if(this.RetiredResources.Count==0){ return; }
        var Completed=this.Fence->GetCompletedValue();
        var Kept=0;
        for(var Index=0;Index<this.RetiredResources.Count;Index++){
            var Entry=this.RetiredResources[Index];
            if(Entry.Fence<=Completed){ _=((IUnknown*)Entry.Resource)->Release(); }
            else { this.RetiredResources[Kept++]=Entry; }
        }
        this.RetiredResources.RemoveRange(Kept,this.RetiredResources.Count-Kept);
    }
    /// <summary>まだ送っていないテクスチャを、このフレームの最初に送る。送ったテクスチャはシェーダーが読める状態にし、一時の置き場はこのフレームの後で放す。</summary>
    private void RecordPendingTextureUploads(){
        if(this.PendingTextureUploads.Count==0){
            this.RecordPendingRegionUploads([]);
            return;
        }
        var Barriers=new D3D12_RESOURCE_BARRIER[this.PendingTextureUploads.Count];
        for(var Index=0;Index<this.PendingTextureUploads.Count;Index++){
            var Upload=this.PendingTextureUploads[Index];
            for(var Level=0;Level<Upload.Footprints.Length;Level++){
                D3D12_TEXTURE_COPY_LOCATION Destination=new(){ pResource=(ID3D12Resource*)Upload.Texture,Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
                Destination.SubresourceIndex=(uint)Level;
                D3D12_TEXTURE_COPY_LOCATION Source=new(){ pResource=(ID3D12Resource*)Upload.Staging,Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
                Source.PlacedFootprint=Upload.Footprints[Level];
                this.CommandList->CopyTextureRegion(&Destination,0,0,0,&Source,null);
            }
            Barriers[Index]=D3D12_RESOURCE_BARRIER.InitTransition((ID3D12Resource*)Upload.Texture,D3D12_RESOURCE_STATE_COPY_DEST,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
            this.Retire((void*)Upload.Staging);
        }
        // 丸ごと送った画像はまだ写し先の状態なので、作った後に書いたページをその上に重ねてから読み取りの状態にする。
        this.RecordPendingRegionUploads(new HashSet<nint>(this.PendingTextureUploads.Select(static Upload=>Upload.Texture)));
        fixed(D3D12_RESOURCE_BARRIER*PBarriers=Barriers){
            this.CommandList->ResourceBarrier((uint)Barriers.Length,PBarriers);
        }
        this.PendingTextureUploads.Clear();
    }
    /// <summary>
    /// 画像の一部の送りを記録する。<paramref name="Whole"/>（このフレームに丸ごと送った、まだ写し先の状態の画像）はそのまま写す。
    /// それ以外は、画素シェーダーの読み取り → 写し先 → 読み取り、と状態を変えて写す。
    /// </summary>
    private void RecordPendingRegionUploads(HashSet<nint>Whole){
        if(this.PendingRegionUploads.Count==0){ return; }
        foreach(var Upload in this.PendingRegionUploads){
            D3D12_TEXTURE_COPY_LOCATION WholeDestination=new(){ pResource=(ID3D12Resource*)Upload.Texture,Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
            D3D12_TEXTURE_COPY_LOCATION WholeSource=new(){ pResource=(ID3D12Resource*)Upload.Staging,Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
            WholeSource.PlacedFootprint=Upload.Footprint;
            if(Whole.Contains(Upload.Texture)){
                this.CommandList->CopyTextureRegion(&WholeDestination,Upload.X,Upload.Y,0,&WholeSource,null);
                this.Retire((void*)Upload.Staging);
                continue;
            }
            var ToCopy=D3D12_RESOURCE_BARRIER.InitTransition((ID3D12Resource*)Upload.Texture,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_COPY_DEST,0);
            this.CommandList->ResourceBarrier(1,&ToCopy);
            D3D12_TEXTURE_COPY_LOCATION Destination=new(){ pResource=(ID3D12Resource*)Upload.Texture,Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
            Destination.SubresourceIndex=0;
            D3D12_TEXTURE_COPY_LOCATION Source=new(){ pResource=(ID3D12Resource*)Upload.Staging,Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
            Source.PlacedFootprint=Upload.Footprint;
            this.CommandList->CopyTextureRegion(&Destination,Upload.X,Upload.Y,0,&Source,null);
            var ToRead=D3D12_RESOURCE_BARRIER.InitTransition((ID3D12Resource*)Upload.Texture,D3D12_RESOURCE_STATE_COPY_DEST,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,0);
            this.CommandList->ResourceBarrier(1,&ToRead);
            this.Retire((void*)Upload.Staging);
        }
        this.PendingRegionUploads.Clear();
    }
    /// <summary>材質を足すのに失敗したとき、その途中で積んだテクスチャの送りを取り消す（テクスチャそのものは呼び出し側が放す）。</summary>
    private void CancelPendingTextureUploads(int Count){
        for(var Index=this.PendingTextureUploads.Count-1;Index>=Count;Index--){
            _=((ID3D12Resource*)this.PendingTextureUploads[Index].Staging)->Release();
            this.PendingTextureUploads.RemoveAt(Index);
        }
    }
    /// <summary>描画器を捨てるとき、GPU の完了を待った後に呼ぶ。送らなかった一時の置き場と、放し待ちの資源をすべて放す。</summary>
    private void ReleaseUploadRing(){
        foreach(var Upload in this.PendingRegionUploads){ _=((ID3D12Resource*)Upload.Staging)->Release(); }
        this.PendingRegionUploads.Clear();
        foreach(var Upload in this.PendingTextureUploads){ _=((ID3D12Resource*)Upload.Staging)->Release(); }
        this.PendingTextureUploads.Clear();
        foreach(var Entry in this.RetiredResources){ _=((IUnknown*)Entry.Resource)->Release(); }
        this.RetiredResources.Clear();
        foreach(var Resource in this.RetiringWithNextFrame){ _=((IUnknown*)Resource)->Release(); }
        this.RetiringWithNextFrame.Clear();
    }
}
