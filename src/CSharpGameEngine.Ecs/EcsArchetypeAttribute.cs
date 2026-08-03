namespace CSharpGameEngine.Ecs;

/// <summary>
/// この属性を付けたpartial structを、EcsArchetypeGenerator(CSharpGameEngine.CodeAnalysis)が
/// アーキタイプとして認識し、正規化・SoAストレージ・Worldアクセス用コードを生成する。
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class EcsArchetypeAttribute : Attribute
{
}
