namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>A spot light's semantic basis. <see cref="Forward"/> is the direction the cone extends toward.</summary>
public readonly struct SpotLightBasis
{
    /// <summary>The direction the spot cone extends toward.</summary>
    public RufVector Forward { get; }

    public SpotLightBasis(RufVector forward)
    {
        Forward = forward.Normalize();
    }

    /// <summary>The direction from a surface toward the light - always the opposite of <see cref="Forward"/>.</summary>
    public RufVector DirectionToLight => -Forward;
}
