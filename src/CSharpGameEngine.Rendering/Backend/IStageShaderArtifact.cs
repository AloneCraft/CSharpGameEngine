namespace CSharpGameEngine.Rendering.Backend;
/// <summary>所有権のある正規形式のアーティファクト内容。プロバイダー固有の解析済み状態は、このインターフェースの背後に保持する。</summary>
public interface IStageShaderArtifact {
    ReadOnlyMemory<byte>Serialized { get; }
}
