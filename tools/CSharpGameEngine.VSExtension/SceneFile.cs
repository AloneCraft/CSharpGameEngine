using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// VSIX-local, independent reader for <c>*.scene.json</c> (Docs/EditorSpec.md §3, ADR-0020).
/// Deliberately does not reference <c>CSharpGameEngine.Scene</c> - that project is net10.0 and
/// cannot be referenced from this net472 project (same TFM-compatibility wall as
/// <c>CSharpGameEngine.Ecs</c>) - so this duplicates the small node/tree model instead, mirroring
/// the existing <c>SceneDocument.cs</c> "thin VSIX-local DTO" convention. Uses plain <see cref="Guid"/>
/// rather than <c>CSharpGameEngine.Core.StableId</c> for the same reason.
/// </summary>
internal sealed class SceneFileNode {
    public required Guid Id { get; init;}

    public required string Name { get; init;}

    /// <summary>Null for a root node.</summary>
    public Guid?ParentId { get; init;}

    public required Vector3 Position { get; init;}

    public required Quaternion Rotation { get; init;}

    public required Vector3 Scale { get; init;}

    /// <summary>Relative path (from the containing scene file's directory) to a .glb/.gltf/.fbx. Null for a mesh-less node.</summary>
    public string?MeshAssetPath { get; init;}

    public Matrix4x4 LocalTransform=>
        Matrix4x4.CreateScale(Scale)*Matrix4x4.CreateFromQuaternion(Rotation)*Matrix4x4.CreateTranslation(Position);
}

/// <summary>One <see cref="SceneFileNode"/> plus its resolved children, produced by <see cref="SceneFile.BuildTree"/>.</summary>
internal sealed class SceneFileNodeTreeItem {
    public required SceneFileNode Node { get; init;}

    public required IReadOnlyList<SceneFileNodeTreeItem>Children { get; init;}
}

internal sealed class SceneFile {
    public required IReadOnlyList<SceneFileNode>Nodes { get; init;}

    private static readonly JsonSerializerOptions JsonOptions=new(){ PropertyNameCaseInsensitive=true };

    public static SceneFile Load(string Path)=>Parse(File.ReadAllText(Path));

    public static SceneFile Parse(string Json){
        JsonDocumentDto Document=JsonSerializer.Deserialize<JsonDocumentDto>(Json,JsonOptions)
            ??throw new InvalidDataException("シーンJSONの読み込みに失敗しました。");

        List<SceneFileNode>Nodes=new(Document.Nodes.Count);
        foreach(JsonNodeDto Node in Document.Nodes){
            Nodes.Add(new SceneFileNode {
                Id=Guid.ParseExact(Node.Id,"D"),
                Name=Node.Name,
                ParentId=Node.ParentId is null?null:Guid.ParseExact(Node.ParentId,"D"),
                Position=ToVector3(Node.Transform.Position),
                Rotation=ToQuaternion(Node.Transform.Rotation),
                Scale=ToVector3(Node.Transform.Scale),
                MeshAssetPath=Node.MeshAssetPath,
            });
        }

        return new SceneFile { Nodes=Nodes };
    }

    public void Save(string Path)=>File.WriteAllText(Path,ToJson());

    public string ToJson(){
        JsonDocumentDto Document=new(){
            SchemaVersion=1,
            Nodes=[.. Nodes.Select(ToJsonNode)],
        };

        return JsonSerializer.Serialize(Document,JsonOptions);
    }

    private static JsonNodeDto ToJsonNode(SceneFileNode Node)=>new(){
        Id=Node.Id.ToString("D"),
        Name=Node.Name,
        ParentId=Node.ParentId?.ToString("D"),
        Transform=new JsonTransformDto {
            Position=FromVector3(Node.Position),
            Rotation=FromQuaternion(Node.Rotation),
            Scale=FromVector3(Node.Scale),
        },
        MeshAssetPath=Node.MeshAssetPath,
    };

    private static float[]FromVector3(Vector3 V)=>[V.X,V.Y,V.Z];

    private static float[]FromQuaternion(Quaternion Q)=>[Q.X,Q.Y,Q.Z,Q.W];

    /// <summary>Returns a new <see cref="SceneFile"/> with <paramref name="Node"/> appended - this type is immutable, so callers must re-<see cref="Save"/> the result (ADR-0021's shape-palette "add primitive" flow).</summary>
    public SceneFile AddNode(SceneFileNode Node)=>new(){ Nodes=[.. Nodes,Node]};

    /// <summary>The first node with no parent, or null for an empty/parent-less-free scene. Used to attach newly-spawned primitives under the scene's root (ADR-0021).</summary>
    public Guid?FindFirstRootNodeId()=>Nodes.FirstOrDefault(N=>N.ParentId is null)?.Id;

    /// <summary>True if any existing node already has this exact name - used to pick a non-colliding "Sphere_1"-style name for a newly-spawned primitive (ADR-0021).</summary>
    public bool ContainsNodeNamed(string Name)=>Nodes.Any(N=>N.Name==Name);

    /// <summary>
    /// Resolves every node's world-space transform by walking its parent chain (child's
    /// <see cref="SceneFileNode.LocalTransform"/> composed with the parent's already-resolved
    /// world transform). Guards against cycles independently of <see cref="BuildTree"/> (via a
    /// "currently resolving" set, so callers who skip <see cref="BuildTree"/> can't hang on a
    /// corrupt file) since this is on the rendering path, not just the Hierarchy display path.
    /// </summary>
    public IReadOnlyDictionary<Guid,Matrix4x4>ComputeWorldTransforms(){
        Dictionary<Guid,SceneFileNode>ById=[];
        foreach(SceneFileNode Node in Nodes){
            ById[Node.Id]=Node;
        }

        Dictionary<Guid,Matrix4x4>Resolved=[];
        HashSet<Guid>Visiting=[];
        foreach(SceneFileNode Node in Nodes){
            ResolveWorldTransform(Node,ById,Resolved,Visiting);
        }

        return Resolved;
    }

    private static Matrix4x4 ResolveWorldTransform(SceneFileNode Node,Dictionary<Guid,SceneFileNode>ById,Dictionary<Guid,Matrix4x4>Resolved,HashSet<Guid>Visiting){
        if(Resolved.TryGetValue(Node.Id,out Matrix4x4 Cached)){
            return Cached;
        }

        if(!Visiting.Add(Node.Id)){
            throw new InvalidOperationException($"シーンノードの親子関係に循環参照が検出されました(ノードId: {Node.Id})。");
        }

        Matrix4x4 World=Node.ParentId is Guid ParentId&&ById.TryGetValue(ParentId,out SceneFileNode?Parent)
            ?Node.LocalTransform*ResolveWorldTransform(Parent,ById,Resolved,Visiting)
            :Node.LocalTransform;

        Visiting.Remove(Node.Id);
        Resolved[Node.Id]=World;
        return World;
    }

    /// <summary>
    /// Resolves <see cref="SceneFileNode.ParentId"/> into a tree; a node with a null/missing parent
    /// is a root (mirrors <c>CSharpGameEngine.Scene.SceneNodeTree</c>'s rule exactly, ADR-0020).
    /// Throws if any node is unreachable from every root (a cycle, or a reference that never
    /// resolves to a root) rather than silently dropping it from the Hierarchy view.
    /// </summary>
    public IReadOnlyList<SceneFileNodeTreeItem>BuildTree(){
        Dictionary<Guid,SceneFileNode>ById=[];
        foreach(SceneFileNode Node in Nodes){
            ById[Node.Id]=Node;
        }

        Dictionary<Guid,List<SceneFileNode>>ChildrenByParent=[];
        foreach(SceneFileNode Node in Nodes){
            if(Node.ParentId is Guid ParentId&&ById.ContainsKey(ParentId)){
                if(!ChildrenByParent.TryGetValue(ParentId,out List<SceneFileNode>?Siblings)){
                    Siblings=[];
                    ChildrenByParent[ParentId]=Siblings;
                }

                Siblings.Add(Node);
            }
        }

        HashSet<Guid>Visited=[];
        List<SceneFileNodeTreeItem>Roots=[];
        foreach(SceneFileNode Node in Nodes){
            var IsRoot=Node.ParentId is null||!ById.ContainsKey(Node.ParentId.Value);
            if(IsRoot){
                Roots.Add(BuildItem(Node,ChildrenByParent,Visited));
            }
        }

        if(Visited.Count!=Nodes.Count){
            throw new InvalidOperationException(
                $"シーンノードの親子関係に循環参照または解決不能な参照があります({Nodes.Count-Visited.Count}件のノードがツリーに到達できません)。");
        }

        return Roots;
    }

    private static SceneFileNodeTreeItem BuildItem(SceneFileNode Node,Dictionary<Guid,List<SceneFileNode>>ChildrenByParent,HashSet<Guid>Visited){
        Visited.Add(Node.Id);

        List<SceneFileNodeTreeItem>Children=[];
        if(ChildrenByParent.TryGetValue(Node.Id,out List<SceneFileNode>?ChildNodes)){
            foreach(SceneFileNode Child in ChildNodes){
                Children.Add(BuildItem(Child,ChildrenByParent,Visited));
            }
        }

        return new SceneFileNodeTreeItem { Node=Node,Children=Children };
    }

    private static Vector3 ToVector3(float[]V)=>new(V[0],V[1],V[2]);

    private static Quaternion ToQuaternion(float[]V)=>new(V[0],V[1],V[2],V[3]);

    private sealed class JsonDocumentDto {
        public int SchemaVersion { get; set;}

        public List<JsonNodeDto>Nodes { get; set;}=[];
    }

    private sealed class JsonNodeDto {
        public string Id { get; set;}="";

        public string Name { get; set;}="";

        public string?ParentId { get; set;}

        public JsonTransformDto Transform { get; set;}=new();

        public string?MeshAssetPath { get; set;}
    }

    private sealed class JsonTransformDto {
        public float[]Position { get; set;}=[0f,0f,0f];

        public float[]Rotation { get; set;}=[0f,0f,0f,1f];

        public float[]Scale { get; set;}=[1f,1f,1f];
    }
}
