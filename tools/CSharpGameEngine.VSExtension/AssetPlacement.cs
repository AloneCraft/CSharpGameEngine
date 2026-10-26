using System;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.SceneAnalysis;
namespace CSharpGameEngine.VSExtension;
internal sealed record AssetPlacementRequest(AssetWorkspaceItem Item,NodePath?Parent);
internal static class AssetPlacement {
    internal static event Action<AssetPlacementRequest>?Requested;
    internal static bool CanPlace(AssetWorkspaceItem Item,string?ScenePath)=>Item.Kind==AssetWorkspaceKind.Model&&AssetReferencePlanner.InCatalog(Item.Source.RelativePath)
        &&Item.State is not(AssetWorkspaceState.MissingSource or AssetWorkspaceState.InvalidMetadata)
        &&ScenePath is not null&&CgeProjectContext.Resolve(ScenePath)?.ProjectFilePath==Item.Source.ProjectPath;
    internal static void Place(AssetWorkspaceItem Item,NodePath?Parent)=>Requested?.Invoke(new(Item,Parent));
}
