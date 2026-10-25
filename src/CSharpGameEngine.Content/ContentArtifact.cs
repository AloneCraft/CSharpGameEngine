using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Content;

public enum ContentArtifactKind:ushort {
    Mesh=1,
    Texture=2,
    Material=3,
    Scene=4,
    WorldCell=5,
    Audio=6,
    Shader=7,
    Model=8,
    Skeleton=9,
    AnimationClip=10,
    DistanceField=11,
    License=12,
}

public readonly record struct ArtifactProvenance(
    string ToolVersion,
    string SourceUri,
    Hash256 SourceHash,
    string ImporterIdentity,
    Hash256 OptionsHash);

/// <summary>An immutable engine-owned content object before or after canonical envelope encoding.</summary>
public sealed class ContentArtifact {
    public ContentArtifact(
        ContentArtifactKind Kind,
        ArtifactProvenance Provenance,
        IEnumerable<AssetArtifactReference>?Dependencies,
        byte[]Payload){
        if(!ContentArtifactCodec.IsKnownKind(Kind)){ throw new ArgumentOutOfRangeException(nameof(Kind)); }
        ContentArtifactCodec.ValidateProvenance(Provenance);
        if(Payload is null){ throw new ArgumentNullException(nameof(Payload)); }
        var DependencyBuilder=ImmutableArray.CreateBuilder<AssetArtifactReference>();
        if(Dependencies is not null){ DependencyBuilder.AddRange(Dependencies); }
        DependencyBuilder.Sort(static(Left,Right)=>Left.Key.CompareTo(Right.Key));
        for(var Index=0;Index<DependencyBuilder.Count;Index++){
            AssetArtifactReference Dependency=DependencyBuilder[Index];
            if(!Dependency.Key.IsValid||!Dependency.ContentHash.IsValid){
                throw new ArgumentException("Artifact dependencies require valid keys and content hashes.",nameof(Dependencies));
            }
            if(Index>0&&DependencyBuilder[Index-1].Key==Dependency.Key){
                throw new ArgumentException($"Duplicate artifact dependency '{Dependency.Key}'.",nameof(Dependencies));
            }
        }
        this.Kind=Kind;
        this.Provenance=Provenance;
        this.Dependencies=DependencyBuilder.ToImmutable();
        this.Payload=ImmutableArray.Create(Payload);
        this.ContentHash=Hash256.Compute(Payload);
    }

    public ContentArtifactKind Kind{get;}
    public ArtifactProvenance Provenance{get;}
    public ImmutableArray<AssetArtifactReference>Dependencies{get;}
    public ImmutableArray<byte>Payload{get;}
    public Hash256 ContentHash{get;}
}

public enum ContentArtifactErrorCode {
    None,
    TooShort,
    InvalidMagic,
    UnsupportedSchema,
    UnknownKind,
    InvalidLength,
    InvalidChecksum,
    InvalidContentHash,
    InvalidProvenance,
    InvalidDependency,
    NonCanonicalDependencyOrder,
}

public readonly record struct ContentArtifactError(
    ContentArtifactErrorCode Code,string Field,string Message) {
    public static ContentArtifactError None=>new(ContentArtifactErrorCode.None,string.Empty,string.Empty);
}

/// <summary>Canonical little-endian L6 artifact envelope. It never exposes importer/provider types.</summary>
public static class ContentArtifactCodec {
    private const uint Magic=0x41454743;
    private const ushort SchemaVersion=1;
    private const int HeaderSize=88;
    private const int ContentHashOffset=24;
    private const int ChecksumOffset=56;
    private const int DependencySize=CanonicalAssetKeyCodec.EncodedLength+32;
    private const int MaximumProvenanceBytes=1024*1024;
    private const int MaximumDependencyCount=65535;
    private const int MaximumStringBytes=256*1024;

    public static byte[]Write(ContentArtifact Artifact){
        if(Artifact is null){ throw new ArgumentNullException(nameof(Artifact)); }
        byte[]Provenance=WriteProvenance(Artifact.Provenance);
        int DependencyBytes=checked(Artifact.Dependencies.Length*DependencySize);
        int TotalLength=checked(HeaderSize+Provenance.Length+DependencyBytes+Artifact.Payload.Length);
        var Result=new byte[TotalLength];
        BinaryPrimitives.WriteUInt32LittleEndian(Result.AsSpan(0,4),Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(Result.AsSpan(4,2),SchemaVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(Result.AsSpan(6,2),(ushort)Artifact.Kind);
        BinaryPrimitives.WriteUInt32LittleEndian(Result.AsSpan(8,4),(uint)Provenance.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(Result.AsSpan(12,4),(uint)Artifact.Dependencies.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(Result.AsSpan(16,8),(ulong)Artifact.Payload.Length);
        Artifact.ContentHash.ToByteArray().CopyTo(Result,ContentHashOffset);
        Provenance.CopyTo(Result,HeaderSize);
        int Offset=HeaderSize+Provenance.Length;
        foreach(AssetArtifactReference Dependency in Artifact.Dependencies){
            WriteDependency(Result.AsSpan(Offset,DependencySize),Dependency);
            Offset+=DependencySize;
        }
        Artifact.Payload.AsSpan().CopyTo(Result.AsSpan(Offset));
        Hash256 Checksum=Hash256.Compute(Result);
        Checksum.ToByteArray().CopyTo(Result,ChecksumOffset);
        return Result;
    }

    public static bool TryRead(
        ReadOnlySpan<byte>Bytes,
        out ContentArtifact?Artifact,
        out ContentArtifactError Error){
        Artifact=null;
        Error=ContentArtifactError.None;
        if(Bytes.Length<HeaderSize){ Error=CreateError(ContentArtifactErrorCode.TooShort,"header","Artifact is shorter than the fixed header.");return false; }
        if(BinaryPrimitives.ReadUInt32LittleEndian(Bytes.Slice(0,4))!=Magic){ Error=CreateError(ContentArtifactErrorCode.InvalidMagic,"magic","Artifact magic is invalid.");return false; }
        ushort Version=BinaryPrimitives.ReadUInt16LittleEndian(Bytes.Slice(4,2));
        if(Version!=SchemaVersion){ Error=CreateError(ContentArtifactErrorCode.UnsupportedSchema,"schemaVersion",$"Unsupported artifact schema {Version}.");return false; }
        var Kind=(ContentArtifactKind)BinaryPrimitives.ReadUInt16LittleEndian(Bytes.Slice(6,2));
        if(!IsKnownKind(Kind)){ Error=CreateError(ContentArtifactErrorCode.UnknownKind,"kind","Artifact kind is unknown.");return false; }
        uint ProvenanceLength=BinaryPrimitives.ReadUInt32LittleEndian(Bytes.Slice(8,4));
        uint DependencyCount=BinaryPrimitives.ReadUInt32LittleEndian(Bytes.Slice(12,4));
        ulong PayloadLength=BinaryPrimitives.ReadUInt64LittleEndian(Bytes.Slice(16,8));
        if(ProvenanceLength>MaximumProvenanceBytes||DependencyCount>MaximumDependencyCount||PayloadLength>int.MaxValue){
            Error=CreateError(ContentArtifactErrorCode.InvalidLength,"length","Artifact length fields exceed supported bounds.");
            return false;
        }
        long ExpectedLength=HeaderSize+(long)ProvenanceLength+(long)DependencyCount*DependencySize+(long)PayloadLength;
        if(ExpectedLength!=Bytes.Length){
            Error=CreateError(ContentArtifactErrorCode.InvalidLength,"length","Artifact byte length does not match its header.");
            return false;
        }
        Hash256 DeclaredChecksum=Hash256.FromBytes(Bytes.Slice(ChecksumOffset,32).ToArray());
        byte[]ChecksumInput=Bytes.ToArray();
        Array.Clear(ChecksumInput,ChecksumOffset,32);
        if(Hash256.Compute(ChecksumInput)!=DeclaredChecksum){
            Error=CreateError(ContentArtifactErrorCode.InvalidChecksum,"checksum","Artifact checksum does not match its bytes.");
            return false;
        }
        int PayloadOffset=checked(HeaderSize+(int)ProvenanceLength+(int)DependencyCount*DependencySize);
        Hash256 DeclaredContentHash=Hash256.FromBytes(Bytes.Slice(ContentHashOffset,32).ToArray());
        byte[]Payload=Bytes.Slice(PayloadOffset,(int)PayloadLength).ToArray();
        if(Hash256.Compute(Payload)!=DeclaredContentHash){
            Error=CreateError(ContentArtifactErrorCode.InvalidContentHash,"contentHash","Artifact payload hash does not match the header.");
            return false;
        }
        if(!TryReadProvenance(Bytes.Slice(HeaderSize,(int)ProvenanceLength),out var Provenance,out Error)){ return false; }
        var Dependencies=new AssetArtifactReference[DependencyCount];
        int Offset=HeaderSize+(int)ProvenanceLength;
        AssetKey?Previous=null;
        for(var Index=0;Index<Dependencies.Length;Index++){
            if(!TryReadDependency(Bytes.Slice(Offset,DependencySize),out var Dependency,out Error)){ return false; }
            if(Previous is { }PreviousKey&&PreviousKey.CompareTo(Dependency.Key)>=0){
                Error=CreateError(ContentArtifactErrorCode.NonCanonicalDependencyOrder,"dependencies","Artifact dependencies are not in unique canonical order.");
                return false;
            }
            Dependencies[Index]=Dependency;
            Previous=Dependency.Key;
            Offset+=DependencySize;
        }
        try {
            Artifact=new ContentArtifact(Kind,Provenance,Dependencies,Payload);
            return true;
        }catch(ArgumentException Exception){
            Error=CreateError(ContentArtifactErrorCode.InvalidProvenance,"artifact",Exception.Message);
            return false;
        }
    }

    public static Hash256 ComputeAddress(byte[]CanonicalEnvelope){
        if(CanonicalEnvelope is null){ throw new ArgumentNullException(nameof(CanonicalEnvelope)); }
        return Hash256.Compute(CanonicalEnvelope);
    }

    internal static bool IsKnownKind(ContentArtifactKind Kind)=>Kind is
        ContentArtifactKind.Mesh or ContentArtifactKind.Texture or ContentArtifactKind.Material
        or ContentArtifactKind.Scene or ContentArtifactKind.WorldCell or ContentArtifactKind.Audio
        or ContentArtifactKind.Shader or ContentArtifactKind.Model or ContentArtifactKind.Skeleton
        or ContentArtifactKind.AnimationClip or ContentArtifactKind.DistanceField or ContentArtifactKind.License;

    internal static void ValidateProvenance(ArtifactProvenance Provenance){
        ValidateText(Provenance.ToolVersion,nameof(Provenance.ToolVersion));
        ValidateText(Provenance.SourceUri,nameof(Provenance.SourceUri));
        ValidateText(Provenance.ImporterIdentity,nameof(Provenance.ImporterIdentity));
        if(!Provenance.SourceHash.IsValid){ throw new ArgumentException("A valid source hash is required.",nameof(Provenance)); }
        if(!Provenance.OptionsHash.IsValid){ throw new ArgumentException("A valid options hash is required.",nameof(Provenance)); }
    }

    private static byte[]WriteProvenance(ArtifactProvenance Provenance){
        ValidateProvenance(Provenance);
        using var Stream=new MemoryStream();
        WriteString(Stream,Provenance.ToolVersion);
        WriteString(Stream,Provenance.SourceUri);
        WriteHash(Stream,Provenance.SourceHash);
        WriteString(Stream,Provenance.ImporterIdentity);
        WriteHash(Stream,Provenance.OptionsHash);
        if(Stream.Length>MaximumProvenanceBytes){ throw new ArgumentException("Artifact provenance exceeds the supported size.",nameof(Provenance)); }
        return Stream.ToArray();
    }

    private static bool TryReadProvenance(
        ReadOnlySpan<byte>Bytes,
        out ArtifactProvenance Provenance,
        out ContentArtifactError Error){
        Provenance=default;
        Error=ContentArtifactError.None;
        int Offset=0;
        if(!TryReadString(Bytes,ref Offset,out string ToolVersion)
            ||!TryReadString(Bytes,ref Offset,out string SourceUri)
            ||Bytes.Length-Offset<32){
            Error=CreateError(ContentArtifactErrorCode.InvalidProvenance,"provenance","Artifact provenance is truncated.");
            return false;
        }
        Hash256 SourceHash=Hash256.FromBytes(Bytes.Slice(Offset,32).ToArray());
        Offset+=32;
        if(!TryReadString(Bytes,ref Offset,out string Importer)||Bytes.Length-Offset!=32){
            Error=CreateError(ContentArtifactErrorCode.InvalidProvenance,"provenance","Artifact provenance has an invalid field boundary.");
            return false;
        }
        Hash256 OptionsHash=Hash256.FromBytes(Bytes.Slice(Offset,32).ToArray());
        Provenance=new ArtifactProvenance(ToolVersion,SourceUri,SourceHash,Importer,OptionsHash);
        try {
            ValidateProvenance(Provenance);
            return true;
        }catch(ArgumentException Exception){
            Error=CreateError(ContentArtifactErrorCode.InvalidProvenance,"provenance",Exception.Message);
            return false;
        }
    }

    private static void WriteDependency(Span<byte>Bytes,AssetArtifactReference Dependency){
        CanonicalAssetKeyCodec.Write(Bytes,Dependency.Key);
        Dependency.ContentHash.ToByteArray().CopyTo(Bytes.Slice(CanonicalAssetKeyCodec.EncodedLength,32));
    }

    private static bool TryReadDependency(
        ReadOnlySpan<byte>Bytes,
        out AssetArtifactReference Dependency,
        out ContentArtifactError Error){
        Dependency=default;
        Error=ContentArtifactError.None;
        if(!CanonicalAssetKeyCodec.TryRead(Bytes,out var Key)){
            Error=CreateError(ContentArtifactErrorCode.InvalidDependency,"dependencies.assetKey","Artifact dependency AssetKey is invalid.");
            return false;
        }
        Hash256 ContentHash=Hash256.FromBytes(Bytes.Slice(CanonicalAssetKeyCodec.EncodedLength,32).ToArray());
        if(!ContentHash.IsValid){
            Error=CreateError(ContentArtifactErrorCode.InvalidDependency,"dependencies.contentHash","Artifact dependency content hash is invalid.");
            return false;
        }
        Dependency=new AssetArtifactReference(Key,ContentHash);
        return true;
    }

    private static void WriteString(Stream Stream,string Value){
        byte[]Bytes=Encoding.UTF8.GetBytes(Value);
        if(Bytes.Length>MaximumStringBytes){ throw new ArgumentException("Artifact provenance string exceeds the supported size.",nameof(Value)); }
        Span<byte>Length=stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(Length,(uint)Bytes.Length);
        Stream.Write(Length);
        Stream.Write(Bytes);
    }

    private static void WriteHash(Stream Stream,Hash256 Hash){
        byte[]Bytes=Hash.ToByteArray();
        Stream.Write(Bytes,0,Bytes.Length);
    }

    private static bool TryReadString(ReadOnlySpan<byte>Bytes,ref int Offset,out string Value){
        Value=string.Empty;
        if(Bytes.Length-Offset<4){ return false; }
        uint Length=BinaryPrimitives.ReadUInt32LittleEndian(Bytes.Slice(Offset,4));
        Offset+=4;
        if(Length>MaximumStringBytes||Length>Bytes.Length-Offset){ return false; }
        try {
            Value=new UTF8Encoding(false,true).GetString(Bytes.Slice(Offset,(int)Length));
        }catch(DecoderFallbackException){
            return false;
        }
        Offset+=(int)Length;
        return true;
    }

    private static void ValidateText(string Value,string Name){
        if(string.IsNullOrWhiteSpace(Value)){ throw new ArgumentException("A non-empty provenance value is required.",Name); }
        if(Value.Contains('\r')||Value.Contains('\n')){ throw new ArgumentException("Provenance values cannot contain line breaks.",Name); }
    }
    private static ContentArtifactError CreateError(ContentArtifactErrorCode Code,string Field,string Message)=>new(Code,Field,Message);
}
