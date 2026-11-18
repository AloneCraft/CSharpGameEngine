using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.VSExtension;
/// <summary>A saved image-output edit and the exact catalog value it permits the following scene commit to write.</summary>
internal sealed record SceneImageOutputPrecondition(string MetadataPath,string MetadataText,string AssetTypeName,string Expression,string AssetPath,string?MetadataBefore=null,SceneImageOutputObservation?SceneObservation=null) {
    internal bool CanPersistRollback(string RestoredBuffer,string DiskText)=>this.MetadataBefore is not null
        &&string.Equals(RestoredBuffer,this.MetadataBefore,StringComparison.Ordinal)
        &&(string.Equals(DiskText,this.MetadataText,StringComparison.Ordinal)||string.Equals(DiskText,this.MetadataBefore,StringComparison.Ordinal));
    internal async Task<bool>MatchesAsync(Project?Project,Compilation Compilation,string SceneClass,CancellationToken CancellationToken){
        if(Project is null){ return false; }
        var Documents=Project.AdditionalDocuments.Where(Document=>string.Equals(Document.FilePath,this.MetadataPath,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(Documents.Length!=1){ return false; }
        var Text=await Documents[0].GetTextAsync(CancellationToken).ConfigureAwait(false);
        CancellationToken.ThrowIfCancellationRequested();
        return string.Equals(Text.ToString(),this.MetadataText,StringComparison.Ordinal)
            &&SceneTypedAssetValue.TryGetCatalogPath(Compilation,SceneClass,this.AssetTypeName,this.Expression,out var Path)
            &&string.Equals(Path,this.AssetPath,StringComparison.Ordinal);
    }
}
