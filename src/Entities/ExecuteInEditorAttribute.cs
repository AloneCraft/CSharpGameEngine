using System;

namespace CSharpGameEngine.Entities;

/// <summary>
/// ECSシステムを分離されたEditor Preview Worldで実行できるようにする属性。
/// この属性がないシステムはエディターホストでは実行されない。
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited=false, AllowMultiple=false)]
public sealed class ExecuteInEditorAttribute:Attribute {
    public bool Realtime { get; set; }
}
