namespace CSharpGameEngine.Rendering.Backend;
/// <summary>所有する名簿データと、任意の直近適用済みアーティファクトから、プロバイダーの世代を作成する。</summary>
public interface IStageRenderBackendFactory {
    IStageRenderBackend Create(int Width,int Height,IReadOnlyList<StageMeshCreateInfo>Meshes,
        IReadOnlyList<StageMaterialCreateInfo>Materials,IReadOnlyList<StageObjectCreateInfo>Objects,IStageShaderArtifact?LastKnownGood=null);
}
