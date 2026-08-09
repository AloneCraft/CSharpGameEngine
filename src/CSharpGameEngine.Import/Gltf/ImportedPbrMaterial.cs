using System.Numerics;

namespace CSharpGameEngine.Import.Gltf;

/// <summary>
/// A glTF metallic-roughness material as used by <see cref="GltfImporter.ImportScene"/>. Each
/// texture slot is independently optional (raw encoded PNG/JPEG bytes, undecoded) - a material
/// baked to a flat factor (no spatial variation) carries a null texture and a real factor value,
/// exactly like <see cref="ImportedMesh.BaseColorTextureBytes"/>/BaseColorFactor already do.
/// </summary>
public sealed class ImportedPbrMaterial
{
    public required Vector4 BaseColorFactor { get; init; }

    public required float MetallicFactor { get; init; }

    public required float RoughnessFactor { get; init; }

    public byte[]? BaseColorTextureBytes { get; init; }

    public byte[]? NormalTextureBytes { get; init; }

    /// <summary>glTF-standard packed texture: G = roughness, B = metallic (R is often occlusion, unused here).</summary>
    public byte[]? MetallicRoughnessTextureBytes { get; init; }
}
