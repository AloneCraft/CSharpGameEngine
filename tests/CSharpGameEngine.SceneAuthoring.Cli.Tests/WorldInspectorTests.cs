using CSharpGameEngine.Entities;

namespace CSharpGameEngine.SceneAuthoring.Cli.Tests;

public class WorldInspectorTests
{
    [Fact]
    public void Inspect_ReadsPositionRotationScale_ByConventionalTypeName()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position, Rotation, Scale>();
        Entity entity = world.CreateEntity(archetype);
        world.GetRef<Position>(entity) = new Position { X = 1f, Y = 2f, Z = 3f };
        world.GetRef<Rotation>(entity) = new Rotation { X = 0f, Y = 0f, Z = 0f, W = 1f };
        world.GetRef<Scale>(entity) = new Scale { X = 2f, Y = 2f, Z = 2f };

        IReadOnlyList<ScenePreviewNode> nodes = WorldInspector.Inspect(world);

        ScenePreviewNode node = Assert.Single(nodes);
        Assert.Equal(entity.Id, node.EntityId);
        Assert.Equal([1f, 2f, 3f], node.Position);
        Assert.Equal([0f, 0f, 0f, 1f], node.Rotation);
        Assert.Equal([2f, 2f, 2f], node.Scale);
        Assert.Contains("Position", node.ComponentTypeNames);
        Assert.Contains("Rotation", node.ComponentTypeNames);
        Assert.Contains("Scale", node.ComponentTypeNames);
    }

    [Fact]
    public void Inspect_ReadsParent_ByExactType_NotByName()
    {
        World world = new();
        Entity root = world.CreateEntity(world.CreateArchetype<SceneNodeTag>());
        Entity child = world.CreateEntity(world.CreateArchetype<Position, Parent>());
        world.GetRef<Parent>(child) = new Parent { Value = root };

        IReadOnlyList<ScenePreviewNode> nodes = WorldInspector.Inspect(world);

        ScenePreviewNode rootNode = Assert.Single(nodes, n => n.EntityId == root.Id);
        ScenePreviewNode childNode = Assert.Single(nodes, n => n.EntityId == child.Id);
        Assert.Null(rootNode.ParentEntityId);
        Assert.Equal(root.Id, childNode.ParentEntityId);
    }

    [Fact]
    public void Inspect_OrdersNodesByEntityIdAscending()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<SceneNodeTag>();
        Entity a = world.CreateEntity(archetype);
        Entity b = world.CreateEntity(archetype);
        Entity c = world.CreateEntity(archetype);

        IReadOnlyList<ScenePreviewNode> nodes = WorldInspector.Inspect(world);

        Assert.Equal([a.Id, b.Id, c.Id], [.. nodes.Select(n => n.EntityId)]);
    }

    [Fact]
    public void Inspect_UnrecognizedComponentType_IsRecordedByNameOnly_TransformStaysDefault()
    {
        World world = new();
        Entity entity = world.CreateEntity(world.CreateArchetype<Health>());
        world.GetRef<Health>(entity) = new Health { Value = 100f };

        IReadOnlyList<ScenePreviewNode> nodes = WorldInspector.Inspect(world);

        ScenePreviewNode node = Assert.Single(nodes);
        Assert.Contains("Health", node.ComponentTypeNames);
        Assert.Equal([0f, 0f, 0f], node.Position);
        Assert.Equal([0f, 0f, 0f, 1f], node.Rotation);
        Assert.Equal([1f, 1f, 1f], node.Scale);
    }

    [Fact]
    public void Inspect_MultipleArchetypes_AreAllIncluded()
    {
        World world = new();
        Entity a = world.CreateEntity(world.CreateArchetype<Position>());
        Entity b = world.CreateEntity(world.CreateArchetype<Position, Health>());

        IReadOnlyList<ScenePreviewNode> nodes = WorldInspector.Inspect(world);

        Assert.Equal(2, nodes.Count);
        Assert.Contains(nodes, n => n.EntityId == a.Id);
        Assert.Contains(nodes, n => n.EntityId == b.Id);
    }
}
