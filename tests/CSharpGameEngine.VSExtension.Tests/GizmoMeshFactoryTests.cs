using System.Linq;
using System.Numerics;
using CSharpGameEngine.VSExtension;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class GizmoMeshFactoryTests
{
    [Fact]
    public void Create_ProducesTwentyFourVerticesAndThirtySixIndices()
    {
        SimpleMesh mesh = GizmoMeshFactory.Create();

        Assert.Equal(24, mesh.Positions.Length);
        Assert.Equal(24, mesh.Normals.Length);
        Assert.Equal(36, mesh.Indices.Length);
    }

    [Fact]
    public void Create_AllNormalsAreUnitLength()
    {
        SimpleMesh mesh = GizmoMeshFactory.Create();

        foreach (Vector3 normal in mesh.Normals)
        {
            Assert.Equal(1f, normal.Length(), precision: 5);
        }
    }

    [Fact]
    public void Create_NoDegenerateTriangles()
    {
        SimpleMesh mesh = GizmoMeshFactory.Create();

        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            Vector3 a = mesh.Positions[mesh.Indices[i]];
            Vector3 b = mesh.Positions[mesh.Indices[i + 1]];
            Vector3 c = mesh.Positions[mesh.Indices[i + 2]];

            float area = Vector3.Cross(b - a, c - a).Length();
            Assert.True(area > 0.0001f, $"Degenerate triangle at index {i}");
        }
    }

    [Fact]
    public void Create_IsAxisAlignedAndCenteredAtOrigin()
    {
        SimpleMesh mesh = GizmoMeshFactory.Create(size: 0.4f);

        Assert.All(mesh.Positions, p => Assert.True(System.Math.Abs(p.X) <= 0.2001f && System.Math.Abs(p.Y) <= 0.2001f && System.Math.Abs(p.Z) <= 0.2001f));
        Assert.Equal(Vector3.Zero, mesh.BoundsCenter);
        Assert.True(mesh.BoundsRadius > 0f);
    }

    [Fact]
    public void Create_UsesDefaultTint_DistinctFromSelectionOrange()
    {
        SimpleMesh mesh = GizmoMeshFactory.Create();

        Assert.Equal(1f, mesh.BaseColorFactor.W);
        Assert.True(mesh.BaseColorFactor.Z > mesh.BaseColorFactor.X, "Default gizmo tint should read as blue-ish, not orange.");
    }

    [Fact]
    public void Create_NonPositiveSize_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => GizmoMeshFactory.Create(size: 0f));
    }
}
