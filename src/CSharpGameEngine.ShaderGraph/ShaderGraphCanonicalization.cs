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
        var Document = JsonSerializer.Deserialize<ShaderGraphDocument>(Json, CanonicalOptions);
        if (Document is not null) {
            RejectNulls(Document);
        }
        return Document;
    }
    /// <summary>
    /// System.Text.Json 8 puts a JSON null into a member or list item the model declares non-null, and the validator,
    /// the emitter and the canonical form then failed with a NullReferenceException that named nothing (in the editor and
    /// the build). Such a null is malformed JSON for the graph, so it throws as malformed JSON does, naming its path.
    /// </summary>
    private static void RejectNulls(ShaderGraphDocument Document) {
        Require(Document.Target, "$.target");
        if (Document.EntryPoint is { } EntryPoint) {
            Require(EntryPoint.Name, "$.entryPoint.name");
        }
        Items(Document.Subgraphs, "$.subgraphs", static (Subgraph, Path) => {
            Items(Subgraph.Inputs, Path + ".inputs", static (Port, PortPath) => Require(Port.Name, PortPath + ".name"));
            Items(Subgraph.Outputs, Path + ".outputs", static (Port, PortPath) => Require(Port.Name, PortPath + ".name"));
            Items(Subgraph.Nodes, Path + ".nodes", static (Node, NodePath) => {
                Require(Node.Type, NodePath + ".type");
                Items(Node.Arguments, NodePath + ".arguments", static (Argument, ArgumentPath) => {
                    Require(Argument.Name, ArgumentPath + ".name");
                    Require(Argument.Value, ArgumentPath + ".value");
                });
            });
            Items(Subgraph.Edges, Path + ".edges", static (Edge, EdgePath) => {
                Require(Edge.FromPort, EdgePath + ".fromPort");
                Require(Edge.ToPort, EdgePath + ".toPort");
            });
            Items(Subgraph.Regions, Path + ".regions", static (Region, RegionPath) => {
                Require(Region.NodeIds, RegionPath + ".nodeIds");
                Require(Region.ElseNodeIds, RegionPath + ".elseNodeIds");
            });
        });
        Items(Document.Dependencies, "$.dependencies", static (Dependency, Path) => Require(Dependency.Path, Path + ".path"));
        if (Document.Layout is { } Layout) {
            Items(Layout.Nodes, "$.layout.nodes", static (Node, Path) => { });
            Items(Layout.Regions, "$.layout.regions", static (Region, Path) => { });
        }
    }
    private static T Require<T>(T? Value, string Path) where T : class {
        return Value ?? throw new JsonException($"The Shader Graph has null where a value is required at {Path}.", Path, null, null);
    }
    private static void Items<T>(List<T>? List, string Path, Action<T, string> Check) where T : class {
        Require(List, Path);
        for (var Index = 0; Index < List!.Count; Index++) {
            var ItemPath = Path + "[" + Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
            Check(Require(List[Index], ItemPath), ItemPath);
        }
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
            Dependencies = Document.Dependencies.OrderBy(Dependency => Dependency.Path, StringComparer.Ordinal).ThenBy(Dependency => Dependency.Id.ToString(), StringComparer.Ordinal).Select(Dependency => new ShaderGraphDependency { Id = Dependency.Id, Path = Dependency.Path.Replace('\\', '/') }).ToList(),
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
            Kind = Region.Kind,
            Name = Region.Name,
            ControlNodeId = Region.ControlNodeId,
            ParentRegionId = Region.ParentRegionId,
            ParentBranch = Region.ParentBranch,
            NodeIds = Region.NodeIds.OrderBy(NodeId => NodeId.ToString(), StringComparer.Ordinal).ToList(),
            ElseNodeIds = Region.ElseNodeIds.OrderBy(NodeId => NodeId.ToString(), StringComparer.Ordinal).ToList()
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
