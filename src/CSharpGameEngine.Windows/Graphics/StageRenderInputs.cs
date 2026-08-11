using System.Numerics;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>One mesh's geometry for <see cref="StageRenderer"/> v2 (ADR-0018): positions/normals/tangents/UVs are independent GPU buffers, shared by every <see cref="StageObjectCreateInfo"/> that references this mesh index.</summary>
public readonly struct StageMeshCreateInfo {
    public required IReadOnlyList<Vector3>Positions { get; init;}
    public required IReadOnlyList<Vector3>Normals { get; init;}

    /// <summary>xyz = tangent direction, w = bitangent sign. Required (not optional) so every mesh can be normal-mapped; <see cref="CSharpGameEngine.Import.Gltf.GltfImporter.ImportScene"/> always produces one (reading TANGENT or generating it).</summary>
    public required IReadOnlyList<Vector4>Tangents { get; init;}

    public required IReadOnlyList<Vector2>Uvs { get; init;}
    public required IReadOnlyList<uint>Indices { get; init;}
}

/// <summary>
/// One glTF-metallic-roughness-style material. Each texture slot is optional raw encoded
/// (PNG/JPEG) bytes, undecoded - a material baked to a flat factor (no spatial variation, the
/// common case for this engine's baked UE content) supplies null and relies on the factor alone,
/// mirroring <see cref="ModelRenderer"/>'s 1x1-fallback-texture pattern.
/// </summary>
public readonly struct StageMaterialCreateInfo {
    public required Vector4 BaseColorFactor { get; init;}
    public required float RoughnessFactor { get; init;}
    public required float MetallicFactor { get; init;}

    public byte[]?BaseColorTextureBytes { get; init;}
    public byte[]?NormalTextureBytes { get; init;}

    /// <summary>glTF-standard packed texture: G = roughness, B = metallic.</summary>
    public byte[]?MetallicRoughnessTextureBytes { get; init;}
}

/// <summary>One drawable: a (mesh, material) pair. Multiple objects may reference the same mesh and/or material index - e.g. twenty cubes sharing one baked mesh, or two different-shaped props sharing one flat-color material.</summary>
public readonly struct StageObjectCreateInfo {
    public required int MeshIndex { get; init;}
    public required int MaterialIndex { get; init;}
}

/// <summary>Simplified exponential height fog (ADR-0018): density falls off with height above ground (Y=0), rather than modeling UE's separate second-fog-layer/inscattering-cubemap features.</summary>
public readonly record struct FogParams(float Density,float HeightFalloff,Vector3 Color){
    public static FogParams None=>new(0f,0f,Vector3.Zero);
}
