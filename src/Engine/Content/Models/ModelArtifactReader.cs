using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Content.Models;
public sealed class ModelArtifactReader(IContentArtifactReader Reader,ModelContentIndex Index):IContentArtifactReader {
    private readonly IContentArtifactReader Reader=Reader;
    private readonly Dictionary<Hash256,ModelContentEntry[]>Entries=Index.Entries.GroupBy(static Entry=>Hash256.Parse(Entry.Address)).ToDictionary(static Group=>Group.Key,static Group=>Group.ToArray());
    public async ValueTask<ReadOnlyMemory<byte>>ReadAsync(Hash256 Address,CancellationToken CancellationToken=default){
        if(!this.Entries.TryGetValue(Address,out var Entries)){ throw new InvalidDataException("Model artifact address is absent from the content index."); }
        var Bytes=await this.Reader.ReadAsync(Address,CancellationToken).ConfigureAwait(false);
        if(!ContentArtifactCodec.TryRead(Bytes.Span,out var Artifact,out _)||Artifact is null||!Bytes.Span.SequenceEqual(ContentArtifactCodec.Write(Artifact))){ throw new InvalidDataException("Model artifact is invalid or noncanonical."); }
        var Hash=Artifact.ContentHash.ToString();
        if(Entries.Any(Entry=>Entry.EnvelopeBytes!=Bytes.Length||Entry.Kind!=Artifact.Kind||Entry.Hash!=Hash||Entry.SourceUri!=Artifact.Provenance.SourceUri)){
            throw new InvalidDataException("Model envelope differs from its indexed hash, kind, provenance or size.");
        }
        return Bytes;
    }
}
