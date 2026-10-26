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
/// シーン JSON ドキュメントへの操作1件を commit します（ADR-0070）。C# シーンと同じ <see cref="SceneModelEditApplier"/> でシーンモデルを編集し、<see cref="SceneJsonConverter"/> で書き戻します。canonical layout は未変更の値を byte 単位で維持するため、変更されるのは編集した行だけです。書き戻した JSON は再読み込みして検証します。projection 後も node id を維持し、予測上の各ノードは編集前の JSON node id を引き継ぎます。
/// </summary>
internal static class SceneJsonCommitPlanner {
    /// <summary>
    /// ファイルがシーンである条件は、プロジェクトの <c>Scenes</c> フォルダー内にある <c>*.json</c>（<see cref="SceneFiles"/>）です。パスだけが与えられた場合は、<c>*.csproj</c> を持つ最も近い親フォルダーをプロジェクトとします。ビルドで <c>Scenes\**\*.json</c> を含める規則に合わせています。
    /// </summary>
    internal static bool IsJsonScene(string?FilePath)=>
        SceneFiles.IsScene(FilePath,FindProjectDirectory(FilePath));

    /// <summary>プロジェクトが既知の場合の同じ規則（Roslyn プロジェクトのファイルパス）。</summary>
    internal static bool IsJsonScene(string?FilePath,string?ProjectFilePath)=>
        SceneFiles.IsScene(FilePath,string.IsNullOrEmpty(ProjectFilePath)?null:System.IO.Path.GetDirectoryName(ProjectFilePath));

    /// <summary>
    /// シーンモデルが JSON ドキュメントから読み込まれ、JSON writer が commit できるかどうか。モデルのファイルがシーンであることはすでに分かっており、ここでは JSON 形式とそれ以外を区別します。
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
    /// <see cref="SceneSourceCommitContext.Text"/> は JSON テキスト、<see cref="SceneSourceCommitContext.Root"/> は designer の C# tree のルート、<see cref="SceneSourceCommitContext.Compilation"/> はその tree を含む解析用 compilation です。
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
        var OldIds=NodeIdsByPath(OldText);
        var JsonIdByEditorId=new Dictionary<EditorNodeId,string>();
        foreach(var Pair in OldIds){
            if(Latest.Projection.TryGetId(Pair.Key,out var EditorId)){ JsonIdByEditorId[EditorId]=Pair.Value; }
        }
        string?IdFor(NodePath Path){
            if(!PredictedProjection.TryGetId(Path,out var EditorId)){ return Guid.NewGuid().ToString("D"); }
            if(JsonIdByEditorId.TryGetValue(EditorId,out var Id)){ return Id; }
            // 同じ名前とコンポーネント型を持つ兄弟ノードには解析ごとに新しい editor id が割り当てられます（reconciler は区別を推測しません）。
            // そのため編集ごとに id が置き換わっていました。そのようなノードで、
            // 同じ位置の以前のノードと一致し、予測結果に editor id がない場合は、以前の id を維持します。
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
        // M0095：canonical text を作成者のテキストにマージするため、変更された値だけが変わります。
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
    /// JSON シーンを designer view として読み込みます。同等の C# class を JSON パスとともに解析し、<paramref name="BaseCompilation"/> 上で解析します。テキストが有効なシーンでない場合、最初のドキュメント問題を <paramref name="Error"/> に示します。
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
    /// <summary>
    /// JSON tree だけから読み取った、各 node の path と ID。以前は ID を得るためだけに commit ごとに全テキストを再解析しており、大きなシーンでは commit 時間の半分を占めていました。エラーのないドキュメントに対してのみ commit するため、そのノードは同じ path の解析結果ノードです。
    /// </summary>
    private static Dictionary<NodePath,string>NodeIdsByPath(string JsonText){
        var Result=new Dictionary<NodePath,string>();
        void Walk(SceneJsonValue Node,System.Collections.Immutable.ImmutableArray<int>Path){
            if(Node.Kind!=SceneJsonKind.Object){ return; }
            if(Node.Get("id") is { Kind:SceneJsonKind.String,Text:{ Length:>0 }Id }){ Result[new NodePath(Path)]=Id; }
            if(Node.Get("children") is { Kind:SceneJsonKind.Array }Children){
                for(var Index=0;Index<Children.Items.Count;Index++){ Walk(Children.Items[Index],Path.Add(Index)); }
            }
        }
        try {
            if(SceneJsonReader.Parse(JsonText).Get("root") is { }Root){ Walk(Root,System.Collections.Immutable.ImmutableArray<int>.Empty); }
        } catch(SceneJsonException){
        }
        return Result;
    }
    /// <summary>reconciler が兄弟ノードを区別するために使う、同じ名前とコンポーネント型。</summary>
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
    /// <summary>writer は "\n" を出力します。CRLF で保存されたファイルは CRLF を維持し、編集行だけが変わります。</summary>
    private static string MatchLineEndings(string Text,string Existing)=>
        Existing.Contains("\r\n")?Text.Replace("\n","\r\n"):Text;
}
