using System.Numerics;

namespace CSharpGameEngine.Rendering;

/// <summary>
/// One vertex of the screen layer (ADR-0078/M0104): the game's UI drawn over the finished frame, after tone mapping, in the pixels of the
/// render surface. <see cref="Position"/> has its origin at the top-left corner and +Y pointing down. <see cref="Color"/> is straight
/// (not premultiplied) sRGB RGBA, written as it is because the surface holds sRGB-encoded values.
/// </summary>
public readonly record struct RenderScreenVertex(Vector2 Position,Vector2 Uv,Vector4 Color);

/// <summary>How a batch's texture becomes coverage.</summary>
public enum RenderScreenShading:byte {
    /// <summary>The texel is the color: <c>texture × vertex color</c> (images, solid quads with the white texture).</summary>
    Image=0,
    /// <summary>The texture's alpha is a signed distance (0.5 on the outline, larger inside): glyphs of any size from one atlas.</summary>
    DistanceField=1,
}

/// <summary>
/// A run of triangles drawn with one texture. Vertices form complete triangles; batches are drawn in array order, so a later batch
/// covers an earlier one.
/// </summary>
public readonly record struct RenderScreenBatch(RenderTextureResource Texture,RenderScreenShading Shading,int FirstVertex,int VertexCount);

/// <summary>The limits and the checks of the screen layer, shared by the renderer and the code that builds a layer.</summary>
public static class RenderScreenLayer {
    /// <summary>Vertices per frame (4096 quads of two triangles).</summary>
    public const int MaximumVertices=24_576;
    /// <summary>Batches per frame, and so the textures a frame can use.</summary>
    public const int MaximumBatches=32;

    /// <summary>Throws <see cref="ArgumentException"/> unless the layer is a valid one (finite values, complete triangles, in-range batches).</summary>
    public static void Validate(ReadOnlySpan<RenderScreenVertex>Vertices,ReadOnlySpan<RenderScreenBatch>Batches){
        if(Vertices.Length>MaximumVertices){
            throw new ArgumentException($"The screen layer exceeds {MaximumVertices} vertices.",nameof(Vertices));
        }
        if(Batches.Length>MaximumBatches){
            throw new ArgumentException($"The screen layer exceeds {MaximumBatches} batches.",nameof(Batches));
        }
        foreach(var Vertex in Vertices){
            if(!float.IsFinite(Vertex.Position.X)||!float.IsFinite(Vertex.Position.Y)
                ||!float.IsFinite(Vertex.Uv.X)||!float.IsFinite(Vertex.Uv.Y)
                ||!float.IsFinite(Vertex.Color.X)||!float.IsFinite(Vertex.Color.Y)
                ||!float.IsFinite(Vertex.Color.Z)||!float.IsFinite(Vertex.Color.W)
                ||Vertex.Color.W<0f||Vertex.Color.W>1f){
                throw new ArgumentException("Screen vertices require finite values and alpha between zero and one.",nameof(Vertices));
            }
        }
        foreach(var Batch in Batches){
            if(Batch.Texture is null){
                throw new ArgumentException("A screen batch needs a texture.",nameof(Batches));
            }
            if(Batch.Shading is not (RenderScreenShading.Image or RenderScreenShading.DistanceField)){
                throw new ArgumentException("A screen batch has an unknown shading.",nameof(Batches));
            }
            if(Batch.FirstVertex<0||Batch.VertexCount<0||Batch.VertexCount%3!=0
                ||Batch.FirstVertex>Vertices.Length||Batch.VertexCount>Vertices.Length-Batch.FirstVertex){
                throw new ArgumentException("A screen batch must cover complete triangles inside the vertices.",nameof(Batches));
            }
        }
    }
}
