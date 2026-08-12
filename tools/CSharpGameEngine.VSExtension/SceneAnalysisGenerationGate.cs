namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Gives every active-document analysis a monotonically increasing identity. An analysis result may
/// be published only while its request is still current; this prevents a slow result for document A
/// from overwriting document B after the user has already switched editors.
/// </summary>
internal sealed class SceneAnalysisGenerationGate<TDocument> where TDocument:class {
    private long Generation;
    private TDocument?ActiveDocument;

    internal SceneAnalysisRequest<TDocument>ActivateDocument(TDocument?Document){
        ActiveDocument=Document;
        return new SceneAnalysisRequest<TDocument>(++Generation,Document);
    }

    internal SceneAnalysisRequest<TDocument>Refresh()=>new(++Generation,ActiveDocument);

    internal bool IsCurrent(SceneAnalysisRequest<TDocument>Request)=>
        Request.Generation==Generation&&Equals(Request.Document,ActiveDocument);
}

internal readonly record struct SceneAnalysisRequest<TDocument>(long Generation,TDocument?Document) where TDocument:class;
