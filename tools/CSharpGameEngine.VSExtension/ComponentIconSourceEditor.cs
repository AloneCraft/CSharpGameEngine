using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.VSExtension;

/// <summary>コンポーネントアイコンのメタデータ編集に対する、Roslyn の純粋な結果。</summary>
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
/// クリックした <c>struct</c> 宣言に属する属性だけを書き換えます。名前から型を解決しません。partial component では、アイコン未設定時にグリフの宣言範囲が正しい所有者を示すため重要です。
/// </summary>
internal static class ComponentIconSourceEditor {
    internal static ComponentIconSourceEditResult Apply(
        SyntaxNode Root,int DeclarationSpanStart,string?SelectedProjectRelativePath){
        var NormalizedPath=string.Empty;
        if(SelectedProjectRelativePath is not null
            &&!ProjectIconPath.TryNormalize(SelectedProjectRelativePath,out NormalizedPath)){
            return ComponentIconSourceEditResult.Failure(VsixText.Get("ComponentIcon_ProjectRelativeImageRequired"));
        }

        var Declaration=Root.DescendantNodes()
                           .OfType<StructDeclarationSyntax>()
                           .FirstOrDefault(Candidate=>Candidate.SpanStart==DeclarationSpanStart);
        if(Declaration is null){
            return ComponentIconSourceEditResult.Failure(VsixText.Get("ComponentIcon_ComponentNotFound"));
        }

        var Existing=Declaration.AttributeLists
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
        var Replacement=Existing.WithArgumentList(CreateArguments(NormalizedPath));
        return ComponentIconSourceEditResult.Success(Root.ReplaceNode(Existing,Replacement));
    }

    private static ComponentIconSourceEditResult Add(
        SyntaxNode Root,StructDeclarationSyntax Declaration,string NormalizedPath){
        var LeadingText=Declaration.GetLeadingTrivia().ToFullString();
        var NewLine=LeadingText.IndexOf("\r\n",StringComparison.Ordinal)>=0?"\r\n":"\n";
        var Indentation=GetTrailingIndentation(LeadingText);
        var Attribute=SyntaxFactory.Attribute(
                                              SyntaxFactory.ParseName(SceneComponentIconMetadata.AttributeFullyQualifiedName),
                                              CreateArguments(NormalizedPath));
        var List=SyntaxFactory.AttributeList(
                                             SyntaxFactory.SingletonSeparatedList(Attribute))
                              .WithLeadingTrivia(Declaration.GetLeadingTrivia())
                              .WithTrailingTrivia(
                                                  SyntaxFactory.EndOfLine(NewLine),
                                                  SyntaxFactory.Whitespace(Indentation));
        var Replacement=Declaration
                       .WithLeadingTrivia(default(SyntaxTriviaList))
                       .WithAttributeLists(Declaration.AttributeLists.Insert(0,List));
        return ComponentIconSourceEditResult.Success(Root.ReplaceNode(Declaration,Replacement));
    }

    private static ComponentIconSourceEditResult Remove(
        SyntaxNode Root,StructDeclarationSyntax Declaration,AttributeSyntax Existing){
        var OwningList=Declaration.AttributeLists
                                 .FirstOrDefault(List=>List.Attributes.Any(Attribute=>ReferenceEquals(Attribute,Existing)));
        if(OwningList is null){
            return ComponentIconSourceEditResult.Failure(VsixText.Get("ComponentIcon_RemoveAttributeFailed"));
        }

        if(OwningList.Attributes.Count>1){
            var Replacement=OwningList.WithAttributes(OwningList.Attributes.Remove(Existing));
            return ComponentIconSourceEditResult.Success(Root.ReplaceNode(OwningList,Replacement));
        }

        var DeclarationWithoutAttribute=Declaration
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
        var Name=Attribute.Name.ToString().Replace("global::",string.Empty);
        return string.Equals(Name,"SceneComponentIcon",StringComparison.Ordinal)
            ||string.Equals(Name,"SceneComponentIconAttribute",StringComparison.Ordinal)
            ||string.Equals(Name,"CSharpGameEngine.Entities.SceneComponentIcon",StringComparison.Ordinal)
            ||string.Equals(Name,"CSharpGameEngine.Entities.SceneComponentIconAttribute",StringComparison.Ordinal);
    }

    private static string GetTrailingIndentation(string LeadingTrivia){
        var LastLineBreak=Math.Max(LeadingTrivia.LastIndexOf('\n'),LeadingTrivia.LastIndexOf('\r'));
        var Candidate=LastLineBreak<0?LeadingTrivia:LeadingTrivia.Substring(LastLineBreak+1);
        return Candidate.All(char.IsWhiteSpace)?Candidate:string.Empty;
    }
}
