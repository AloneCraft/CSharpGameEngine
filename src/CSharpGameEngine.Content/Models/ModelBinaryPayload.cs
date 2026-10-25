using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
namespace CSharpGameEngine.Content.Models;
/// <summary>A fixed-schema binary projection. JSON is used only as an in-memory record construction adapter, never on the wire.</summary>
internal static class ModelBinaryPayload {
    private static readonly UTF8Encoding Utf8=new(false,true);
    private static readonly JsonSerializerOptions ShapeOptions=new() { IncludeFields=true,IgnoreReadOnlyProperties=true,TypeInfoResolver=new DefaultJsonTypeInfoResolver() };
    private static readonly ConcurrentDictionary<Type,JsonPropertyInfo[]>Members=new();
    private static JsonPropertyInfo[]Shape(Type Type)=>Members.GetOrAdd(Type,Type=>{
        if(Type.Namespace!="CSharpGameEngine.Content.Models"&&Type!=typeof(System.Numerics.Vector2)&&Type!=typeof(System.Numerics.Vector3)&&Type!=typeof(System.Numerics.Vector4)&&Type!=typeof(System.Numerics.Quaternion)&&Type!=typeof(System.Numerics.Matrix4x4)){
            throw new InvalidDataException($"Unsupported model scalar or record: {Type.FullName}.");
        }
        var Properties=ShapeOptions.GetTypeInfo(Type).Properties.Where(Property=>Property.Get is not null&&Property.Set is not null);
        if(Type==typeof(System.Numerics.Matrix4x4)){ Properties=Properties.Where(Property=>Property.Name.Length==3&&Property.Name[0]=='M'&&Property.Name[1] is >= '1' and <= '4'&&Property.Name[2] is >= '1' and <= '4'); }
        else if(Type.Namespace=="System.Numerics"){ Properties=Properties.Where(Property=>Property.Name is "X" or "Y" or "Z" or "W"); }
        return Properties.OrderBy(Property=>Property.Name,StringComparer.Ordinal).ToArray();
    });
    internal static void Write(BinaryWriter Writer,Type Type,JsonElement Value,int MaximumBytes){
        if(Writer.BaseStream.Position>MaximumBytes){ throw new InvalidDataException("IMP803: model payload exceeds its configured bound."); }
        var Underlying=Nullable.GetUnderlyingType(Type);
        if(!Type.IsValueType||Underlying is not null){
            var Present=Value.ValueKind!=JsonValueKind.Null;
            Writer.Write(Present);
            if(!Present){ return; }
            Type=Underlying??Type;
        }
        if(Type.IsEnum){ Type=Enum.GetUnderlyingType(Type); }
        if(Type==typeof(float)){
            var Number=Value.GetSingle();
            if(!float.IsFinite(Number)){ throw new InvalidDataException("IMP102: nonfinite scalar."); }
            Writer.Write(Number==0f?0f:Number);return;
        }
        if(Type==typeof(int)){ Writer.Write(Value.GetInt32());return; }
        if(Type==typeof(uint)){ Writer.Write(Value.GetUInt32());return; }
        if(Type==typeof(ushort)){ Writer.Write(Value.GetUInt16());return; }
        if(Type==typeof(byte)){ Writer.Write(Value.GetByte());return; }
        if(Type==typeof(bool)){ Writer.Write(Value.GetBoolean());return; }
        if(Type==typeof(Int128)){
            Span<byte>Bytes=stackalloc byte[16];
            BinaryPrimitives.WriteInt128LittleEndian(Bytes,Int128.Parse(Value.GetRawText(),CultureInfo.InvariantCulture));
            Writer.Write(Bytes);return;
        }
        if(Type==typeof(string)){ WriteText(Writer,Value.GetString()!,MaximumBytes);return; }
        if(Type.IsGenericType&&Type.GetGenericTypeDefinition()==typeof(ImmutableArray<>)){
            Writer.Write(Value.GetArrayLength());
            var ElementType=Type.GetGenericArguments()[0];
            foreach(var Element in Value.EnumerateArray()){ Write(Writer,ElementType,Element,MaximumBytes); }
            return;
        }
        if(Type.IsGenericType&&Type.GetGenericTypeDefinition()==typeof(ImmutableSortedDictionary<,>)&&Type.GetGenericArguments()[0]==typeof(string)){
            var Properties=Value.EnumerateObject().OrderBy(Property=>Property.Name,StringComparer.Ordinal).ToArray();
            Writer.Write(Properties.Length);
            var ValueType=Type.GetGenericArguments()[1];
            foreach(var Property in Properties){ WriteText(Writer,Property.Name,MaximumBytes);Write(Writer,ValueType,Property.Value,MaximumBytes); }
            return;
        }
        foreach(var Property in Shape(Type)){ Write(Writer,Property.PropertyType,Value.GetProperty(Property.Name),MaximumBytes); }
    }
    internal static void Read(BinaryReader Reader,Utf8JsonWriter Writer,Type Type,int Depth){
        if(Depth>64){ throw new InvalidDataException("IMP803: model nesting exceeds its bound."); }
        var Underlying=Nullable.GetUnderlyingType(Type);
        if(!Type.IsValueType||Underlying is not null){
            if(!ReadBoolean(Reader)){ Writer.WriteNullValue();return; }
            Type=Underlying??Type;
        }
        if(Type.IsEnum){ Type=Enum.GetUnderlyingType(Type); }
        if(Type==typeof(float)){
            var Number=Reader.ReadSingle();
            if(!float.IsFinite(Number)){ throw new InvalidDataException("IMP102: nonfinite scalar."); }
            Writer.WriteNumberValue(Number);return;
        }
        if(Type==typeof(int)){ Writer.WriteNumberValue(Reader.ReadInt32());return; }
        if(Type==typeof(uint)){ Writer.WriteNumberValue(Reader.ReadUInt32());return; }
        if(Type==typeof(ushort)){ Writer.WriteNumberValue(Reader.ReadUInt16());return; }
        if(Type==typeof(byte)){ Writer.WriteNumberValue(Reader.ReadByte());return; }
        if(Type==typeof(bool)){ Writer.WriteBooleanValue(ReadBoolean(Reader));return; }
        if(Type==typeof(Int128)){
            var Bytes=Reader.ReadBytes(16);
            if(Bytes.Length!=16){ throw new EndOfStreamException(); }
            Writer.WriteRawValue(BinaryPrimitives.ReadInt128LittleEndian(Bytes).ToString(CultureInfo.InvariantCulture));return;
        }
        if(Type==typeof(string)){ Writer.WriteStringValue(ReadText(Reader));return; }
        if(Type.IsGenericType&&Type.GetGenericTypeDefinition()==typeof(ImmutableArray<>)){
            var Count=ReadCount(Reader);
            var ElementType=Type.GetGenericArguments()[0];
            Writer.WriteStartArray();
            for(var Index=0;Index<Count;Index++){ Read(Reader,Writer,ElementType,Depth+1); }
            Writer.WriteEndArray();return;
        }
        if(Type.IsGenericType&&Type.GetGenericTypeDefinition()==typeof(ImmutableSortedDictionary<,>)&&Type.GetGenericArguments()[0]==typeof(string)){
            var Count=ReadCount(Reader);
            var ValueType=Type.GetGenericArguments()[1];
            string?Previous=null;
            Writer.WriteStartObject();
            for(var Index=0;Index<Count;Index++){
                var Key=ReadText(Reader);
                if(Previous is not null&&string.CompareOrdinal(Previous,Key)>=0){ throw new InvalidDataException("Noncanonical model dictionary order."); }
                Writer.WritePropertyName(Key);
                Read(Reader,Writer,ValueType,Depth+1);
                Previous=Key;
            }
            Writer.WriteEndObject();return;
        }
        Writer.WriteStartObject();
        foreach(var Property in Shape(Type)){
            Writer.WritePropertyName(Property.Name);
            Read(Reader,Writer,Property.PropertyType,Depth+1);
        }
        Writer.WriteEndObject();
    }
    private static bool ReadBoolean(BinaryReader Reader)=>Reader.ReadByte() switch { 0=>false,1=>true,_=>throw new InvalidDataException("Noncanonical boolean or presence marker.") };
    private static int ReadCount(BinaryReader Reader){
        var Count=Reader.ReadInt32();
        if(Count<0||Count>Reader.BaseStream.Length-Reader.BaseStream.Position){ throw new InvalidDataException("IMP803: invalid model collection or text length."); }
        return Count;
    }
    private static void WriteText(BinaryWriter Writer,string Text,int MaximumBytes){
        if(Writer.BaseStream.Position+4L+Utf8.GetByteCount(Text)>MaximumBytes){ throw new InvalidDataException("IMP803: model text exceeds its configured bound."); }
        var Bytes=Utf8.GetBytes(Text);
        Writer.Write(Bytes.Length);Writer.Write(Bytes);
    }
    private static string ReadText(BinaryReader Reader){
        var Count=ReadCount(Reader);
        var Bytes=Reader.ReadBytes(Count);
        if(Bytes.Length!=Count){ throw new EndOfStreamException(); }
        return Utf8.GetString(Bytes);
    }
}
