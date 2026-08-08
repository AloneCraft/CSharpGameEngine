namespace CSharpGameEngine.Entities;

/// <summary>
/// ユーザー向けのArchetypeハンドル。<see cref="World.CreateArchetype{T1}"/>系メソッドが返し、
/// <see cref="World.CreateEntity"/>/<see cref="EntityCommandBuffer.CreateEntity"/>に渡す。
/// 内部の<see cref="Archetype"/>(SoAストレージそのもの)を直接ユーザーへ見せず、
/// 「このComponent集合でEntityを作る」という意図だけを表現する薄いラッパー。
/// </summary>
public readonly struct EntityArchetype
{
    internal readonly Archetype Archetype;

    internal EntityArchetype(Archetype archetype)
    {
        this.Archetype = archetype;
    }
}
