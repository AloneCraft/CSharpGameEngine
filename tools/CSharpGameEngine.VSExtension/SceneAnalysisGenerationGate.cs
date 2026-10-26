namespace CSharpGameEngine.VSExtension;

/// <summary>
/// アクティブドキュメントの解析ごとに単調増加する識別子を割り当てます。解析結果を発行できるのは、その要求がまだ最新の場合だけです。ユーザーがすでに別の editor に切り替えた後で、遅れて届いたドキュメント A の結果がドキュメント B を上書きするのを防ぎます。
/// </summary>
internal sealed class SceneAnalysisGenerationGate<TDocument> where TDocument:class {
    private long Generation;
    private TDocument?ActiveDocument;

    internal SceneAnalysisRequest<TDocument>ActivateDocument(TDocument?Document){
        this.ActiveDocument=Document;
        return new SceneAnalysisRequest<TDocument>(++this.Generation,Document);
    }

    internal SceneAnalysisRequest<TDocument>Refresh()=>new(++this.Generation, this.ActiveDocument);

    internal bool IsCurrent(SceneAnalysisRequest<TDocument>Request)=>
        Request.Generation== this.Generation&&Equals(Request.Document, this.ActiveDocument);
}

internal readonly record struct SceneAnalysisRequest<TDocument>(long Generation,TDocument?Document) where TDocument:class;
