using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace CSharpGameEngine.Interchange;
/// <summary>CGEX v1: little-endian version and bounded length, SHA-256, and canonical UTF-8 document.</summary>
public static class InterchangeCodec {
    private const uint Magic=0x58454743;
    private const int HeaderSize=44;
    public const int DefaultMaximumBytes=256*1024*1024;
    private static readonly JsonSerializerOptions Options=new() {
        IncludeFields=true,IgnoreReadOnlyProperties=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=64
    };
    public static byte[]Write(InterchangeDocument Document,int MaximumBytes=DefaultMaximumBytes){
        ArgumentNullException.ThrowIfNull(Document);
        if(Document.SchemaVersion!=1){ throw new InvalidDataException("Unsupported interchange document schema."); }
        var Element=JsonSerializer.SerializeToElement(Document,Options);
        using var Buffer=new MemoryStream();
        using(var Writer=new Utf8JsonWriter(Buffer)){ WriteCanonical(Writer,Element); }
        if(Buffer.Length>MaximumBytes-HeaderSize){ throw new InvalidDataException("IMP803: interchange document exceeds its configured bound."); }
        var Result=new byte[checked(HeaderSize+(int)Buffer.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(Result,Magic);
        BinaryPrimitives.WriteInt32LittleEndian(Result.AsSpan(4),1);
        BinaryPrimitives.WriteInt32LittleEndian(Result.AsSpan(8),(int)Buffer.Length);
        Buffer.GetBuffer().AsSpan(0,(int)Buffer.Length).CopyTo(Result.AsSpan(HeaderSize));
        SHA256.HashData(Result.AsSpan(HeaderSize),Result.AsSpan(12,32));
        return Result;
    }
    public static InterchangeDocument Read(ReadOnlySpan<byte>Bytes,int MaximumBytes=DefaultMaximumBytes){
        if(Bytes.Length<HeaderSize||Bytes.Length>MaximumBytes){ throw new InvalidDataException("IMP803: invalid interchange length."); }
        if(BinaryPrimitives.ReadUInt32LittleEndian(Bytes)!=Magic||BinaryPrimitives.ReadInt32LittleEndian(Bytes[4..])!=1){ throw new InvalidDataException("Invalid interchange magic or schema."); }
        if(BinaryPrimitives.ReadInt32LittleEndian(Bytes[8..])!=Bytes.Length-HeaderSize){ throw new InvalidDataException("Invalid interchange payload length."); }
        Span<byte>Hash=stackalloc byte[32];
        SHA256.HashData(Bytes[HeaderSize..],Hash);
        if(!CryptographicOperations.FixedTimeEquals(Hash,Bytes.Slice(12,32))){ throw new InvalidDataException("Invalid interchange checksum."); }
        try {
            var Document=JsonSerializer.Deserialize<InterchangeDocument>(Bytes[HeaderSize..],Options)??throw new InvalidDataException("Missing interchange document.");
            if(!Bytes.SequenceEqual(Write(Document,MaximumBytes))){ throw new InvalidDataException("Noncanonical interchange document."); }
            return Document;
        } catch(JsonException Error){ throw new InvalidDataException("Invalid interchange payload.",Error); }
    }
    private static void WriteCanonical(Utf8JsonWriter Writer,JsonElement Element){
        switch(Element.ValueKind){
            case JsonValueKind.Object:
                Writer.WriteStartObject();
                foreach(var Property in Element.EnumerateObject().OrderBy(Property=>Property.Name,StringComparer.Ordinal)){
                    Writer.WritePropertyName(Property.Name);
                    WriteCanonical(Writer,Property.Value);
                }
                Writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                Writer.WriteStartArray();
                foreach(var Value in Element.EnumerateArray()){ WriteCanonical(Writer,Value); }
                Writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                var Raw=Element.GetRawText();
                Writer.WriteRawValue(Raw is "-0" or "-0.0"?"0":Raw);
                break;
            default:Element.WriteTo(Writer);break;
        }
    }
}
