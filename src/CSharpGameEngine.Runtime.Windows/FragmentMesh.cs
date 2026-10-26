using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// ベイク済みの事前破砕アセットに含まれる破片を描画する（ADR-0078/M0113）。破片はメッシュの一部で、破片自身のEntityの
/// <see cref="Transform"/>（中心位置と、乗っているボディの回転）に従って配置する。Destructionプラグインが破片ごとにEntityを作成して移動させ、
/// レンダラーはEntityの生成／破棄に合わせてオブジェクトを追加／除去する（M0105）。ソース表面にある破片の三角形には<see cref="OuterColor"/>、
/// Voronoiの切断面には<see cref="InteriorColor"/>を使う（UEの内側マテリアルスロット）。
/// </summary>
public struct FragmentMesh {
    /// <summary>コンテンツルート内のアセット（例：<c>Assets/Destruction/Crate.cgefracture</c>）。</summary>
    public string AssetPath;
    public int Fragment;
    /// <summary>外側表面の線形RGB。</summary>
    public Vector3 OuterColor;
    /// <summary>切断面の線形RGB。</summary>
    public Vector3 InteriorColor;
}
/// <summary>ADR-0078/M0113：<see cref="FragmentMesh"/>を持ち、まだ描画されていないEntityに、レンダラーオブジェクトを割り当てる。</summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(CameraOriginSync))]
[RunsBefore(typeof(TransformSync))]
internal struct FragmentMeshBind:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref FragmentMesh Mesh,EngineHost Host,ref State State){
        Host.Roster?.BindFragmentMesh(Entity,in Transform,in Mesh,Host,ref State);
    }
}
/// <summary>破片のレンダラーメッシュ。破片のローカル座標系にある、単一マテリアルスロットのフラットシェーディング三角形。</summary>
internal static class FragmentMeshBuilder {
    /// <summary>
    /// <paramref name="Slot"/>に属する三角形。それぞれ独立した3頂点を持ち、外向き巻き順に基づくフラット法線、最初の辺に沿う接線、
    /// 隣り合う破片のテクスチャがつながるようソース座標系の位置を使ったボックス投影UVを含む。スロットに三角形がない場合はnull。
    /// </summary>
    internal static StageMeshCreateInfo?Build(PrefractureAsset Asset,int Fragment,byte Slot){
        var Mesh=Asset.Meshes[Fragment];
        var Center=Asset.Fragments[Fragment].LocalCenter;
        List<Vector3>Positions=[];
        List<Vector3>Normals=[];
        List<Vector4>Tangents=[];
        List<Vector2>Uvs=[];
        List<uint>Indices=[];
        for(var Triangle=0;Triangle<Mesh.Slots.Count;Triangle++){
            if(Mesh.Slots[Triangle]!=Slot){ continue; }
            var A=ToVector(Mesh.Vertices[Mesh.Indices[Triangle*3]]);
            var B=ToVector(Mesh.Vertices[Mesh.Indices[Triangle*3+1]]);
            var C=ToVector(Mesh.Vertices[Mesh.Indices[Triangle*3+2]]);
            var Normal=Vector3.Cross(B-A,C-A);
            if(Normal.LengthSquared()<=1e-20f){ continue; }
            Normal=Vector3.Normalize(Normal);
            var Edge=Vector3.Normalize(B-A);
            var Offset=ToVector(Center);
            foreach(var Point in (ReadOnlySpan<Vector3>)[A,B,C]){
                Indices.Add((uint)Positions.Count);
                Positions.Add(Point);
                Normals.Add(Normal);
                Tangents.Add(new Vector4(Edge,1f));
                Uvs.Add(BoxProjection(Point+Offset,Normal));
            }
        }
        if(Indices.Count==0){ return null; }
        return new StageMeshCreateInfo { Positions=Positions,Normals=Normals,Tangents=Tangents,Uvs=Uvs,Indices=Indices };
    }
    private static Vector3 ToVector(LogicalVector3 Value)=>Value.TryToSingle(out var Result)?Result:throw new OverflowException("A fragment vertex does not fit a float.");
    private static Vector2 BoxProjection(Vector3 Point,Vector3 Normal){
        var Absolute=Vector3.Abs(Normal);
        return Absolute.X>=Absolute.Y&&Absolute.X>=Absolute.Z?new Vector2(Point.Z,Point.Y):Absolute.Y>=Absolute.Z?new Vector2(Point.X,Point.Z):new Vector2(Point.X,Point.Y);
    }
}
