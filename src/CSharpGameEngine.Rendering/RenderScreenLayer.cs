using System.Numerics;

namespace CSharpGameEngine.Rendering;

/// <summary>
/// スクリーンレイヤーの頂点1つ（ADR-0078/M0104）。トーンマッピング後に描画するゲーム UI を、描画面のピクセル座標で表す。<see cref="Position"/> の原点は左上で、+
/// Y は下向き。<see cref="Color"/> はアルファ乗算前の sRGB RGBA であり、面の値が sRGB 符号化済みなのでそのまま書き込む。
/// </summary>
public readonly record struct RenderScreenVertex(Vector2 Position,Vector2 Uv,Vector4 Color);

/// <summary>バッチのテクスチャを被覆率に変換する方法。</summary>
public enum RenderScreenShading:byte {
    /// <summary>テクセルがそのまま色になる。<c>texture × vertex color</c>（画像や、白テクスチャを使う単色四角形）。</summary>
    Image=0,
    /// <summary>テクスチャのアルファ値は符号付き距離を表す（輪郭で0.5、内側ほど大きい）。1つのアトラスから任意サイズのグリフを描画する。</summary>
    DistanceField=1,
}

/// <summary>単一のテクスチャを使って描画する三角形の連続範囲。頂点は完全な三角形を構成する。バッチは配列順に描画するため、後のバッチが前のバッチを覆う。</summary>
public readonly record struct RenderScreenBatch(RenderTextureResource Texture,RenderScreenShading Shading,int FirstVertex,int VertexCount);

/// <summary>スクリーンレイヤーの上限と検査条件。レンダラーとレイヤー構築コードで共有する。</summary>
public static class RenderScreenLayer {
    /// <summary>1フレームあたりの頂点数（4096個の四角形、各2三角形）。</summary>
    public const int MaximumVertices=24_576;
    /// <summary>1フレームあたりのバッチ数。フレームで使用できるテクスチャ数も決まる。</summary>
    public const int MaximumBatches=32;

    /// <summary>レイヤーが有効な形式（有限値、完全な三角形、範囲内のバッチ）でなければ <see cref="ArgumentException"/> をスローする。</summary>
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
