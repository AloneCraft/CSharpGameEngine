using System;

namespace CSharpGameEngine.Entities;

/// <summary>
/// Opts an ECS system into the isolated Editor Preview World.
/// Systems without this attribute are never run by the editor host.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited=false, AllowMultiple=false)]
public sealed class ExecuteInEditorAttribute:Attribute {
    public bool Realtime { get; set; }
}
