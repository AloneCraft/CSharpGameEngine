using System;
using System.IO;
using System.Text.Json;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Runs the <c>[SceneDefinition]</c> preview out-of-process via the shared bridge runner (ADR-0031,
/// the same <see cref="ExternalBridgeProcess"/> mechanism <see cref="ExternalGltfLoader"/>/
/// <c>ExternalFbxLoader</c> already use for the identical net472-cannot-reference-net10.0 problem).
/// The bridge needs two inputs (source .cs path, target built assembly path) but the shared transport
/// only carries one "input path", so this writes a small request DTO to a temp JSON file first and
/// passes that file's path as the input.
/// </summary>
internal static class ExternalScenePreviewLoader
{
    private const string BridgeProjectRelativePath = @"tools\CSharpGameEngine.SceneAuthoring.Cli";
    private static readonly TimeSpan BridgeTimeout = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static ScenePreviewBridgeDocument Load(string SourceFilePath, string TargetAssemblyPath, string RepoRoot)
    {
        string RequestJsonPath = Path.Combine(Path.GetTempPath(), $"csge-scenepreview-request-{Guid.NewGuid():N}.json");
        string ResponseJsonPath = Path.Combine(Path.GetTempPath(), $"csge-scenepreview-response-{Guid.NewGuid():N}.json");

        try
        {
            ScenePreviewRequest Request = new() { SourceFilePath = SourceFilePath, TargetAssemblyPath = TargetAssemblyPath };
            File.WriteAllText(RequestJsonPath, JsonSerializer.Serialize(Request));

            string Json = ExternalBridgeProcess.RunAndReadJson(BridgeProjectRelativePath, RequestJsonPath, ResponseJsonPath, RepoRoot, BridgeTimeout, UseResidentServer: true);

            ScenePreviewBridgeDocument? Document = JsonSerializer.Deserialize<ScenePreviewBridgeDocument>(Json, JsonOptions);

            if (Document is null || !Document.Success)
            {
                throw new InvalidOperationException($"シーンプレビューの生成に失敗しました: {Document?.ErrorMessage ?? "(詳細不明)"}");
            }

            return Document;
        }
        finally
        {
            TryDelete(RequestJsonPath);
            TryDelete(ResponseJsonPath);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
