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
        if(this.PendingTextureUploads.Count==0){ return; }
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
        fixed(D3D12_RESOURCE_BARRIER*PBarriers=Barriers){
            this.CommandList->ResourceBarrier((uint)Barriers.Length,PBarriers);
        }
        this.PendingTextureUploads.Clear();
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
        foreach(var Upload in this.PendingTextureUploads){ _=((ID3D12Resource*)Upload.Staging)->Release(); }
        this.PendingTextureUploads.Clear();
        foreach(var Entry in this.RetiredResources){ _=((IUnknown*)Entry.Resource)->Release(); }
        this.RetiredResources.Clear();
        foreach(var Resource in this.RetiringWithNextFrame){ _=((IUnknown*)Resource)->Release(); }
        this.RetiringWithNextFrame.Clear();
    }
}
