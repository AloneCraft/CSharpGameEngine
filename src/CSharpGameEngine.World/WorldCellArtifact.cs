using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Numerics;

namespace CSharpGameEngine.World;

public enum WorldCellArtifactErrorCode {
    None,
    InvalidEnvelope,
    InvalidKind,
    InvalidPayload,
    InvalidIdentity,
    InvalidBounds,
    InvalidEntityPayload,
    InvalidDependency,
}

public readonly record struct WorldCellArtifactError(
    WorldCellArtifactErrorCode Code,string Field,string Message) {
    public static WorldCellArtifactError None=>new(WorldCellArtifactErrorCode.None,string.Empty,string.Empty);
}

/// <summary>WorldCellコンテンツアーティファクト内の正規ペイロードをベイクして読み込む。</summary>
public static class WorldCellArtifactCodec {
    private const uint Magic=0x43574743;
    private const ushort SchemaVersion=2;
    private const int HeaderSize=84;

    public static ContentArtifact Bake(
        WorldCellDefinition Definition,
        ArtifactProvenance Provenance){
        if(Definition is null){ throw new ArgumentNullException(nameof(Definition)); }
        ValidateDefinition(Definition);
        WorldCellId[]Dependencies=Definition.Dependencies
            .OrderBy(static Dependency=>Dependency.ToString(),StringComparer.Ordinal)
            .ToArray();
        for(var Index=1;Index<Dependencies.Length;Index++){
            if(Dependencies[Index-1]==Dependencies[Index]){ throw new ArgumentException("World cell dependencies must be unique.",nameof(Definition)); }
        }
        byte[][]Coordinates=[
            Definition.Bounds.Center.R.ToCanonicalBytes(),Definition.Bounds.Center.U.ToCanonicalBytes(),Definition.Bounds.Center.F.ToCanonicalBytes(),
            Definition.Bounds.HalfExtents.R.ToCanonicalBytes(),Definition.Bounds.HalfExtents.U.ToCanonicalBytes(),Definition.Bounds.HalfExtents.F.ToCanonicalBytes()];
        var CoordinateSize=Coordinates.Sum(static Value=>2+Value.Length);
        byte[]Payload=new byte[checked(HeaderSize+CoordinateSize+CanonicalAssetKeyCodec.EncodedLength+4+Dependencies.Length*36)];
        BinaryPrimitives.WriteUInt32LittleEndian(Payload.AsSpan(0,4),Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(Payload.AsSpan(4,2),SchemaVersion);
        WriteIdentity(Payload.AsSpan(8,36),Definition.Id.ToString());
        WriteIdentity(Payload.AsSpan(44,36),Definition.Frame.ToString());
        BinaryPrimitives.WriteInt32LittleEndian(Payload.AsSpan(80,4),Definition.LevelOfDetail);
        var Offset=HeaderSize;
        foreach(var Value in Coordinates){
            BinaryPrimitives.WriteUInt16LittleEndian(Payload.AsSpan(Offset,2),checked((ushort)Value.Length));
            Value.CopyTo(Payload.AsSpan(Offset+2));Offset+=2+Value.Length;
        }
        CanonicalAssetKeyCodec.Write(Payload.AsSpan(Offset,CanonicalAssetKeyCodec.EncodedLength),Definition.EntityPayload);
        Offset+=CanonicalAssetKeyCodec.EncodedLength;
        BinaryPrimitives.WriteUInt32LittleEndian(Payload.AsSpan(Offset,4),(uint)Dependencies.Length);Offset+=4;
        foreach(WorldCellId Dependency in Dependencies){
            WriteIdentity(Payload.AsSpan(Offset,36),Dependency.ToString());
            Offset+=36;
        }
        return new ContentArtifact(
            ContentArtifactKind.WorldCell,Provenance,Definition.ArtifactClosure,Payload);
    }

    public static bool TryRead(
        ReadOnlySpan<byte>Envelope,
        out WorldCellDefinition?Definition,
        out WorldCellArtifactError Error){
        Definition=null;
        Error=WorldCellArtifactError.None;
        if(!ContentArtifactCodec.TryRead(Envelope,out var Artifact,out var ArtifactError)){
            Error=CreateError(WorldCellArtifactErrorCode.InvalidEnvelope,ArtifactError.Field,ArtifactError.Message);
            return false;
        }
        if(Artifact!.Kind!=ContentArtifactKind.WorldCell){
            Error=CreateError(WorldCellArtifactErrorCode.InvalidKind,"kind","Content artifact is not a WorldCell.");
            return false;
        }
        ReadOnlySpan<byte>Payload=Artifact.Payload.AsSpan();
        if(Payload.Length<HeaderSize
            ||BinaryPrimitives.ReadUInt32LittleEndian(Payload.Slice(0,4))!=Magic
            ||BinaryPrimitives.ReadUInt16LittleEndian(Payload.Slice(4,2))!=SchemaVersion
            ||BinaryPrimitives.ReadUInt16LittleEndian(Payload.Slice(6,2))!=0){
            Error=CreateError(WorldCellArtifactErrorCode.InvalidPayload,"payload.header","World cell payload header is invalid.");
            return false;
        }
        var Offset=HeaderSize;
        if(!TryReadCoordinates(Payload,ref Offset,out var Center,out var HalfExtents)){
            Error=CreateError(WorldCellArtifactErrorCode.InvalidBounds,"payload.bounds","World cell coordinates are malformed or outside the selected numeric range.");return false;
        }
        if(Payload.Length-Offset<CanonicalAssetKeyCodec.EncodedLength+4){
            Error=CreateError(WorldCellArtifactErrorCode.InvalidPayload,"payload.length","World cell payload is truncated.");return false;
        }
        var EntityOffset=Offset;Offset+=CanonicalAssetKeyCodec.EncodedLength;
        uint DependencyCount=BinaryPrimitives.ReadUInt32LittleEndian(Payload.Slice(Offset,4));Offset+=4;
        if(DependencyCount>65535||Offset+(long)DependencyCount*36!=Payload.Length){
            Error=CreateError(WorldCellArtifactErrorCode.InvalidPayload,"payload.length","World cell payload length is invalid.");
            return false;
        }
        if(!WorldCellId.TryParse(Encoding.ASCII.GetString(Payload.Slice(8,36)),out var Cell)||!Cell.IsValid
            ||!ReferenceFrameId.TryParse(Encoding.ASCII.GetString(Payload.Slice(44,36)),out var Frame)||!Frame.IsValid){
            Error=CreateError(WorldCellArtifactErrorCode.InvalidIdentity,"payload.identity","World cell or reference frame identity is invalid.");
            return false;
        }
        int LevelOfDetail=BinaryPrimitives.ReadInt32LittleEndian(Payload.Slice(80,4));
        if(LevelOfDetail<0){
            Error=CreateError(WorldCellArtifactErrorCode.InvalidPayload,"payload.levelOfDetail","World cell level of detail cannot be negative.");
            return false;
        }
        FrameLocalBounds Bounds=new(Center,HalfExtents);
        if(!Bounds.IsValid){
            Error=CreateError(WorldCellArtifactErrorCode.InvalidBounds,"payload.bounds","World cell half extents must be positive.");
            return false;
        }
        if(!CanonicalAssetKeyCodec.TryRead(
            Payload.Slice(EntityOffset,CanonicalAssetKeyCodec.EncodedLength),out var EntityPayload)){
            Error=CreateError(WorldCellArtifactErrorCode.InvalidEntityPayload,"payload.entityPayload","World cell entity payload identity is invalid.");
            return false;
        }
        var Dependencies=ImmutableArray.CreateBuilder<WorldCellId>((int)DependencyCount);
        WorldCellId?Previous=null;
        for(var Index=0;Index<DependencyCount;Index++){
            if(!WorldCellId.TryParse(Encoding.ASCII.GetString(Payload.Slice(Offset,36)),out var Dependency)||!Dependency.IsValid){
                Error=CreateError(WorldCellArtifactErrorCode.InvalidDependency,"payload.dependencies","World cell dependency is invalid.");
                return false;
            }
            if(Dependency==Cell||Previous is { }PreviousValue
                &&string.CompareOrdinal(PreviousValue.ToString(),Dependency.ToString())>=0){
                Error=CreateError(WorldCellArtifactErrorCode.InvalidDependency,"payload.dependencies","World cell dependencies must be unique, canonical, and not self-referential.");
                return false;
            }
            Dependencies.Add(Dependency);
            Previous=Dependency;
            Offset+=36;
        }
        if(!Artifact.Dependencies.Any(Reference=>Reference.Key==EntityPayload)){
            Error=CreateError(WorldCellArtifactErrorCode.InvalidEntityPayload,"dependencies","Artifact closure omits the world cell entity payload.");
            return false;
        }
        Definition=new WorldCellDefinition(
            Cell,Frame,Bounds,EntityPayload,LevelOfDetail,
            Dependencies.ToImmutable(),Artifact.Dependencies);
        return true;
    }

    private static void ValidateDefinition(WorldCellDefinition Definition){
        if(!Definition.Id.IsValid){ throw new ArgumentException("A valid WorldCellId is required.",nameof(Definition)); }
        if(!Definition.Frame.IsValid){ throw new ArgumentException("A valid ReferenceFrameId is required.",nameof(Definition)); }
        if(!Definition.Bounds.IsValid){ throw new ArgumentException("World cell half extents must be positive.",nameof(Definition)); }
        if(!Definition.EntityPayload.IsValid){ throw new ArgumentException("A valid entity payload identity is required.",nameof(Definition)); }
        if(Definition.LevelOfDetail<0){ throw new ArgumentException("World cell level of detail cannot be negative.",nameof(Definition)); }
        if(!Definition.ArtifactClosure.Any(Reference=>Reference.Key==Definition.EntityPayload)){
            throw new ArgumentException("Artifact closure must contain the entity payload.",nameof(Definition));
        }
        if(Definition.ArtifactClosure.Any(static Reference=>!Reference.IsValid)){
            throw new ArgumentException("Artifact closure contains an invalid reference.",nameof(Definition));
        }
        if(Definition.Dependencies.Any(Dependency=>!Dependency.IsValid||Dependency==Definition.Id)){
            throw new ArgumentException("World cell dependencies must be valid and not self-referential.",nameof(Definition));
        }
    }

    private static void WriteIdentity(Span<byte>Bytes,string Value){
        byte[]Text=Encoding.ASCII.GetBytes(Value);
        if(Text.Length!=36){ throw new ArgumentException("Canonical stable identity must contain 36 bytes.",nameof(Value)); }
        Text.CopyTo(Bytes);
    }
    private static bool TryReadCoordinates(ReadOnlySpan<byte>Payload,ref int Offset,out LogicalVector3 Center,out LogicalVector3 HalfExtents){
        Center=default;HalfExtents=default;
        Span<LogicalScalar>Values=stackalloc LogicalScalar[6];
        for(var Index=0;Index<Values.Length;Index++){
            if(Payload.Length-Offset<2){ return false; }
            var Length=BinaryPrimitives.ReadUInt16LittleEndian(Payload.Slice(Offset,2));Offset+=2;
            if(Length>Payload.Length-Offset||!LogicalScalar.TryReadCanonical(Payload.Slice(Offset,Length),out Values[Index])){ return false; }
            Offset+=Length;
        }
        Center=new(Values[0],Values[1],Values[2]);HalfExtents=new(Values[3],Values[4],Values[5]);return true;
    }
    private static WorldCellArtifactError CreateError(WorldCellArtifactErrorCode Code,string Field,string Message)=>new(Code,Field,Message);
}
