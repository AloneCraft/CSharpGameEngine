namespace CSharpGameEngine.Compiler.Hlsl;

public readonly record struct HlslDefine(string Name,string?Value){
    public override string ToString()=> this.Value is null? this.Name: $"{this.Name}={this.Value}";
}
