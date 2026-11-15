using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Import.Images;
public sealed record ImagePublishedOutput(string Key,string Kind,string Usage,string Uri,string ContentHash,string Address,string OptionsHash="");
public sealed record ImageOutputManifest(string Schema,string AssetId,string SourceHash,string OptionsHash,string DefaultOutput,ImagePublishedOutput[]Outputs);
/// <summary>L7 の画像解釈、参考分析、原子的な公開処理。ソースは書き換えない。</summary>
public static class ImageOutputCompiler {
    public static AssetImageResult Execute(AssetImageRequest Request){
        try {
            if(Request.RequestId==Guid.Empty||Request.ImageCommand is <1 or >6){ throw new ArgumentException("IMAGE_REQUEST: invalid command."); }
            var Root=Path.GetFullPath(Request.Root);var Source=Path.GetFullPath(Request.SourcePath);
            if(!Source.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!ImageImportSettings.Supports(Source)){
                throw new ArgumentException("IMAGE_SOURCE: expected PNG/JPG/JPEG inside the project.");
            }
            var Bytes=ReadSource(Source);var Dimensions=DimensionsOf(Bytes);ImageArtifactCodec.ValidateSize(Dimensions.Width,Dimensions.Height);
            var Snapshot=AssetAuthoringService.Execute(new(){RequestId=Request.RequestId,Command=AssetAuthoringCommand.Inspect,RepositoryRoot=Root,SourcePath=Source});
            if(!Snapshot.Success){ throw new InvalidDataException(string.Join("; ",Snapshot.Diagnostics.Select(D=>D.Message))); }
            var Existing=Snapshot.State==AssetAuthoringState.Ready;
            var Settings=Existing?ImageImportSettings.FromOptions(Snapshot.Options.ToDictionary(O=>O.Name,O=>O.Value)):ImageImportSettings.Initial(Source,Request.TargetUsage);
            if(Request.ImageCommand is 2 or 6){ Settings=Request.Settings??throw new ArgumentException("IMAGE_SETTINGS: settings required."); }
            if(Request.ImageCommand==5&&Request.TargetUsage is { }Usage&&!Settings.Outputs.Any(O=>O.Kind==ImageOutputKind.Texture&&O.Usage==Usage)){
                var Output=ImageOutputSettings.For(Usage);var Key=Output.Key;var Suffix=2;
                while(Settings.Outputs.Any(O=>O.Key==Output.Key)){ Output.Key=Key+Suffix++; }
                Settings.Outputs=[..Settings.Outputs,Output];
            }
            Settings.Validate();
            if(Request.ImageCommand==6&&Request.ExpectedMetadataHash!=Snapshot.MetadataSha256){ throw new IOException("IMAGE_CONFLICT: metadata changed before preview."); }
            var Id=AssetId.Parse(Snapshot.AssetId);
            var Metadata=AssetMetadata.Create(Id,Path.GetRelativePath(Root,Source).Replace('\\','/'),ImageImportSettings.Importer,Settings.ToOptions());
            if(Request.ImageCommand is 2 or 4 or 5){
                if(Request.ExpectedMetadataHash!=Snapshot.MetadataSha256){ throw new IOException("IMAGE_CONFLICT: metadata changed; refresh before applying."); }
                var Saved=AssetAuthoringService.Execute(new(){RequestId=Request.RequestId,Command=AssetAuthoringCommand.SaveMetadata,RepositoryRoot=Root,SourcePath=Source,
                    ExpectedMetadataSha256=Request.ExpectedMetadataHash,AssetId=Snapshot.AssetId,Importer=ImageImportSettings.Importer,
                    Options=Settings.ToOptions().Select(P=>new AssetAuthoringOption{Name=P.Key,Value=P.Value}).ToArray()});
                if(!Saved.Success){ throw new InvalidDataException(string.Join("; ",Saved.Diagnostics.Select(D=>D.Code+": "+D.Message))); }
                Snapshot=Saved;Existing=true;
            }
            if(Request.ImageCommand==3&&!Existing){ throw new InvalidDataException("IMAGE_METADATA_MISSING: register the source explicitly before importing."); }
            var Pixels=Decode(Bytes,Dimensions.Width,Dimensions.Height);
            var Candidate=SuggestNormal(Pixels);
            var Result=new AssetImageResult{RequestId=Request.RequestId,Success=true,Settings=Settings,AssetId=Snapshot.AssetId,MetadataHash=Snapshot.MetadataSha256,
                MetadataJson=System.Text.Encoding.UTF8.GetString(Metadata.ToCanonicalUtf8()),Width=Dimensions.Width,Height=Dimensions.Height,
                NormalCandidate=Candidate,NormalCandidateReason=Candidate?"RGB vectors are mostly unit length and face +Z; this is only a suggestion and does not determine green-channel orientation.":string.Empty};
            if(Request.ImageCommand is not(1 or 6)){
                Result.ManifestPath=Publish(Root,Source,Bytes,Snapshot,Settings,Pixels,Dimensions.Width,Dimensions.Height);
                Result.Diagnostic=AssetImportStatus.Clear(Root,Source);
            }
            return Result;
        }catch(Exception Error)when(Error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException or NotSupportedException or JsonException){
            return new(){RequestId=Request.RequestId,Diagnostic=Request.ImageCommand is 1 or 6?Error.Message:AssetImportStatus.RecordFailure(Request.Root,Request.SourcePath,Error.Message)};
        }
    }
    internal static (int Width,int Height,byte[]Pixels)DecodeEncoded(byte[]Bytes){
        var Size=DimensionsOf(Bytes);ImageArtifactCodec.ValidateSize(Size.Width,Size.Height);return(Size.Width,Size.Height,Decode(Bytes,Size.Width,Size.Height));
    }
    public static bool SuggestNormal(ReadOnlySpan<byte>Pixels){
        if(Pixels.Length<16||Pixels.Length%4!=0){ return false; }
        var Count=Pixels.Length/4;var Step=Math.Max(1,Count/4096);var Valid=0;var Samples=0;
        for(var I=0;I<Count;I+=Step){
            var V=new Vector3(Pixels[I*4],Pixels[I*4+1],Pixels[I*4+2])/127.5f-Vector3.One;
            if(V.Z>0.1f&&MathF.Abs(V.LengthSquared()-1f)<0.15f){ Valid++; }Samples++;
        }
        return Valid>=Samples*0.9f;
    }
    public static TextureOutputData BuildTexture(int Width,int Height,ReadOnlySpan<byte>Pixels,ImageOutputSettings Settings){
        ImageArtifactCodec.ValidateSize(Width,Height);
        if(Settings.Kind!=ImageOutputKind.Texture||Pixels.Length!=checked(Width*Height*4)){ throw new ArgumentException("IMAGE_TEXTURE: invalid source/output."); }
        var Result=Pixels.ToArray();
        for(var I=0;I<Result.Length;I+=4){
            if(Settings.Usage==TextureUsage.Normal){
                if(Settings.FlipGreen){ Result[I+1]=(byte)(255-Result[I+1]); }Normalize(Result,I);
            } else if(Settings.Usage==TextureUsage.Packed){
                var Ao=Channel(Pixels,I,Settings.OcclusionChannel);var Roughness=Channel(Pixels,I,Settings.RoughnessChannel);var Metallic=Channel(Pixels,I,Settings.MetallicChannel);
                Result[I]=Ao;Result[I+1]=Settings.InvertRoughness?(byte)(255-Roughness):Roughness;Result[I+2]=Metallic;Result[I+3]=255;
            } else if(Settings.Usage is TextureUsage.Metallic or TextureUsage.Roughness or TextureUsage.AmbientOcclusion){
                var Value=Channel(Pixels,I,Settings.Channel);if(Settings.Usage==TextureUsage.Roughness&&Settings.InvertRoughness){ Value=(byte)(255-Value); }
                Result[I]=Value;Result[I+1]=Value;Result[I+2]=Value;Result[I+3]=255;
            }
        }
        var Chain=RenderTextureMipChain.Build(Width,Height,Result,Settings.IsSrgb?RenderTextureColorSpace.Srgb:RenderTextureColorSpace.Linear);
        var Levels=Chain.Levels.Take(Settings.Mipmaps?Chain.Levels.Count:1).Select(Level=>new TextureMipData(Level.Width,Level.Height,Level.Pixels.ToArray())).ToArray();
        if(Settings.Usage==TextureUsage.Normal){ foreach(var Level in Levels){ for(var I=0;I<Level.Pixels.Length;I+=4){ Normalize(Level.Pixels,I); } } }
        return new(Settings.IsSrgb,Levels);
    }
    private static byte Channel(ReadOnlySpan<byte>Pixels,int I,string Channel)=>Channel switch {
        "r"=>Pixels[I],"g"=>Pixels[I+1],"b"=>Pixels[I+2],"a"=>Pixels[I+3],"zero"=>0,"one"=>255,_=>throw new ArgumentException("IMAGE_CHANNEL")
    };
    private static void Normalize(byte[]Pixels,int I){
        var Normal=new Vector3(Pixels[I],Pixels[I+1],Pixels[I+2])/127.5f-Vector3.One;
        Normal=Normal.LengthSquared()<0.0001f?Vector3.UnitZ:Vector3.Normalize(Normal);
        for(var C=0;C<3;C++){ Pixels[I+C]=(byte)Math.Clamp((int)MathF.Round((Normal[C]+1f)*127.5f),0,255); }
    }
    private static string Publish(string Root,string Source,byte[]Bytes,AssetAuthoringResult Snapshot,ImageImportSettings Settings,byte[]Pixels,int Width,int Height){
        var Id=AssetId.Parse(Snapshot.AssetId);var Hash=Hash256.Compute(Bytes);var OptionsHash=Hash256.Compute(JsonSerializer.SerializeToUtf8Bytes(Settings.ToOptions()));
        var Cache=Path.Combine(Root,"obj","cge-content","images");var Store=new ContentAddressedStore(Cache);
        var Provenance=new ArtifactProvenance("cge.image/1-stb2.30.16",Path.GetRelativePath(Root,Source).Replace('\\','/'),Hash,ImageImportSettings.Importer,OptionsHash);
        var Outputs=new List<ImagePublishedOutput>();
        var ManifestPath=Path.Combine(Cache,Id+".json");
        var Previous=File.Exists(ManifestPath)?JsonSerializer.Deserialize<ImageOutputManifest>(File.ReadAllBytes(ManifestPath)):null;
        if(Previous is not null&&(Previous.Schema!="cge-image-outputs/1"||Previous.AssetId!=Id.ToString())){ throw new InvalidDataException("IMAGE_CACHE: invalid previous manifest."); }
        foreach(var Output in Settings.Outputs){
            var Profile=Output.Kind==ImageOutputKind.Image?new SortedDictionary<string,string>{{"kind","image"}}:new SortedDictionary<string,string>{
                {"kind","texture"},{"usage",ImageOutputSettings.Token(Output.Usage)},{"flipGreen",Output.FlipGreen.ToString()},{"mipmaps",Output.Mipmaps.ToString()},
                {"channel",Output.Channel},{"occlusion",Output.OcclusionChannel},{"roughness",Output.RoughnessChannel},{"metallic",Output.MetallicChannel},{"invertRoughness",Output.InvertRoughness.ToString()}};
            var ProfileHash=Hash256.Compute(JsonSerializer.SerializeToUtf8Bytes(Profile));
            var Prior=Previous?.SourceHash==Hash.ToString()?Previous.Outputs.SingleOrDefault(P=>P.Key==Output.Key&&P.OptionsHash==ProfileHash.ToString()):null;
            if(Prior is not null){
                var Envelope=Store.ReadAsync(Hash256.Parse(Prior.Address)).AsTask().GetAwaiter().GetResult();
                if(ContentArtifactCodec.TryRead(Envelope.Span,out var Cached,out _)&&Cached is not null&&Cached.ContentHash.ToString()==Prior.ContentHash
                    &&Cached.Provenance.SourceUri==Provenance.SourceUri&&Cached.Provenance.ToolVersion==Provenance.ToolVersion){ Outputs.Add(Prior);continue; }
            }
            var Key=new AssetKey(Id,Output.GetSubAssetId());
            var Kind=Output.Kind==ImageOutputKind.Image?ContentArtifactKind.Image:ContentArtifactKind.Texture;
            var Payload=Output.Kind==ImageOutputKind.Image?ImageArtifactCodec.WriteImage(new(Width,Height,Bytes[0]==137?"image/png":"image/jpeg",Bytes))
                :ImageArtifactCodec.WriteTexture(BuildTexture(Width,Height,Pixels,Output));
            var Artifact=new ContentArtifact(Kind,Provenance with{OptionsHash=ProfileHash},null,Payload);
            var Published=Store.PublishAsync(Key,ContentArtifactCodec.Write(Artifact)).AsTask().GetAwaiter().GetResult();
            Outputs.Add(new(Output.Key,Output.Kind.ToString(),ImageOutputSettings.Token(Output.Usage),new AssetUri(Key).ToString(),Published.Reference.ContentHash.ToString(),Published.Address.ToString(),ProfileHash.ToString()));
        }
        if(Hash256.Compute(ReadSource(Source))!=Hash||Hash256.Compute(File.ReadAllBytes(Source+".meta")).ToString()!=Snapshot.MetadataSha256){
            throw new IOException("IMAGE_CONFLICT: source or metadata changed while importing; previous manifest retained.");
        }
        var Manifest=new ImageOutputManifest("cge-image-outputs/1",Id.ToString(),Hash.ToString(),OptionsHash.ToString(),Settings.DefaultOutput,Outputs.ToArray());
        var PathValue=Path.Combine(Cache,Id+".json");var Temporary=PathValue+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllBytes(Temporary,JsonSerializer.SerializeToUtf8Bytes(Manifest));File.Move(Temporary,PathValue,true); }
        finally { if(File.Exists(Temporary)){ File.Delete(Temporary); } }
        return PathValue;
    }
    private static byte[]ReadSource(string Source){
        var Info=new FileInfo(Source);if(Info.Length is <1 or >128*1024*1024){ throw new InvalidDataException("IMAGE_SIZE: encoded source exceeds 128 MiB."); }
        var Bytes=File.ReadAllBytes(Source);if(Bytes.Length!=Info.Length){ throw new IOException("IMAGE_CONFLICT: source changed."); }return Bytes;
    }
    private static byte[]Decode(byte[]Bytes,int Width,int Height){
        try {
            var Image=StbImageSharp.ImageResult.FromMemory(Bytes,StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            if(Image.Width!=Width||Image.Height!=Height){ throw new InvalidDataException("IMAGE_SIZE: decoder/header disagree."); }return Image.Data;
        }catch(Exception Error)when(Error is not OutOfMemoryException){ throw new InvalidDataException("IMAGE_DECODE: "+Error.Message,Error); }
    }
    private static(int Width,int Height)DimensionsOf(ReadOnlySpan<byte>Bytes){
        if(Bytes.Length>=24&&Bytes[..8].SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})&&Bytes.Slice(12,4).SequenceEqual("IHDR"u8)){
            return(BinaryPrimitives.ReadInt32BigEndian(Bytes.Slice(16,4)),BinaryPrimitives.ReadInt32BigEndian(Bytes.Slice(20,4)));
        }
        if(Bytes.Length>=4&&Bytes[0]==255&&Bytes[1]==216){
            var I=2;
            while(I+3<Bytes.Length){
                if(Bytes[I++]!=255){ throw new InvalidDataException("IMAGE_HEADER: invalid JPEG marker."); }
                while(I<Bytes.Length&&Bytes[I]==255){ I++; }if(I>=Bytes.Length){ break; }
                var Marker=Bytes[I++];if(Marker is 0xd9 or 0xda){ break; }if(Marker is 0x01 or >=0xd0 and <=0xd7){ continue; }
                if(I+2>Bytes.Length){ break; }var Length=BinaryPrimitives.ReadUInt16BigEndian(Bytes.Slice(I,2));
                if(Length<2||I+Length>Bytes.Length){ break; }
                if(Marker is >=0xc0 and <=0xcf and not(0xc4 or 0xc8 or 0xcc)){
                    if(Length<8){ break; }return(BinaryPrimitives.ReadUInt16BigEndian(Bytes.Slice(I+5,2)),BinaryPrimitives.ReadUInt16BigEndian(Bytes.Slice(I+3,2)));
                }
                I+=Length;
            }
        }
        throw new InvalidDataException("IMAGE_HEADER: expected a bounded PNG or JPEG.");
    }
}
