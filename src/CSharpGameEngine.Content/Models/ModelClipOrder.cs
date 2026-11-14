using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
namespace CSharpGameEngine.Content.Models;
public static class ModelClipOrder {
    public const string Schema="cge.animation-order/1";
    public static ModelRuntimeBlock Create(IEnumerable<string>Names){
        var Values=Names.ToArray();Validate(Values);
        using var Bytes=new MemoryStream();
        using(var Writer=new Utf8JsonWriter(Bytes)){ Writer.WriteStartArray();foreach(var Name in Values){ Writer.WriteStringValue(Name); }Writer.WriteEndArray(); }
        if(Bytes.Length>16*1024*1024){ throw new InvalidDataException("Animation order exceeds its payload bound."); }
        return new(Schema,[..Bytes.ToArray()]);
    }
    public static ImmutableArray<ModelAnimationClipData>Apply(ModelData Model,ImmutableArray<ModelAnimationClipData>Clips){
        var Blocks=Model.RuntimeBlocks.Where(static Block=>Block.Schema==Schema).ToArray();
        if(Blocks.Length==0){
            if(Clips.Length>1){ throw new InvalidDataException("A model with multiple clips must declare its authored animation order."); }
            return Clips;
        }
        if(Blocks.Length!=1||Blocks[0].Payload.Length>16*1024*1024){ throw new InvalidDataException("Invalid animation order block."); }
        string[]Names;
        try {
            using var Json=JsonDocument.Parse(Blocks[0].Payload.ToArray());
            Names=Json.RootElement.EnumerateArray().Select(static Value=>Value.GetString()??throw new InvalidDataException("Animation name is null.")).ToArray();
        }catch(Exception Error)when(Error is JsonException or InvalidOperationException){ throw new InvalidDataException("Invalid animation order payload.",Error); }
        Validate(Names);
        if(Names.Length!=Clips.Length||Clips.Select(static Clip=>Clip.Name).Distinct(StringComparer.Ordinal).Count()!=Clips.Length){ throw new InvalidDataException("Animation order and stored clips differ."); }
        var ByName=Clips.ToDictionary(static Clip=>Clip.Name,StringComparer.Ordinal);
        if(Names.Any(Name=>!ByName.ContainsKey(Name))){ throw new InvalidDataException("Animation order references an absent clip."); }
        return [..Names.Select(Name=>ByName[Name])];
    }
    private static void Validate(string[]Names){
        if(Names.Length>65535||Names.Distinct(StringComparer.Ordinal).Count()!=Names.Length||Names.Any(static Name=>string.IsNullOrWhiteSpace(Name)||Encoding.UTF8.GetByteCount(Name)>4096||Name.Any(char.IsControl)||!Name.IsNormalized(NormalizationForm.FormC))){
            throw new InvalidDataException("Animation order contains invalid or duplicate names.");
        }
    }
}
