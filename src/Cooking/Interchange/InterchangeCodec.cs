using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace CSharpGameEngine.Interchange;
/// <summary>CGEX v2：リトルエンディアンのスカラーペイロード、長さ上限、SHA-256を使用する。文書スキーマは1のまま。</summary>
public static class InterchangeCodec {
    private const uint Magic=0x58454743;
    private const int HeaderSize=44;
    public const int WireVersion=2;
    public const int DefaultMaximumBytes=256*1024*1024;
    private static readonly JsonSerializerOptions Options=new() {
        IncludeFields=true,IgnoreReadOnlyProperties=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=64
    };
    public static byte[]Write(InterchangeDocument Document,int MaximumBytes=DefaultMaximumBytes){
        ArgumentNullException.ThrowIfNull(Document);
        if(Document.SchemaVersion!=1){ throw new InvalidDataException("Unsupported interchange document schema."); }
        var Element=JsonSerializer.SerializeToElement(Document,Options);
        using var Buffer=new MemoryStream();
        using(var Writer=new BinaryWriter(Buffer,System.Text.Encoding.UTF8,true)){ InterchangeBinaryPayload.Write(Writer,typeof(InterchangeDocument),Element,MaximumBytes-HeaderSize); }
        if(Buffer.Length>MaximumBytes-HeaderSize){ throw new InvalidDataException("IMP803: interchange document exceeds its configured bound."); }
        var Result=new byte[checked(HeaderSize+(int)Buffer.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(Result,Magic);
        BinaryPrimitives.WriteInt32LittleEndian(Result.AsSpan(4),WireVersion);
        BinaryPrimitives.WriteInt32LittleEndian(Result.AsSpan(8),(int)Buffer.Length);
        Buffer.GetBuffer().AsSpan(0,(int)Buffer.Length).CopyTo(Result.AsSpan(HeaderSize));
        SHA256.HashData(Result.AsSpan(HeaderSize),Result.AsSpan(12,32));
        return Result;
    }
    public static InterchangeDocument Read(ReadOnlySpan<byte>Bytes,int MaximumBytes=DefaultMaximumBytes){
        if(Bytes.Length<HeaderSize||Bytes.Length>MaximumBytes){ throw new InvalidDataException("IMP803: invalid interchange length."); }
        if(BinaryPrimitives.ReadUInt32LittleEndian(Bytes)!=Magic||BinaryPrimitives.ReadInt32LittleEndian(Bytes[4..])!=WireVersion){ throw new InvalidDataException("Invalid interchange magic or schema."); }
        if(BinaryPrimitives.ReadInt32LittleEndian(Bytes[8..])!=Bytes.Length-HeaderSize){ throw new InvalidDataException("Invalid interchange payload length."); }
        Span<byte>Hash=stackalloc byte[32];
        SHA256.HashData(Bytes[HeaderSize..],Hash);
        if(!CryptographicOperations.FixedTimeEquals(Hash,Bytes.Slice(12,32))){ throw new InvalidDataException("Invalid interchange checksum."); }
        try {
            using var Input=new MemoryStream(Bytes[HeaderSize..].ToArray(),false);
            using var Reader=new BinaryReader(Input,System.Text.Encoding.UTF8,true);
            using var Json=new MemoryStream();
            using(var Writer=new Utf8JsonWriter(Json)){ InterchangeBinaryPayload.Read(Reader,Writer,typeof(InterchangeDocument),0); }
            if(Input.Position!=Input.Length){ throw new InvalidDataException("Trailing interchange payload bytes."); }
            var Document=JsonSerializer.Deserialize<InterchangeDocument>(Json.GetBuffer().AsSpan(0,checked((int)Json.Length)),Options)??throw new InvalidDataException("Missing interchange document.");
            var StructuralErrors=InterchangeValidator.Validate(Document);
            if(StructuralErrors.Any(Error=>Error.Severity==CSharpGameEngine.Cooking.ImportSeverity.Error)){ throw new InvalidDataException("Invalid interchange structure: "+StructuralErrors[0].Message); }
            if(!Bytes.SequenceEqual(Write(Document,MaximumBytes))){ throw new InvalidDataException("Noncanonical interchange document."); }
            return Document;
        } catch(Exception Error) when(Error is JsonException or EndOfStreamException or ArgumentException or OverflowException or NullReferenceException or InvalidOperationException or IndexOutOfRangeException){ throw new InvalidDataException("Invalid interchange payload.",Error); }
    }
}
