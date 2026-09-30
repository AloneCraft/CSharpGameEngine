using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
namespace CSharpGameEngine.CodeAnalysis.Tests;
/// <summary>An additional file (such as a scene JSON) held in memory for generator and analyzer tests.</summary>
internal sealed class InMemoryAdditionalText(string Path,string Text):AdditionalText {
    private readonly SourceText Content=SourceText.From(Text);
    public override string Path { get; }=Path;
    public override SourceText GetText(CancellationToken CancellationToken=default)=>this.Content;
}
