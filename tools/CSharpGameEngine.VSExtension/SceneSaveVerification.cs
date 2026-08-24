using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0051: 書き戻した構文木を<b>読み直して</b>、意図した<see cref="SceneModel"/>と意味的に一致するかを
/// 検算する。
///
/// <para>
/// M0049でJSONへ移行した動機は、ユーザーからの「C#に反映されていない」という報告だった。
/// 当時の書き戻しは「適用して書き込んで終わり」で、結果が正しいかを誰も確認していなかったため、
/// 壊れたことに気付けるのが実機で目視したときだけだった。ここで検算を入れることで
/// 「壊れやすい」を<b>「壊れたら必ず判る(そして書き込まない)」</b>へ変える。
/// </para>
///
/// <para>
/// 比較は<see cref="SceneModelStructuralSignature"/>で行う — ソース位置や書式の違いでは変わらず、
/// ノード構成・名前・Component値の変化だけを捉える(そのクラスのXMLコメント参照)。
/// </para>
/// </summary>
internal static class SceneSaveVerification {
    /// <summary>
    /// <paramref name="EditedRoot"/>を再解析して得た<see cref="SceneModel"/>が
    /// <paramref name="Expected"/>と意味的に一致するか。一致しない場合は
    /// <paramref name="ErrorMessage"/>に理由が入る。
    ///
    /// <para>
    /// <paramref name="OriginalCompilation"/>は再解析に使う。書き戻しはScene宣言の中身しか
    /// 変えないため、Component型やenumの解決に必要な参照は元のCompilationのもので足りる
    /// (構文木だけを差し替える)。
    /// </para>
    /// </summary>
    internal static bool Verify(
        SyntaxNode EditedRoot,
        SyntaxNode OriginalRoot,
        Compilation OriginalCompilation,
        SceneModel Expected,
        string ClassFullyQualifiedName,
        CancellationToken CancellationToken,
        out string?ErrorMessage){
        ErrorMessage=null;

        var EditedTree=CSharpSyntaxTree.Create(
                                               (CSharpSyntaxNode)EditedRoot,
                                               (CSharpParseOptions?)OriginalRoot.SyntaxTree.Options,
                                               OriginalRoot.SyntaxTree.FilePath,
                                               OriginalRoot.SyntaxTree.Encoding);

        Compilation Reanalyzed;
        try {
            Reanalyzed=OriginalCompilation.ReplaceSyntaxTree(OriginalRoot.SyntaxTree,EditedTree);
        } catch(Exception Ex){
            try {
                ActivityLog.LogError(nameof(SceneSaveVerification),Ex.ToString());
            } catch(Exception){
                // Unit tests and early package loading may not have a Visual Studio Activity Log service.
            }
            ErrorMessage=VsixText.Get("SaveVerificationReanalysisFailed");
            return false;
        }

        var Type=Reanalyzed.GetTypeByMetadataName(ClassFullyQualifiedName);
        if(Type is null||!SceneTypePredicates.ImplementsSceneInterface(Type)){
            ErrorMessage=VsixText.Get("SaveVerificationSceneDeclarationMissing");
            return false;
        }

        var Actual=SceneDeclarationAnalyzer.Analyze(Type,Reanalyzed,CancellationToken);
        if(Actual.HasErrors||Actual.Root is null){
            ErrorMessage=VsixText.Get("SaveVerificationAnalysisErrors");
            return false;
        }

        var ExpectedSignature=SceneModelStructuralSignature.Compute(Expected);
        var ActualSignature=SceneModelStructuralSignature.Compute(Actual);
        if(!string.Equals(ExpectedSignature,ActualSignature,StringComparison.Ordinal)){
            ErrorMessage=VsixText.Format("SaveVerificationMismatch",ExpectedSignature,ActualSignature);
            return false;
        }

        return true;
    }
}
