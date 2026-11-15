namespace CSharpGameEngine.Contracts;
public static class ImageOutputIdentity {
    public static SubAssetId GetSubAssetId(this ImageOutputSettings Output)=>SubAssetIdentity.Derive(Output.Kind==ImageOutputKind.Image?SubAssetKind.Image:SubAssetKind.Texture,Output.Key);
}
