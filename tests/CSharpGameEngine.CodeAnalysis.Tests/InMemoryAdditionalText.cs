using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>M0049: テストから<c>*.scene.json</c>を<c>Context.AdditionalTextsProvider</c>へ渡すための最小実装。</summary>
internal sealed class InMemoryAdditionalText(string Path,string Text):AdditionalText {
    public override string Path { get; }=Path;

    public override SourceText GetText(CancellationToken CancellationToken=default)=>
        SourceText.From(Text,Encoding.UTF8);
}
