using System.Numerics;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

public class ProceduralMeshFactoryTests
{
    [Fact]
    public void CreatePlane_ProducesFourVerticesOnYZeroFacingUp()
    {
        ProceduralMeshData mesh = ProceduralMeshFactory.CreatePlane(width: 10f, depth: 4f);

        Assert.Equal(4, mesh.Positions.Length);
        Assert.Equal(4, mesh.Normals.Length);
        Assert.Equal(6, mesh.Indices.Length);

        foreach (Vector3 position in mesh.Positions)
        {
            Assert.Equal(0f, position.Y);
            Assert.InRange(position.X, -5f, 5f);
            Assert.InRange(position.Z, -2f, 2f);
        }

        foreach (Vector3 normal in mesh.Normals)
        {
            Assert.Equal(Vector3.UnitY, normal);
        }
    }

    [Fact]
    public void CreatePlane_IndicesFormTwoValidTriangles()
    {
        ProceduralMeshData mesh = ProceduralMeshFactory.CreatePlane(width: 2f, depth: 2f);

        Assert.Equal(6, mesh.Indices.Length);
        foreach (uint index in mesh.Indices)
        {
            Assert.InRange(index, 0u, (uint)mesh.Positions.Length - 1);
        }
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 0f)]
    [InlineData(-1f, 1f)]
    public void CreatePlane_NonPositiveDimensions_Throws(float width, float depth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralMeshFactory.CreatePlane(width, depth));
    }

    [Fact]
    public void CreateBox_Produces24VerticesAnd36Indices()
    {
        ProceduralMeshData mesh = ProceduralMeshFactory.CreateBox(new Vector3(2f, 4f, 6f));

        Assert.Equal(24, mesh.Positions.Length);
        Assert.Equal(24, mesh.Normals.Length);
        Assert.Equal(36, mesh.Indices.Length);
    }

    [Fact]
    public void CreateBox_AllVerticesStayWithinHalfExtents()
    {
        Vector3 size = new(2f, 4f, 6f);
        ProceduralMeshData mesh = ProceduralMeshFactory.CreateBox(size);
        Vector3 half = size / 2f;

        foreach (Vector3 position in mesh.Positions)
        {
            Assert.InRange(position.X, -half.X, half.X);
            Assert.InRange(position.Y, -half.Y, half.Y);
            Assert.InRange(position.Z, -half.Z, half.Z);
        }
    }

    [Fact]
    public void CreateBox_AllNormalsAreAxisAlignedUnitVectors()
    {
        ProceduralMeshData mesh = ProceduralMeshFactory.CreateBox(Vector3.One);

        foreach (Vector3 normal in mesh.Normals)
        {
            Assert.Equal(1f, normal.Length(), precision: 5);

            int nonZeroComponents = (normal.X != 0f ? 1 : 0) + (normal.Y != 0f ? 1 : 0) + (normal.Z != 0f ? 1 : 0);
            Assert.Equal(1, nonZeroComponents);
        }
    }

    [Fact]
    public void CreateBox_IndicesAreAllWithinBounds()
    {
        ProceduralMeshData mesh = ProceduralMeshFactory.CreateBox(Vector3.One);

        foreach (uint index in mesh.Indices)
        {
            Assert.InRange(index, 0u, (uint)mesh.Positions.Length - 1);
        }
    }

    [Theory]
    [InlineData(0f, 1f, 1f)]
    [InlineData(1f, 0f, 1f)]
    [InlineData(1f, 1f, 0f)]
    [InlineData(-1f, 1f, 1f)]
    public void CreateBox_NonPositiveSizeComponent_Throws(float x, float y, float z)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralMeshFactory.CreateBox(new Vector3(x, y, z)));
    }
}
