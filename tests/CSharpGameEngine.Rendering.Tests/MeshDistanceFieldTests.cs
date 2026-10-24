using System.Numerics;
namespace CSharpGameEngine.Rendering.Tests;
/// <summary>M0110: mesh signed distance fields made ahead of the game from the triangles, and their file.</summary>
public sealed class MeshDistanceFieldTests {
    [Fact]
    public void Cube_HasTheBoxDistanceWithTheRightSignEverywhere(){
        var (Positions,Indices)=Box(new Vector3(1f,1f,1f));
        var Field=MeshDistanceFieldBuilder.Build(Positions,Indices)!;
        Assert.NotNull(Field);
        var Worst=0f;
        for(var Z=0;Z<Field.ResolutionZ;Z++){
            for(var Y=0;Y<Field.ResolutionY;Y++){
                for(var X=0;X<Field.ResolutionX;X++){
                    var Centre=Field.BoundsMin+(new Vector3(X,Y,Z)+new Vector3(0.5f))*Field.VoxelSize;
                    var Expected=BoxDistance(Centre,new Vector3(0.5f));
                    var Actual=Field.VoxelDistance(X,Y,Z);
                    Assert.Equal(MathF.Sign(Expected),MathF.Sign(Actual));
                    Worst=MathF.Max(Worst,MathF.Abs(Actual-Expected));
                }
            }
        }
        // Exact distances to the triangles, stored as half floats in voxels (11 bits).
        Assert.True(Worst<Field.VoxelSize*0.01f,$"worst {Worst}");
        // Between voxel centres the field is trilinear: within a voxel of the true distance.
        AssertNear(-0.5f,Field.Sample(Vector3.Zero),Field.VoxelSize);
        AssertNear(0f,Field.Sample(new Vector3(0.5f,0f,0f)),Field.VoxelSize);
        // Outside the volume the distance to the volume's box is added: 3 m from the face is about 3 m.
        AssertNear(3f,Field.Sample(new Vector3(3.5f,0f,0f)),Field.VoxelSize*3f);
    }
    [Fact]
    public void Sphere_IsNegativeInsideAndPositiveOutside(){
        var (Positions,Indices)=Sphere(2f,24,48);
        var Field=MeshDistanceFieldBuilder.Build(Positions,Indices)!;
        for(var Z=0;Z<Field.ResolutionZ;Z++){
            for(var Y=0;Y<Field.ResolutionY;Y++){
                for(var X=0;X<Field.ResolutionX;X++){
                    var Centre=Field.BoundsMin+(new Vector3(X,Y,Z)+new Vector3(0.5f))*Field.VoxelSize;
                    var Expected=Centre.Length()-2f;
                    // The faceted sphere is a little inside the true one; away from the surface the sign is exact.
                    if(MathF.Abs(Expected)<0.05f){ continue; }
                    Assert.Equal(MathF.Sign(Expected),MathF.Sign(Field.VoxelDistance(X,Y,Z)));
                    Assert.InRange(Field.VoxelDistance(X,Y,Z)-Expected,-0.05f,0.05f);
                }
            }
        }
    }
    [Fact]
    public void OpenSheet_StaysUnsignedOnBothSides(){
        Vector3[]Positions=[new(-1f,0f,-1f),new(1f,0f,-1f),new(1f,0f,1f),new(-1f,0f,1f)];
        uint[]Indices=[0,2,1,0,3,2];
        var Field=MeshDistanceFieldBuilder.Build(Positions,Indices)!;
        AssertNear(0.3f,Field.Sample(new Vector3(0.2f,0.3f,0.1f)),Field.VoxelSize);
        AssertNear(0.3f,Field.Sample(new Vector3(0.2f,-0.3f,0.1f)),Field.VoxelSize);
    }
    [Fact]
    public void Build_IsDeterministicAndRoundTripsThroughTheFile(){
        var (Positions,Indices)=Sphere(1.3f,10,20);
        var First=MeshDistanceFieldBuilder.Build(Positions,Indices)!;
        var Second=MeshDistanceFieldBuilder.Build(Positions,Indices)!;
        Assert.Equal(First.Distances.ToArray(),Second.Distances.ToArray());
        Assert.Equal(MeshDistanceField.ComputeMeshHash(Positions,Indices),First.MeshHash);
        var (EmptyPositions,EmptyIndices)=(new Vector3[]{ Vector3.Zero },Array.Empty<uint>());
        var Bytes=MeshDistanceFieldCodec.Write([(First.MeshHash,First),(MeshDistanceField.ComputeMeshHash(EmptyPositions,EmptyIndices),null)]);
        Assert.Equal(Bytes,MeshDistanceFieldCodec.Write([(Second.MeshHash,Second),(MeshDistanceField.ComputeMeshHash(EmptyPositions,EmptyIndices),null)]));
        Assert.True(MeshDistanceFieldCodec.TryRead(Bytes,out var Entries,out var Error),Error);
        Assert.Equal(2,Entries.Count);
        var Read=Entries[0].Field!;
        Assert.Equal((First.ResolutionX,First.ResolutionY,First.ResolutionZ,First.BoundsMin,First.VoxelSize),(Read.ResolutionX,Read.ResolutionY,Read.ResolutionZ,Read.BoundsMin,Read.VoxelSize));
        Assert.Equal(First.Distances.ToArray(),Read.Distances.ToArray());
        Assert.Null(Entries[1].Field);
    }
    [Fact]
    public void EmptyAndDegenerateMeshes_HaveNoField(){
        Assert.Null(MeshDistanceFieldBuilder.Build([],[]));
        Assert.Null(MeshDistanceFieldBuilder.Build([Vector3.Zero,Vector3.UnitX,new Vector3(2f,0f,0f)],[0,1,2]));
        Assert.Null(MeshDistanceFieldBuilder.Build([Vector3.Zero],[0,0,0]));
    }
    [Fact]
    public void InvalidMeshes_Throw(){
        Assert.Throws<ArgumentException>(()=>MeshDistanceFieldBuilder.Build([Vector3.Zero,new Vector3(float.NaN,0f,0f),Vector3.UnitY],[0,1,2]));
        Assert.Throws<ArgumentException>(()=>MeshDistanceFieldBuilder.Build([Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[0,1,3]));
        Assert.Throws<ArgumentException>(()=>MeshDistanceFieldBuilder.Build([Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[0,1]));
        Assert.Throws<ArgumentOutOfRangeException>(()=>MeshDistanceFieldBuilder.Build([Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[0,1,2],MaximumResolution:4));
    }
    [Fact]
    public void HugeAndThinMeshes_KeepFiniteDistancesAndTheirShape(){
        var (Huge,HugeIndices)=Box(new Vector3(200000f,50000f,200000f));
        var Field=MeshDistanceFieldBuilder.Build(Huge,HugeIndices)!;
        Assert.All(Field.Distances.ToArray(),Value=>Assert.True(Half.IsFinite(Value)));
        AssertNear(-25000f,Field.Sample(Vector3.Zero),Field.VoxelSize);
        Assert.Equal(DefaultLongestSide,Field.ResolutionX);
        // A 20 x 0.2 x 20 m floor: the short axis keeps its two voxels of margin on each side.
        var (Floor,FloorIndices)=Box(new Vector3(20f,0.2f,20f));
        var Plate=MeshDistanceFieldBuilder.Build(Floor,FloorIndices)!;
        Assert.InRange(Plate.ResolutionY,5,7);
        AssertNear(1.1f,Plate.Sample(new Vector3(0f,1.2f,0f)),Plate.VoxelSize*2f);
    }
    [Fact]
    public void File_RejectsBrokenInput(){
        var (Positions,Indices)=Box(Vector3.One);
        var Field=MeshDistanceFieldBuilder.Build(Positions,Indices)!;
        var Bytes=MeshDistanceFieldCodec.Write([(Field.MeshHash,Field)]);
        Assert.False(MeshDistanceFieldCodec.TryRead(Bytes.AsSpan(0,Bytes.Length-1),out _,out _));
        Assert.False(MeshDistanceFieldCodec.TryRead([..Bytes,0],out _,out _));
        var WrongMagic=Bytes.ToArray();
        WrongMagic[0]=(byte)'X';
        Assert.False(MeshDistanceFieldCodec.TryRead(WrongMagic,out _,out _));
        var NotFinite=Bytes.ToArray();
        // The first distance: half-float NaN.
        NotFinite[16+60]=0x00;
        NotFinite[16+61]=0x7E;
        Assert.False(MeshDistanceFieldCodec.TryRead(NotFinite,out _,out var Error));
        Assert.Contains("not finite",Error);
        Assert.Throws<ArgumentException>(()=>MeshDistanceFieldCodec.Write([(default,Field)]));
    }
    private static void AssertNear(float Expected,float Actual,float Tolerance)=>Assert.InRange(Actual,Expected-Tolerance,Expected+Tolerance);
    private const int DefaultLongestSide=MeshDistanceFieldBuilder.DefaultMaximumResolution;
    private static float BoxDistance(Vector3 Point,Vector3 HalfSize){
        var Q=Vector3.Abs(Point)-HalfSize;
        return Vector3.Max(Q,Vector3.Zero).Length()+MathF.Min(MathF.Max(Q.X,MathF.Max(Q.Y,Q.Z)),0f);
    }
    /// <summary>A closed axis-aligned box centred on the origin, outward-facing triangles.</summary>
    private static (Vector3[]Positions,uint[]Indices) Box(Vector3 Size){
        var H=Size*0.5f;
        Vector3[]Positions=[
            new(-H.X,-H.Y,-H.Z),new(H.X,-H.Y,-H.Z),new(H.X,H.Y,-H.Z),new(-H.X,H.Y,-H.Z),
            new(-H.X,-H.Y,H.Z),new(H.X,-H.Y,H.Z),new(H.X,H.Y,H.Z),new(-H.X,H.Y,H.Z),
        ];
        uint[]Indices=[0,2,1,0,3,2, 4,5,6,4,6,7, 0,1,5,0,5,4, 3,6,2,3,7,6, 0,4,7,0,7,3, 1,2,6,1,6,5];
        return (Positions,Indices);
    }
    /// <summary>A closed UV sphere.</summary>
    private static (Vector3[]Positions,uint[]Indices) Sphere(float Radius,int Rings,int Segments){
        List<Vector3>Positions=[];
        for(var Ring=0;Ring<=Rings;Ring++){
            var Theta=MathF.PI*Ring/Rings;
            for(var Segment=0;Segment<Segments;Segment++){
                var Phi=2f*MathF.PI*Segment/Segments;
                Positions.Add(new Vector3(MathF.Sin(Theta)*MathF.Cos(Phi),MathF.Cos(Theta),MathF.Sin(Theta)*MathF.Sin(Phi))*Radius);
            }
        }
        List<uint>Indices=[];
        for(var Ring=0;Ring<Rings;Ring++){
            for(var Segment=0;Segment<Segments;Segment++){
                var A=(uint)(Ring*Segments+Segment);
                var B=(uint)(Ring*Segments+(Segment+1)%Segments);
                var C=(uint)((Ring+1)*Segments+Segment);
                var D=(uint)((Ring+1)*Segments+(Segment+1)%Segments);
                Indices.AddRange([A,C,B,B,C,D]);
            }
        }
        return ([.. Positions],[.. Indices]);
    }
}
