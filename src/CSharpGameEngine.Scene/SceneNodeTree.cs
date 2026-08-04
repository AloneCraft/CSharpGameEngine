using CSharpGameEngine.Core;

namespace CSharpGameEngine.Scene;

/// <summary>One <see cref="SceneNode"/> plus its resolved children, produced by <see cref="SceneNodeTree.Build"/>.</summary>
public sealed class SceneNodeTreeItem
{
    public required SceneNode Node { get; init; }

    public required IReadOnlyList<SceneNodeTreeItem> Children { get; init; }
}

/// <summary>
/// Resolves a flat <see cref="SceneNode"/> list's <see cref="SceneNode.ParentId"/> references into a
/// tree, for display in a Hierarchy-style view. A node is a root when <see cref="SceneNode.ParentId"/>
/// is null or does not match any node's <see cref="SceneNode.Id"/> in the same list (defensive: a
/// scene file referencing a missing parent still displays instead of silently disappearing).
/// </summary>
public sealed class SceneNodeTree
{
    public required IReadOnlyList<SceneNodeTreeItem> Roots { get; init; }

    /// <summary>
    /// Every node has at most one parent, so recursion only ever proceeds from a validated root
    /// through the "children of" mapping built below - a node that participates in a pure cycle (no
    /// member has a null/missing parent) can never be reached as a root and is therefore never
    /// recursed into, so infinite recursion cannot occur by construction. Such nodes simply never
    /// end up in the visited set, which is what the reachability check below detects and throws for.
    /// </summary>
    public static SceneNodeTree Build(IReadOnlyList<SceneNode> nodes)
    {
        Dictionary<StableId, SceneNode> byId = [];
        foreach (SceneNode node in nodes)
        {
            byId[node.Id] = node;
        }

        Dictionary<StableId, List<SceneNode>> childrenByParent = [];
        foreach (SceneNode node in nodes)
        {
            if (node.ParentId is StableId parentId && byId.ContainsKey(parentId))
            {
                if (!childrenByParent.TryGetValue(parentId, out List<SceneNode>? siblings))
                {
                    siblings = [];
                    childrenByParent[parentId] = siblings;
                }

                siblings.Add(node);
            }
        }

        HashSet<StableId> visited = [];
        List<SceneNodeTreeItem> roots = [];
        foreach (SceneNode node in nodes)
        {
            bool isRoot = node.ParentId is null || !byId.ContainsKey(node.ParentId.Value);
            if (isRoot)
            {
                roots.Add(BuildItem(node, childrenByParent, visited));
            }
        }

        if (visited.Count != nodes.Count)
        {
            throw new InvalidOperationException(
                $"シーンノードの親子関係に循環参照または解決不能な参照があります({nodes.Count - visited.Count}件のノードがツリーに到達できません)。");
        }

        return new SceneNodeTree { Roots = roots };
    }

    private static SceneNodeTreeItem BuildItem(SceneNode node, Dictionary<StableId, List<SceneNode>> childrenByParent, HashSet<StableId> visited)
    {
        visited.Add(node.Id);

        List<SceneNodeTreeItem> children = [];
        if (childrenByParent.TryGetValue(node.Id, out List<SceneNode>? childNodes))
        {
            foreach (SceneNode child in childNodes)
            {
                children.Add(BuildItem(child, childrenByParent, visited));
            }
        }

        return new SceneNodeTreeItem { Node = node, Children = children };
    }
}
