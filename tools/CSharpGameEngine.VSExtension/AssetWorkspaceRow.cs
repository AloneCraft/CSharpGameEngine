using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension;
internal sealed class AssetWorkspaceRow:INotifyPropertyChanged {
    internal AssetWorkspaceItem Item{get;}
    public string Name=>System.IO.Path.GetFileName(this.Item.Source.SourcePath);
    public bool IsRenaming{get;private set;}
    public string RenameText{get;set;}=string.Empty;
    internal bool BeginRename(){
        if(this.IsReadOnly||(this.Item.GetCapabilities()&AssetWorkspaceCapabilities.Organize)==0||this.ThumbnailKey?.SubAssetId is not null){ return false; }
        this.RenameText=this.Name;this.IsRenaming=true;
        this.PropertyChanged?.Invoke(this,new(nameof(this.RenameText)));this.PropertyChanged?.Invoke(this,new(nameof(this.IsRenaming)));return true;
    }
    internal void EndRename(){
        this.IsRenaming=false;this.RenameText=this.Name;
        this.PropertyChanged?.Invoke(this,new(nameof(this.IsRenaming)));this.PropertyChanged?.Invoke(this,new(nameof(this.RenameText)));
    }
    internal string RenameDestination(){
        if(string.IsNullOrWhiteSpace(this.RenameText)||this.RenameText.IndexOfAny(System.IO.Path.GetInvalidFileNameChars())>=0
            ||this.RenameText.EndsWith(".",StringComparison.Ordinal)||this.RenameText.EndsWith(" ",StringComparison.Ordinal)){
            throw new ArgumentException(VsixText.Get("Assets_InvalidName"));
        }
        return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(this.Item.Source.SourcePath)!,this.RenameText);
    }
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
    private readonly Func<CancellationToken,Task<BitmapSource?>>ThumbnailLoader;
    private bool ThumbnailUnavailable;
    public bool IsThumbnailLoading{get;private set;}
    public string ThumbnailStatusText=>this.IsThumbnailLoading?VsixText.Get("Assets_ThumbnailLoading"):this.ThumbnailUnavailable?VsixText.Get("Assets_ThumbnailUnavailable"):string.Empty;
    public event PropertyChangedEventHandler?PropertyChanged;
    internal AssetKey?ThumbnailKey{get;}
    internal AssetWorkspaceRow(AssetWorkspaceItem Item,AssetKey?ThumbnailKey=null,Func<CancellationToken,Task<BitmapSource?>>?ThumbnailLoader=null){
        this.Item=Item;this.ThumbnailKey=ThumbnailKey??Item.GetKey();
        this.ThumbnailLoader=ThumbnailLoader??(Cancellation=>AssetThumbnailService.RequestAsync(this.Item,Cancellation,this.ThumbnailKey));
    }
    private object?Tip;
    /// <summary>タイルのツールチップ。フォントは見本・対応言語・表示できない記号（開いたときに読む）、それ以外はパス。</summary>
    public object ToolTipContent=>this.IsThumbnailLoading?this.Path+Environment.NewLine+this.ThumbnailStatusText
        :this.ThumbnailUnavailable?this.Path+Environment.NewLine+VsixText.Get("Assets_ThumbnailUnavailableHint")
        :this.Item.Kind==AssetWorkspaceKind.Font?(this.Tip??=AssetFontView.CreateTooltip(this.Item.Source.SourcePath)):this.Path;
    public ImageSource?Thumbnail{
        get {
            if(this.Image?.TryGetTarget(out var Existing)==true){ return Existing; }
            if(!this.Requested){ _=this.LoadAsync(); }
            return null;
        }
    }
    internal void CancelThumbnail(){
        var Cancellation=this.ThumbnailCancellation;
        this.ThumbnailCancellation=null;this.Requested=false;this.SetThumbnailState(false,false);Cancellation?.Cancel();
    }
    internal async Task LoadAsync(){
        if(!AssetThumbnailService.SupportsThumbnail(this.Item,this.ThumbnailKey)){ return; }
        var Cancellation=new System.Threading.CancellationTokenSource();this.ThumbnailCancellation=Cancellation;
        this.Requested=true;this.SetThumbnailState(true,false);
        try {
            var Result=await this.ThumbnailLoader(Cancellation.Token);
            if(Cancellation.IsCancellationRequested||!ReferenceEquals(this.ThumbnailCancellation,Cancellation)){ return; }
            this.ThumbnailUnavailable=Result is null;
            if(Result is null){ return; }
            this.Image=new WeakReference<ImageSource>(Result);
            this.PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(this.Thumbnail)));
        }catch(OperationCanceledException){ if(ReferenceEquals(this.ThumbnailCancellation,Cancellation)){ this.Requested=false; } }
        catch(Exception Error)when(Error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException){
            if(ReferenceEquals(this.ThumbnailCancellation,Cancellation)){ this.ThumbnailUnavailable=true; }
            Microsoft.VisualStudio.Shell.ActivityLog.LogWarning(nameof(AssetWorkspaceRow),Error.Message);
        }finally {
            if(ReferenceEquals(this.ThumbnailCancellation,Cancellation)){ this.ThumbnailCancellation=null;this.SetThumbnailState(false,this.ThumbnailUnavailable); }
            Cancellation.Dispose();
        }
    }
    private void SetThumbnailState(bool Loading,bool Unavailable){
        if(this.IsThumbnailLoading==Loading&&this.ThumbnailUnavailable==Unavailable){ return; }
        this.IsThumbnailLoading=Loading;this.ThumbnailUnavailable=Unavailable;
        this.PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(this.IsThumbnailLoading)));
        this.PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(this.ThumbnailStatusText)));
        this.PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(this.ToolTipContent)));
    }
}
