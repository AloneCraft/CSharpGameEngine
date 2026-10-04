using System;
using System.Collections.Generic;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Scene Designer が pin する対象 Scene の安定キー。ファイルパスやクラス単純名ではなく Roslyn の ProjectId と
/// クラス完全修飾名の組で識別し、同名クラスが複数プロジェクトにあっても別 Scene として衝突しない。
/// </summary>
internal readonly struct SceneKey : IEquatable<SceneKey> {
    internal SceneKey(string ProjectId,string ClassFullyQualifiedName){
        this.ProjectId=ProjectId??throw new ArgumentNullException(nameof(ProjectId));
        this.ClassFullyQualifiedName=ClassFullyQualifiedName??throw new ArgumentNullException(nameof(ClassFullyQualifiedName));
    }

    internal string ProjectId { get; }

    internal string ClassFullyQualifiedName { get; }

    public bool Equals(SceneKey Other)=>
        string.Equals(this.ProjectId,Other.ProjectId,StringComparison.Ordinal)
        &&string.Equals(this.ClassFullyQualifiedName,Other.ClassFullyQualifiedName,StringComparison.Ordinal);

    public override bool Equals(object?Object)=>Object is SceneKey Other&&this.Equals(Other);

    public override int GetHashCode()=>
        (StringComparer.Ordinal.GetHashCode(this.ProjectId)*397)^StringComparer.Ordinal.GetHashCode(this.ClassFullyQualifiedName);

    public override string ToString()=>this.ProjectId+":"+this.ClassFullyQualifiedName;
}

/// <summary>
/// C# の編集を跨いで Designer session 内で Node を同定する ID。<see cref="NodePath"/> は値の編集でしか
/// 不変でない(構造編集やリネームでずれる)ため、セッション寿命で安定な ID を別に割り当てる。
/// </summary>
internal readonly struct EditorNodeId : IEquatable<EditorNodeId> {
    internal EditorNodeId(long Value)=>this.Value=Value;

    internal long Value { get; }

    public bool Equals(EditorNodeId Other)=>this.Value==Other.Value;

    public override bool Equals(object?Object)=>Object is EditorNodeId Other&&this.Equals(Other);

    public override int GetHashCode()=>this.Value.GetHashCode();

    public override string ToString()=>this.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Designer セッションへ投影された Node(セッション ID・親 ID・解析結果の組)。</summary>
internal sealed class SceneProjectionNode {
    internal SceneProjectionNode(EditorNodeId Id,EditorNodeId?ParentId,SceneNode Node){
        this.Id=Id;
        this.ParentId=ParentId;
        this.Node=Node;
    }

    internal EditorNodeId Id { get; }

    internal EditorNodeId?ParentId { get; }

    internal SceneNode Node { get; }

    internal NodePath Path=>this.Node.Path;
}

/// <summary>
/// SceneModel から導出した Editor 側の投影。Editor 用の ECS World は持たず(ADR-0040)、Hierarchy 表示や
/// Renderer binding はこの投影の ID 表を介して解析結果を参照する。
/// </summary>
internal sealed class SceneProjection {
    private readonly Dictionary<EditorNodeId,SceneProjectionNode>NodesById;
    private readonly Dictionary<NodePath,EditorNodeId>IdsByPath;

    internal SceneProjection(SceneKey Key,IEnumerable<SceneProjectionNode>Nodes){
        this.Key=Key;
        this.NodesById=Nodes.ToDictionary(static Node=>Node.Id);
        this.IdsByPath=this.NodesById.Values.ToDictionary(static Node=>Node.Path,static Node=>Node.Id);
    }

    internal SceneKey Key { get; }

    internal IReadOnlyCollection<SceneProjectionNode>Nodes=>this.NodesById.Values;

    internal SceneProjectionNode?Root=>this.NodesById.Values.SingleOrDefault(static Node=>Node.ParentId is null);

    internal bool TryGetNode(EditorNodeId Id,out SceneProjectionNode Node)=>this.NodesById.TryGetValue(Id,out Node!);

    internal bool TryGetId(NodePath Path,out EditorNodeId Id)=>this.IdsByPath.TryGetValue(Path,out Id);
}

/// <summary>
/// C# 再解析のたびに新旧の投影を照合し、セッション Node ID を引き継ぐ。第1パスは Name + Component
/// composition の完全一致、第2パスは Component 構成のみの一致(リネーム検出)で、候補がちょうど1件の
/// ときだけ引き継ぐ。区別不能な兄弟の ID は推測しない。
/// </summary>
internal sealed class SceneProjectionReconciler {
    private long NextId;

    internal SceneProjectionReconciler(long FirstId=1)=>this.NextId=FirstId;

    internal SceneProjection Reconcile(SceneKey Key,SceneModel Model,SceneProjection?Previous){
        if(Model.Root is not { } Root){
            return new SceneProjection(Key,[]);
        }

        Dictionary<EditorNodeId,SceneProjectionNode>OldNodes=Previous is null
            ?[]
            :Previous.Nodes.ToDictionary(static Node=>Node.Id);
        List<SceneProjectionNode>Result=[];
        // Each node's old children, looked up once: scanning every old node for each node was quadratic (a 4000-node
        // scene spent about 0.3 s per reconcile, twice per commit). The groups keep the old nodes' order.
        ILookup<EditorNodeId,SceneProjectionNode>OldChildren=OldNodes.Values
            .Where(static Candidate=>Candidate.ParentId is not null)
            .ToLookup(static Candidate=>Candidate.ParentId!.Value);
        this.ReconcileNode(Root,ParentId:null,OldNode:Previous?.Root,OldChildren,Result);
        return new SceneProjection(Key,Result);
    }

    private void ReconcileNode(SceneNode Node,EditorNodeId?ParentId,SceneProjectionNode?OldNode,
        ILookup<EditorNodeId,SceneProjectionNode>OldChildren,List<SceneProjectionNode>Result){
        EditorNodeId Id=OldNode?.Id??this.AllocateId();
        Result.Add(new SceneProjectionNode(Id,ParentId,Node));

        Dictionary<string,List<SceneProjectionNode>>OldByExactKey=[];
        if(OldNode is not null){
            foreach(SceneProjectionNode Candidate in OldChildren[OldNode.Id]){
                Add(OldByExactKey,ExactKey(Candidate.Node),Candidate);
            }
        }

        HashSet<EditorNodeId>Matched=[];
        Dictionary<SceneNode,SceneProjectionNode?>Matches=[];

        // 先に全兄弟の完全一致を取る。途中でfallbackすると、後ろにある完全一致の兄弟をrename候補と
        // 誤認してしまうため、二段階に分ける。
        foreach(SceneNode Child in Node.Children){
            SceneProjectionNode?MatchingOld=null;
            string Key=ExactKey(Child);
            if(OldByExactKey.TryGetValue(Key,out List<SceneProjectionNode>?Candidates)&&Candidates.Count==1&&!Matched.Contains(Candidates[0].Id)){
                MatchingOld=Candidates[0];
                Matched.Add(MatchingOld.Id);
            }

            Matches.Add(Child,MatchingOld);
        }

        // Nameだけが変わった場合でも、同じ親の未対応Component構成が一意なら安全にIDを維持できる。
        // 完全同一兄弟はCandidatesが複数になるので、ここでは決して勝手に選ばない。
        foreach(SceneNode Child in Node.Children){
            if(Matches[Child] is not null){
                continue;
            }

            SceneProjectionNode[]Candidates=OldNode is null
                ?[]
                :OldChildren[OldNode.Id]
                    .Where(Candidate=>!Matched.Contains(Candidate.Id)
                        &&string.Equals(ComponentKey(Candidate.Node),ComponentKey(Child),StringComparison.Ordinal))
                    .ToArray();
            if(Candidates.Length==1){
                Matches[Child]=Candidates[0];
                Matched.Add(Candidates[0].Id);
            }
        }

        foreach(SceneNode Child in Node.Children){
            this.ReconcileNode(Child,Id,Matches[Child],OldChildren,Result);
        }
    }

    private EditorNodeId AllocateId()=>new(this.NextId++);

    private static void Add(Dictionary<string,List<SceneProjectionNode>>Dictionary,string Key,SceneProjectionNode Node){
        if(!Dictionary.TryGetValue(Key,out List<SceneProjectionNode>?Nodes)){
            Nodes=[];
            Dictionary.Add(Key,Nodes);
        }

        Nodes.Add(Node);
    }

    private static string ExactKey(SceneNode Node)=>(Node.Name??"\0")+"\u001f"+ComponentKey(Node);

    private static string ComponentKey(SceneNode Node)=>
        string.Join("\u001e",Node.Components.Select(static Component=>Component.TypeFullyQualifiedName));
}

internal enum SceneDiffKind {
    NodeAdded,
    NodeRemoved,
    NodeReparented,
    NodeReordered,
    ComponentAdded,
    ComponentRemoved,
    ComponentChanged,
    MemberChanged,
}

internal sealed class SceneDiffEntry {
    internal SceneDiffEntry(SceneDiffKind Kind,EditorNodeId NodeId,string?ComponentType=null,string?MemberName=null){
        this.Kind=Kind;
        this.NodeId=NodeId;
        this.ComponentType=ComponentType;
        this.MemberName=MemberName;
    }

    internal SceneDiffKind Kind { get; }

    internal EditorNodeId NodeId { get; }

    internal string?ComponentType { get; }

    internal string?MemberName { get; }
}

/// <summary>投影間の意味差分。Renderer の増分更新用で、Transform 変更だけの時に mesh 再取得を避けるための情報。</summary>
internal sealed class SceneDiff {
    internal SceneDiff(IReadOnlyList<SceneDiffEntry>Entries)=>this.Entries=Entries;

    internal IReadOnlyList<SceneDiffEntry>Entries { get; }

    internal bool IsEmpty=>this.Entries.Count==0;
}

internal static class SceneDiffEngine {
    internal static SceneDiff Compare(SceneProjection?Before,SceneProjection After){
        if(Before is null){
            return new SceneDiff(After.Nodes.Select(static Node=>new SceneDiffEntry(SceneDiffKind.NodeAdded,Node.Id)).ToList());
        }

        List<SceneDiffEntry>Entries=[];
        Dictionary<EditorNodeId,SceneProjectionNode>Old=Before.Nodes.ToDictionary(static Node=>Node.Id);
        Dictionary<EditorNodeId,SceneProjectionNode>New=After.Nodes.ToDictionary(static Node=>Node.Id);

        foreach(SceneProjectionNode Node in Old.Values){
            if(!New.ContainsKey(Node.Id)){
                Entries.Add(new SceneDiffEntry(SceneDiffKind.NodeRemoved,Node.Id));
            }
        }

        foreach(SceneProjectionNode Node in New.Values){
            if(!Old.TryGetValue(Node.Id,out SceneProjectionNode?OldNode)){
                Entries.Add(new SceneDiffEntry(SceneDiffKind.NodeAdded,Node.Id));
                continue;
            }

            if(!Nullable.Equals(OldNode.ParentId,Node.ParentId)){
                Entries.Add(new SceneDiffEntry(SceneDiffKind.NodeReparented,Node.Id));
            } else if(!OldNode.Path.Equals(Node.Path)){
                Entries.Add(new SceneDiffEntry(SceneDiffKind.NodeReordered,Node.Id));
            }

            CompareComponents(OldNode.Node,Node.Node,Node.Id,Entries);
        }

        return new SceneDiff(Entries);
    }

    private static void CompareComponents(SceneNode Before,SceneNode After,EditorNodeId NodeId,List<SceneDiffEntry>Entries){
        Dictionary<string,SceneComponent>Old=Before.Components.ToDictionary(static Component=>Component.TypeFullyQualifiedName);
        Dictionary<string,SceneComponent>New=After.Components.ToDictionary(static Component=>Component.TypeFullyQualifiedName);

        foreach(string Type in Old.Keys){
            if(!New.ContainsKey(Type)){
                Entries.Add(new SceneDiffEntry(SceneDiffKind.ComponentRemoved,NodeId,Type));
            }
        }

        foreach(KeyValuePair<string,SceneComponent>Pair in New){
            if(!Old.TryGetValue(Pair.Key,out SceneComponent?OldComponent)){
                Entries.Add(new SceneDiffEntry(SceneDiffKind.ComponentAdded,NodeId,Pair.Key));
                continue;
            }

            if(!string.Equals(OldComponent.EmitExpressionText,Pair.Value.EmitExpressionText,StringComparison.Ordinal)){
                Entries.Add(new SceneDiffEntry(SceneDiffKind.ComponentChanged,NodeId,Pair.Key));
            }

            Dictionary<string,SceneComponentMember>OldMembers=OldComponent.Members.ToDictionary(static Member=>Member.MemberName);
            foreach(SceneComponentMember Member in Pair.Value.Members){
                if(!OldMembers.TryGetValue(Member.MemberName,out SceneComponentMember?OldMember)
                    ||!string.Equals(OldMember.Value.EmitExpressionText,Member.Value.EmitExpressionText,StringComparison.Ordinal)){
                    Entries.Add(new SceneDiffEntry(SceneDiffKind.MemberChanged,NodeId,Pair.Key,Member.MemberName));
                }
            }
        }
    }
}
