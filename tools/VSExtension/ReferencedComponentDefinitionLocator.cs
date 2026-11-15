using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.VSExtension;

/// <summary>Visual Studio が metadata としてロードした参照先コンポーネントのソース範囲。</summary>
internal sealed class ReferencedComponentDefinitionLocation {
    internal ReferencedComponentDefinitionLocation(string FilePath,SourceText Text,TextSpan Span){
        this.FilePath=FilePath;
        this.Text=Text;
        this.Span=Span;
    }

    internal string FilePath { get; }

    internal SourceText Text { get; }

    internal TextSpan Span { get; }
}

/// <summary>
/// コンパクトなユーザーゲームのソリューションで、プロジェクト参照先が Roslyn の source project ではなく metadata reference としてロードされた場合に、そのコンポーネントのソースを検索します。
/// </summary>
internal static class ReferencedComponentDefinitionLocator {
    internal static bool TryFind(
        Compilation Compilation,INamedTypeSymbol ComponentType,string?MemberName,
        CancellationToken CancellationToken,out ReferencedComponentDefinitionLocation Location){
        Location=null!;
        string?ProjectDirectory=TryFindProjectDirectory(Compilation,ComponentType);
        return ProjectDirectory is not null&&TryFindInProjectDirectory(
            ProjectDirectory,ComponentType.ContainingNamespace.ToDisplayString(),ComponentType.Name,
            MemberName,CancellationToken,out Location);
    }

    internal static bool TryFindInProjectDirectory(
        string ProjectDirectory,string NamespaceName,string TypeName,string?MemberName,
        CancellationToken CancellationToken,out ReferencedComponentDefinitionLocation Location){
        foreach(string FilePath in EnumerateSourceFiles(ProjectDirectory)){
            CancellationToken.ThrowIfCancellationRequested();
            string Source;
            try {
                Source=File.ReadAllText(FilePath);
            } catch(IOException) {
                continue;
            } catch(UnauthorizedAccessException) {
                continue;
            }

            SyntaxTree Tree=CSharpSyntaxTree.ParseText(Source,path:FilePath,cancellationToken:CancellationToken);
            SyntaxNode Root=Tree.GetRoot(CancellationToken);
            TypeDeclarationSyntax?Declaration=Root.DescendantNodes().OfType<TypeDeclarationSyntax>()
                .FirstOrDefault(Candidate=>Candidate.Identifier.ValueText==TypeName
                    &&string.Equals(GetNamespaceName(Candidate),NamespaceName,StringComparison.Ordinal));
            if(Declaration is null){
                continue;
            }

            SyntaxNode?Target=MemberName is { Length:>0 }
                ?FindMemberDeclaration(Declaration,MemberName)
                :Declaration;
            if(Target is null){
                continue;
            }

            Location=new ReferencedComponentDefinitionLocation(
                FilePath,Tree.GetText(CancellationToken),Target.Span);
            return true;
        }

        Location=null!;
        return false;
    }

    private static string?TryFindProjectDirectory(Compilation Compilation,INamedTypeSymbol ComponentType){
        IAssemblySymbol Assembly=ComponentType.ContainingAssembly;
        MetadataReference?Reference=Compilation.References.FirstOrDefault(Candidate=>
            SymbolEqualityComparer.Default.Equals(Compilation.GetAssemblyOrModuleSymbol(Candidate),Assembly));
        string?AssemblyPath=(Reference as PortableExecutableReference)?.FilePath??Reference?.Display;
        if(string.IsNullOrWhiteSpace(AssemblyPath)||!Path.IsPathRooted(AssemblyPath)){
            return null;
        }

        DirectoryInfo?Directory=new DirectoryInfo(Path.GetDirectoryName(AssemblyPath)!);
        for(var Depth=0;Directory is not null&&Depth<8;Depth++,Directory=Directory.Parent){
            try {
                if(Directory.EnumerateFiles("*.csproj",SearchOption.TopDirectoryOnly).Any(Candidate=>
                    string.Equals(Path.GetFileNameWithoutExtension(Candidate.Name),Assembly.Identity.Name,StringComparison.OrdinalIgnoreCase))){
                    return Directory.FullName;
                }
            } catch(IOException) {
                return null;
            } catch(UnauthorizedAccessException) {
                return null;
            }
        }

        return null;
    }

    private static IEnumerable<string>EnumerateSourceFiles(string ProjectDirectory){
        IEnumerable<string>Files;
        try {
            Files=Directory.EnumerateFiles(ProjectDirectory,"*.cs",SearchOption.AllDirectories);
        } catch(IOException) {
            return [];
        } catch(UnauthorizedAccessException) {
            return [];
        }

        return Files.Where(static FilePath=>!IsBuildOutputPath(FilePath));
    }

    private static bool IsBuildOutputPath(string FilePath){
        string[]Segments=FilePath.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        return Segments.Any(static Segment=>string.Equals(Segment,"bin",StringComparison.OrdinalIgnoreCase)
            ||string.Equals(Segment,"obj",StringComparison.OrdinalIgnoreCase));
    }

    private static string GetNamespaceName(TypeDeclarationSyntax Declaration)=>string.Join(".",
        Declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse()
            .Select(static Namespace=>Namespace.Name.ToString()));

    private static MemberDeclarationSyntax?FindMemberDeclaration(
        TypeDeclarationSyntax Declaration,string MemberPath){
        string MemberName=MemberPath.Split('.').Last();
        return Declaration.Members.FirstOrDefault(Candidate=>Candidate switch {
            FieldDeclarationSyntax Field=>Field.Declaration.Variables.Any(Variable=>
                string.Equals(Variable.Identifier.ValueText,MemberName,StringComparison.Ordinal)),
            EventFieldDeclarationSyntax Event=>Event.Declaration.Variables.Any(Variable=>
                string.Equals(Variable.Identifier.ValueText,MemberName,StringComparison.Ordinal)),
            PropertyDeclarationSyntax Property=>string.Equals(Property.Identifier.ValueText,MemberName,StringComparison.Ordinal),
            EventDeclarationSyntax Event=>string.Equals(Event.Identifier.ValueText,MemberName,StringComparison.Ordinal),
            MethodDeclarationSyntax Method=>string.Equals(Method.Identifier.ValueText,MemberName,StringComparison.Ordinal),
            _=>false,
        });
    }
}