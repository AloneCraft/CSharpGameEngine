using System.Numerics;

namespace CSharpGameEngine.Import.Gltf;

/// <summary>
/// A procedural grid drawn in world space instead of a baked texture (M0098). Unreal's prototype grid
/// material is a world-aligned projection of a tiling wire texture, so a 1024 px bake of a whole floor
/// cannot reproduce it; the engine re-evaluates the lines per pixel. The material's base colour factor
/// is the surface colour between lines. Carried in glTF as <c>materials[].extras.CGE_world_grid</c>.
/// </summary>
/// <param name="GridSizeMeters">Distance between the main grid lines, in metres.</param>
/// <param name="SubGridDivisions">How many sub grid cells fit in one main grid cell.</param>
/// <param name="WireHalfWidth">Half of a line's width as a fraction of its own cell; the same for the main and the sub grid.</param>
/// <param name="GridColor">Linear RGB of the main grid lines.</param>
/// <param name="SubGridColor">Linear RGB of the sub grid lines.</param>
public readonly record struct ImportedWorldGrid(float GridSizeMeters,int SubGridDivisions,float WireHalfWidth,Vector3 GridColor,Vector3 SubGridColor){
    /// <summary>The material extras key that holds the grid.</summary>
    public const string ExtrasKey="CGE_world_grid";
}
