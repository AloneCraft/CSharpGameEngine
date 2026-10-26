using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Content.Models;
public sealed record ModelContentEntry(string SourceUri,string Asset,string Name,ContentArtifactKind Kind,string Uri,string Hash,string Address,int EnvelopeBytes,bool LastKnownGood);
/// <summary>ビルドツールとランタイムローダーで共有する、ソースセレクターから厳密なアーティファクトへのマッピング。</summary>
public sealed class ModelContentIndex:IContentArtifactLocator {
    private const string Schema="cge-content-index/1";
    private const int MaximumBytes=16*1024*1024;
    private static readonly JsonSerializerOptions Options=new() { UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow };
    private readonly ImmutableDictionary<AssetArtifactReference,Hash256>ByReference;
    public ImmutableArray<ModelContentEntry>Entries{get;}
    public ModelContentIndex(IEnumerable<ModelContentEntry>Entries){
        var Ordered=Entries.OrderBy(E=>E.SourceUri,StringComparer.Ordinal).ThenBy(E=>E.Uri,StringComparer.Ordinal).ToImmutableArray();
        if(Ordered.Length>65535){ throw new InvalidDataException("Content index entry limit exceeded."); }
        var Builder=ImmutableDictionary.CreateBuilder<AssetArtifactReference,Hash256>();
        var Keys=new HashSet<AssetKey>();
        var Ids=new Dictionary<string,AssetId>(StringComparer.Ordinal);
        var Paths=new Dictionary<AssetId,string>();
        foreach(var Entry in Ordered){
            if(!AssetId.TryParse(Entry.Asset,out var Asset)||!AssetUri.TryParse(Entry.Uri,out var Uri,out _)||Uri.Key.AssetId!=Asset||!Hash256.TryParse(Entry.Hash,out var Hash)||!Hash.IsValid||!Hash256.TryParse(Entry.Address,out var Address)||!Address.IsValid||!ContentArtifactCodec.IsKnownKind(Entry.Kind)||Entry.EnvelopeBytes<=0||Entry.EnvelopeBytes>512*1024*1024){ throw new InvalidDataException("Invalid content index identity, kind or size."); }
            if(string.IsNullOrWhiteSpace(Entry.SourceUri)||Entry.SourceUri.Contains('\\')||Entry.SourceUri.StartsWith('/')||Entry.SourceUri.Split('/').Any(P=>P is ".." or "." or "")||Entry.SourceUri.Contains(':')||Entry.SourceUri.Any(char.IsControl)||!Entry.SourceUri.IsNormalized(System.Text.NormalizationForm.FormC)){ throw new InvalidDataException("Invalid content index source URI."); }
            if(!Keys.Add(Uri.Key)){ throw new InvalidDataException("Duplicate content index key."); }
            if(Ids.TryGetValue(Entry.SourceUri,out var Existing)&&Existing!=Asset||Paths.TryGetValue(Asset,out var Path)&&Path!=Entry.SourceUri){ throw new InvalidDataException("Conflicting source and asset identity mapping."); }
            Ids[Entry.SourceUri]=Asset;Paths[Asset]=Entry.SourceUri;
            Builder.Add(new(Uri.Key,Hash),Address);
        }
        this.Entries=Ordered;
        this.ByReference=Builder.ToImmutable();
    }
    public bool TryResolve(AssetArtifactReference Reference,out Hash256 Address)=>this.ByReference.TryGetValue(Reference,out Address);
    public AssetArtifactReference Resolve(string SourceUri,ContentArtifactKind Kind,string?Name=null){
        var Matches=this.Entries.Where(E=>E.SourceUri==SourceUri&&E.Kind==Kind&&(Name is null||E.Name==Name)).Take(2).ToArray();
        if(Matches.Length!=1){ throw new InvalidDataException(Matches.Length==0?"Content selector was not found.":"Content selector is ambiguous; specify its name."); }
        return new(AssetUri.Parse(Matches[0].Uri).Key,Hash256.Parse(Matches[0].Hash));
    }
    public byte[]Write(){
        var Bytes=JsonSerializer.SerializeToUtf8Bytes(new IndexDocument(Schema,this.Entries),Options);
        if(Bytes.Length>MaximumBytes){ throw new InvalidDataException("Content index exceeds its byte limit."); }
        return Bytes;
    }
    public static ModelContentIndex Read(byte[]Bytes){
        if(Bytes.Length>MaximumBytes){ throw new InvalidDataException("Content index exceeds its byte limit."); }
        try {
            var Document=JsonSerializer.Deserialize<IndexDocument>(Bytes,Options)??throw new InvalidDataException("Missing content index.");
            if(Document.Schema!=Schema||Document.Entries.IsDefault){ throw new InvalidDataException("Unsupported content index schema."); }
            var Result=new ModelContentIndex(Document.Entries);
            if(!Bytes.AsSpan().SequenceEqual(Result.Write())){ throw new InvalidDataException("Noncanonical content index."); }
            return Result;
        } catch(Exception Error) when(Error is JsonException or ArgumentException or NullReferenceException){ throw new InvalidDataException("Invalid content index.",Error); }
    }
    private sealed record IndexDocument(string Schema,ImmutableArray<ModelContentEntry>Entries);
}
