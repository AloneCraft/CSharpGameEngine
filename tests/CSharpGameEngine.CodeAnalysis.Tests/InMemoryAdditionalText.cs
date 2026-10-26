using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
namespace CSharpGameEngine.CodeAnalysis.Tests;
/// <summary>ジェネレーターとアナライザーテスト用にメモリ内で保持する追加ファイル（シーンJSONなど）。</summary>
internal sealed class InMemoryAdditionalText(string Path,string Text):AdditionalText {
    private readonly SourceText Content=SourceText.From(Text);
    public override string Path { get; }=Path;
    public override SourceText GetText(CancellationToken CancellationToken=default)=>this.Content;
}
