namespace CSharpGameEngine.Generation;

/// <summary>
/// この属性を付けたpartial structを、GenerateEntityGenerator(CSharpGameEngine.CodeAnalysis)が
/// Entityとして認識し、<see cref="Kind"/>で選択したバックエンド(AoSまたはECS SoA)だけを生成する。
/// </summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class GenerateEntityAttribute : Attribute
{
    public GenerateEntityAttribute(EntityStorageKind kind)
    {
        this.Kind = kind;
    }

    public EntityStorageKind Kind { get; }
}
