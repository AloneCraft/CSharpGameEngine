using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace CSharpGameEngine.ShaderGraph;
public static class ShaderGraphCanonicalization {
    private static readonly JsonSerializerOptions CanonicalOptions = new JsonSerializerOptions {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new StableIdJsonConverter() }
    };
    public static string ToCanonicalJson(ShaderGraphDocument Document) {
        var Canonical = Normalize(Document);
        return JsonSerializer.Serialize(Canonical, CanonicalOptions);
    }
    public static ShaderGraphDocument? FromJson(string Json) {
        return JsonSerializer.Deserialize<ShaderGraphDocument>(Json, CanonicalOptions);
    }
    public static string ComputeSemanticHash(ShaderGraphDocument Document) {
        var Semantic = Normalize(Document);
        Semantic.Layout = null;
        var Serialized = JsonSerializer.Serialize(Semantic, CanonicalOptions);
        var Hasher = SHA256.Create(); var Hash = Hasher.ComputeHash(Encoding.UTF8.GetBytes(Serialized)); return BitConverter.ToString(Hash).Replace("-", string.Empty);
    }
    private static ShaderGraphDocument Normalize(ShaderGraphDocument Document) {
        var Subgraphs = Document.Subgraphs.OrderBy(Subgraph => Subgraph.Id.ToString(), StringComparer.Ordinal).Select(NormalizeSubgraph).ToList();
        return new ShaderGraphDocument {
            Version = Document.Version,
            Id = Document.Id,
            Target = Document.Target,
            EntryPoint = Document.EntryPoint,
            RootSubgraphId = Document.RootSubgraphId,
            Subgraphs = Subgraphs,
            Layout = NormalizeLayout(Document.Layout)
        };
    }
    private static ShaderGraphSubgraph NormalizeSubgraph(ShaderGraphSubgraph Subgraph) {
        var Nodes = new List<ShaderGraphNode>(Subgraph.Nodes.Count);
        foreach (var Node in Subgraph.Nodes.OrderBy(Node => Node.Id.ToString(), StringComparer.Ordinal)) {
            var Arguments = new List<ShaderGraphArgument>(Node.Arguments.Count);
            foreach (var Argument in Node.Arguments.OrderBy(Argument => Argument.Name, StringComparer.Ordinal)) {
                Arguments.Add(new ShaderGraphArgument { Name = Argument.Name, Value = Argument.Value });
            }
            Nodes.Add(new ShaderGraphNode {
                Id = Node.Id,
                Type = Node.Type,
                Name = Node.Name,
                Arguments = Arguments
            });
        }
        return new ShaderGraphSubgraph {
            Id = Subgraph.Id,
            Name = Subgraph.Name,
            Inputs = Subgraph.Inputs.OrderBy(Port => Port.Name, StringComparer.Ordinal).ToList(),
            Outputs = Subgraph.Outputs.OrderBy(Port => Port.Name, StringComparer.Ordinal).ToList(),
            Nodes = Nodes,
            Edges = Subgraph.Edges.OrderBy(Edge => Edge.FromNodeId.ToString(), StringComparer.Ordinal).ThenBy(Edge => Edge.ToNodeId.ToString(), StringComparer.Ordinal).ThenBy(Edge => Edge.FromPort, StringComparer.Ordinal).ThenBy(Edge => Edge.ToPort, StringComparer.Ordinal).ToList(),
            Regions = Subgraph.Regions.OrderBy(Region => Region.Id.ToString(), StringComparer.Ordinal).Select(NormalizeRegion).ToList()
        };
    }
    private static ShaderGraphRegion NormalizeRegion(ShaderGraphRegion Region) {
        return new ShaderGraphRegion {
            Id = Region.Id,
            Name = Region.Name,
            NodeIds = Region.NodeIds.OrderBy(NodeId => NodeId.ToString(), StringComparer.Ordinal).ToList()
        };
    }
    private static ShaderGraphEditorLayout? NormalizeLayout(ShaderGraphEditorLayout? Layout) {
        if (Layout is null) {
            return null;
        }
        return new ShaderGraphEditorLayout {
            Nodes = Layout.Nodes.OrderBy(Node => Node.NodeId.ToString(), StringComparer.Ordinal).ToList(),
            Regions = Layout.Regions.OrderBy(Region => Region.RegionId.ToString(), StringComparer.Ordinal).ToList()
        };
    }
}

internal sealed class StableIdJsonConverter:JsonConverter<StableId> {
    public override StableId Read(ref Utf8JsonReader Reader,Type TypeToConvert,JsonSerializerOptions Options) {
        var Value=Reader.GetString();
        if(!StableId.TryParse(Value,out var Parsed)){ throw new JsonException("StableId must be a GUID string."); }
        return Parsed;
    }
    public override void Write(Utf8JsonWriter Writer,StableId Value,JsonSerializerOptions Options) {
        Writer.WriteStringValue(Value.ToString());
    }
}