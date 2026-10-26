namespace CSharpGameEngine.Rendering.Backend;
/// <summary>Creates a provider generation from owned roster data and an optional last applied artifact.</summary>
public interface IStageRenderBackendFactory {
    IStageRenderBackend Create(int Width,int Height,IReadOnlyList<StageMeshCreateInfo>Meshes,
        IReadOnlyList<StageMaterialCreateInfo>Materials,IReadOnlyList<StageObjectCreateInfo>Objects,IStageShaderArtifact?LastKnownGood=null);
}
