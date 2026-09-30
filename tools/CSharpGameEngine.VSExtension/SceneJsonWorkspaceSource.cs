using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// The Scene Designer's view of the <c>*.scene.json</c> additional documents of a Roslyn project
/// (ADR-0070). A JSON scene enters the analysis compilation as its equivalent C# class
/// (<see cref="SceneJsonDesignerSource"/>) whose syntax tree carries the JSON path, so every consumer
/// of <see cref="CSharpGameEngine.SceneAnalysis.SceneModel"/> reads it exactly like a C# scene. The
/// text is the live one: an unsaved edit in the JSON buffer is what the designer shows.
/// </summary>
internal static class SceneJsonWorkspaceSource {
    internal static IEnumerable<TextDocument>JsonScenes(Project Project)=>
        Project.AdditionalDocuments.Where(static Document=>SceneJsonCommitPlanner.IsJsonScene(Document.FilePath));
    /// <summary>The <c>namespace.className</c> a document declares; <c>null</c> when it cannot be read.</summary>
    internal static string?TryReadClassFullyQualifiedName(string JsonText){
        try {
            var Document=SceneJsonReader.Parse(JsonText);
            if(Document.Get("className") is not { Kind:SceneJsonKind.String,Text:{ Length:>0 }ClassName }){ return null; }
            return Document.Get("namespace") is { Kind:SceneJsonKind.String,Text:{ Length:>0 }Namespace }
                ?Namespace+"."+ClassName
                :ClassName;
        } catch(SceneJsonException){
            return null;
        }
    }
    /// <summary>The JSON scene of <paramref name="Project"/> that declares <paramref name="ClassFullyQualifiedName"/>.</summary>
    internal static async Task<TextDocument?>FindAsync(Project Project,string ClassFullyQualifiedName,CancellationToken CancellationToken){
        foreach(var Document in JsonScenes(Project)){
            var Text=await Document.GetTextAsync(CancellationToken).ConfigureAwait(false);
            if(string.Equals(TryReadClassFullyQualifiedName(Text.ToString()),ClassFullyQualifiedName,StringComparison.Ordinal)){
                return Document;
            }
        }
        return null;
    }
    /// <summary>
    /// Adds the designer tree of <paramref name="Document"/> to <paramref name="Compilation"/>.
    /// <c>null</c> when the document is not a valid scene (its problems are reported by the build and
    /// the JSON schema, not here).
    /// </summary>
    internal static async Task<(Compilation Compilation,SyntaxTree Tree)?>AddDesignerTreeAsync(
        Project Project,Compilation Compilation,TextDocument Document,CancellationToken CancellationToken){
        var Text=await Document.GetTextAsync(CancellationToken).ConfigureAwait(false);
        var Scene=SceneJsonSceneAnalyzer.Analyze(Text.ToString(),Compilation);
        if(SceneJsonDesignerSource.Build(Scene) is not { }Source){ return null; }
        var Options=Project.ParseOptions as CSharpParseOptions??CSharpParseOptions.Default;
        var Tree=CSharpSyntaxTree.ParseText(Source,Options,Document.FilePath??string.Empty,cancellationToken:CancellationToken);
        return (Compilation.AddSyntaxTrees(Tree),Tree);
    }
}
