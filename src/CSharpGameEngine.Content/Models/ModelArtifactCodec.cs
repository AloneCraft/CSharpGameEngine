using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace CSharpGameEngine.Content.Models;
/// <summary>L6 runtime payloads. The envelope remains ContentArtifactCodec; no Cooking/Import assembly is referenced.</summary>
public static class ModelArtifactCodec {
    private const uint Magic=0x4d454743;
    private const ushort Schema=1;
    private const int HeaderSize=12;
    public const int MaximumPayloadBytes=512*1024*1024;
    private static readonly JsonSerializerOptions Options=new() { IncludeFields=true,IgnoreReadOnlyProperties=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=64 };
    public static byte[]WriteMesh(ModelMeshData Value)=>Write(Value,ContentArtifactKind.Mesh);
    public static ModelMeshData ReadMesh(ReadOnlySpan<byte>Bytes)=>Read<ModelMeshData>(Bytes,ContentArtifactKind.Mesh);
    public static byte[]WriteModel(ModelData Value)=>Write(Value,ContentArtifactKind.Model);
    public static ModelData ReadModel(ReadOnlySpan<byte>Bytes)=>Read<ModelData>(Bytes,ContentArtifactKind.Model);
    public static byte[]WriteSkeleton(ModelSkeletonData Value)=>Write(Value,ContentArtifactKind.Skeleton);
    public static ModelSkeletonData ReadSkeleton(ReadOnlySpan<byte>Bytes)=>Read<ModelSkeletonData>(Bytes,ContentArtifactKind.Skeleton);
    public static byte[]WriteClip(ModelAnimationClipData Value)=>Write(Value,ContentArtifactKind.AnimationClip);
    public static ModelAnimationClipData ReadClip(ReadOnlySpan<byte>Bytes)=>Read<ModelAnimationClipData>(Bytes,ContentArtifactKind.AnimationClip);
    public static byte[]WriteLicense(ModelLicenseData Value)=>Write(Value,ContentArtifactKind.License);
    public static ModelLicenseData ReadLicense(ReadOnlySpan<byte>Bytes)=>Read<ModelLicenseData>(Bytes,ContentArtifactKind.License);
    private static byte[]Write<T>(T Value,ContentArtifactKind Kind) where T:class{
        ArgumentNullException.ThrowIfNull(Value);
        ModelDataValidation.Validate(Value);
        var Element=JsonSerializer.SerializeToElement(Value,Options);
        using var Buffer=new MemoryStream();
        using(var Writer=new BinaryWriter(Buffer,System.Text.Encoding.UTF8,true)){ ModelBinaryPayload.Write(Writer,typeof(T),Element,MaximumPayloadBytes-HeaderSize); }
        if(Buffer.Length>MaximumPayloadBytes-HeaderSize){ throw new InvalidDataException("Model payload exceeds its bound."); }
        var Bytes=new byte[checked(HeaderSize+(int)Buffer.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(Bytes,Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(4),Schema);
        BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(6),(ushort)Kind);
        BinaryPrimitives.WriteInt32LittleEndian(Bytes.AsSpan(8),(int)Buffer.Length);
        Buffer.GetBuffer().AsSpan(0,(int)Buffer.Length).CopyTo(Bytes.AsSpan(HeaderSize));
        return Bytes;
    }
    private static T Read<T>(ReadOnlySpan<byte>Bytes,ContentArtifactKind Kind) where T:class{
        if(Bytes.Length<HeaderSize||Bytes.Length>MaximumPayloadBytes||BinaryPrimitives.ReadUInt32LittleEndian(Bytes)!=Magic||BinaryPrimitives.ReadUInt16LittleEndian(Bytes[4..])!=Schema||BinaryPrimitives.ReadUInt16LittleEndian(Bytes[6..])!=(ushort)Kind||BinaryPrimitives.ReadInt32LittleEndian(Bytes[8..])!=Bytes.Length-HeaderSize){ throw new InvalidDataException("Invalid model payload header, kind or length."); }
        try {
            using var Input=new MemoryStream(Bytes[HeaderSize..].ToArray(),false);
            using var Reader=new BinaryReader(Input,System.Text.Encoding.UTF8,true);
            using var Json=new MemoryStream();
            using(var Writer=new Utf8JsonWriter(Json)){ ModelBinaryPayload.Read(Reader,Writer,typeof(T),0); }
            if(Input.Position!=Input.Length){ throw new InvalidDataException("Trailing model payload data."); }
            var Value=JsonSerializer.Deserialize<T>(Json.GetBuffer().AsSpan(0,checked((int)Json.Length)),Options)??throw new InvalidDataException("Missing model payload.");
            ModelDataValidation.Validate(Value);
            if(!Bytes.SequenceEqual(Write(Value,Kind))){ throw new InvalidDataException("Noncanonical model payload."); }
            return Value;
        } catch(Exception Error) when(Error is JsonException or EndOfStreamException or ArgumentException or OverflowException or NullReferenceException or InvalidOperationException or IndexOutOfRangeException){ throw new InvalidDataException("Invalid model payload.",Error); }
    }
}
