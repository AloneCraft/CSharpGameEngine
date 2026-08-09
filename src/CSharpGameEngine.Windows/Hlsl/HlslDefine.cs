namespace CSharpGameEngine.Compiler.Hlsl;

public readonly record struct HlslDefine(string Name, string? Value)
{
    public override string ToString() => Value is null ? Name : $"{Name}={Value}";
}
