using System;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// VSIX-side deserialization target for both <c>CSharpGameEngine.Import.Fbx.Cli</c>'s
/// <c>FbxBridgeDocument</c> (ADR-0010) and <c>CSharpGameEngine.Import.Gltf.Cli</c>'s
/// <c>GltfBridgeDocument</c> (ADR-0019) - both bridges produce the identical flat JSON shape, so
/// <see cref="ExternalFbxLoader"/> and <see cref="ExternalGltfLoader"/> share this one type and its
/// <see cref="ToSimpleMesh"/> conversion instead of duplicating it per format. <c>Uvs</c> is
/// intentionally not modeled here: this VSIX's renderers have no texture support.
/// </summary>
internal sealed class BridgeDocument {
    public bool Success { get; set;}

    public float[]Positions { get; set;}=[];

    public float[]Normals { get; set;}=[];

    public uint[]Indices { get; set;}=[];

    public float[]BaseColorFactor { get; set;}=[];

    public SimpleMesh ToSimpleMesh(){
        Vector3[]positions=ToVector3Array(Positions);
        Vector3[]normals=ToVector3Array(Normals);
        Vector4 baseColorFactor=BaseColorFactor.Length==4
            ?new Vector4(BaseColorFactor[0],BaseColorFactor[1],BaseColorFactor[2],BaseColorFactor[3])
            :new Vector4(1f,1f,1f,1f);

        if(positions.Length==0){
            throw new InvalidOperationException("インポートされたメッシュに頂点がありません。");
        }

        (Vector3 Center,var Radius)=SimpleMesh.ComputeBoundingSphere(positions);

        return new SimpleMesh {
            Positions=positions,
            Normals=normals,
            Indices=Indices,
            BaseColorFactor=baseColorFactor,
            BoundsCenter=Center,
            BoundsRadius=Radius,
        };
    }

    private static Vector3[]ToVector3Array(float[]Flat){
        Vector3[]Result=new Vector3[Flat.Length/3];
        for(var i=0;i<Result.Length;i++){
            Result[i]=new Vector3(Flat[(i*3)+0],Flat[(i*3)+1],Flat[(i*3)+2]);
        }

        return Result;
    }
}
