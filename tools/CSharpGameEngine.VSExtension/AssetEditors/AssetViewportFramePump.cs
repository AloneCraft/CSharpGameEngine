using System;
using System.Threading;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension;
/// <summary>VS の描画スレッドを待たせず、最新のビューポート状態を順序どおりホストへ送る。</summary>
internal sealed class AssetViewportFramePump:IDisposable {
    private readonly object Gate=new();
    private readonly RenderPreviewHostClient Client;
    private readonly Action<RenderPreviewHostException> Failed;
    private readonly AutoResetEvent WorkAvailable=new(initialState:false);
    private readonly Thread Worker;
    private RenderPreviewScene?DesiredScene;
    private RenderPreviewScene?PendingFrameScene;
    private RenderPreviewFrame?PendingFrame;
    private (int Width,int Height)?PendingResize;
    private long DesiredRevision;
    private long AppliedRevision;
    private long PendingFrameRevision;
    private bool WorkerFinished;
    private bool Stopped;
    private bool Disposed;
    internal AssetViewportFramePump(RenderPreviewHostClient Client,Action<RenderPreviewHostException> Failed){
        this.Client=Client??throw new ArgumentNullException(nameof(Client));
        this.Failed=Failed??throw new ArgumentNullException(nameof(Failed));
        this.Worker=new Thread(this.Run){ IsBackground=true,Name="CgeAssetPreviewFramePump" };
        this.Worker.Start();
    }
    internal void SetScene(RenderPreviewScene Scene){
        if(Scene is null){ throw new ArgumentNullException(nameof(Scene)); }
        lock(this.Gate){
            if(this.Stopped||this.Disposed){ return; }
            this.DesiredScene=Scene;
            this.DesiredRevision++;
        }
    }
    internal bool Submit(RenderPreviewFrame Frame,(int Width,int Height)?Resize=null){
        if(Frame is null){ throw new ArgumentNullException(nameof(Frame)); }
        lock(this.Gate){
            if(this.Stopped||this.Disposed){ return false; }
            this.PendingFrame=Frame;
            this.PendingFrameRevision=this.DesiredRevision;
            this.PendingFrameScene=this.DesiredScene;
            if(Resize is not null){ this.PendingResize=Resize; }
            this.WorkAvailable.Set();
            return true;
        }
    }
    private void Run(){
        try {
            while(true){
                this.WorkAvailable.WaitOne();
                while(true){
                    RenderPreviewFrame Frame;
                    RenderPreviewScene?Scene;
                    (int Width,int Height)?Resize;
                    long Revision;
                    lock(this.Gate){
                        if(this.Stopped||this.Disposed){
                            this.WorkerFinished=true;
                            return;
                        }
                        if(this.PendingFrame is null){ break; }
                        Frame=this.PendingFrame;
                        this.PendingFrame=null;
                        Revision=this.PendingFrameRevision;
                        Scene=Revision==this.AppliedRevision?null:this.PendingFrameScene;
                        this.PendingFrameScene=null;
                        Resize=this.PendingResize;
                        this.PendingResize=null;
                    }
                    try {
                        if(Resize is{ }Size){ this.Client.Resize(Size.Width,Size.Height); }
                        if(Scene is not null){
                            this.Client.ReplaceScene(Scene);
                            this.AppliedRevision=Revision;
                        }
                        this.Client.UpdateFrame(Frame);
                        this.Client.Render(captureForVerification:false);
                    } catch(Exception Error){
                        var Failure=Error as RenderPreviewHostException??new RenderPreviewHostException(
                            $"Preview frame processing failed: {Error.Message}",RequiresFreshSurfaceGeneration:true,Error);
                        lock(this.Gate){
                            this.Stopped=true;
                            this.PendingFrame=null;
                            this.PendingFrameScene=null;
                            this.PendingResize=null;
                            this.WorkerFinished=true;
                            if(this.Disposed){ return; }
                            // Dispose must not finish before this notification has been delivered.
                            this.Failed(Failure);
                        }
                        return;
                    }
                }
            }
        } finally { this.WorkAvailable.Dispose(); }
    }
    public void Dispose(){
        lock(this.Gate){
            if(this.Disposed){ return; }
            this.Disposed=true;
            this.Stopped=true;
            this.PendingFrame=null;
            this.PendingFrameScene=null;
            this.PendingResize=null;
            if(!this.WorkerFinished){ this.WorkAvailable.Set(); }
        }
        // HWND の破棄または再利用より前に、所有ホストの終了を確定させる。
        this.Client.Abort();
    }
}
