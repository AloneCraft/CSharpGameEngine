using System;
namespace CSharpGameEngine.VSExtension;
/// <summary>ネイティブのサイズ変更通知では最新サイズだけを保存し、通信は例外を処理できる描画tickで行う。</summary>
internal sealed class AssetViewportResizeQueue {
    private int Width;
    private int Height;
    private bool Pending;
    internal void Request(int Width,int Height){
        this.Width=Width<=0||Height<=0?0:Width;
        this.Height=Width<=0||Height<=0?0:Height;
        this.Pending=true;
    }
    internal void Clear()=>this.Pending=false;
    internal bool Apply(Action<int,int>Resize){
        if(!this.Pending){ return false; }
        this.Pending=false;
        Resize(this.Width,this.Height);
        return true;
    }
}
