using System.Text.Json;

namespace CSharpGameEngine.SceneAuthoring.Cli;

/// <summary>
/// The actual preview work, extracted from <c>Program.cs</c> (ADR-0023) so both one-shot mode and
/// the resident server (<c>CSharpGameEngine.Bridge.Hosting.BridgeServerHost</c>) share identical
/// logic. Returns diagnostics as a value instead of calling <see cref="Console"/> directly - those
/// are process-wide statics that would race across the resident server's concurrent requests.
/// </summary>
internal static class ScenePreviewBridgeWorker {
    public static(int ExitCode,string Output)Run(string InputPath,string OutputPath){
        try {
            ScenePreviewRequest Request=JsonSerializer.Deserialize<ScenePreviewRequest>(File.ReadAllText(InputPath))
                ??throw new InvalidOperationException($"リクエストファイル '{InputPath}' を読み取れませんでした。");

            IReadOnlyList<string>NodeNames=SceneDefinitionSourceParser.ParseNodeNamesInDeclarationOrder(File.ReadAllText(Request.SourceFilePath));
            var SceneClassSimpleNameHint=Path.GetFileNameWithoutExtension(Request.SourceFilePath);

            SceneRuntimeInvoker.InvocationResult InvocationResult=SceneRuntimeInvoker.Invoke(Request.TargetAssemblyPath,SceneClassSimpleNameHint);
            IReadOnlyList<ScenePreviewNode>Nodes=WorldInspector.Inspect(InvocationResult.Snapshot);
            ScenePreviewNode[]NamedNodes=ApplyRecoveredNames(Nodes,NodeNames);

            ScenePreviewBridgeDocument Document=new(){
                Success=true,
                SceneClassFullName=InvocationResult.SceneClassFullName,
                Nodes=NamedNodes,
            };

            File.WriteAllText(OutputPath,JsonSerializer.Serialize(Document));
            return(0,string.Empty);
        } catch(Exception Ex){
            File.WriteAllText(OutputPath,JsonSerializer.Serialize(ScenePreviewBridgeDocument.ForFailure(Ex.Message)));
            return(1,Ex.Message);
        }
    }

    /// <summary>
    /// Zips positionally only when the counts match: <c>nodes</c> is already <c>Entity.Id</c>-sorted
    /// (= depth-first declaration order, by construction of <c>SceneRuntimeEmitter</c>) and
    /// <paramref name="NodeNames"/> is the same depth-first order recovered from source text - but if
    /// anything caused the counts to diverge, leaving every node unnamed is safer than risking a
    /// wrong pairing (ADR-0031).
    /// </summary>
    private static ScenePreviewNode[]ApplyRecoveredNames(IReadOnlyList<ScenePreviewNode>Nodes,IReadOnlyList<string>NodeNames){
        if(Nodes.Count!=NodeNames.Count){
            return[.. Nodes];
        }

        ScenePreviewNode[]Named=new ScenePreviewNode[Nodes.Count];

        for(var i=0;i<Nodes.Count;i++){
            Named[i]=Nodes[i] with { Name=NodeNames[i]};
        }

        return Named;
    }
}
