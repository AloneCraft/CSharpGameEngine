using System.Numerics;
using CSharpGameEngine.Geometry;

namespace CSharpGameEngine.Geometry.Tests;

public class ProceduralMeshFactoryTests {
    private static void AssertIndicesInRange(ProceduralMeshData Mesh){
        foreach(var Index in Mesh.Indices){
            Assert.InRange(Index,0u,(uint)Mesh.Positions.Length-1);
        }
    }

    private static void AssertNoDegenerateTriangles(ProceduralMeshData Mesh){
        Assert.True(Mesh.Indices.Length%3==0,"Index count must be a multiple of 3.");

        for(var I=0;I<Mesh.Indices.Length;I+=3){
            var A=Mesh.Positions[Mesh.Indices[I]];
            var B=Mesh.Positions[Mesh.Indices[I+1]];
            var C=Mesh.Positions[Mesh.Indices[I+2]];

            var Area=Vector3.Cross(B-A,C-A).Length()/2f;
            Assert.True(Area>1e-6f, $"Degenerate triangle at index {I} (area={Area}).");
        }
    }

    private static void AssertNormalsAreUnitLength(ProceduralMeshData Mesh){
        foreach(var Normal in Mesh.Normals){
            Assert.Equal(1f,Normal.Length(),precision:4);
        }
    }

    [Fact]
    public void CreatePlane_ProducesFourVerticesOnYZeroFacingUp(){
        var Mesh=ProceduralMeshFactory.CreatePlane(Width:10f,Depth:4f);

        Assert.Equal(4,Mesh.Positions.Length);
        Assert.Equal(4,Mesh.Normals.Length);
        Assert.Equal(6,Mesh.Indices.Length);

        foreach(var Position in Mesh.Positions){
            Assert.Equal(0f,Position.Y);
            Assert.InRange(Position.X,-5f,5f);
            Assert.InRange(Position.Z,-2f,2f);
        }

        foreach(var Normal in Mesh.Normals){
            Assert.Equal(Vector3.UnitY,Normal);
        }
    }

    [Fact]
    public void CreatePlane_IndicesFormTwoValidTriangles(){
        var Mesh=ProceduralMeshFactory.CreatePlane(Width:2f,Depth:2f);

        Assert.Equal(6,Mesh.Indices.Length);
        AssertIndicesInRange(Mesh);
        AssertNoDegenerateTriangles(Mesh);
    }

    [Theory]
    [InlineData(0f,1f)]
    [InlineData(1f,0f)]
    [InlineData(-1f,1f)]
    public void CreatePlane_NonPositiveDimensions_Throws(float Width,float Depth){
        Assert.Throws<ArgumentOutOfRangeException>(()=>ProceduralMeshFactory.CreatePlane(Width,Depth));
    }

    [Fact]
    public void CreateBox_Produces24VerticesAnd36Indices(){
        var Mesh=ProceduralMeshFactory.CreateBox(new Vector3(2f,4f,6f));

        Assert.Equal(24,Mesh.Positions.Length);
        Assert.Equal(24,Mesh.Normals.Length);
        Assert.Equal(36,Mesh.Indices.Length);
        AssertIndicesInRange(Mesh);
        AssertNoDegenerateTriangles(Mesh);
    }

    [Fact]
    public void CreateBox_AllVerticesStayWithinHalfExtents(){
        Vector3 Size=new(2f,4f,6f);
        var Mesh=ProceduralMeshFactory.CreateBox(Size);
        var Half=Size/2f;

        foreach(var Position in Mesh.Positions){
            Assert.InRange(Position.X,-Half.X,Half.X);
            Assert.InRange(Position.Y,-Half.Y,Half.Y);
            Assert.InRange(Position.Z,-Half.Z,Half.Z);
        }
    }

    [Fact]
    public void CreateBox_AllNormalsAreAxisAlignedUnitVectors(){
        var Mesh=ProceduralMeshFactory.CreateBox(Vector3.One);

        foreach(var Normal in Mesh.Normals){
            Assert.Equal(1f,Normal.Length(),precision:5);

            var NonZeroComponents=(Normal.X!=0f?1:0)+(Normal.Y!=0f?1:0)+(Normal.Z!=0f?1:0);
            Assert.Equal(1,NonZeroComponents);
        }
    }

    [Theory]
    [InlineData(0f,1f,1f)]
    [InlineData(1f,0f,1f)]
    [InlineData(1f,1f,0f)]
    [InlineData(-1f,1f,1f)]
    public void CreateBox_NonPositiveSizeComponent_Throws(float X,float Y,float Z){
        Assert.Throws<ArgumentOutOfRangeException>(()=>ProceduralMeshFactory.CreateBox(new Vector3(X,Y,Z)));
    }

    [Fact]
    public void CreateSphere_AllVerticesLieOnTheSphereSurface(){
        var Mesh=ProceduralMeshFactory.CreateSphere(2.5f);

        foreach(var Position in Mesh.Positions){
            Assert.Equal(2.5f,Position.Length(),precision:4);
        }

        AssertIndicesInRange(Mesh);
        AssertNoDegenerateTriangles(Mesh);
        AssertNormalsAreUnitLength(Mesh);
    }

    [Fact]
    public void CreateSphere_NonPositiveRadius_Throws(){
        Assert.Throws<ArgumentOutOfRangeException>(()=>ProceduralMeshFactory.CreateSphere(0f));
    }

    [Fact]
    public void CreateCylinder_VerticesStayWithinRadiusAndHalfHeight(){
        var Mesh=ProceduralMeshFactory.CreateCylinder(Radius:1.5f,Height:4f);

        foreach(var Position in Mesh.Positions){
            Assert.InRange(new Vector2(Position.X,Position.Z).Length(),0f,1.5f+1e-4f);
            Assert.InRange(Position.Y,-2f-1e-4f,2f+1e-4f);
        }

        AssertIndicesInRange(Mesh);
        AssertNoDegenerateTriangles(Mesh);
        AssertNormalsAreUnitLength(Mesh);
    }

    [Theory]
    [InlineData(0f,1f)]
    [InlineData(1f,0f)]
    public void CreateCylinder_NonPositiveDimension_Throws(float Radius,float Height){
        Assert.Throws<ArgumentOutOfRangeException>(()=>ProceduralMeshFactory.CreateCylinder(Radius,Height));
    }

    [Fact]
    public void CreateCone_ApexAtTopAndBaseAtBottom(){
        var Mesh=ProceduralMeshFactory.CreateCone(Radius:1f,Height:3f);

        Assert.Contains(Mesh.Positions,P=>P==new Vector3(0,1.5f,0));
        foreach(var Position in Mesh.Positions){
            Assert.InRange(Position.Y,-1.5f-1e-4f,1.5f+1e-4f);
        }

        AssertIndicesInRange(Mesh);
        AssertNoDegenerateTriangles(Mesh);
        AssertNormalsAreUnitLength(Mesh);
    }

    [Theory]
    [InlineData(0f,1f)]
    [InlineData(1f,0f)]
    public void CreateCone_NonPositiveDimension_Throws(float Radius,float Height){
        Assert.Throws<ArgumentOutOfRangeException>(()=>ProceduralMeshFactory.CreateCone(Radius,Height));
    }

    [Fact]
    public void CreateTorus_VerticesStayWithinMajorPlusMinorRadius(){
        var Mesh=ProceduralMeshFactory.CreateTorus(MajorRadius:2f,MinorRadius:0.5f);

        foreach(var Position in Mesh.Positions){
            Assert.InRange(new Vector2(Position.X,Position.Z).Length(),2f-0.5f-1e-3f,2f+0.5f+1e-3f);
            Assert.InRange(Position.Y,-0.5f-1e-4f,0.5f+1e-4f);
        }

        AssertIndicesInRange(Mesh);
        AssertNoDegenerateTriangles(Mesh);
        AssertNormalsAreUnitLength(Mesh);
    }

    [Fact]
    public void CreateTorus_MinorRadiusNotSmallerThanMajor_Throws(){
        Assert.Throws<ArgumentOutOfRangeException>(()=>ProceduralMeshFactory.CreateTorus(MajorRadius:1f,MinorRadius:1f));
    }

    [Theory]
    [InlineData(0f,0.5f)]
    [InlineData(1f,0f)]
    public void CreateTorus_NonPositiveDimension_Throws(float MajorRadius,float MinorRadius){
        Assert.Throws<ArgumentOutOfRangeException>(()=>ProceduralMeshFactory.CreateTorus(MajorRadius,MinorRadius));
    }

    [Fact]
    public void CreateDisk_AllVerticesOnYZeroFacingUp(){
        var Mesh=ProceduralMeshFactory.CreateDisk(Radius:1.2f);

        foreach(var Position in Mesh.Positions){
            Assert.Equal(0f,Position.Y);
            Assert.InRange(new Vector2(Position.X,Position.Z).Length(),0f,1.2f+1e-4f);
        }

        foreach(var Normal in Mesh.Normals){
            Assert.Equal(Vector3.UnitY,Normal);
        }

        AssertIndicesInRange(Mesh);
        AssertNoDegenerateTriangles(Mesh);
    }

    [Fact]
    public void CreateDisk_NonPositiveRadius_Throws(){
        Assert.Throws<ArgumentOutOfRangeException>(()=>ProceduralMeshFactory.CreateDisk(0f));
    }

    [Fact]
    public void CreateCapsule_VerticesStayWithinRadiusAndHalfHeight(){
        var Mesh=ProceduralMeshFactory.CreateCapsule(Radius:1f,Height:4f);

        Assert.Contains(Mesh.Positions,P=>P==new Vector3(0,2f,0));
        Assert.Contains(Mesh.Positions,P=>P==new Vector3(0,-2f,0));

        foreach(var Position in Mesh.Positions){
            Assert.InRange(new Vector2(Position.X,Position.Z).Length(),0f,1f+1e-4f);
            Assert.InRange(Position.Y,-2f-1e-4f,2f+1e-4f);
        }

        AssertIndicesInRange(Mesh);
        AssertNoDegenerateTriangles(Mesh);
        AssertNormalsAreUnitLength(Mesh);
    }

    [Fact]
    public void CreateCapsule_HeightNotExceedingTwiceRadius_Throws(){
        Assert.Throws<ArgumentOutOfRangeException>(()=>ProceduralMeshFactory.CreateCapsule(Radius:1f,Height:2f));
    }

    [Theory]
    [InlineData(0f,4f)]
    [InlineData(-1f,4f)]
    public void CreateCapsule_NonPositiveRadius_Throws(float Radius,float Height){
        Assert.Throws<ArgumentOutOfRangeException>(()=>ProceduralMeshFactory.CreateCapsule(Radius,Height));
    }
}
