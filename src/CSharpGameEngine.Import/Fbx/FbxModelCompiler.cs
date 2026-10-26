using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using CSharpGameEngine.Content;
using CSharpGameEngine.Content.Models;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Import.Images;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Import.Fbx;
public sealed record ModelPublishedOutput(string Name,string Kind,string Uri,string Hash,string Address);
public sealed record CompositeModelManifest(string Schema,string AssetId,string SourceHash,ModelPublishedOutput[]Outputs);
/// <summary>モデルのアセット作成／インポートを行う明示的なコマンド。検証後、キャッシュ出力をまとめて公開する。</summary>
public static class FbxModelCompiler {
    public static AssetModelResult Execute(AssetModelRequest Request){
        try {
            if(Request.RequestId==Guid.Empty||Request.ModelCommand is not(1 or 2 or 3)){ throw new ArgumentException("MODEL_REQUEST: invalid command."); }
            var Source=Path.GetFullPath(Request.SourcePath);var Root=Path.GetFullPath(Request.Root);
            if(!Path.GetExtension(Source).Equals(".fbx",StringComparison.OrdinalIgnoreCase)){ throw new ArgumentException("MODEL_SOURCE: expected FBX."); }
            var Snapshot=AssetAuthoringService.Execute(new(){RequestId=Request.RequestId,Command=AssetAuthoringCommand.Inspect,RepositoryRoot=Root,SourcePath=Source});
            if(!Snapshot.Success){ throw new InvalidDataException(string.Join("; ",Snapshot.Diagnostics.Select(D=>D.Message))); }
            if(Request.ModelCommand==2&&Snapshot.State!=AssetAuthoringState.Ready){ throw new InvalidDataException("MODEL_METADATA_MISSING: register the model explicitly."); }
            var Options=Snapshot.Options.ToDictionary(O=>O.Name,O=>O.Value,StringComparer.Ordinal);
            var Settings=Request.ModelCommand==3?Request.Settings??throw new ArgumentException("Model settings required."):ModelImportSettings.FromOptions(Options);
            _=ModelImportSettings.FromOptions(Settings.ToOptions());
            if(Request.ModelCommand==3){
                if(Request.ExpectedMetadataHash!=Snapshot.MetadataSha256){ throw new IOException("MODEL_CONFLICT: metadata changed."); }
                foreach(var Key in Options.Keys.Where(K=>K.StartsWith("model.",StringComparison.Ordinal)).ToArray()){ Options.Remove(Key); }
                foreach(var Pair in Settings.ToOptions()){ Options[Pair.Key]=Pair.Value; }
                var Metadata=AssetMetadata.Create(AssetId.Parse(Snapshot.AssetId),Snapshot.SourceUri,Snapshot.Importer,Options);
                return new(){RequestId=Request.RequestId,Success=true,Settings=Settings,MetadataJson=System.Text.Encoding.UTF8.GetString(Metadata.ToCanonicalUtf8()),MetadataHash=Snapshot.MetadataSha256};
            }
            var Bytes=File.ReadAllBytes(Source);var Hash=Hash256.Compute(Bytes);
            var Model=FbxCompositeImporter.Import(Source,Settings);
            var Entries=new List<ModelSubassetSummary>();
            void Summary(string Name,SubAssetKind Kind)=>Entries.Add(new(){Name=Name,Kind=SubAssetIdentity.KindName(Kind),Id=SubAssetIdentity.Derive(Kind,Options.TryGetValue("subasset.alias."+SubAssetIdentity.KindName(Kind)+":"+Name,out var Alias)?Alias:Name).ToString()});
            foreach(var Mesh in Model.Meshes){ Summary(Mesh.Name,SubAssetKind.Mesh); }
            foreach(var Material in Model.Materials){ Summary(Material.Name,SubAssetKind.Material); }
            if(Model.Skeleton is { }Skeleton){ Summary(Skeleton.Name,SubAssetKind.Skeleton); }
            foreach(var Clip in Model.Clips){ Summary(Clip.Name,SubAssetKind.Clip); }
            foreach(var Image in Model.EmbeddedImages){ Summary(Image.Name,SubAssetKind.Image); }
            var Result=new AssetModelResult{RequestId=Request.RequestId,Success=true,Subassets=Entries.ToArray(),Settings=Settings,MetadataHash=Snapshot.MetadataSha256};
            if(Request.ModelCommand==1){ return Result; }
            var Id=AssetId.Parse(Snapshot.AssetId);var Cache=Path.Combine(Root,"obj","cge-content","models");var Store=new ContentAddressedStore(Cache);
            var Provenance=new ArtifactProvenance("cge.fbx-composite/1-assimp4.1",Snapshot.SourceUri,Hash,Snapshot.Importer,Hash256.Compute(JsonSerializer.SerializeToUtf8Bytes(Options.OrderBy(P=>P.Key,StringComparer.Ordinal))));
            var Published=new List<ModelPublishedOutput>();var Reads=new Dictionary<string,Hash256>(StringComparer.OrdinalIgnoreCase);
            ModelReference Publish(string Name,SubAssetKind Kind,ContentArtifactKind ContentKind,byte[]Payload,IEnumerable<AssetArtifactReference>?Dependencies=null){
                var IdentityName=Options.TryGetValue("subasset.alias."+SubAssetIdentity.KindName(Kind)+":"+Name,out var Alias)?Alias:Name;
                var Key=new AssetKey(Id,SubAssetIdentity.Derive(Kind,IdentityName));var Artifact=new ContentArtifact(ContentKind,Provenance,Dependencies,Payload);
                var Item=Store.PublishAsync(Key,ContentArtifactCodec.Write(Artifact)).AsTask().GetAwaiter().GetResult();
                if(Published.Any(P=>P.Uri==new AssetUri(Key).ToString())){ throw new InvalidDataException("MODEL_ID_COLLISION: "+IdentityName); }
                Published.Add(new(IdentityName,SubAssetIdentity.KindName(Kind),new AssetUri(Key).ToString(),Item.Reference.ContentHash.ToString(),Item.Address.ToString()));return ModelReference.From(Item.Reference);
            }
            AssetArtifactReference White=default,Flat=default;
            if(Model.Materials.Length>0){
                White=Publish("@default:white",SubAssetKind.Texture,ContentArtifactKind.Texture,ImageArtifactCodec.WriteTexture(new(false,[new(1,1,[255,255,255,255])]))).ToReference();
            Flat=Publish("@default:normal",SubAssetKind.Texture,ContentArtifactKind.Texture,ImageArtifactCodec.WriteTexture(new(false,[new(1,1,[128,128,255,255])]))).ToReference();
            }
            var TextureCache=new Dictionary<string,AssetArtifactReference>(StringComparer.Ordinal);
            AssetArtifactReference Texture(string?Name,TextureUsage Usage){
                if(Name is null){ return Usage==TextureUsage.Normal?Flat:White; }
                var CacheKey=Name+":"+ImageOutputSettings.Token(Usage);if(TextureCache.TryGetValue(CacheKey,out var Existing)){ return Existing; }
                byte[]Encoded;int Width;int Height;byte[]Pixels;
                if(Name.StartsWith('*')){
                    var Embedded=Model.EmbeddedImages.SingleOrDefault(I=>I.Name==Name)??throw new InvalidDataException("MODEL_IMAGE: missing embedded image.");
                    Encoded=Embedded.Encoded;
                    if(Encoded.Length>0){ var Image=ImageOutputCompiler.DecodeEncoded(Encoded);Width=Image.Width;Height=Image.Height;Pixels=Image.Pixels; }
                    else { Width=Embedded.Width;Height=Embedded.Height;Pixels=Embedded.Rgba;Encoded=PngSourceEncoder.Encode(Width,Height,Pixels); }
                }else{
                    var PathValue=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source)!,Name.Replace('\\',Path.DirectorySeparatorChar)));
                    if(!PathValue.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){ throw new InvalidDataException("MODEL_IMAGE: dependency escapes project."); }
                    var Info=new FileInfo(PathValue);if(Info.Length is <1 or >128*1024*1024){ throw new InvalidDataException("MODEL_IMAGE: encoded image exceeds limit."); }
                    Encoded=File.ReadAllBytes(PathValue);Reads[PathValue]=Hash256.Compute(Encoded);var Image=ImageOutputCompiler.DecodeEncoded(Encoded);Width=Image.Width;Height=Image.Height;Pixels=Image.Pixels;
                }
                if(!Published.Any(P=>P.Kind=="image"&&P.Name==Name)){
                    Publish(Name,SubAssetKind.Image,ContentArtifactKind.Image,ImageArtifactCodec.WriteImage(new(Width,Height,Encoded[0]==137?"image/png":"image/jpeg",Encoded)));
                }
                var Ref=Publish(CacheKey,SubAssetKind.Texture,ContentArtifactKind.Texture,ImageArtifactCodec.WriteTexture(ImageOutputCompiler.BuildTexture(Width,Height,Pixels,ImageOutputSettings.For(Usage)))).ToReference();TextureCache.Add(CacheKey,Ref);return Ref;
            }
            var Materials=new List<ModelReference>();
            foreach(var Material in Model.Materials){
                var Base=Texture(Material.ColorImage,TextureUsage.Color);var Normal=Texture(Material.NormalImage,TextureUsage.Normal);var Emissive=Texture(Material.EmissionImage,TextureUsage.Emission);
                var Definition=new RenderMaterialArtifactDefinition(Material.BaseColor,1f,0f,Material.Emissive,1f,Base,Normal,White,Emissive);
                var Envelope=RenderArtifactCodec.CreateMaterialEnvelope(Provenance,Definition);ContentArtifactCodec.TryRead(Envelope,out var Artifact,out _);
                Materials.Add(Publish(Material.Name,SubAssetKind.Material,ContentArtifactKind.Material,Artifact!.Payload.ToArray(),Artifact.Dependencies));
            }
            var Meshes=Model.Meshes.Select(M=>Publish(M.Name,SubAssetKind.Mesh,ContentArtifactKind.Mesh,ModelArtifactCodec.WriteMesh(M))).ToImmutableArray();
            var Skeletons=Model.Skeleton is { }Rig?ImmutableArray.Create(Publish(Rig.Name,SubAssetKind.Skeleton,ContentArtifactKind.Skeleton,ModelArtifactCodec.WriteSkeleton(Rig))):[];
            foreach(var Clip in Model.Clips){ Publish(Clip.Name,SubAssetKind.Clip,ContentArtifactKind.AnimationClip,ModelArtifactCodec.WriteClip(Clip)); }
            var RootData=new ModelData(Path.GetFileNameWithoutExtension(Source).Normalize(),Model.Nodes,Meshes,[..Materials],Skeletons,Model.Skins,[],[],default);
            var RootArtifact=new ContentArtifact(ContentArtifactKind.Model,Provenance,Published.Select(P=>new AssetArtifactReference(AssetUri.Parse(P.Uri).Key,Hash256.Parse(P.Hash))),ModelArtifactCodec.WriteModel(RootData));
            var RootPublished=Store.PublishAsync(new(Id,null),ContentArtifactCodec.Write(RootArtifact)).AsTask().GetAwaiter().GetResult();
            Published.Add(new("Model","model",new AssetUri(new(Id,null)).ToString(),RootPublished.Reference.ContentHash.ToString(),RootPublished.Address.ToString()));
            if(Hash256.Compute(File.ReadAllBytes(Source))!=Hash||Reads.Any(P=>!File.Exists(P.Key)||Hash256.Compute(File.ReadAllBytes(P.Key))!=P.Value)
                ||AssetMetadata.TryParse(File.ReadAllBytes(Source+".meta"),out var Current,out _)==false||Current is null||Current.AssetId!=Id||Hash256.Compute(Current.ToCanonicalUtf8()).ToString()!=Snapshot.MetadataSha256){
                throw new IOException("MODEL_CONFLICT: source, dependency or metadata changed; previous manifest retained.");
            }
            var Manifest=Path.Combine(Cache,Id+".json");var Temporary=Manifest+"."+Guid.NewGuid().ToString("N")+".tmp";
            try { File.WriteAllBytes(Temporary,JsonSerializer.SerializeToUtf8Bytes(new CompositeModelManifest("cge-model-outputs/1",Id.ToString(),Hash.ToString(),Published.ToArray())));File.Move(Temporary,Manifest,true); }
            finally { if(File.Exists(Temporary)){ File.Delete(Temporary); } }
            Result.ManifestPath=Manifest;Result.Diagnostic=AssetImportStatus.Clear(Root,Source);return Result;
        }catch(Exception Error)when(Error is not OutOfMemoryException and not StackOverflowException){ return new(){RequestId=Request.RequestId,Diagnostic=Request.ModelCommand==2?AssetImportStatus.RecordFailure(Request.Root,Request.SourcePath,Error.Message):Error.Message}; }
    }
}
