using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>Component palette/Inspector から要求する C# 定義移動の対象。</summary>
internal sealed record DefinitionNavigationRequest(
    string ComponentTypeFullyQualifiedName,
    string? MemberName = null);

/// <summary>Roslyn symbol を Visual Studio のソース位置へ変換して開く共通処理。</summary>
internal static class DefinitionNavigationService {
    internal static async Task<bool>NavigateAsync(
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
        SourceText Text;
        Microsoft.CodeAnalysis.Text.TextSpan DeclarationSpan;
        if(SymbolLocation is { SourceTree: { } SourceTree }){
            FilePath=SourceTree.FilePath;
            Text=await SourceTree.GetTextAsync(CancellationToken).ConfigureAwait(false);
            DeclarationSpan=await ResolveDeclarationSpanAsync(Target,SymbolLocation,CancellationToken).ConfigureAwait(false);
        } else if(ReferencedComponentDefinitionLocator.TryFind(
            Compilation,ComponentType,MemberName,CancellationToken,out ReferencedComponentDefinitionLocation ReferencedLocation)){
            FilePath=ReferencedLocation.FilePath;
            Text=ReferencedLocation.Text;
            DeclarationSpan=ReferencedLocation.Span;
        } else {
            return false;
        }

        LinePositionSpan LineSpan=Text.Lines.GetLinePositionSpan(DeclarationSpan);
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
        if(Package.GetGlobalService(typeof(SDTE))is not DTE Dte
            ||Package.GetGlobalService(typeof(SVsTextManager))is not IVsTextManager TextManager){
            return false;
        }

        try {
            Dte.ItemOperations.OpenFile(FilePath);
            if(TextManager.GetActiveView(1,null,out IVsTextView?View)!=VSConstants.S_OK
                ||View is null){
                return false;
            }

            int Line=LineSpan.Start.Line;
            int Column=LineSpan.Start.Character;
            int EndLine=LineSpan.End.Line;
            int EndColumn=LineSpan.End.Character;
            if(!ErrorHandler.Succeeded(View.SetSelection(Line,Column,EndLine,EndColumn))){
                return false;
            }

            View.EnsureSpanVisible(new Microsoft.VisualStudio.TextManager.Interop.TextSpan {
                iStartLine=Line,
                iStartIndex=Column,
                iEndLine=EndLine,
                iEndIndex=EndColumn,
            });
            return true;
        } catch(COMException) {
            return false;
        } catch(InvalidOperationException) {
            return false;
        }
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