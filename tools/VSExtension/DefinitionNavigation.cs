using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.VSExtension;

/// <summary>Component palette/Inspector から要求する C# 定義移動の対象。</summary>
internal sealed record DefinitionNavigationRequest(
    string ComponentTypeFullyQualifiedName,
    string? MemberName = null);

/// <summary>Visual Studio でマークする Component 定義のソース範囲。</summary>
internal sealed record DefinitionNavigationTarget(string FilePath,TextSpan Span);

/// <summary>Roslyn symbol を Component 定義のソース範囲へ変換する共通処理。</summary>
internal static class DefinitionNavigationService {
    internal static async Task<DefinitionNavigationTarget?>ResolveAsync(
        INamedTypeSymbol ComponentType,Compilation Compilation,string?MemberName,
        CancellationToken CancellationToken){
        ISymbol Target=ResolveTarget(ComponentType,MemberName);
        Location?SymbolLocation=Target.Locations
            .Where(static Candidate=>Candidate.IsInSource
                &&Candidate.SourceTree is not null
                &&!string.IsNullOrWhiteSpace(Candidate.SourceTree.FilePath))
            .OrderBy(Candidate=>Candidate.SourceTree!.FilePath,StringComparer.OrdinalIgnoreCase)
            .ThenBy(Candidate=>Candidate.SourceSpan.Start)
            .FirstOrDefault();

        string FilePath;
        TextSpan DeclarationSpan;
        if(SymbolLocation is { SourceTree: { } SourceTree }){
            FilePath=SourceTree.FilePath;
            DeclarationSpan=await ResolveDeclarationSpanAsync(Target,SymbolLocation,CancellationToken).ConfigureAwait(false);
        } else if(ReferencedComponentDefinitionLocator.TryFind(
            Compilation,ComponentType,MemberName,CancellationToken,out ReferencedComponentDefinitionLocation ReferencedLocation)){
            FilePath=ReferencedLocation.FilePath;
            DeclarationSpan=ReferencedLocation.Span;
        } else {
            return null;
        }

        return new DefinitionNavigationTarget(FilePath,DeclarationSpan);
    }
    private static ISymbol ResolveTarget(INamedTypeSymbol ComponentType,string?MemberName){
        ISymbol Target=ComponentType;
        if(MemberName is not { Length:>0 } RequiredMemberName){
            return Target;
        }

        INamedTypeSymbol CurrentType=ComponentType;
        foreach(string Part in RequiredMemberName.Split('.')){
            ISymbol?Found=CurrentType.GetMembers(Part).FirstOrDefault();
            if(Found is null){
                break;
            }

            Target=Found;
            ITypeSymbol?NextType=Found switch {
                IPropertySymbol Property=>Property.Type,
                IFieldSymbol Field=>Field.Type,
                _=>null,
            };
            if(NextType is not INamedTypeSymbol NamedType){
                break;
            }

            CurrentType=NamedType;
        }

        return Target;
    }

    private static async Task<Microsoft.CodeAnalysis.Text.TextSpan>ResolveDeclarationSpanAsync(
        ISymbol Target,Location Location,CancellationToken CancellationToken){
        if(Location.SourceTree is null){
            return Location.SourceSpan;
        }

        SyntaxNode Root=await Location.SourceTree.GetRootAsync(CancellationToken).ConfigureAwait(false);
        SyntaxNode Node=Root.FindNode(Location.SourceSpan,getInnermostNodeForTie:true);
        SyntaxNode?Declaration=Target is INamedTypeSymbol
            ?Node.AncestorsAndSelf().FirstOrDefault(static Candidate=>
                Candidate is BaseTypeDeclarationSyntax||Candidate is DelegateDeclarationSyntax)
            :Node.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault();
        return Declaration?.Span??Location.SourceSpan;
    }
}