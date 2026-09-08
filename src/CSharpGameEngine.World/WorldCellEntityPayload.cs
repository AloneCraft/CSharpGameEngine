using System.Buffers.Binary;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.World;

/// <summary>Canonical link from a baked cell artifact to source-generated C# entity construction.</summary>
public static class WorldCellEntityPayloadCodec {
    private const uint Magic=0x50454743;
    private const ushort Version=1;
    private const int HeaderSize=8;
    private const int PayloadSize=HeaderSize+CanonicalAssetKeyCodec.EncodedLength;

    public static ContentArtifact Bake(
        AssetKey EntityPayload,ArtifactProvenance Provenance,
        IEnumerable<AssetArtifactReference>?Dependencies=null){
        if(!EntityPayload.IsValid){ throw new ArgumentException("A valid generated entity payload key is required.",nameof(EntityPayload)); }
        var Payload=new byte[PayloadSize];
        BinaryPrimitives.WriteUInt32LittleEndian(Payload.AsSpan(0,4),Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(Payload.AsSpan(4,2),Version);
        CanonicalAssetKeyCodec.Write(Payload.AsSpan(HeaderSize),EntityPayload);
        return new ContentArtifact(ContentArtifactKind.Scene,Provenance,Dependencies,Payload);
    }

    public static bool TryRead(ContentArtifact Artifact,out AssetKey EntityPayload,out string Error){
        EntityPayload=default;
        Error=string.Empty;
        if(Artifact is null){ Error="Entity payload artifact is required.";return false; }
        if(Artifact.Kind!=ContentArtifactKind.Scene){ Error="Entity payload artifact kind must be Scene.";return false; }
        var Payload=Artifact.Payload.AsSpan();
        if(Payload.Length!=PayloadSize){ Error="Entity payload has an invalid canonical length.";return false; }
        if(BinaryPrimitives.ReadUInt32LittleEndian(Payload.Slice(0,4))!=Magic){ Error="Entity payload magic is invalid.";return false; }
        if(BinaryPrimitives.ReadUInt16LittleEndian(Payload.Slice(4,2))!=Version){ Error="Entity payload schema is unsupported.";return false; }
        if(Payload[6]!=0||Payload[7]!=0){ Error="Entity payload reserved bytes must be zero.";return false; }
        if(!CanonicalAssetKeyCodec.TryRead(Payload.Slice(HeaderSize),out EntityPayload)){
            Error="Entity payload AssetKey is invalid.";
            return false;
        }
        return true;
    }
}
