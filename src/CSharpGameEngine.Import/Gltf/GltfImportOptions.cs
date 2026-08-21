namespace CSharpGameEngine.Import.Gltf;

/// <summary>
/// Controls the physically meaningful conversions performed while importing a glTF scene.
/// glTF leaves the real-world luminance represented by an emissive unit unspecified; the engine
/// deliberately makes that scale explicit instead of silently baking an arbitrary HDR multiplier
/// into a material.
/// </summary>
public sealed record GltfImportOptions {
    /// <summary>Default mapping for one glTF emissive unit, in cd/m² (nits).</summary>
    public const float DefaultEmissiveNitsPerUnit=100f;

    /// <summary>
    /// Luminance represented by glTF <c>emissiveFactor == (1,1,1)</c> and an emissive strength of
    /// one. The value must be finite and positive.
    /// </summary>
    public float EmissiveNitsPerUnit { get;init;}=DefaultEmissiveNitsPerUnit;

    internal void Validate(){
        if(!float.IsFinite(this.EmissiveNitsPerUnit)||this.EmissiveNitsPerUnit<=0f){
            throw new ArgumentOutOfRangeException(nameof(this.EmissiveNitsPerUnit),"EmissiveNitsPerUnit must be finite and greater than zero.");
        }
    }
}
