namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// A directional light's semantic basis. <see cref="Forward"/> is the light's travel direction
/// (the direction photons move), not the direction toward the light - see
/// <see cref="DirectionToLight"/> for the latter. Point lights have no Forward (see
/// Docs/Specifications/Spatial-RUF.md "Point Light").
/// </summary>
public readonly struct DirectionalLightBasis {
    /// <summary>The direction light travels.</summary>
    public RufVector Forward { get;}

    public DirectionalLightBasis(RufVector Forward){
        this.Forward= Forward.Normalize();
    }

    /// <summary>The direction from a surface toward the light - always the opposite of <see cref="Forward"/>.</summary>
    public RufVector DirectionToLight=>-Forward;
}
