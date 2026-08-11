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
internal static class WorldInspector {
    internal static IReadOnlyList<ScenePreviewNode>Inspect(Array Snapshot){
        List<ScenePreviewNode>Nodes=[];

        foreach(var BoxedEntry in Snapshot){
            if(BoxedEntry is null){
                continue;
            }

            Type EntryType=BoxedEntry.GetType();
            RawNode Node=new(ReadInt(EntryType,BoxedEntry,"EntityId"));

            if(ReadBool(EntryType,BoxedEntry,"HasParent")){
                Node.ParentEntityId=ReadInt(EntryType,BoxedEntry,"ParentEntityId");
            }

            if(ReadField(EntryType,BoxedEntry,"ComponentValues")is object?[]ComponentValues){
                foreach(var ComponentValue in ComponentValues){
                    if(ComponentValue is not null){
                        ApplyComponent(Node,ComponentValue.GetType(),ComponentValue);
                    }
                }
            }

            Nodes.Add(Node.ToScenePreviewNode());
        }

        return[.. Nodes.OrderBy(N=>N.EntityId)];
    }

    private static void ApplyComponent(RawNode Node,Type ClrType,object Value){
        Node.ComponentTypeNames.Add(ClrType.Name);

        switch(ClrType.Name){
            case "Position":
                Node.Position=ReadVector3Like(ClrType,Value);
                break;
            case "Rotation":
                Node.Rotation=ReadQuaternionLike(ClrType,Value);
                break;
            case "Scale":
                Node.Scale=ReadVector3Like(ClrType,Value);
                break;
        }
    }

    private static object?ReadField(Type type,object Instance,string FieldName)=>
        type.GetField(FieldName,BindingFlags.Public|BindingFlags.Instance)?.GetValue(Instance);

    private static int ReadInt(Type type,object Instance,string FieldName)=>
        ReadField(type,Instance,FieldName)is int Value?Value:0;

    private static bool ReadBool(Type type,object Instance,string FieldName)=>
        ReadField(type,Instance,FieldName)is true;

    private static float[]ReadVector3Like(Type ClrType,object Value)=>
        [ReadFloatField(ClrType,Value,"X"),ReadFloatField(ClrType,Value,"Y"),ReadFloatField(ClrType,Value,"Z")];

    private static float[]ReadQuaternionLike(Type ClrType,object Value)=>
        [ReadFloatField(ClrType,Value,"X"),ReadFloatField(ClrType,Value,"Y"),ReadFloatField(ClrType,Value,"Z"),ReadFloatField(ClrType,Value,"W")];

    private static float ReadFloatField(Type ClrType,object Value,string FieldName){
        FieldInfo?Field=ClrType.GetField(FieldName,BindingFlags.Public|BindingFlags.Instance);
        return Field is not null&&Field.GetValue(Value)is float FloatValue?FloatValue:0f;
    }

    private sealed class RawNode(int EntityId){
        public int EntityId { get;}=EntityId;

        public int?ParentEntityId { get; set;}

        public float[]Position { get; set;}=[0f,0f,0f];

        public float[]Rotation { get; set;}=[0f,0f,0f,1f];

        public float[]Scale { get; set;}=[1f,1f,1f];

        public List<string>ComponentTypeNames { get;}=[];

        public ScenePreviewNode ToScenePreviewNode()=>new(){
            EntityId=this.EntityId,
            ParentEntityId=this.ParentEntityId,
            Position=this.Position,
            Rotation=this.Rotation,
            Scale=this.Scale,
            ComponentTypeNames=[.. this.ComponentTypeNames],
        };
    }
}
