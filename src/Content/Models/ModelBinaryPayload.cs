using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
namespace CSharpGameEngine.Content.Models;
/// <summary>固定スキーマのバイナリープロジェクション。JSONはメモリ上のレコード構築用アダプターとしてのみ使用し、通信形式には使わない。</summary>
internal static class ModelBinaryPayload {
    private static readonly UTF8Encoding Utf8=new(false,true);
    internal static void Write(BinaryWriter Writer,Type Type,JsonElement Value,int MaximumBytes){
        if(Writer.BaseStream.Position>MaximumBytes){ throw new InvalidDataException("IMP803: model payload exceeds its configured bound."); }
        var Shape=ModelBinarySchema.Get(Type);
        if(Shape.HasPresence){
            var Present=Value.ValueKind!=JsonValueKind.Null;
            Writer.Write(Present);
            if(!Present){ return; }
        }
        if(Shape.Kind==ModelBinaryKind.Single){
            var Number=Value.GetSingle();
            if(!float.IsFinite(Number)){ throw new InvalidDataException("IMP102: nonfinite scalar."); }
            Writer.Write(Number==0f?0f:Number);return;
        }
        if(Shape.Kind==ModelBinaryKind.Int32){ Writer.Write(Value.GetInt32());return; }
        if(Shape.Kind==ModelBinaryKind.UInt32){ Writer.Write(Value.GetUInt32());return; }
        if(Shape.Kind==ModelBinaryKind.UInt16){ Writer.Write(Value.GetUInt16());return; }
        if(Shape.Kind==ModelBinaryKind.Byte){ Writer.Write(Value.GetByte());return; }
        if(Shape.Kind==ModelBinaryKind.Boolean){ Writer.Write(Value.GetBoolean());return; }
        if(Shape.Kind==ModelBinaryKind.Int128){
            Span<byte>Bytes=stackalloc byte[16];
            BinaryPrimitives.WriteInt128LittleEndian(Bytes,Int128.Parse(Value.GetRawText(),CultureInfo.InvariantCulture));
            Writer.Write(Bytes);return;
        }
        if(Shape.Kind==ModelBinaryKind.Text){ WriteText(Writer,Value.GetString()!,MaximumBytes);return; }
        if(Shape.Kind==ModelBinaryKind.Array){
            Writer.Write(Value.GetArrayLength());
            var ElementType=Shape.ElementType!;
            foreach(var Element in Value.EnumerateArray()){ Write(Writer,ElementType,Element,MaximumBytes); }
            return;
        }
        foreach(var Property in Shape.Members){ Write(Writer,Property.PropertyType,Value.GetProperty(Property.Name),MaximumBytes); }
    }
    internal static void Read(BinaryReader Reader,Utf8JsonWriter Writer,Type Type,int Depth){
        if(Depth>64){ throw new InvalidDataException("IMP803: model nesting exceeds its bound."); }
        var Shape=ModelBinarySchema.Get(Type);
        if(Shape.HasPresence){
            if(!ReadBoolean(Reader)){ Writer.WriteNullValue();return; }
        }
        if(Shape.Kind==ModelBinaryKind.Single){
            var Number=Reader.ReadSingle();
            if(!float.IsFinite(Number)){ throw new InvalidDataException("IMP102: nonfinite scalar."); }
            Writer.WriteNumberValue(Number);return;
        }
        if(Shape.Kind==ModelBinaryKind.Int32){ Writer.WriteNumberValue(Reader.ReadInt32());return; }
        if(Shape.Kind==ModelBinaryKind.UInt32){ Writer.WriteNumberValue(Reader.ReadUInt32());return; }
        if(Shape.Kind==ModelBinaryKind.UInt16){ Writer.WriteNumberValue(Reader.ReadUInt16());return; }
        if(Shape.Kind==ModelBinaryKind.Byte){ Writer.WriteNumberValue(Reader.ReadByte());return; }
        if(Shape.Kind==ModelBinaryKind.Boolean){ Writer.WriteBooleanValue(ReadBoolean(Reader));return; }
        if(Shape.Kind==ModelBinaryKind.Int128){
            var Bytes=Reader.ReadBytes(16);
            if(Bytes.Length!=16){ throw new EndOfStreamException(); }
            Writer.WriteRawValue(BinaryPrimitives.ReadInt128LittleEndian(Bytes).ToString(CultureInfo.InvariantCulture));return;
        }
        if(Shape.Kind==ModelBinaryKind.Text){ Writer.WriteStringValue(ReadText(Reader));return; }
        if(Shape.Kind==ModelBinaryKind.Array){
            var Count=ReadCount(Reader);
            var ElementType=Shape.ElementType!;
            Writer.WriteStartArray();
            for(var Index=0;Index<Count;Index++){ Read(Reader,Writer,ElementType,Depth+1); }
            Writer.WriteEndArray();return;
        }
        Writer.WriteStartObject();
        foreach(var Property in Shape.Members){
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
