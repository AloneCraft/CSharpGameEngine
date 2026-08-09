using System.Numerics;
using CSharpGameEngine.Geometry;

namespace CSharpGameEngine.Geometry.Tests;

public class ProceduralMeshFactoryTests
{
    private static void AssertIndicesInRange(ProceduralMeshData mesh)
    {
        foreach (uint index in mesh.Indices)
        {
            Assert.InRange(index, 0u, (uint)mesh.Positions.Length - 1);
        }
    }

    private static void AssertNoDegenerateTriangles(ProceduralMeshData mesh)
    {
        Assert.True(mesh.Indices.Length % 3 == 0, "Index count must be a multiple of 3.");

        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            Vector3 a = mesh.Positions[mesh.Indices[i]];
            Vector3 b = mesh.Positions[mesh.Indices[i + 1]];
            Vector3 c = mesh.Positions[mesh.Indices[i + 2]];

            float area = Vector3.Cross(b - a, c - a).Length() / 2f;
            Assert.True(area > 1e-6f, $"Degenerate triangle at index {i} (area={area}).");
        }
    }

    private static void AssertNormalsAreUnitLength(ProceduralMeshData mesh)
    {
        foreach (Vector3 normal in mesh.Normals)
        {
            Assert.Equal(1f, normal.Length(), precision: 4);
        }
    }

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
        AssertIndicesInRange(mesh);
        AssertNoDegenerateTriangles(mesh);
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
        AssertIndicesInRange(mesh);
        AssertNoDegenerateTriangles(mesh);
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

    [Theory]
    [InlineData(0f, 1f, 1f)]
    [InlineData(1f, 0f, 1f)]
    [InlineData(1f, 1f, 0f)]
    [InlineData(-1f, 1f, 1f)]
    public void CreateBox_NonPositiveSizeComponent_Throws(float x, float y, float z)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralMeshFactory.CreateBox(new Vector3(x, y, z)));
    }

    [Fact]
    public void CreateSphere_AllVerticesLieOnTheSphereSurface()
    {
        ProceduralMeshData mesh = ProceduralMeshFactory.CreateSphere(2.5f);

        foreach (Vector3 position in mesh.Positions)
        {
            Assert.Equal(2.5f, position.Length(), precision: 4);
        }

        AssertIndicesInRange(mesh);
        AssertNoDegenerateTriangles(mesh);
        AssertNormalsAreUnitLength(mesh);
    }

    [Fact]
    public void CreateSphere_NonPositiveRadius_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralMeshFactory.CreateSphere(0f));
    }

    [Fact]
    public void CreateCylinder_VerticesStayWithinRadiusAndHalfHeight()
    {
        ProceduralMeshData mesh = ProceduralMeshFactory.CreateCylinder(radius: 1.5f, height: 4f);

        foreach (Vector3 position in mesh.Positions)
        {
            Assert.InRange(new Vector2(position.X, position.Z).Length(), 0f, 1.5f + 1e-4f);
            Assert.InRange(position.Y, -2f - 1e-4f, 2f + 1e-4f);
        }

        AssertIndicesInRange(mesh);
        AssertNoDegenerateTriangles(mesh);
        AssertNormalsAreUnitLength(mesh);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 0f)]
    public void CreateCylinder_NonPositiveDimension_Throws(float radius, float height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralMeshFactory.CreateCylinder(radius, height));
    }

    [Fact]
    public void CreateCone_ApexAtTopAndBaseAtBottom()
    {
        ProceduralMeshData mesh = ProceduralMeshFactory.CreateCone(radius: 1f, height: 3f);

        Assert.Contains(mesh.Positions, p => p == new Vector3(0, 1.5f, 0));
        foreach (Vector3 position in mesh.Positions)
        {
            Assert.InRange(position.Y, -1.5f - 1e-4f, 1.5f + 1e-4f);
        }

        AssertIndicesInRange(mesh);
        AssertNoDegenerateTriangles(mesh);
        AssertNormalsAreUnitLength(mesh);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 0f)]
    public void CreateCone_NonPositiveDimension_Throws(float radius, float height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralMeshFactory.CreateCone(radius, height));
    }

    [Fact]
    public void CreateTorus_VerticesStayWithinMajorPlusMinorRadius()
    {
        ProceduralMeshData mesh = ProceduralMeshFactory.CreateTorus(majorRadius: 2f, minorRadius: 0.5f);

        foreach (Vector3 position in mesh.Positions)
        {
            Assert.InRange(new Vector2(position.X, position.Z).Length(), 2f - 0.5f - 1e-3f, 2f + 0.5f + 1e-3f);
            Assert.InRange(position.Y, -0.5f - 1e-4f, 0.5f + 1e-4f);
        }

        AssertIndicesInRange(mesh);
        AssertNoDegenerateTriangles(mesh);
        AssertNormalsAreUnitLength(mesh);
    }

    [Fact]
    public void CreateTorus_MinorRadiusNotSmallerThanMajor_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralMeshFactory.CreateTorus(majorRadius: 1f, minorRadius: 1f));
    }

    [Theory]
    [InlineData(0f, 0.5f)]
    [InlineData(1f, 0f)]
    public void CreateTorus_NonPositiveDimension_Throws(float majorRadius, float minorRadius)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralMeshFactory.CreateTorus(majorRadius, minorRadius));
    }

    [Fact]
    public void CreateDisk_AllVerticesOnYZeroFacingUp()
    {
        ProceduralMeshData mesh = ProceduralMeshFactory.CreateDisk(radius: 1.2f);

        foreach (Vector3 position in mesh.Positions)
        {
            Assert.Equal(0f, position.Y);
            Assert.InRange(new Vector2(position.X, position.Z).Length(), 0f, 1.2f + 1e-4f);
        }

        foreach (Vector3 normal in mesh.Normals)
        {
            Assert.Equal(Vector3.UnitY, normal);
        }

        AssertIndicesInRange(mesh);
        AssertNoDegenerateTriangles(mesh);
    }

    [Fact]
    public void CreateDisk_NonPositiveRadius_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralMeshFactory.CreateDisk(0f));
    }

    [Fact]
    public void CreateCapsule_VerticesStayWithinRadiusAndHalfHeight()
    {
        ProceduralMeshData mesh = ProceduralMeshFactory.CreateCapsule(radius: 1f, height: 4f);

        Assert.Contains(mesh.Positions, p => p == new Vector3(0, 2f, 0));
        Assert.Contains(mesh.Positions, p => p == new Vector3(0, -2f, 0));

        foreach (Vector3 position in mesh.Positions)
        {
            Assert.InRange(new Vector2(position.X, position.Z).Length(), 0f, 1f + 1e-4f);
            Assert.InRange(position.Y, -2f - 1e-4f, 2f + 1e-4f);
        }

        AssertIndicesInRange(mesh);
        AssertNoDegenerateTriangles(mesh);
        AssertNormalsAreUnitLength(mesh);
    }

    [Fact]
    public void CreateCapsule_HeightNotExceedingTwiceRadius_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralMeshFactory.CreateCapsule(radius: 1f, height: 2f));
    }

    [Theory]
    [InlineData(0f, 4f)]
    [InlineData(-1f, 4f)]
    public void CreateCapsule_NonPositiveRadius_Throws(float radius, float height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralMeshFactory.CreateCapsule(radius, height));
    }
}
