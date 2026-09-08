using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Content;

public sealed class ShippingContentManifestEntry {
    public ShippingContentManifestEntry(
        AssetArtifactReference Reference,Hash256 Address,ContentArtifactKind Kind,
        ArtifactProvenance Provenance,IEnumerable<AssetArtifactReference>?Dependencies,
        int EnvelopeByteLength){
        if(!Reference.IsValid){ throw new ArgumentException("A valid artifact reference is required.",nameof(Reference)); }
        if(!Address.IsValid){ throw new ArgumentException("A valid CAS address is required.",nameof(Address)); }
        if(!ContentArtifactCodec.IsKnownKind(Kind)){ throw new ArgumentOutOfRangeException(nameof(Kind)); }
        ContentArtifactCodec.ValidateProvenance(Provenance);
        if(EnvelopeByteLength<=0){ throw new ArgumentOutOfRangeException(nameof(EnvelopeByteLength)); }
        var Builder=ImmutableArray.CreateBuilder<AssetArtifactReference>();
        if(Dependencies is not null){ Builder.AddRange(Dependencies); }
        Builder.Sort(static(Left,Right)=>Left.Key.CompareTo(Right.Key));
        for(var Index=0;Index<Builder.Count;Index++){
            if(!Builder[Index].IsValid){ throw new ArgumentException("Manifest dependencies require valid references.",nameof(Dependencies)); }
            if(Index>0&&Builder[Index-1].Key==Builder[Index].Key){
                throw new ArgumentException("Manifest dependencies must have unique AssetKey values.",nameof(Dependencies));
            }
        }
        this.Reference=Reference;
        this.Address=Address;
        this.Kind=Kind;
        this.Provenance=Provenance;
        this.Dependencies=Builder.ToImmutable();
        this.EnvelopeByteLength=EnvelopeByteLength;
    }

    public AssetArtifactReference Reference{get;}
    public Hash256 Address{get;}
    public ContentArtifactKind Kind{get;}
    public ArtifactProvenance Provenance{get;}
    public ImmutableArray<AssetArtifactReference>Dependencies{get;}
    public int EnvelopeByteLength{get;}
}

/// <summary>An immutable exact artifact closure for one selected shipping product scope.</summary>
public sealed class ShippingContentManifest:IContentArtifactLocator {
    private readonly ImmutableDictionary<AssetArtifactReference,ShippingContentManifestEntry>ByReference;
    private readonly ImmutableDictionary<AssetKey,ShippingContentManifestEntry>ByKey;

    public ShippingContentManifest(
        IEnumerable<AssetArtifactReference>Roots,
        IEnumerable<ShippingContentManifestEntry>Entries){
        ArgumentNullException.ThrowIfNull(Roots);
        ArgumentNullException.ThrowIfNull(Entries);
        var RootBuilder=ImmutableArray.CreateBuilder<AssetArtifactReference>();
        RootBuilder.AddRange(Roots);
        RootBuilder.Sort(static(Left,Right)=>Left.Key.CompareTo(Right.Key));
        if(RootBuilder.Count==0){ throw new ArgumentException("A shipping manifest requires at least one root.",nameof(Roots)); }
        for(var Index=0;Index<RootBuilder.Count;Index++){
            if(!RootBuilder[Index].IsValid){ throw new ArgumentException("Manifest roots require valid references.",nameof(Roots)); }
            if(Index>0&&RootBuilder[Index-1].Key==RootBuilder[Index].Key){
                throw new ArgumentException("Manifest roots must have unique AssetKey values.",nameof(Roots));
            }
        }
        var EntryBuilder=ImmutableArray.CreateBuilder<ShippingContentManifestEntry>();
        foreach(var Entry in Entries){
            if(Entry is null){ throw new ArgumentException("Manifest entries cannot contain null.",nameof(Entries)); }
            EntryBuilder.Add(Entry);
        }
        EntryBuilder.Sort(static(Left,Right)=>Left.Reference.Key.CompareTo(Right.Reference.Key));
        var ReferenceBuilder=ImmutableDictionary.CreateBuilder<AssetArtifactReference,ShippingContentManifestEntry>();
        var KeyBuilder=ImmutableDictionary.CreateBuilder<AssetKey,ShippingContentManifestEntry>();
        foreach(var Entry in EntryBuilder){
            if(!ReferenceBuilder.TryAdd(Entry.Reference,Entry)){
                throw new ArgumentException($"Manifest contains duplicate artifact reference {Entry.Reference.Key}.",nameof(Entries));
            }
            if(!KeyBuilder.TryAdd(Entry.Reference.Key,Entry)){
                throw new ArgumentException($"Manifest contains more than one content version for AssetKey {Entry.Reference.Key}.",nameof(Entries));
            }
        }
        this.Roots=RootBuilder.ToImmutable();
        this.Entries=EntryBuilder.ToImmutable();
        this.ByReference=ReferenceBuilder.ToImmutable();
        this.ByKey=KeyBuilder.ToImmutable();
        this.ValidateExactClosure();
        this.Fingerprint=Hash256.Compute(ShippingContentManifestCodec.Write(this));
    }

    public ImmutableArray<AssetArtifactReference>Roots{get;}
    public ImmutableArray<ShippingContentManifestEntry>Entries{get;}
    public Hash256 Fingerprint{get;}
    public bool TryResolve(AssetArtifactReference Reference,out Hash256 Address){
        if(this.ByReference.TryGetValue(Reference,out var Entry)){ Address=Entry.Address;return true; }
        Address=default;
        return false;
    }
    public bool TryResolve(AssetKey Key,out ShippingContentManifestEntry?Entry)=>this.ByKey.TryGetValue(Key,out Entry);
    public bool TryGetEntry(AssetArtifactReference Reference,out ShippingContentManifestEntry?Entry)=>this.ByReference.TryGetValue(Reference,out Entry);

    public ImmutableArray<ShippingContentManifestEntry>GetDependencyClosure(AssetArtifactReference Root){
        if(!this.ByReference.ContainsKey(Root)){ throw new ArgumentException("The requested root is absent from the manifest.",nameof(Root)); }
        var Result=ImmutableArray.CreateBuilder<ShippingContentManifestEntry>();
        var Visited=new HashSet<AssetArtifactReference>();
        var Pending=new Stack<(AssetArtifactReference Reference,bool Exit)>();
        Pending.Push((Root,false));
        while(Pending.Count!=0){
            var Current=Pending.Pop();
            if(Current.Exit){ Result.Add(this.ByReference[Current.Reference]);continue; }
            if(!Visited.Add(Current.Reference)){ continue; }
            ShippingContentManifestEntry Entry=this.ByReference[Current.Reference];
            Pending.Push((Current.Reference,true));
            for(var Index=Entry.Dependencies.Length-1;Index>=0;Index--){
                if(!Visited.Contains(Entry.Dependencies[Index])){ Pending.Push((Entry.Dependencies[Index],false)); }
            }
        }
        return Result.ToImmutable();
    }

    private void ValidateExactClosure(){
        var State=new Dictionary<AssetArtifactReference,byte>();
        var Reachable=new HashSet<AssetArtifactReference>();
        var Pending=new Stack<(AssetArtifactReference Reference,bool Exit)>();
        for(var RootIndex=this.Roots.Length-1;RootIndex>=0;RootIndex--){ Pending.Push((this.Roots[RootIndex],false)); }
        while(Pending.Count!=0){
            var Current=Pending.Pop();
            if(Current.Exit){ State[Current.Reference]=2;continue; }
            if(!this.ByReference.TryGetValue(Current.Reference,out var Entry)){
                throw new ArgumentException($"Manifest dependency is missing: {Current.Reference.Key}.",nameof(this.Entries));
            }
            if(State.TryGetValue(Current.Reference,out byte Existing)){
                if(Existing==1){ throw new ArgumentException($"Manifest dependency cycle contains {Current.Reference.Key}.",nameof(this.Entries)); }
                continue;
            }
            State.Add(Current.Reference,1);
            Reachable.Add(Current.Reference);
            Pending.Push((Current.Reference,true));
            for(var Index=Entry.Dependencies.Length-1;Index>=0;Index--){ Pending.Push((Entry.Dependencies[Index],false)); }
        }
        if(Reachable.Count!=this.Entries.Length){
            var Extra=this.Entries.First(Entry=>!Reachable.Contains(Entry.Reference));
            throw new ArgumentException($"Manifest contains unreachable artifact {Extra.Reference.Key}.",nameof(this.Entries));
        }
    }
}

public enum ShippingContentManifestErrorCode:byte {
    None,InvalidJson,UnsupportedSchema,InvalidIdentity,InvalidEntry,NonCanonical,
}

public readonly record struct ShippingContentManifestError(
    ShippingContentManifestErrorCode Code,string Field,string Message) {
    public static ShippingContentManifestError None=>new(ShippingContentManifestErrorCode.None,string.Empty,string.Empty);
}

public static class ShippingContentManifestCodec {
    public const int CurrentSchemaVersion=1;
    private static readonly JsonSerializerOptions Options=new(){ PropertyNameCaseInsensitive=false };

    public static byte[]Write(ShippingContentManifest Manifest){
        ArgumentNullException.ThrowIfNull(Manifest);
        var Document=new ManifestDocument {
            SchemaVersion=CurrentSchemaVersion,
            Roots=Manifest.Roots.Select(ToReferenceDocument).ToArray(),
            Artifacts=Manifest.Entries.Select(static Entry=>new EntryDocument {
                Asset=Entry.Reference.Key.ToString(),
                ContentHash=Entry.Reference.ContentHash.ToString(),
                Address=Entry.Address.ToString(),
                Kind=(ushort)Entry.Kind,
                EnvelopeByteLength=Entry.EnvelopeByteLength,
                Provenance=new ProvenanceDocument {
                    ToolVersion=Entry.Provenance.ToolVersion,
                    SourceUri=Entry.Provenance.SourceUri,
                    SourceHash=Entry.Provenance.SourceHash.ToString(),
                    ImporterIdentity=Entry.Provenance.ImporterIdentity,
                    OptionsHash=Entry.Provenance.OptionsHash.ToString(),
                },
                Dependencies=Entry.Dependencies.Select(ToReferenceDocument).ToArray(),
            }).ToArray(),
        };
        return JsonSerializer.SerializeToUtf8Bytes(Document,Options);
    }

    public static bool TryRead(
        ReadOnlySpan<byte>Utf8,out ShippingContentManifest?Manifest,
        out ShippingContentManifestError Error){
        Manifest=null;
        Error=ShippingContentManifestError.None;
        try {
            ManifestDocument?Document=JsonSerializer.Deserialize<ManifestDocument>(Utf8,Options);
            if(Document is null){ return Fail(ShippingContentManifestErrorCode.InvalidJson,string.Empty,"Manifest is empty.",out Error); }
            if(Document.SchemaVersion!=CurrentSchemaVersion){
                return Fail(ShippingContentManifestErrorCode.UnsupportedSchema,"schemaVersion",$"Unsupported shipping manifest schema {Document.SchemaVersion}.",out Error);
            }
            if(Document.Roots is null||Document.Artifacts is null){
                return Fail(ShippingContentManifestErrorCode.InvalidEntry,string.Empty,"Manifest requires roots and artifacts.",out Error);
            }
            var Roots=Document.Roots.Select(ParseReference).ToArray();
            var Entries=new ShippingContentManifestEntry[Document.Artifacts.Length];
            for(var Index=0;Index<Entries.Length;Index++){
                EntryDocument Entry=Document.Artifacts[Index]??throw new InvalidDataException("Manifest artifact cannot be null.");
                ProvenanceDocument Provenance=Entry.Provenance??throw new InvalidDataException("Manifest provenance is required.");
                var Reference=new AssetArtifactReference(ParseKey(Entry.Asset),Hash256.Parse(Entry.ContentHash??string.Empty));
                var ArtifactProvenance=new ArtifactProvenance(
                    Provenance.ToolVersion??string.Empty,
                    Provenance.SourceUri??string.Empty,
                    Hash256.Parse(Provenance.SourceHash??string.Empty),
                    Provenance.ImporterIdentity??string.Empty,
                    Hash256.Parse(Provenance.OptionsHash??string.Empty));
                var Dependencies=(Entry.Dependencies??throw new InvalidDataException("Manifest dependencies are required."))
                    .Select(ParseReference).ToArray();
                Entries[Index]=new ShippingContentManifestEntry(
                    Reference,Hash256.Parse(Entry.Address??string.Empty),(ContentArtifactKind)Entry.Kind,
                    ArtifactProvenance,Dependencies,Entry.EnvelopeByteLength);
            }
            Manifest=new ShippingContentManifest(Roots,Entries);
            byte[]Canonical=Write(Manifest);
            if(!Utf8.SequenceEqual(Canonical)){
                Manifest=null;
                return Fail(ShippingContentManifestErrorCode.NonCanonical,string.Empty,"Manifest bytes are not in canonical form.",out Error);
            }
            return true;
        }catch(JsonException Exception){
            return Fail(ShippingContentManifestErrorCode.InvalidJson,string.Empty,Exception.Message,out Error);
        }catch(Exception Exception) when(Exception is ArgumentException or FormatException or InvalidDataException or OverflowException){
            return Fail(ShippingContentManifestErrorCode.InvalidEntry,string.Empty,Exception.Message,out Error);
        }
    }

    private static ReferenceDocument ToReferenceDocument(AssetArtifactReference Reference)=>new(){
        Asset=Reference.Key.ToString(),ContentHash=Reference.ContentHash.ToString(),
    };
    private static AssetArtifactReference ParseReference(ReferenceDocument?Document){
        if(Document is null){ throw new InvalidDataException("Manifest reference cannot be null."); }
        return new AssetArtifactReference(ParseKey(Document.Asset),Hash256.Parse(Document.ContentHash??string.Empty));
    }
    private static AssetKey ParseKey(string?Value)=>AssetUri.Parse(AssetUri.Scheme+(Value??string.Empty)).Key;
    private static bool Fail(
        ShippingContentManifestErrorCode Code,string Field,string Message,
        out ShippingContentManifestError Error){
        Error=new(Code,Field,Message);
        return false;
    }

    private sealed class ManifestDocument {
        [JsonPropertyName("schemaVersion")]public int SchemaVersion{get;set;}
        [JsonPropertyName("roots")]public ReferenceDocument[]?Roots{get;set;}
        [JsonPropertyName("artifacts")]public EntryDocument[]?Artifacts{get;set;}
    }
    private sealed class ReferenceDocument {
        [JsonPropertyName("asset")]public string?Asset{get;set;}
        [JsonPropertyName("contentHash")]public string?ContentHash{get;set;}
    }
    private sealed class EntryDocument {
        [JsonPropertyName("asset")]public string?Asset{get;set;}
        [JsonPropertyName("contentHash")]public string?ContentHash{get;set;}
        [JsonPropertyName("address")]public string?Address{get;set;}
        [JsonPropertyName("kind")]public ushort Kind{get;set;}
        [JsonPropertyName("envelopeByteLength")]public int EnvelopeByteLength{get;set;}
        [JsonPropertyName("provenance")]public ProvenanceDocument?Provenance{get;set;}
        [JsonPropertyName("dependencies")]public ReferenceDocument[]?Dependencies{get;set;}
    }
    private sealed class ProvenanceDocument {
        [JsonPropertyName("toolVersion")]public string?ToolVersion{get;set;}
        [JsonPropertyName("sourceUri")]public string?SourceUri{get;set;}
        [JsonPropertyName("sourceHash")]public string?SourceHash{get;set;}
        [JsonPropertyName("importerIdentity")]public string?ImporterIdentity{get;set;}
        [JsonPropertyName("optionsHash")]public string?OptionsHash{get;set;}
    }
}

public static class ShippingContentManifestBuilder {
    public static async ValueTask<ShippingContentManifest>BuildAsync(
        IContentArtifactReader Reader,IContentArtifactLocator Locator,
        IEnumerable<AssetArtifactReference>Roots,CancellationToken CancellationToken=default){
        ArgumentNullException.ThrowIfNull(Reader);
        ArgumentNullException.ThrowIfNull(Locator);
        ArgumentNullException.ThrowIfNull(Roots);
        AssetArtifactReference[]RootArray=Roots.ToArray();
        var Entries=new Dictionary<AssetArtifactReference,ShippingContentManifestEntry>();
        var KeyVersions=new Dictionary<AssetKey,AssetArtifactReference>();
        var Pending=new Stack<AssetArtifactReference>(RootArray.Reverse());
        while(Pending.Count!=0){
            CancellationToken.ThrowIfCancellationRequested();
            AssetArtifactReference Reference=Pending.Pop();
            if(Entries.ContainsKey(Reference)){ continue; }
            if(KeyVersions.TryGetValue(Reference.Key,out var Existing)&&Existing!=Reference){
                throw new InvalidDataException($"Reachable closure contains more than one version of AssetKey {Reference.Key}.");
            }
            if(!Locator.TryResolve(Reference,out var Address)){
                throw new InvalidDataException($"Reachable artifact is absent from the published index: {Reference.Key}.");
            }
            ReadOnlyMemory<byte>Bytes=await Reader.ReadAsync(Address,CancellationToken).ConfigureAwait(false);
            if(!ContentArtifactCodec.TryRead(Bytes.Span,out var Artifact,out var Error)||Artifact is null){
                throw new InvalidDataException($"Reachable artifact is invalid: {Reference.Key}: {Error.Field}: {Error.Message}");
            }
            if(Artifact.ContentHash!=Reference.ContentHash){
                throw new InvalidDataException($"Reachable artifact content hash does not match its reference: {Reference.Key}.");
            }
            if(ContentArtifactCodec.ComputeAddress(Bytes.ToArray())!=Address){
                throw new InvalidDataException($"Reachable artifact address does not match its bytes: {Reference.Key}.");
            }
            var Entry=new ShippingContentManifestEntry(
                Reference,Address,Artifact.Kind,Artifact.Provenance,Artifact.Dependencies,Bytes.Length);
            Entries.Add(Reference,Entry);
            KeyVersions.Add(Reference.Key,Reference);
            for(var Index=Artifact.Dependencies.Length-1;Index>=0;Index--){ Pending.Push(Artifact.Dependencies[Index]); }
        }
        try { return new ShippingContentManifest(RootArray,Entries.Values); }
        catch(ArgumentException Exception){ throw new InvalidDataException("Published artifact graph is not a valid exact shipping closure.",Exception); }
    }
}

internal static class ManifestArtifactValidator {
    internal static bool TryValidate(
        ReadOnlyMemory<byte>Bytes,ShippingContentManifestEntry Entry,
        out ContentArtifact?Artifact,out string Error){
        Artifact=null;
        Error=string.Empty;
        if(Bytes.Length!=Entry.EnvelopeByteLength){ Error="Artifact byte length does not match the manifest.";return false; }
        if(ContentArtifactCodec.ComputeAddress(Bytes.ToArray())!=Entry.Address){ Error="Artifact address does not match the manifest.";return false; }
        if(!ContentArtifactCodec.TryRead(Bytes.Span,out Artifact,out var ArtifactError)||Artifact is null){
            Error=$"{ArtifactError.Field}: {ArtifactError.Message}";
            return false;
        }
        if(Artifact.ContentHash!=Entry.Reference.ContentHash){ Error="Artifact content hash does not match the manifest.";return false; }
        if(Artifact.Kind!=Entry.Kind){ Error="Artifact kind does not match the manifest.";return false; }
        if(Artifact.Provenance!=Entry.Provenance){ Error="Artifact provenance does not match the manifest.";return false; }
        if(!Artifact.Dependencies.SequenceEqual(Entry.Dependencies)){ Error="Artifact dependencies do not match the manifest.";return false; }
        return true;
    }
}
