using System.Numerics;
using CSharpGameEngine.Rendering;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>One mesh's geometry for <see cref="StageRenderer"/> v2 (ADR-0018): positions/normals/tangents/UVs are independent GPU buffers, shared by every <see cref="StageObjectCreateInfo"/> that references this mesh index.</summary>
public readonly struct StageMeshCreateInfo {
    public required IReadOnlyList<Vector3>Positions { get; init;}
    public required IReadOnlyList<Vector3>Normals { get; init;}

    /// <summary>xyz = tangent direction, w = bitangent sign. Required (not optional) so every mesh can be normal-mapped; <see cref="CSharpGameEngine.Import.Gltf.GltfImporter.ImportScene"/> always produces one (reading TANGENT or generating it).</summary>
    public required IReadOnlyList<Vector4>Tangents { get; init;}

    public required IReadOnlyList<Vector2>Uvs { get; init;}
    public required IReadOnlyList<uint>Indices { get; init;}

    /// <summary>Skin influences of a skinned mesh (ADR-0066); null for rigid geometry. Each skinned mesh is skinned on its own, so instances need their own mesh.</summary>
    public StageSkinCreateInfo?Skin { get; init;}
    /// <summary>The mesh's signed distance field (ADR-0078/M0110), made ahead of the game; objects of a mesh that has one are in the global distance field.</summary>
    public MeshDistanceField?DistanceField { get; init;}
}

/// <summary>Eight joint indices and weights per vertex into a palette of <see cref="JointCount"/> matrices.</summary>
public sealed class StageSkinCreateInfo {
    public required int JointCount { get; init;}
    public required IReadOnlyList<ushort>JointIndices { get; init;}
    public required IReadOnlyList<float>Weights { get; init;}
}

/// <summary>
/// L0 adapter for one validated glTF-metallic-roughness-style material. Texture slots contain
/// immutable canonical RGBA8 resources; encoded PNG/JPEG bytes never cross this boundary.
/// </summary>
public readonly struct StageMaterialCreateInfo() {

    public required Vector4 BaseColorFactor { get; init;}
    public required float RoughnessFactor { get; init;}
    public required float MetallicFactor { get; init;}

    /// <summary>HDR emissive RGB factor, added before tone mapping and bloom.</summary>
    public Vector3 EmissiveFactor { get; init;}

    /// <summary>Physically authored emitted luminance in cd/m² (nits).</summary>
    public Vector3 EmissiveNits { get; init;}

    /// <summary>Mean linear emissive-texture luminance used to scale direct-light patches. One preserves factor/strength-only materials.</summary>
    public float EmissiveTextureLuminanceScale { get; init;}=1f;


    public RenderTextureResource?BaseColorTexture { get; init;}
    public RenderTextureResource?NormalTexture { get; init;}

    /// <summary>Optional raw glTF emissive texture. It modulates both visible self-emission and emission patches.</summary>
    public RenderTextureResource?EmissiveTexture { get; init;}

    /// <summary>glTF-standard packed texture: G = roughness, B = metallic.</summary>
    public RenderTextureResource?MetallicRoughnessTexture { get; init;}

    /// <summary>A world-space procedural grid drawn over the base colour (M0098), or null for an ordinary material.</summary>
    public StageWorldGrid?WorldGrid { get; init;}
}

/// <summary>
/// A procedural grid drawn per pixel from the world position, so it keeps a constant size on floors and walls
/// regardless of mesh size or UVs (the shader projects it along the dominant world axes). The material's
/// base colour is the surface between the lines.
/// </summary>
/// <param name="GridSizeMeters">Distance between the main grid lines.</param>
/// <param name="SubGridDivisions">How many sub grid cells fit in one main grid cell.</param>
/// <param name="WireHalfWidth">Half of a line's width as a fraction of its own cell (main and sub grid alike).</param>
/// <param name="GridColor">Linear RGB of the main grid lines.</param>
/// <param name="SubGridColor">Linear RGB of the sub grid lines.</param>
public readonly record struct StageWorldGrid(float GridSizeMeters,int SubGridDivisions,float WireHalfWidth,Vector3 GridColor,Vector3 SubGridColor);

/// <summary>One drawable: a (mesh, material) pair. Multiple objects may reference the same mesh and/or material index - e.g. twenty cubes sharing one baked mesh, or two different-shaped props sharing one flat-color material.</summary>
public readonly struct StageObjectCreateInfo {
    public required int MeshIndex { get; init;}
    public required int MaterialIndex { get; init;}
    /// <summary>The slot is free: the object was removed after creation (M0105). Only the Render Server sets it, when it rebuilds a renderer after a device loss.</summary>
    public bool Removed { get; init;}
}

/// <summary>Simplified exponential height fog (ADR-0018): density falls off with height above ground (Y=0), rather than modeling UE's separate second-fog-layer/inscattering-cubemap features.</summary>
public readonly record struct FogParams(float Density,float HeightFalloff,Vector3 Color){
    public static FogParams None=>new(0f,0f,Vector3.Zero);
}
