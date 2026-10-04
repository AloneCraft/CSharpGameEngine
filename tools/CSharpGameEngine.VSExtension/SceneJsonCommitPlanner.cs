using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// Commits one gesture to a scene JSON document (ADR-0070). The edit is applied to the scene
/// model with the same <see cref="SceneModelEditApplier"/> as C# scenes, written back with
/// <see cref="SceneJsonConverter"/> (whose canonical layout keeps untouched values byte-identical, so
/// the change is the edited lines only) and re-read to verify it. Node ids survive through the
/// projection: each predicted node keeps the JSON id of the node it was before the edit.
/// </summary>
internal static class SceneJsonCommitPlanner {
    /// <summary>
    /// Whether a file is a scene: a <c>*.json</c> under its project's <c>Scenes</c> folder
    /// (<see cref="SceneFiles"/>). With only a path, the project is the nearest folder holding a
    /// <c>*.csproj</c>, as the build that includes <c>Scenes\**\*.json</c> sees it.
    /// </summary>
    internal static bool IsJsonScene(string?FilePath)=>
        SceneFiles.IsScene(FilePath,FindProjectDirectory(FilePath));

    /// <summary>The same rule with the project known (a Roslyn project's file path).</summary>
    internal static bool IsJsonScene(string?FilePath,string?ProjectFilePath)=>
        SceneFiles.IsScene(FilePath,string.IsNullOrEmpty(ProjectFilePath)?null:System.IO.Path.GetDirectoryName(ProjectFilePath));

    /// <summary>
    /// Whether a scene model was read from a JSON document, so the JSON writer can commit to it. A
    /// model's file is a scene already; this only tells the JSON form from any other.
    /// </summary>
    internal static bool IsJsonDocument(string?FilePath)=>
        FilePath is not null&&FilePath.EndsWith(SceneFiles.Extension,StringComparison.OrdinalIgnoreCase);

    private static string?FindProjectDirectory(string?FilePath){
        if(string.IsNullOrEmpty(FilePath)){ return null; }
        try {
            for(var Directory=System.IO.Path.GetDirectoryName(FilePath);!string.IsNullOrEmpty(Directory);Directory=System.IO.Path.GetDirectoryName(Directory)){
                if(System.IO.Directory.Exists(Directory)&&System.IO.Directory.EnumerateFiles(Directory!,"*.csproj").Any()){
                    return Directory;
                }
            }
        } catch(Exception Error)when(Error is ArgumentException or System.IO.IOException or UnauthorizedAccessException or NotSupportedException){
        }
        return null;
    }
    /// <param name="Context">
    /// <see cref="SceneSourceCommitContext.Text"/> is the JSON text, <see cref="SceneSourceCommitContext.Root"/>
    /// the root of its designer C# tree and <see cref="SceneSourceCommitContext.Compilation"/> the analysis
    /// compilation that contains that tree.
    /// </param>
    internal static SceneSourceCommitPlan Plan(
        SceneSourceCommitContext Context,SceneDocumentSnapshot Latest,
        SceneEditCommitRequest Request,SceneProjectionReconciler Reconciler,
        CancellationToken CancellationToken){
        if(Context.Model.HasErrors||Context.Model.Root is null||Latest.HasBlockingErrors||Latest.Projection is null){
            return SceneSourceCommitPlan.Failure(VsixText.Get("SessionSaveAnalysisErrors"));
        }
        var Merged=SceneEditConflictMerger.Merge(Request,Latest);
        if(!Merged.Succeeded){
            return SceneSourceCommitPlan.Failure(Merged.ErrorMessage??VsixText.Get("SourceEditOperationFailed"));
        }
        if(!SceneModelEditApplier.TryApply(
            Context.Model,Latest.Projection,Reconciler,Request.SceneKey,Merged.Operations,
            out SceneModel Predicted,out SceneProjection PredictedProjection)){
            return SceneSourceCommitPlan.Failure(VsixText.Get("SourceEditOperationFailed"));
        }
        var BaseCompilation=Context.Compilation.RemoveSyntaxTrees(Context.Root.SyntaxTree);
        var OldText=Context.Text.ToString();
        var Before=SceneJsonSceneAnalyzer.Analyze(OldText,BaseCompilation);
        var OldIds=Before.NodeIdsByPath();
        var JsonIdByEditorId=new Dictionary<EditorNodeId,string>();
        foreach(var Pair in OldIds){
            if(Latest.Projection.TryGetId(Pair.Key,out var EditorId)){ JsonIdByEditorId[EditorId]=Pair.Value; }
        }
        string?IdFor(NodePath Path){
            if(!PredictedProjection.TryGetId(Path,out var EditorId)){ return Guid.NewGuid().ToString("D"); }
            if(JsonIdByEditorId.TryGetValue(EditorId,out var Id)){ return Id; }
            // Siblings with the same name and component types get a new editor id on every analysis (the reconciler does
            // not guess between them), so their ids were replaced on every edit. Such a node keeps the id of the old node
            // at its place when that node looks the same and the prediction no longer has its editor id.
            if(OldIds.TryGetValue(Path,out var OldId)&&Latest.Projection.TryGetId(Path,out var OldEditorId)
                &&!PredictedProjection.TryGetNode(OldEditorId,out _)&&Latest.Projection.TryGetNode(OldEditorId,out var OldNode)
                &&PredictedProjection.TryGetNode(EditorId,out var NewNode)&&LooksTheSame(OldNode.Node,NewNode.Node)){
                return OldId;
            }
            return Guid.NewGuid().ToString("D");
        }
        var Converted=SceneJsonConverter.Convert(Predicted,SchemaReferenceOf(OldText),IdFor);
        if(Converted.Text is null){
            return SceneSourceCommitPlan.Failure(string.Join(Environment.NewLine,Converted.Problems));
        }
        // M0095: the canonical text is merged into the author's, so only the changed values change.
        var NewText=SceneJsonTextMerge.Merge(OldText,MatchLineEndings(Converted.Text,OldText));
        if(!TryAnalyze(NewText,Context.Model.FilePath,BaseCompilation,Request.SceneKey.ClassFullyQualifiedName,CancellationToken,
            out var Canonical,out var FinalRoot,out _,out var Error)){
            return SceneSourceCommitPlan.Failure(Error);
        }
        var CanonicalProjection=Reconciler.Reconcile(Request.SceneKey,Canonical!,PredictedProjection);
        var Changes=SceneTextChangePlanner.PlanMinimal(Context.Text,NewText);
        var WriteStep=new SceneTextWriteStep(Context.Text,Changes,SceneUndoTransactionNaming.Format(Request.UndoDescription));
        return SceneSourceCommitPlan.Success(Request.DocumentVersion!=Latest.DocumentVersion,Canonical!,CanonicalProjection,FinalRoot!,WriteStep);
    }
    /// <summary>
    /// Reads a JSON scene into the designer's view: its equivalent C# class is parsed with the JSON path
    /// and analyzed in <paramref name="BaseCompilation"/>. <paramref name="Error"/> names the first
    /// document problem when the text is not a valid scene.
    /// </summary>
    internal static bool TryAnalyze(
        string JsonText,string JsonPath,Compilation BaseCompilation,string ClassFullyQualifiedName,CancellationToken CancellationToken,
        out SceneModel?Model,out SyntaxNode?Root,out Compilation?AnalysisCompilation,out string Error){
        Model=null;
        Root=null;
        AnalysisCompilation=null;
        var Scene=SceneJsonSceneAnalyzer.Analyze(JsonText,BaseCompilation);
        if(SceneJsonDesignerSource.Build(Scene) is not { }Source){
            Error=Scene.Problems.Length>0?Scene.Problems[0].Descriptor.Id+": "+string.Format(Scene.Problems[0].Descriptor.MessageFormat.ToString(),Scene.Problems[0].Arguments):VsixText.Get("SessionSaveAnalysisErrors");
            return false;
        }
        var Options=BaseCompilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions??CSharpParseOptions.Default;
        var Tree=CSharpSyntaxTree.ParseText(Source,Options,JsonPath,cancellationToken:CancellationToken);
        var Compilation=BaseCompilation.AddSyntaxTrees(Tree);
        AnalysisCompilation=Compilation;
        var Type=Compilation.GetTypeByMetadataName(ClassFullyQualifiedName);
        if(Type is null||!SceneTypePredicates.ImplementsSceneInterface(Type)){
            Error=VsixText.Get("SessionSaveSourceUnavailable");
            return false;
        }
        Model=SceneDeclarationAnalyzer.Analyze(Type,Compilation,CancellationToken);
        Root=Tree.GetRoot(CancellationToken);
        if(Model.HasErrors){
            Error=VsixText.Get("SessionSaveAnalysisErrors");
            return false;
        }
        Error=string.Empty;
        return true;
    }
    /// <summary>The same name and component types, which is all the reconciler tells siblings apart by.</summary>
    private static bool LooksTheSame(SceneNode Old,SceneNode New)=>
        string.Equals(Old.Name,New.Name,StringComparison.Ordinal)
        &&Old.Components.Select(static Component=>Component.TypeFullyQualifiedName)
            .SequenceEqual(New.Components.Select(static Component=>Component.TypeFullyQualifiedName),StringComparer.Ordinal);
    private static string?SchemaReferenceOf(string JsonText){
        try {
            return SceneJsonReader.Parse(JsonText).Get("$schema") is { Kind:SceneJsonKind.String }Schema?Schema.Text:null;
        } catch(SceneJsonException){
            return null;
        }
    }
    /// <summary>The writer emits "\n"; a file saved with CRLF keeps CRLF so only edited lines change.</summary>
    private static string MatchLineEndings(string Text,string Existing)=>
        Existing.Contains("\r\n")?Text.Replace("\n","\r\n"):Text;
}
