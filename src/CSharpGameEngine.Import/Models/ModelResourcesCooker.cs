using System.Text.Json;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Cooking;
using CSharpGameEngine.Import.Images;
using CSharpGameEngine.Interchange;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Import.Models;
public sealed class ModelResourcesCooker:IAssetCooker {
    public string Name=>"ModelResources";
    public IReadOnlyList<string>InputExtensions=>[".cgemodelresources"];
    private sealed record ResourceOutput(string Name,string Uri,string File);
    public CookResult Cook(CookRequest Request){
        try {
            if(new FileInfo(Request.InputPath).Length>65536){ throw new InvalidDataException("IMP903: model resource request is too large."); }
            using var Json=JsonDocument.Parse(File.ReadAllBytes(Request.InputPath));
            var Root=Json.RootElement;
            if(Root.GetProperty("Schema").GetString()!="cge-model-resources/1"){ throw new InvalidDataException("IMP903: unsupported model resource request."); }
            var Asset=AssetId.Parse(Root.GetProperty("AssetId").GetString()!);
            if(!Hash256.TryParse(Root.GetProperty("SourceHash").GetString(),out var SourceHash)||!Hash256.TryParse(Root.GetProperty("OptionsHash").GetString(),out var OptionsHash)){
                throw new InvalidDataException("IMP903: invalid model resource provenance.");
            }
            var Tool=Root.GetProperty("ToolVersion").GetString()!;
            var Input=Path.Combine(Path.GetDirectoryName(Request.InputPath)!,"source.cgex");
            if(new FileInfo(Input).Length>512L*1024*1024){ throw new InvalidDataException("IMP903: model interchange input is too large."); }
            var Document=InterchangeCodec.Read(File.ReadAllBytes(Input));
            var Errors=ModelImportPipeline.ValidateEngine(Document).Where(static Diagnostic=>Diagnostic.Severity==ImportSeverity.Error).ToArray();
            if(Errors.Length!=0){ throw new InvalidDataException(string.Join("\n",Errors.Select(static Diagnostic=>Diagnostic.ToString()))); }
            var Provenance=new ArtifactProvenance(Tool,Document.Provenance.SourceUri,SourceHash,Document.Provenance.ImporterIdentity,OptionsHash);
            var Outputs=new List<string>();
            var Entries=new List<ResourceOutput>();
            var Keys=new HashSet<AssetKey>();
            AssetArtifactReference Publish(SubAssetKind Kind,string Name,byte[]Envelope){
                var Key=new AssetKey(Asset,SubAssetIdentity.Derive(Kind,Name));
                if(!Keys.Add(Key)||!ContentArtifactCodec.TryRead(Envelope,out var Artifact,out _)||Artifact is null){ throw new InvalidDataException("IMP903: duplicate or invalid model resource."); }
                var FileName=$"{Entries.Count:D6}.cgea";
                File.WriteAllBytes(Path.Combine(Request.OutputDirectory,FileName),Envelope);
                Outputs.Add(FileName);Entries.Add(new(Name,new AssetUri(Key).ToString(),FileName));
                return new(Key,Artifact.ContentHash);
            }
            byte[]Envelope(ContentArtifactKind Kind,byte[]Payload)=>ContentArtifactCodec.Write(new(Kind,Provenance,[],Payload));
            var White=Publish(SubAssetKind.Texture,"@cge/white",Envelope(ContentArtifactKind.Texture,ImageArtifactCodec.WriteTexture(new(false,[new(1,1,[255,255,255,255])]))));
            var Flat=Publish(SubAssetKind.Texture,"@cge/normal",Envelope(ContentArtifactKind.Texture,ImageArtifactCodec.WriteTexture(new(false,[new(1,1,[128,128,255,255])]))));
            var Decoded=new Dictionary<int,(int Width,int Height,byte[]Pixels)>();
            var Textures=new Dictionary<(int Index,TextureUsage Usage),AssetArtifactReference>();
            AssetArtifactReference Texture(InterchangeTextureRef?Reference,TextureUsage Usage,AssetArtifactReference Fallback){
                if(Reference is not{}Value){ return Fallback; }
                if(Value.TexCoord!=0||Value.Transform.Offset!=System.Numerics.Vector2.Zero||Value.Transform.Scale!=System.Numerics.Vector2.One||Value.Transform.Rotation!=0f){
                    throw new InvalidDataException("IMP143: the runtime material requires texture mapping baked into UV0 before resource preparation.");
                }
                if(Textures.TryGetValue((Value.TextureIndex,Usage),out var Existing)){ return Existing; }
                var Source=Document.Textures[Value.TextureIndex];
                if(!Decoded.TryGetValue(Source.ImageIndex,out var Image)){
                    var SourceImage=Document.Images[Source.ImageIndex];
                    Image=ImageOutputCompiler.DecodeEncoded(SourceImage.EncodedBytes.ToArray());
                    Decoded.Add(Source.ImageIndex,Image);
                    Publish(SubAssetKind.Image,$"image/{Source.ImageIndex}",Envelope(ContentArtifactKind.Image,ImageArtifactCodec.WriteImage(new(Image.Width,Image.Height,SourceImage.MimeType,SourceImage.EncodedBytes.ToArray()))));
                }
                var Settings=ImageOutputSettings.For(Usage);
                if(Usage==TextureUsage.Packed){ Settings.OcclusionChannel="r";Settings.RoughnessChannel="g";Settings.MetallicChannel="b"; }
                var Payload=ImageArtifactCodec.WriteTexture(ImageOutputCompiler.BuildTexture(Image.Width,Image.Height,Image.Pixels,Settings));
                var Result=Publish(SubAssetKind.Texture,$"{Source.Name}/{Usage}",Envelope(ContentArtifactKind.Texture,Payload));
                Textures.Add((Value.TextureIndex,Usage),Result);
                return Result;
            }
            foreach(var Material in Document.Materials){
                var Definition=new RenderMaterialArtifactDefinition(Material.BaseColorFactor,Material.RoughnessFactor,Material.MetallicFactor,Material.EmissiveNits,Material.EmissiveTextureLuminanceScale,
                    Texture(Material.BaseColor,TextureUsage.Color,White),Texture(Material.Normal,TextureUsage.Normal,Flat),Texture(Material.MetallicRoughness,TextureUsage.Packed,White),Texture(Material.Emissive,TextureUsage.Emission,White),ModelWorldGridBlock.Read(Material.Blocks));
                Publish(SubAssetKind.Material,Material.Name,RenderArtifactCodec.CreateMaterialEnvelope(Provenance,Definition));
            }
            File.WriteAllBytes(Path.Combine(Request.OutputDirectory,"resources.json"),JsonSerializer.SerializeToUtf8Bytes(new { Schema="cge-model-resources/1",AssetId=Asset.ToString(),Outputs=Entries }));
            Outputs.Add("resources.json");
            return CookResult.Success([..Outputs]);
        }catch(Exception Error)when(Error is IOException or ArgumentException or JsonException or OverflowException){
            return CookResult.Failure("IMP903: "+Error.Message);
        }
    }
}
