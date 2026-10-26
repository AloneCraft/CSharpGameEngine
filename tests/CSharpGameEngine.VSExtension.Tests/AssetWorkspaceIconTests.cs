using CSharpGameEngine.Contracts;
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
    private static void Equal(ImageMoniker Expected,ImageMoniker Actual){ Assert.Equal(Expected.Guid,Actual.Guid);Assert.Equal(Expected.Id,Actual.Id); }
}
