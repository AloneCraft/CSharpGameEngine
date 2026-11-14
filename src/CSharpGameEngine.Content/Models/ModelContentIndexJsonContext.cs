using System.Text.Json.Serialization;
namespace CSharpGameEngine.Content.Models;
[JsonSourceGenerationOptions(UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ModelContentIndex.IndexDocument))]
internal partial class ModelContentIndexJsonContext:JsonSerializerContext {
}
