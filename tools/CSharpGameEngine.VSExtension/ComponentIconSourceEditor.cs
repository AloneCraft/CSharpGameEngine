using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.VSExtension;

/// <summary>Pure Roslyn result for a component-icon metadata edit.</summary>
internal sealed class ComponentIconSourceEditResult {
    private ComponentIconSourceEditResult(bool Succeeded,SyntaxNode?Root,string?ErrorMessage){
        this.Succeeded=Succeeded;
        this.Root=Root;
        this.ErrorMessage=ErrorMessage;
    }

    internal bool Succeeded { get; }

    internal SyntaxNode? Root { get; }

    internal string? ErrorMessage { get; }

    internal static ComponentIconSourceEditResult Success(SyntaxNode Root)=>new(true,Root,null);

    internal static ComponentIconSourceEditResult Failure(string ErrorMessage)=>new(false,null,ErrorMessage);
}

/// <summary>
/// Rewrites only the attribute that belongs to the clicked <c>struct</c> declaration.  It does not
/// resolve a type by name, which is important for partial components: the glyph's declaration span
/// remains the authoritative owner when an icon has not been assigned yet.
/// </summary>
internal static class ComponentIconSourceEditor {
    internal static ComponentIconSourceEditResult Apply(
        SyntaxNode Root,int DeclarationSpanStart,string?SelectedProjectRelativePath){
        string NormalizedPath=string.Empty;
        if(SelectedProjectRelativePath is not null
            &&!ProjectIconPath.TryNormalize(SelectedProjectRelativePath,out NormalizedPath)){
            return ComponentIconSourceEditResult.Failure("コンポーネントアイコンはプロジェクト相対の SVG または PNG である必要があります。");
        }

        StructDeclarationSyntax?Declaration=Root.DescendantNodes()
            .OfType<StructDeclarationSyntax>()
            .FirstOrDefault(Candidate=>Candidate.SpanStart==DeclarationSpanStart);
        if(Declaration is null){
            return ComponentIconSourceEditResult.Failure("アイコンを編集する Component struct が現在のソースに見つかりません。");
        }

        AttributeSyntax?Existing=Declaration.AttributeLists
            .SelectMany(static List=>List.Attributes)
            .FirstOrDefault(IsSceneComponentIconAttribute);
        if(SelectedProjectRelativePath is null){
            return Existing is null
                ?ComponentIconSourceEditResult.Success(Root)
                :Remove(Root,Declaration,Existing);
        }

        return Existing is null
            ?Add(Root,Declaration,NormalizedPath)
            :Replace(Root,Existing,NormalizedPath);
    }

    private static ComponentIconSourceEditResult Replace(
        SyntaxNode Root,AttributeSyntax Existing,string NormalizedPath){
        AttributeSyntax Replacement=Existing.WithArgumentList(CreateArguments(NormalizedPath));
        return ComponentIconSourceEditResult.Success(Root.ReplaceNode(Existing,Replacement));
    }

    private static ComponentIconSourceEditResult Add(
        SyntaxNode Root,StructDeclarationSyntax Declaration,string NormalizedPath){
        string LeadingText=Declaration.GetLeadingTrivia().ToFullString();
        string NewLine=LeadingText.IndexOf("\r\n",StringComparison.Ordinal)>=0?"\r\n":"\n";
        string Indentation=GetTrailingIndentation(LeadingText);
        AttributeSyntax Attribute=SyntaxFactory.Attribute(
            SyntaxFactory.ParseName(SceneComponentIconMetadata.AttributeFullyQualifiedName),
            CreateArguments(NormalizedPath));
        AttributeListSyntax List=SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(Attribute))
            .WithLeadingTrivia(Declaration.GetLeadingTrivia())
            .WithTrailingTrivia(
                SyntaxFactory.EndOfLine(NewLine),
                SyntaxFactory.Whitespace(Indentation));
        StructDeclarationSyntax Replacement=Declaration
            .WithLeadingTrivia(default(SyntaxTriviaList))
            .WithAttributeLists(Declaration.AttributeLists.Insert(0,List));
        return ComponentIconSourceEditResult.Success(Root.ReplaceNode(Declaration,Replacement));
    }

    private static ComponentIconSourceEditResult Remove(
        SyntaxNode Root,StructDeclarationSyntax Declaration,AttributeSyntax Existing){
        AttributeListSyntax?OwningList=Declaration.AttributeLists
            .FirstOrDefault(List=>List.Attributes.Any(Attribute=>ReferenceEquals(Attribute,Existing)));
        if(OwningList is null){
            return ComponentIconSourceEditResult.Failure("コンポーネントアイコン属性を現在の宣言から削除できませんでした。");
        }

        if(OwningList.Attributes.Count>1){
            AttributeListSyntax Replacement=OwningList.WithAttributes(OwningList.Attributes.Remove(Existing));
            return ComponentIconSourceEditResult.Success(Root.ReplaceNode(OwningList,Replacement));
        }

        StructDeclarationSyntax DeclarationWithoutAttribute=Declaration
            .WithAttributeLists(Declaration.AttributeLists.Remove(OwningList))
            .WithLeadingTrivia(OwningList.GetLeadingTrivia());
        return ComponentIconSourceEditResult.Success(Root.ReplaceNode(Declaration,DeclarationWithoutAttribute));
    }

    private static AttributeArgumentListSyntax CreateArguments(string NormalizedPath)=>
        SyntaxFactory.AttributeArgumentList(
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.AttributeArgument(
                    SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,SyntaxFactory.Literal(NormalizedPath)))));

    private static bool IsSceneComponentIconAttribute(AttributeSyntax Attribute){
        string Name=Attribute.Name.ToString().Replace("global::",string.Empty);
        return string.Equals(Name,"SceneComponentIcon",StringComparison.Ordinal)
            ||string.Equals(Name,"SceneComponentIconAttribute",StringComparison.Ordinal)
            ||string.Equals(Name,"CSharpGameEngine.Entities.SceneComponentIcon",StringComparison.Ordinal)
            ||string.Equals(Name,"CSharpGameEngine.Entities.SceneComponentIconAttribute",StringComparison.Ordinal);
    }

    private static string GetTrailingIndentation(string LeadingTrivia){
        int LastLineBreak=Math.Max(LeadingTrivia.LastIndexOf('\n'),LeadingTrivia.LastIndexOf('\r'));
        string Candidate=LastLineBreak<0?LeadingTrivia:LeadingTrivia.Substring(LastLineBreak+1);
        return Candidate.All(char.IsWhiteSpace)?Candidate:string.Empty;
    }
}
