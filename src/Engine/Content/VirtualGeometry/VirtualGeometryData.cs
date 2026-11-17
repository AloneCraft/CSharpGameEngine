using System.Numerics;
namespace CSharpGameEngine.Content.VirtualGeometry;
/// <summary>仮想ジオメトリの頂点 1 つ(メッシュの中の座標)。</summary>
public readonly record struct VirtualGeometryVertex(Vector3 Position,Vector3 Normal,Vector4 Tangent,Vector2 Uv);
/// <summary>
/// 塊 1 つ(128 三角形以下)。<see cref="FirstIndex"/> から <see cref="IndexCount"/> 個の頂点の番号が三角形。
/// 選び方(ADR-0100 決定 3): 自分を作ったまとまり <see cref="Group"/> の誤差の画素数がしきい値以下で、自分を使って次の段を作ったまとまり
/// <see cref="ParentGroup"/> の誤差の画素数がしきい値より大きいとき(根の段なら親は無い)に描く。
/// </summary>
public readonly record struct VirtualGeometryCluster(int FirstIndex,int IndexCount,Vector3 BoundsCentre,float BoundsRadius,int Group,int ParentGroup);
/// <summary>
/// まとまり 1 つ。誤差と、誤差を画素に直すための包む球を持つ。段 0 は元のメッシュの塊を 1 つずつ包むまとまり(誤差は位置の格子の誤差)。
/// 親のまとまりの球は、自分の入力を作ったまとまりの球を包み、誤差は入力の誤差の 1.25 倍以上(独立判定の穴と重なりを防ぐ)。
/// </summary>
public readonly record struct VirtualGeometryGroup(Vector3 BoundsCentre,float BoundsRadius,float Error,int Level,int FirstCluster,int ClusterCount);
/// <summary>メッシュ 1 つの仮想ジオメトリ。<see cref="RootGroup"/> は最も粗い段のまとまり。</summary>
public sealed record VirtualGeometryMesh(VirtualGeometryVertex[]Vertices,uint[]Indices,VirtualGeometryCluster[]Clusters,VirtualGeometryGroup[]Groups,int RootGroup,int LevelCount);
