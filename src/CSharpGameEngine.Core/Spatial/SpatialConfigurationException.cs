namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// Thrown for Authoring/Import/Build-time coordinate configuration errors (invalid basis,
/// non-orthogonal axes, zero-length axes, duplicate axes, degenerate LookRotation input, invalid
/// import unit scale, non-finite values). Deliberately not thrown from hot-path per-frame
/// conversions - see Docs/Specifications/Spatial-RUF.md "エラー処理".
/// </summary>
public sealed class SpatialConfigurationException:Exception {
    public SpatialConfigurationException(string message)
        :base(message){
    }

    public SpatialConfigurationException(string message,Exception innerException)
        :base(message,innerException){
    }
}
