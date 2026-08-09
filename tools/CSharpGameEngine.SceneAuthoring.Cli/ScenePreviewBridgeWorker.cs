using System.Text.Json;

namespace CSharpGameEngine.SceneAuthoring.Cli;

/// <summary>
/// The actual preview work, extracted from <c>Program.cs</c> (ADR-0023) so both one-shot mode and
/// the resident server (<c>CSharpGameEngine.Bridge.Hosting.BridgeServerHost</c>) share identical
/// logic. Returns diagnostics as a value instead of calling <see cref="Console"/> directly - those
/// are process-wide statics that would race across the resident server's concurrent requests.
/// </summary>
internal static class ScenePreviewBridgeWorker
{
    public static (int ExitCode, string Output) Run(string inputPath, string outputPath)
    {
        try
        {
            ScenePreviewRequest request = JsonSerializer.Deserialize<ScenePreviewRequest>(File.ReadAllText(inputPath))
                ?? throw new InvalidOperationException($"リクエストファイル '{inputPath}' を読み取れませんでした。");

            IReadOnlyList<string> nodeNames = SceneDefinitionSourceParser.ParseNodeNamesInDeclarationOrder(File.ReadAllText(request.SourceFilePath));
            string sceneClassSimpleNameHint = Path.GetFileNameWithoutExtension(request.SourceFilePath);

            SceneRuntimeInvoker.InvocationResult invocationResult = SceneRuntimeInvoker.Invoke(request.TargetAssemblyPath, sceneClassSimpleNameHint);
            IReadOnlyList<ScenePreviewNode> nodes = WorldInspector.Inspect(invocationResult.Snapshot);
            ScenePreviewNode[] namedNodes = ApplyRecoveredNames(nodes, nodeNames);

            ScenePreviewBridgeDocument document = new()
            {
                Success = true,
                SceneClassFullName = invocationResult.SceneClassFullName,
                Nodes = namedNodes,
            };

            File.WriteAllText(outputPath, JsonSerializer.Serialize(document));
            return (0, string.Empty);
        }
        catch (Exception ex)
        {
            File.WriteAllText(outputPath, JsonSerializer.Serialize(ScenePreviewBridgeDocument.ForFailure(ex.Message)));
            return (1, ex.Message);
        }
    }

    /// <summary>
    /// Zips positionally only when the counts match: <c>nodes</c> is already <c>Entity.Id</c>-sorted
    /// (= depth-first declaration order, by construction of <c>SceneRuntimeEmitter</c>) and
    /// <paramref name="nodeNames"/> is the same depth-first order recovered from source text - but if
    /// anything caused the counts to diverge, leaving every node unnamed is safer than risking a
    /// wrong pairing (ADR-0031).
    /// </summary>
    private static ScenePreviewNode[] ApplyRecoveredNames(IReadOnlyList<ScenePreviewNode> nodes, IReadOnlyList<string> nodeNames)
    {
        if (nodes.Count != nodeNames.Count)
        {
            return [.. nodes];
        }

        ScenePreviewNode[] named = new ScenePreviewNode[nodes.Count];

        for (int i = 0; i < nodes.Count; i++)
        {
            named[i] = nodes[i] with { Name = nodeNames[i] };
        }

        return named;
    }
}
