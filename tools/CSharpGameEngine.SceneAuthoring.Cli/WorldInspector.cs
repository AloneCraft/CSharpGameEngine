using System.Reflection;

namespace CSharpGameEngine.SceneAuthoring.Cli;

/// <summary>
/// Turns the target's generated <c>WorldSnapshot.Capture</c> output into <see cref="ScenePreviewNode"/>s,
/// with no compile-time knowledge of the target project's component types (ADR-0033).
///
/// <para>
/// The pre-ADR-0033 version walked a live <c>World</c> through the internal Archetype/ComponentColumn
/// surface (an <c>InternalsVisibleTo</c> grant, ADR-0031). Under the closed-archetype design there is
/// no runtime-enumerable archetype store to walk - each Archetype is its own compile-time-generated
/// struct type - so the generator instead emits a <c>WorldSnapshot.Capture</c> that boxes every live
/// entity's components into <c>object[]</c>. This class only reflects over that plain data, which is
/// also why the <c>InternalsVisibleTo</c> grant is gone.
/// </para>
///
/// <para>
/// Recognizes <c>Position</c>/<c>Rotation</c>/<c>Scale</c> by simple type name (the shared convention
/// established in ADR-0030 - every current consumer follows it) to populate transform data; anything
/// else is recorded only by name in <see cref="ScenePreviewNode.ComponentTypeNames"/>. The parent link
/// is read from the snapshot entry itself (the generator knows which archetypes carry
/// <c>Parent</c> and resolves it at generation time), not by matching a type here.
/// </para>
/// </summary>
internal static class WorldInspector
{
    internal static IReadOnlyList<ScenePreviewNode> Inspect(Array snapshot)
    {
        List<ScenePreviewNode> nodes = [];

        foreach (object? boxedEntry in snapshot)
        {
            if (boxedEntry is null)
            {
                continue;
            }

            Type entryType = boxedEntry.GetType();
            RawNode node = new(ReadInt(entryType, boxedEntry, "EntityId"));

            if (ReadBool(entryType, boxedEntry, "HasParent"))
            {
                node.ParentEntityId = ReadInt(entryType, boxedEntry, "ParentEntityId");
            }

            if (ReadField(entryType, boxedEntry, "ComponentValues") is object?[] componentValues)
            {
                foreach (object? componentValue in componentValues)
                {
                    if (componentValue is not null)
                    {
                        ApplyComponent(node, componentValue.GetType(), componentValue);
                    }
                }
            }

            nodes.Add(node.ToScenePreviewNode());
        }

        return [.. nodes.OrderBy(n => n.EntityId)];
    }

    private static void ApplyComponent(RawNode node, Type clrType, object value)
    {
        node.ComponentTypeNames.Add(clrType.Name);

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

    private static object? ReadField(Type type, object instance, string fieldName) =>
        type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);

    private static int ReadInt(Type type, object instance, string fieldName) =>
        ReadField(type, instance, fieldName) is int value ? value : 0;

    private static bool ReadBool(Type type, object instance, string fieldName) =>
        ReadField(type, instance, fieldName) is true;

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
