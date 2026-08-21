using System.Numerics;

namespace CSharpGameEngine.Import.Gltf;

/// <summary>
/// A glTF metallic-roughness material as used by <see cref="GltfImporter.ImportScene"/>. Each
/// texture slot is independently optional (raw encoded PNG/JPEG bytes, undecoded) - a material
/// baked to a flat factor (no spatial variation) carries a null texture and a real factor value,
/// exactly like <see cref="ImportedMesh.BaseColorTextureBytes"/>/BaseColorFactor already do.
/// </summary>
public sealed class ImportedPbrMaterial {
    public required Vector4 BaseColorFactor { get; init;}

    public required float MetallicFactor { get; init;}

    public required float RoughnessFactor { get; init;}

    /// <summary>Raw glTF <c>emissiveFactor</c>, before the KHR strength and nits conversion.</summary>
    public Vector3 EmissiveFactor { get; init;}

    /// <summary>glTF <c>KHR_materials_emissive_strength</c>, or one when the extension is absent.</summary>
    public float EmissiveStrength { get; init;}=1f;

    /// <summary>
    /// Physically meaningful emitted luminance in cd/m² (nits), calculated as
    /// <c>EmissiveFactor * EmissiveStrength * GltfImportOptions.EmissiveNitsPerUnit</c>.
    /// </summary>
    public Vector3 EmissiveNits { get; init;}

    /// <summary>Mean linear Rec.709 luminance of a supported emissive texture. One means no texture or an unsupported encoded format.</summary>
    public float EmissiveTextureLuminanceScale { get; init;}=1f;


    public byte[]?BaseColorTextureBytes { get; init;}

    public byte[]?NormalTextureBytes { get; init;}

    /// <summary>Raw encoded glTF <c>emissiveTexture</c>, if present.</summary>
    public byte[]?EmissiveTextureBytes { get; init;}

    /// <summary>glTF-standard packed texture: G = roughness, B = metallic (R is often occlusion, unused here).</summary>
    public byte[]?MetallicRoughnessTextureBytes { get; init;}
}
