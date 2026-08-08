namespace CSharpGameEngine.SceneAuthoring.Cli;

/// <summary>
/// The bridge transport (<c>ExternalBridgeProcess</c>/<c>BridgeServerHost</c>) is fixed to exactly
/// one input path + one output path across all 3 existing bridges. This preview needs two distinct
/// inputs (the source .cs file, and the target project's already-built assembly), so the VSIX writes
/// one of these to a temp JSON file and passes that file's path as the single "input path"
/// (ADR-0031) rather than widening the shared, already-proven transport.
/// </summary>
public sealed record ScenePreviewRequest
{
    public required string SourceFilePath { get; init; }

    public required string TargetAssemblyPath { get; init; }
}
