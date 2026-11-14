using System;
using System.IO;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.SceneAnalysis;
namespace CSharpGameEngine.VSExtension;
internal sealed record AssetPlacementRequest(AssetWorkspaceItem Item,NodePath?Parent);
internal static class AssetPlacement {
    internal static event Action<AssetPlacementRequest>?Requested;
    internal static bool CanPlace(AssetWorkspaceItem Item,string?ScenePath)=>ScenePath is not null&&CanAssignModel(Item,CgeProjectContext.Resolve(ScenePath)?.ProjectFilePath)&&HasStaticMesh(Item.Source.SourcePath);
    internal static bool HasStaticMesh(string SourcePath)=>Path.GetExtension(SourcePath).ToLowerInvariant() switch {
        ".glb" or ".gltf"=>GltfSkeletalProbe.Probe(SourcePath) is { MeshCount:>0,SkinCount:0 },
        ".fbx"=>true,
        _=>false,
    };
    internal static bool CanAssignModel(AssetWorkspaceItem Item,string?ProjectPath)=>!string.IsNullOrEmpty(ProjectPath)&&Item.Kind==AssetWorkspaceKind.Model&&AssetReferencePlanner.InCatalog(Item.Source.RelativePath)
        &&Item.State is not(AssetWorkspaceState.MissingSource or AssetWorkspaceState.InvalidMetadata)
        &&string.Equals(ProjectPath,Item.Source.ProjectPath,StringComparison.OrdinalIgnoreCase);
    internal static void Place(AssetWorkspaceItem Item,NodePath?Parent)=>Requested?.Invoke(new(Item,Parent));
}
