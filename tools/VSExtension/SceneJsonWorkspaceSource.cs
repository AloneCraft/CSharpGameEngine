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
/// Roslyn project のシーン JSON 追加ドキュメント（<c>Scenes\**\*.json</c>）に対する Scene Designer の view（ADR-0070）。JSON シーンは等価な C# class（<see cref="SceneJsonDesignerSource"/>）として解析 compilation に入り、その syntax tree が JSON パスを持つため、<see cref="CSharpGameEngine.SceneAnalysis.SceneModel"/> の consumer は C# シーンと同様に読み取ります。ライブテキストが使われるため、JSON バッファーの未保存編集も designer に表示されます。
/// </summary>
internal static class SceneJsonWorkspaceSource {
    internal static IEnumerable<TextDocument>JsonScenes(Project Project)=>
        Project.AdditionalDocuments.Where(Document=>SceneJsonCommitPlanner.IsJsonScene(Document.FilePath,Project.FilePath));
    /// <summary>ドキュメントが宣言する <c>namespace.className</c>。読み取れない場合は <c>null</c>。</summary>
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
    /// <summary><paramref name="Project"/> 内で <paramref name="ClassFullyQualifiedName"/> を宣言する JSON シーン。</summary>
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
    /// <paramref name="Document"/> の designer tree を <paramref name="Compilation"/> に追加します。
    /// 有効なシーンではない場合は <c>null</c> です。問題はここではなく、build と JSON schema が報告します。
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
