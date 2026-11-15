using CSharpGameEngine.Import.Gltf;

namespace CSharpGameEngine.Import.Gltf.Cli;

/// <summary>
/// <see cref="ImportedMesh"/> を平坦で JSON に適した形式にしたもの。<c>CSharpGameEngine.Import.Fbx.Cli</c> の <c>FbxBridgeDocument</c> に相当する (ADR-0010)。net472 の VSIX 側が <c>System.Numerics.Vector3</c>/<c>Vector4</c> のシリアル化動作や、この net10.0 アセンブリを参照せずに済むよう、意図的にプリミティブ型（平坦な float 配列、独自型なし）に限定する。
/// </summary>
public sealed class GltfBridgeDocument {
    public bool Success { get; init;}

    public float[]Positions { get; init;}=[];

    public float[]Normals { get; init;}=[];

    public float[]Uvs { get; init;}=[];

    public uint[]Indices { get; init;}=[];

    public float[]BaseColorFactor { get; init;}=[1f,1f,1f,1f];

    public static GltfBridgeDocument ForMesh(ImportedMesh Mesh)=>new(){
        Success=true,
        Positions=Flatten3(Mesh.Positions),
        Normals=Flatten3(Mesh.Normals),
        Uvs=Flatten2(Mesh.Uvs),
        Indices=[.. Mesh.Indices],
        BaseColorFactor=[Mesh.BaseColorFactor.X,Mesh.BaseColorFactor.Y,Mesh.BaseColorFactor.Z,Mesh.BaseColorFactor.W],
    };

    public static GltfBridgeDocument ForFailure()=>new(){ Success=false };

    private static float[]Flatten3(IReadOnlyList<System.Numerics.Vector3>Values){
        var Flat=new float[Values.Count*3];
        for(var I=0;I<Values.Count;I++){
            Flat[(I*3)+0]=Values[I].X;
            Flat[(I*3)+1]=Values[I].Y;
            Flat[(I*3)+2]=Values[I].Z;
        }

        return Flat;
    }

    private static float[]Flatten2(IReadOnlyList<System.Numerics.Vector2>Values){
        var Flat=new float[Values.Count*2];
        for(var I=0;I<Values.Count;I++){
            Flat[(I*2)+0]=Values[I].X;
            Flat[(I*2)+1]=Values[I].Y;
        }

        return Flat;
    }
}
