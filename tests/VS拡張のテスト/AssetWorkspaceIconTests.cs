using CSharpGameEngine.Contracts;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class AssetWorkspaceIconTests {
    [Fact]
    public void AssetKindsUseTheAgreedCatalogImages(){
        var Expected=new[]{KnownMonikers.ThreeDScene,KnownMonikers.AbstractCube,KnownMonikers.Image,KnownMonikers.Sound,KnownMonikers.Shader,KnownMonikers.DataTable,KnownMonikers.FontFile,KnownMonikers.TextFile,KnownMonikers.Image};
        var Kinds=(AssetWorkspaceKind[])Enum.GetValues(typeof(AssetWorkspaceKind));Assert.Equal(Expected.Length,Kinds.Length);
        for(var Index=0;Index<Kinds.Length;Index++){ Equal(Expected[Index],AssetWorkspaceIcons.Kind(Kinds[Index])); }
        Equal(KnownMonikers.ImageGroup,AssetWorkspaceIcons.Window);
        Equal(KnownMonikers.TextFile,AssetWorkspaceIcons.Kind((AssetWorkspaceKind)int.MaxValue));
    }
    [Theory]
    [InlineData(AssetWorkspaceState.InvalidMetadata)]
    [InlineData(AssetWorkspaceState.MissingSource)]
    public void FailuresKeepTheirErrorIconWhenTheProviderIsReadOnly(AssetWorkspaceState State){
        var Item=new AssetWorkspaceItem{Kind=AssetWorkspaceKind.Model,State=State,Source=new(){ReadOnly=true}};
        var Row=new AssetWorkspaceRow(Item);
        Assert.True(Row.IsReadOnly);Assert.True(Row.HasStateIcon);Equal(KnownMonikers.StatusError,Row.StateMoniker);Equal(KnownMonikers.Lock,Row.LockMoniker);
    }
    [Fact]
    public void MissingMetadataWarnsWhileNormalSourcesHaveNoStateBadge(){
        Equal(KnownMonikers.StatusWarning,AssetWorkspaceIcons.State(AssetWorkspaceState.MissingMetadata));
        Assert.True(AssetWorkspaceIcons.HasState(AssetWorkspaceState.MissingMetadata));
        foreach(var State in new[]{AssetWorkspaceState.Source,AssetWorkspaceState.Ready,AssetWorkspaceState.ReadOnly}){
            Assert.False(AssetWorkspaceIcons.HasState(State));Assert.Equal(Guid.Empty,AssetWorkspaceIcons.State(State).Guid);
        }
        Assert.True(new AssetWorkspaceRow(new(){State=AssetWorkspaceState.ReadOnly}).IsReadOnly);
    }
    [Fact]
    public void ProviderMetadataDiagnosticIsNotHiddenByTheReadOnlyProjection(){
        var Row=new AssetWorkspaceRow(new(){State=AssetWorkspaceState.ReadOnly,Diagnostic="ASSET_METADATA_INVALID: malformed JSON",Source=new(){ReadOnly=true}});
        Assert.True(Row.IsReadOnly);Assert.True(Row.HasStateIcon);Equal(KnownMonikers.StatusError,Row.StateMoniker);
        Assert.Equal(VsixText.Get("Assets_State_InvalidMetadata"),Row.StateLabel);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThumbnailWaitsVisiblyAndStopsWhenTheRequestFinishes(bool HasImage){
        var Completion=new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable VSTHRD003 // This test-owned gate has no JTF dependency and is completed before the operation is awaited.
        var Row=new AssetWorkspaceRow(new(){Kind=AssetWorkspaceKind.Model},ThumbnailLoader:_=>Completion.Task);
#pragma warning restore VSTHRD003
        var States=new List<bool>();
        Row.PropertyChanged+=(_,Args)=>{ if(Args.PropertyName==nameof(Row.IsThumbnailLoading)){ States.Add(Row.IsThumbnailLoading); } };
        var Operation=Row.LoadAsync();
        Assert.True(Row.IsThumbnailLoading);Assert.False(string.IsNullOrWhiteSpace(Row.ThumbnailStatusText));
        var Image=HasImage?Pixel():null;Completion.SetResult(Image);await Operation;
        Assert.False(Row.IsThumbnailLoading);Assert.Equal(new[]{true,false},States);Assert.Same(Image,Row.Thumbnail);
        Assert.Equal(HasImage?string.Empty:VsixText.Get("Assets_ThumbnailUnavailable"),Row.ThumbnailStatusText);
        if(!HasImage){ Assert.Contains(VsixText.Get("Assets_ThumbnailUnavailableHint"),(string)Row.ToolTipContent); }
    }
    [Fact]
    public async Task CancellingAThumbnailStopsTheIndicatorAndCanBeRepeated(){
        var Row=new AssetWorkspaceRow(new(){Kind=AssetWorkspaceKind.Model},ThumbnailLoader:async Token=>{ await Task.Delay(Timeout.Infinite,Token);return null; });
        var Operation=Row.LoadAsync();Assert.True(Row.IsThumbnailLoading);
        Row.CancelThumbnail();await Operation;Row.CancelThumbnail();
        Assert.False(Row.IsThumbnailLoading);Assert.Equal(string.Empty,Row.ThumbnailStatusText);
    }
    [Fact]
    public async Task ACancelledOldRequestCannotClearTheNewIndicatorOrReplaceItsImage(){
        var Old=new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var Current=new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var Requests=new Queue<Task<BitmapSource?>>(new[]{Old.Task,Current.Task});
        var Row=new AssetWorkspaceRow(new(){Kind=AssetWorkspaceKind.Model},ThumbnailLoader:_=>Requests.Dequeue());
        var First=Row.LoadAsync();Row.CancelThumbnail();var Second=Row.LoadAsync();
        Old.SetResult(Pixel());await First;
        Assert.True(Row.IsThumbnailLoading);Assert.Null(Row.Thumbnail);
        var Image=Pixel();Current.SetResult(Image);await Second;
        Assert.False(Row.IsThumbnailLoading);Assert.Same(Image,Row.Thumbnail);
    }
    [Fact]
    public async Task AnUnexpectedThumbnailFailureDoesNotLeaveTheIndicatorRunning(){
        var Row=new AssetWorkspaceRow(new(){Kind=AssetWorkspaceKind.Model},ThumbnailLoader:_=>Task.FromException<BitmapSource?>(new NotSupportedException("thumbnail failure")));
        await Assert.ThrowsAsync<NotSupportedException>(()=>Row.LoadAsync());
        Assert.False(Row.IsThumbnailLoading);
    }
    [Theory]
    [InlineData(AssetWorkspaceKind.Audio)]
    [InlineData(AssetWorkspaceKind.ShaderGraph)]
    [InlineData(AssetWorkspaceKind.Data)]
    [InlineData(AssetWorkspaceKind.Other)]
    public void IconOnlyKindsDoNotStartLoadingOrReportAMissingPreview(AssetWorkspaceKind Kind){
        var Requests=0;
        var Row=new AssetWorkspaceRow(new(){Kind=Kind},ThumbnailLoader:_=>{ Requests++;return Task.FromResult<BitmapSource?>(null); });
        Assert.Null(Row.Thumbnail);Assert.Equal(0,Requests);
        Assert.False(Row.IsThumbnailLoading);Assert.Equal(string.Empty,Row.ThumbnailStatusText);
    }
    private static BitmapSource Pixel(){
        var Image=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{255,255,255,255},4);Image.Freeze();return Image;
    }
    private static void Equal(ImageMoniker Expected,ImageMoniker Actual){ Assert.Equal(Expected.Guid,Actual.Guid);Assert.Equal(Expected.Id,Actual.Id); }
}
