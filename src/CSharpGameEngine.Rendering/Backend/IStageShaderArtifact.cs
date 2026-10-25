namespace CSharpGameEngine.Rendering.Backend;
/// <summary>Owned canonical artifact payload. Provider-specific parsed state remains behind this interface.</summary>
public interface IStageShaderArtifact {
    ReadOnlyMemory<byte>Serialized { get; }
}
