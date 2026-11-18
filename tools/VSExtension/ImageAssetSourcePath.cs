using System;
namespace CSharpGameEngine.VSExtension;
internal static class ImageAssetSourcePath {
    internal static string?Parse(string?AssetPath,string?AssetTypeName){
        if(AssetPath is null||string.IsNullOrWhiteSpace(AssetPath)){ return null; }
        var Kind=AssetTypeName switch {
            "CSharpGameEngine.Authoring.TextureAsset"=>CSharpGameEngine.Contracts.SubAssetKind.Texture,
            "CSharpGameEngine.Authoring.ImageAsset"=>CSharpGameEngine.Contracts.SubAssetKind.Image,
            _=>(CSharpGameEngine.Contracts.SubAssetKind?)null
        };
        if(Kind is null){ return null; }
        try {
            return CSharpGameEngine.Contracts.AssetSelector.Parse(AssetPath,Kind.Value).Path;
        } catch(ArgumentException){
            return null;
        } catch(FormatException){
            return null;
        }
    }
}
