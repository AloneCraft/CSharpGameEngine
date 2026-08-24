using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class VsixJapaneseLiteralGuardTests {
    [Fact]
    public void CSharpDisplayCode_HasNoJapaneseStringLiteralsOutsideResources(){
        string DirectoryPath=Path.Combine(VsixBuildIdentity.RepoRoot,"tools","CSharpGameEngine.VSExtension");
        List<string>Violations=[];
        foreach(string FilePath in Directory.EnumerateFiles(DirectoryPath,"*.cs",SearchOption.TopDirectoryOnly)){
            SyntaxTree Tree=CSharpSyntaxTree.ParseText(File.ReadAllText(FilePath));
            foreach(SyntaxToken Token in Tree.GetRoot().DescendantTokens()){
                if(Token.Kind() is not (SyntaxKind.StringLiteralToken or SyntaxKind.InterpolatedStringTextToken)
                    ||!Token.ValueText.Any(IsJapaneseCharacter)){
                    continue;
                }
                FileLinePositionSpan Span=Token.GetLocation().GetLineSpan();
                Violations.Add($"{Path.GetFileName(FilePath)}:{Span.StartLinePosition.Line+1}: {Token.Text}");
            }
        }
        Assert.True(Violations.Count==0,
            "Japanese VSIX display text belongs in VsixStrings.ja-JP.resx:"+Environment.NewLine
            +string.Join(Environment.NewLine,Violations));
    }
    private static bool IsJapaneseCharacter(char Value)=>
        Value is >= '\u3040' and <= '\u30ff'
        ||Value is >= '\u3400' and <= '\u9fff'
        ||Value is >= '\uf900' and <= '\ufaff';
}
