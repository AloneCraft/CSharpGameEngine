using System;
using System.Numerics;
using System.Collections.Generic;
using System.IO;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension;

/// <summary>外部ホストから受け取る描画・選択用 CPU 形状。共通モデルの階層変換を保持し、複数材質は Parts に分ける。UV/テクスチャはこの DTO へ転送しない。</summary>
internal sealed class SimpleMesh {
    public SimpleMesh[]Parts { get;init;}=[];
    public RenderPreviewMaterial?Material { get;init;}
    internal static SimpleMesh FromPreparedScene(RenderPreviewScene Scene){
        var Parts=new List<SimpleMesh>();var AllPositions=new List<Vector3>();var AllNormals=new List<Vector3>();var AllIndices=new List<uint>();
        foreach(var Object in Scene.Objects){
            var Source=Scene.Meshes[Object.MeshIndex];var Values=Object.World.Values;
            if(Values.Length!=16||Source.Positions.Length==0||Source.Positions.Length%3!=0||Source.Normals.Length!=Source.Positions.Length){ throw new InvalidDataException("Invalid prepared preview geometry."); }
            var World=new Matrix4x4(Values[0],Values[1],Values[2],Values[3],Values[4],Values[5],Values[6],Values[7],Values[8],Values[9],Values[10],Values[11],Values[12],Values[13],Values[14],Values[15]);
            if(!Matrix4x4.Invert(World,out var Inverse)){ throw new InvalidDataException("Prepared preview has a singular transform."); }
            var NormalMatrix=Matrix4x4.Transpose(Inverse);var Positions=new Vector3[Source.Positions.Length/3];var Normals=new Vector3[Positions.Length];
            for(var Index=0;Index<Positions.Length;Index++){
                Positions[Index]=Vector3.Transform(new(Source.Positions[Index*3],Source.Positions[Index*3+1],Source.Positions[Index*3+2]),World);
                Normals[Index]=Vector3.Normalize(Vector3.TransformNormal(new(Source.Normals[Index*3],Source.Normals[Index*3+1],Source.Normals[Index*3+2]),NormalMatrix));
            }
            var Indices=(uint[])Source.Indices.Clone();
            if(World.GetDeterminant()<0f){ for(var Index=0;Index<Indices.Length;Index+=3){ (Indices[Index+1],Indices[Index+2])=(Indices[Index+2],Indices[Index+1]); } }
            var Material=Scene.Materials[Object.MaterialIndex];var Color=Material.BaseColor;var Bounds=ComputeBoundingSphere(Positions);
            Parts.Add(new(){Positions=Positions,Normals=Normals,Indices=Indices,BaseColorFactor=new(Color[0],Color[1],Color[2],Color[3]),Material=Material,BoundsCenter=Bounds.Center,BoundsRadius=Bounds.Radius});
            var Offset=(uint)AllPositions.Count;AllPositions.AddRange(Positions);AllNormals.AddRange(Normals);foreach(var Index in Indices){ AllIndices.Add(checked(Offset+Index)); }
        }
        if(Parts.Count==0){ throw new InvalidDataException("Prepared preview has no drawable geometry."); }
        if(Parts.Count==1){ return Parts[0]; }
        var AggregateBounds=ComputeBoundingSphere(AllPositions.ToArray());
        return new(){Positions=AllPositions.ToArray(),Normals=AllNormals.ToArray(),Indices=AllIndices.ToArray(),BaseColorFactor=Parts[0].BaseColorFactor,BoundsCenter=AggregateBounds.Center,BoundsRadius=AggregateBounds.Radius,Parts=Parts.ToArray()};
    }
    public required Vector3[]Positions { get; init;}

    public required Vector3[]Normals { get; init;}

    public required uint[]Indices { get; init;}

    public required Vector4 BaseColorFactor { get; init;}

    public required Vector3 BoundsCenter { get; init;}

    public required float BoundsRadius { get; init;}

    public static(Vector3 Center,float Radius)ComputeBoundingSphere(Vector3[]Positions){
        var Min=Positions[0];
        var Max=Positions[0];
        foreach(var P in Positions){
            Min=Vector3.Min(Min,P);
            Max=Vector3.Max(Max,P);
        }

        var Center=(Min+Max)*0.5f;
        var Radius=0f;
        foreach(var P in Positions){
            Radius=Math.Max(Radius,Vector3.Distance(Center,P));
        }

        return(Center,Radius);
    }
}
