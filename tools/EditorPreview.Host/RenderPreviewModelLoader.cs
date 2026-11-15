using CSharpGameEngine.Content;
using CSharpGameEngine.Content.Models;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.EditorPreview.Host;
internal static class RenderPreviewModelLoader {
    internal static string Identity(RenderPreviewModelSource?Source)=>Source is null?string.Empty:Path.GetFullPath(Source.ContentDirectory)+"|"+Source.SourceUri+"|"+Source.ModelHash;
    internal static ModelAssetBundle Open(RenderPreviewModelSource?Source){
        if(Source is null||!Hash256.TryParse(Source.ModelHash,out var Expected)||!Expected.IsValid){ throw new InvalidDataException("A prepared model reference is required for preview."); }
        var Bundle=ModelAssetBundle.OpenAsync(Source.ContentDirectory,Source.SourceUri).GetAwaiter().GetResult();
        if(Bundle.Index.Resolve(Source.SourceUri,ContentArtifactKind.Model).ContentHash!=Expected){ Bundle.Dispose();throw new InvalidDataException("The prepared model reference has changed; request a new preview."); }
        return Bundle;
    }
}
