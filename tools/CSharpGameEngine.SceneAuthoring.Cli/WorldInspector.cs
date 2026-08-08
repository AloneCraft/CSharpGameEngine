using System.Reflection;
using CSharpGameEngine.Entities;

namespace CSharpGameEngine.SceneAuthoring.Cli;

/// <summary>
/// Walks a real, already-executed <see cref="World"/> generically - with no compile-time knowledge
/// of the target project's own component types - via the internal Archetype-walking surface
/// (<c>InternalsVisibleTo</c> grant on <c>CSharpGameEngine.Entities.csproj</c>, ADR-0031).
/// <c>ComponentColumn&lt;T&gt;.Items</c> is read as a plain public array field via
/// <see cref="FieldInfo"/> rather than through <c>Archetype.GetSpan&lt;T&gt;</c>, since a
/// <c>Span&lt;T&gt;</c> cannot be boxed/returned through <see cref="MethodInfo.Invoke"/>.
///
/// Recognizes <c>Position</c>/<c>Rotation</c>/<c>Scale</c> by simple type name (the shared convention
/// established in ADR-0030 - every current consumer follows it) to populate transform data; anything
/// else is recorded only by name in <see cref="ScenePreviewNode.ComponentTypeNames"/>.
/// <see cref="Parent"/> is a type this project references directly, so it is matched precisely
/// (not by name) and unboxed straight to its real <see cref="Entity"/> value.
/// </summary>
internal static class WorldInspector
{
    internal static IReadOnlyList<ScenePreviewNode> Inspect(World world)
    {
        Dictionary<int, RawNode> nodesByEntityId = [];

        for (int archetypeIndex = 0; archetypeIndex < world.ArchetypeCount; archetypeIndex++)
        {
            Archetype archetype = world.GetArchetype(archetypeIndex);
            int[] componentTypeIds = archetype.ComponentTypeIds;
            ReadOnlySpan<Entity> entities = archetype.Entities;

            foreach (Entity entity in entities)
            {
                nodesByEntityId[entity.Id] = new RawNode(entity.Id);
            }

            for (int column = 0; column < componentTypeIds.Length; column++)
            {
                Type clrType = ComponentTypeRegistry.GetClrType(componentTypeIds[column]);
                Array items = ReadItemsArray(archetype.GetColumn(column));

                for (int row = 0; row < entities.Length; row++)
                {
                    object value = items.GetValue(row)!;
                    ApplyComponent(nodesByEntityId[entities[row].Id], clrType, value);
                }
            }
        }

        return [.. nodesByEntityId.Values.OrderBy(n => n.EntityId).Select(n => n.ToScenePreviewNode())];
    }

    private static Array ReadItemsArray(IComponentColumn columnStorage)
    {
        FieldInfo itemsField = columnStorage.GetType().GetField("Items", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"'{columnStorage.GetType().Name}' に 'Items' フィールドが見つかりませんでした。");

        return (Array)itemsField.GetValue(columnStorage)!;
    }

    private static void ApplyComponent(RawNode node, Type clrType, object value)
    {
        node.ComponentTypeNames.Add(clrType.Name);

        if (clrType == typeof(Parent))
        {
            node.ParentEntityId = ((Parent)value).Value.Id;
            return;
        }

        switch (clrType.Name)
        {
            case "Position":
                node.Position = ReadVector3Like(clrType, value);
                break;
            case "Rotation":
                node.Rotation = ReadQuaternionLike(clrType, value);
                break;
            case "Scale":
                node.Scale = ReadVector3Like(clrType, value);
                break;
        }
    }

    private static float[] ReadVector3Like(Type clrType, object value) =>
        [ReadFloatField(clrType, value, "X"), ReadFloatField(clrType, value, "Y"), ReadFloatField(clrType, value, "Z")];

    private static float[] ReadQuaternionLike(Type clrType, object value) =>
        [ReadFloatField(clrType, value, "X"), ReadFloatField(clrType, value, "Y"), ReadFloatField(clrType, value, "Z"), ReadFloatField(clrType, value, "W")];

    private static float ReadFloatField(Type clrType, object value, string fieldName)
    {
        FieldInfo? field = clrType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        return field is not null && field.GetValue(value) is float floatValue ? floatValue : 0f;
    }

    private sealed class RawNode(int entityId)
    {
        public int EntityId { get; } = entityId;

        public int? ParentEntityId { get; set; }

        public float[] Position { get; set; } = [0f, 0f, 0f];

        public float[] Rotation { get; set; } = [0f, 0f, 0f, 1f];

        public float[] Scale { get; set; } = [1f, 1f, 1f];

        public List<string> ComponentTypeNames { get; } = [];

        public ScenePreviewNode ToScenePreviewNode() => new()
        {
            EntityId = this.EntityId,
            ParentEntityId = this.ParentEntityId,
            Position = this.Position,
            Rotation = this.Rotation,
            Scale = this.Scale,
            ComponentTypeNames = [.. this.ComponentTypeNames],
        };
    }
}
