using System;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// <c>[SceneDefinition]</c> で定義されたノードにはメッシュアセットの概念がないため、メモリ上で小さな仮ボックス <see cref="SimpleMesh"/> を手続き生成します（ディスク I/O も bridge 呼び出しもありません、ADR-0031）。<c>CSharpGameEngine.Geometry.ProceduralMeshFactory .CreateBox</c> には意図的に経由させません。このメソッドの頂点・法線計算は RUF 空間抽象（<c>CSharpGameEngine.Spatial.Abstractions</c>/<c>.Unity</c>、いずれも net10.0）を使い、この net472 の VSIX からプロセス内参照できないためです。これは ADR-0031 がすでに記録している <c>CSharpGameEngine.Entities</c> と同じ TFM の壁です。固定サイズの軸揃え立方体は変化しない定数形状であり、変化しうるアルゴリズムではありません。そのため、RUF ではなく単純な <see cref="System.Numerics.Vector3"/> 計算を使ってこの形状だけをローカルに重複実装します。これは wire-protocol DTO で ADR-0023 が選んだ「bridge を作るより単純な処理を重複する」という判断と同じです。面、頂点、index の配置は <c>ProceduralMeshFactory.CreateBox</c> と完全に同じ（6面 x 4頂点、面ごとに法線1つ）ですが、RUF ではなく XYZ を直接使います。
/// </summary>
internal static class GizmoMeshFactory {
    private static readonly Vector4 DefaultTint=new(0.4f,0.65f,0.95f,1f);

    internal static SimpleMesh Create(float Size=0.3f,Vector4?Tint=null){
        if(Size<=0f){
            throw new ArgumentOutOfRangeException(nameof(Size),"size must be positive.");
        }

        var Half=Size/2f;

        (Vector3 Normal,Vector3 U,Vector3 V)[]Faces=[
            (Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY),      // +Z
            (-Vector3.UnitZ,-Vector3.UnitX,Vector3.UnitY),    // -Z
            (Vector3.UnitX,-Vector3.UnitZ,Vector3.UnitY),     // +X
            (-Vector3.UnitX,Vector3.UnitZ,Vector3.UnitY),     // -X
            (Vector3.UnitY,Vector3.UnitX,-Vector3.UnitZ),     // +Y
            (-Vector3.UnitY,Vector3.UnitX,Vector3.UnitZ),     // -Y
        ];

        var Positions=new Vector3[Faces.Length*4];
        var Normals=new Vector3[Faces.Length*4];
        var Indices=new uint[Faces.Length*6];

        for(var Face=0;Face<Faces.Length;Face++){
            (var Normal,var U,var V)=Faces[Face];
            var Center=Normal*Half;
            var UHalf=U*Half;
            var VHalf=V*Half;
            var VertexBase=Face*4;

            Positions[VertexBase+0]=Center-UHalf-VHalf;
            Positions[VertexBase+1]=Center+UHalf-VHalf;
            Positions[VertexBase+2]=Center+UHalf+VHalf;
            Positions[VertexBase+3]=Center-UHalf+VHalf;

            for(var I=0;I<4;I++){
                Normals[VertexBase+I]=Normal;
            }

            var IndexBase=Face*6;
            Indices[IndexBase+0]=(uint)(VertexBase+0);
            Indices[IndexBase+1]=(uint)(VertexBase+1);
            Indices[IndexBase+2]=(uint)(VertexBase+2);
            Indices[IndexBase+3]=(uint)(VertexBase+0);
            Indices[IndexBase+4]=(uint)(VertexBase+2);
            Indices[IndexBase+5]=(uint)(VertexBase+3);
        }

        (var BoundsCenter,var BoundsRadius)=SimpleMesh.ComputeBoundingSphere(Positions);

        return new SimpleMesh {
            Positions=Positions,
            Normals=Normals,
            Indices=Indices,
            BaseColorFactor=Tint??DefaultTint,
            BoundsCenter=BoundsCenter,
            BoundsRadius=BoundsRadius,
        };
    }
}
