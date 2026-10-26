using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
namespace CSharpGameEngine.VSExtension;
/// <summary>L8 の表示情報のみ。アイコンの識別子をアセットデータに永続化することはない。</summary>
internal static class AssetWorkspaceIcons {
    internal static ImageMoniker Window=>KnownMonikers.ImageGroup;
    internal static ImageMoniker Kind(AssetWorkspaceKind Kind)=>Kind switch{
        AssetWorkspaceKind.Scene=>KnownMonikers.ThreeDScene,
        AssetWorkspaceKind.Model=>KnownMonikers.AbstractCube,
        AssetWorkspaceKind.Texture or AssetWorkspaceKind.Image=>KnownMonikers.Image,
        AssetWorkspaceKind.Audio=>KnownMonikers.Sound,
        AssetWorkspaceKind.ShaderGraph=>KnownMonikers.Shader,
        AssetWorkspaceKind.Data=>KnownMonikers.DataTable,
        AssetWorkspaceKind.Font=>KnownMonikers.FontFile,
        _=>KnownMonikers.TextFile,
    };
    internal static ImageMoniker State(AssetWorkspaceState State)=>State switch{
        AssetWorkspaceState.ImportFailed or AssetWorkspaceState.InvalidMetadata or AssetWorkspaceState.MissingSource=>KnownMonikers.StatusError,
        AssetWorkspaceState.MissingMetadata=>KnownMonikers.StatusWarning,
        _=>default,
    };
    internal static bool HasState(AssetWorkspaceState State)=>State is AssetWorkspaceState.ImportFailed or AssetWorkspaceState.InvalidMetadata or AssetWorkspaceState.MissingSource or AssetWorkspaceState.MissingMetadata;
    // Inventory は、プロバイダーを ReadOnly として表示する間もメタデータ診断を保持できる。
    internal static bool HasMetadataError(AssetWorkspaceItem Item)=>Item.State==AssetWorkspaceState.ReadOnly&&!string.IsNullOrEmpty(Item.Diagnostic);
    internal static ImageMoniker State(AssetWorkspaceItem Item)=>HasMetadataError(Item)?KnownMonikers.StatusError:State(Item.State);
    internal static bool HasState(AssetWorkspaceItem Item)=>HasMetadataError(Item)||HasState(Item.State);
    internal static bool ReadOnly(AssetWorkspaceItem Item)=>Item.Source.ReadOnly||Item.State==AssetWorkspaceState.ReadOnly;
}
