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
/// Commits one gesture to a <c>*.scene.json</c> document (ADR-0070). The edit is applied to the scene
/// model with the same <see cref="SceneModelEditApplier"/> as C# scenes, written back with
/// <see cref="SceneJsonConverter"/> (whose canonical layout keeps untouched values byte-identical, so
/// the change is the edited lines only) and re-read to verify it. Node ids survive through the
/// projection: each predicted node keeps the JSON id of the node it was before the edit.
/// </summary>
internal static class SceneJsonCommitPlanner {
    internal const string SceneFileSuffix=".scene.json";
    internal static bool IsJsonScene(string?FilePath)=>
        FilePath is not null&&FilePath.EndsWith(SceneFileSuffix,StringComparison.OrdinalIgnoreCase);
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
        string?IdFor(NodePath Path)=>
            PredictedProjection.TryGetId(Path,out var EditorId)&&JsonIdByEditorId.TryGetValue(EditorId,out var Id)
                ?Id
                :Guid.NewGuid().ToString("D");
        var Converted=SceneJsonConverter.Convert(Predicted,SchemaReferenceOf(OldText),IdFor);
        if(Converted.Text is null){
            return SceneSourceCommitPlan.Failure(string.Join(Environment.NewLine,Converted.Problems));
        }
        var NewText=MatchLineEndings(Converted.Text,OldText);
        if(!TryAnalyze(NewText,Context.Model.FilePath,BaseCompilation,Request.SceneKey.ClassFullyQualifiedName,CancellationToken,
            out var Canonical,out var FinalRoot,out _,out var Error)){
            return SceneSourceCommitPlan.Failure(Error);
        }
        var CanonicalProjection=Reconciler.Reconcile(Request.SceneKey,Canonical!,PredictedProjection);
        var Changes=SceneTextChangePlanner.Plan(Context.Text,SourceText.From(NewText,Context.Text.Encoding));
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
