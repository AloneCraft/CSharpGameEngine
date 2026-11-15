using System;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// VSIX 側でデシリアライズする対象。<c>CSharpGameEngine.Import.Fbx.Cli</c> の <c>FbxBridgeDocument</c> (ADR-0010) と、<c>CSharpGameEngine.Import.Gltf.Cli</c> の <c>GltfBridgeDocument</c> (ADR-0019) の両方に対応する。どちらのブリッジも同じ平坦な JSON 形式を生成するため、<see cref="ExternalFbxLoader"/> と <see cref="ExternalGltfLoader"/> はこの型と <see cref="ToSimpleMesh"/> の変換を共有し、形式ごとに重複させない。VSIX のレンダラーはテクスチャをサポートしないため、<c>Uvs</c> は意図的にモデル化しない。
/// </summary>
internal sealed class BridgeDocument {
    public bool Success { get; set;}

    public float[]Positions { get; set;}=[];

    public float[]Normals { get; set;}=[];

    public uint[]Indices { get; set;}=[];

    public float[]BaseColorFactor { get; set;}=[];

    public SimpleMesh ToSimpleMesh(){
        var PositionValues=ToVector3Array(this.Positions);
        var NormalValues=ToVector3Array(this.Normals);
        var BaseColorValues= this.BaseColorFactor.Length==4
                                ?new Vector4(this.BaseColorFactor[0], this.BaseColorFactor[1], this.BaseColorFactor[2], this.BaseColorFactor[3])
                                :new Vector4(1f,1f,1f,1f);

        if(PositionValues.Length==0){
            throw new InvalidOperationException(VsixText.Get("Bridge_MeshHasNoVertices"));
        }

        (var Center,var Radius)=SimpleMesh.ComputeBoundingSphere(PositionValues);

        return new SimpleMesh {
            Positions=PositionValues,
            Normals=NormalValues,
            Indices= this.Indices,
            BaseColorFactor=BaseColorValues,
            BoundsCenter=Center,
            BoundsRadius=Radius,
        };
    }

    private static Vector3[]ToVector3Array(float[]Flat){
        var Result=new Vector3[Flat.Length/3];
        for(var I=0;I<Result.Length;I++){
            Result[I]=new Vector3(Flat[(I*3)+0],Flat[(I*3)+1],Flat[(I*3)+2]);
        }

        return Result;
    }
}
