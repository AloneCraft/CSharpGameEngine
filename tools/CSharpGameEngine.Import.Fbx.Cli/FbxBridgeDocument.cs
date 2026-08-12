using CSharpGameEngine.Import.Fbx;

namespace CSharpGameEngine.Import.Fbx.Cli;

/// <summary>
/// Flat, JSON-friendly view of an <see cref="ImportedMesh"/> (ADR-0010). This is the sole contract
/// between this out-of-process bridge and the VSIX's <c>ExternalFbxLoader</c> - kept deliberately
/// primitive (flat float arrays, no custom types) so the net472 VSIX side never needs to reference
/// <c>System.Numerics.Vector3</c>/<c>Vector4</c> serialization behavior or this net10.0 assembly.
/// </summary>
public sealed class FbxBridgeDocument {
    public bool Success { get; init;}

    public float[]Positions { get; init;}=[];

    public float[]Normals { get; init;}=[];

    public float[]Uvs { get; init;}=[];

    public uint[]Indices { get; init;}=[];

    public float[]BaseColorFactor { get; init;}=[1f,1f,1f,1f];

    public static FbxBridgeDocument ForMesh(ImportedMesh Mesh)=>new(){
        Success=true,
        Positions=Flatten3(Mesh.Positions),
        Normals=Flatten3(Mesh.Normals),
        Uvs=Flatten2(Mesh.Uvs),
        Indices=[.. Mesh.Indices],
        BaseColorFactor=[Mesh.BaseColorFactor.X,Mesh.BaseColorFactor.Y,Mesh.BaseColorFactor.Z,Mesh.BaseColorFactor.W],
    };

    public static FbxBridgeDocument ForFailure()=>new(){ Success=false };

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
