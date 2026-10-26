using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension;
internal sealed class AssetWorkspaceRow:INotifyPropertyChanged {
    internal AssetWorkspaceItem Item{get;}
    public string Name=>System.IO.Path.GetFileName(this.Item.Source.SourcePath);
    public string Path=>this.Item.Source.RelativePath;
    public string KindLabel=>VsixText.Get("Assets_Kind_"+this.Item.Kind);
    public ImageOutputSettings[]ImageOutputs=>this.Item.ImageSettings?.Outputs??[];
    public string[]OutputNames=>this.Item.ImageSettings?.Outputs.Select(O=>O.Name+" ("+O.Kind+")").ToArray()??[];
    public string ProviderLabel=>VsixText.Get("Assets_Provider_"+this.Item.Source.Provider);
    public string StateLabel=>VsixText.Get("Assets_State_"+(AssetWorkspaceIcons.HasMetadataError(this.Item)?AssetWorkspaceState.InvalidMetadata:this.Item.State));
    public Microsoft.VisualStudio.Imaging.Interop.ImageMoniker KindMoniker=>AssetWorkspaceIcons.Kind(this.Item.Kind);
    public Microsoft.VisualStudio.Imaging.Interop.ImageMoniker StateMoniker=>AssetWorkspaceIcons.State(this.Item);
    public Microsoft.VisualStudio.Imaging.Interop.ImageMoniker LockMoniker=>Microsoft.VisualStudio.Imaging.KnownMonikers.Lock;
    public bool HasStateIcon=>AssetWorkspaceIcons.HasState(this.Item);
    public bool IsReadOnly=>AssetWorkspaceIcons.ReadOnly(this.Item);
    public string ReadOnlyLabel=>VsixText.Get("Assets_State_ReadOnly");
    private WeakReference<ImageSource>?Image;
    private bool Requested;
    private System.Threading.CancellationTokenSource?ThumbnailCancellation;
    public event PropertyChangedEventHandler?PropertyChanged;
    internal AssetKey?ThumbnailKey{get;}
    internal AssetWorkspaceRow(AssetWorkspaceItem Item,AssetKey?ThumbnailKey=null){ this.Item=Item;this.ThumbnailKey=ThumbnailKey??Item.GetKey(); }
    public ImageSource?Thumbnail{
        get {
            if(this.Image?.TryGetTarget(out var Existing)==true){ return Existing; }
            if(!this.Requested){ this.Requested=true;_=this.LoadAsync(); }
            return null;
        }
    }
    internal void CancelThumbnail(){
        var Cancellation=this.ThumbnailCancellation;
        this.ThumbnailCancellation=null;this.Requested=false;Cancellation?.Cancel();
    }
    private async Task LoadAsync(){
        var Cancellation=new System.Threading.CancellationTokenSource();this.ThumbnailCancellation=Cancellation;
        try {
            var Result=await AssetThumbnailService.RequestAsync(this.Item,Cancellation.Token,this.ThumbnailKey);
            if(Result is null||Cancellation.IsCancellationRequested||!ReferenceEquals(this.ThumbnailCancellation,Cancellation)){ return; }
            this.Image=new WeakReference<ImageSource>(Result);
            this.PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(this.Thumbnail)));
        }catch(OperationCanceledException){ if(ReferenceEquals(this.ThumbnailCancellation,Cancellation)){ this.Requested=false; } }
        catch(Exception Error)when(Error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException){
            Microsoft.VisualStudio.Shell.ActivityLog.LogWarning(nameof(AssetWorkspaceRow),Error.Message);
        }finally {
            if(ReferenceEquals(this.ThumbnailCancellation,Cancellation)){ this.ThumbnailCancellation=null; }
            Cancellation.Dispose();
        }
    }
}
