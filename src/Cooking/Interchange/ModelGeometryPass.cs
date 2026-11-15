using System.Collections.Immutable;
using System.Numerics;
using CSharpGameEngine.Cooking;
namespace CSharpGameEngine.Interchange;
internal static class ModelGeometryPass {
    internal static InterchangeMesh Complete(InterchangeMesh Mesh,Action<ImportSeverity,string,string>Report){
        var V=Mesh.Vertices;
        if(V.Normals.IsEmpty){
            var Map=Mesh.Indices.Select(I=>(int)I).ToArray();
            ImmutableArray<T>Remap<T>(ImmutableArray<T>Values)=>Values.IsEmpty?[]:[..Map.Select(I=>Values[I])];
            var Positions=Remap(V.Positions);
            var Normals=new Vector3[Map.Length];
            for(var Index=0;Index<Map.Length;Index+=3){
                var N=Vector3.Cross(Positions[Index+1]-Positions[Index],Positions[Index+2]-Positions[Index]);
                N=N.LengthSquared()>1e-12f?Vector3.Normalize(N):Vector3.UnitY;
                Normals[Index]=N;Normals[Index+1]=N;Normals[Index+2]=N;
            }
            InterchangeInfluences?Influences=null;
            if(V.Influences is{ }I){
                Influences=new(I.PerVertex,[..Map.SelectMany(Vertex=>I.Joints.Skip(Vertex*I.PerVertex).Take(I.PerVertex))],[..Map.SelectMany(Vertex=>I.Weights.Skip(Vertex*I.PerVertex).Take(I.PerVertex))]);
            }
            Mesh=Mesh with {
                Vertices=V with { Count=Map.Length,Positions=Positions,Normals=[..Normals],Tangents=Remap(V.Tangents),TexCoords=[..V.TexCoords.Select(Remap)],Colors=[..V.Colors.Select(Remap)],Influences=Influences,
                    Custom=[..V.Custom.Select(C=>C with { Values=[..Map.SelectMany(Vertex=>C.Values.Skip(Vertex*C.Components).Take(C.Components))] })] },
                Indices=[..Enumerable.Range(0,Map.Length).Select(I=>(uint)I)],
                MorphTargets=[..Mesh.MorphTargets.Select(M=>{
                    var Sparse=M.SparseVertices.Select((Vertex,Index)=>(Vertex,Index)).ToDictionary(P=>P.Vertex,P=>P.Index);
                    ImmutableArray<Vector3>Deltas(ImmutableArray<Vector3>Values)=>Values.IsEmpty?[]:[..Map.Select(Vertex=>M.SparseVertices.IsEmpty?Values[Vertex]:Sparse.TryGetValue(Vertex,out var Index)?Values[Index]:Vector3.Zero)];
                    return M with { SparseVertices=[],PositionDeltas=Deltas(M.PositionDeltas),NormalDeltas=Deltas(M.NormalDeltas),TangentDeltas=Deltas(M.TangentDeltas),
                        InBetweens=[..M.InBetweens.Select(B=>B with { PositionDeltas=Deltas(B.PositionDeltas),NormalDeltas=Deltas(B.NormalDeltas),TangentDeltas=Deltas(B.TangentDeltas) })] };
                })]
            };
            V=Mesh.Vertices;
            Report(ImportSeverity.Warning,"IMP110",$"'{Mesh.Name}': generated flat normals and expanded vertex streams.");
        }
        if(V.TexCoords.IsEmpty){ V=V with { TexCoords=[new Vector2[V.Count].ToImmutableArray()] }; }
        if(V.Tangents.IsEmpty){
            var Tan=new Vector3[V.Count];
            var Bitan=new Vector3[V.Count];
            var Uvs=V.TexCoords[0];
            for(var Index=0;Index+2<Mesh.Indices.Length;Index+=3){
                var I0=(int)Mesh.Indices[Index];var I1=(int)Mesh.Indices[Index+1];var I2=(int)Mesh.Indices[Index+2];
                var E1=V.Positions[I1]-V.Positions[I0];var E2=V.Positions[I2]-V.Positions[I0];
                var U1=Uvs[I1]-Uvs[I0];var U2=Uvs[I2]-Uvs[I0];
                var Denominator=U1.X*U2.Y-U2.X*U1.Y;
                if(MathF.Abs(Denominator)<1e-12f){ continue; }
                var F=1f/Denominator;
                var T=F*(U2.Y*E1-U1.Y*E2);var B=F*(U1.X*E2-U2.X*E1);
                Tan[I0]+=T;Tan[I1]+=T;Tan[I2]+=T;Bitan[I0]+=B;Bitan[I1]+=B;Bitan[I2]+=B;
            }
            var Tangents=new Vector4[V.Count];
            for(var Index=0;Index<V.Count;Index++){
                var N=V.Normals[Index];var T=Tan[Index];var Ortho=T-N*Vector3.Dot(N,T);
                var Safe=Ortho.LengthSquared()>1e-12f?Vector3.Normalize(Ortho):Vector3.Normalize(Vector3.Cross(MathF.Abs(N.Y)<0.99f?Vector3.UnitY:Vector3.UnitX,N));
                Tangents[Index]=new(Safe,Vector3.Dot(Vector3.Cross(N,Safe),Bitan[Index])<0f?-1f:1f);
            }
            V=V with { Tangents=[..Tangents] };
            Report(ImportSeverity.Info,"IMP110",$"'{Mesh.Name}': generated v1 Lengyel tangents.");
        }
        return Mesh with { Vertices=V };
    }
}
