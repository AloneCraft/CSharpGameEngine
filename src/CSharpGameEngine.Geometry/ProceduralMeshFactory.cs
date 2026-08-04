using System;
using System.Collections.Generic;
using System.Numerics;

namespace CSharpGameEngine.Geometry;

/// <summary>
/// Builds simple placeholder meshes directly in code - no importer needed. Moved here from
/// <c>CSharpGameEngine.Graphics.D3D12</c> (ADR-0021) so the shape-palette feature's primitives
/// (<see cref="CreateSphere"/>/<see cref="CreateCylinder"/>/<see cref="CreateCapsule"/>/
/// <see cref="CreateCone"/>/<see cref="CreateTorus"/>/<see cref="CreateDisk"/>) live in one
/// graphics-independent project (pure <see cref="System.Numerics"/>, no D3D12/TerraFX types) that
/// both <c>CSharpGameEngine.Graphics.D3D12</c> and the out-of-process
/// <c>tools/CSharpGameEngine.Primitive.Cli</c> bridge can reference, instead of duplicating the
/// generation math on the net472 VSIX side (ADR-0019's "always go through an out-of-process bridge,
/// never in-process" principle). Winding order is picked for consistency but does not currently
/// affect visibility: every consumer disables backface culling.
/// </summary>
public static class ProceduralMeshFactory
{
    private const int DefaultSegments = 24;
    private const int DefaultHemisphereRings = 8;
    private const int DefaultMinorSegments = 12;

    /// <summary>An XZ-plane rectangle centered at the origin, Y=0, facing +Y. Used as the ground, and as the shape palette's "矩形" primitive.</summary>
    public static ProceduralMeshData CreatePlane(float width, float depth)
    {
        if (width <= 0f || depth <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "width and depth must both be positive.");
        }

        float halfWidth = width / 2f;
        float halfDepth = depth / 2f;

        Vector3[] positions =
        [
            new Vector3(-halfWidth, 0f, -halfDepth),
            new Vector3(halfWidth, 0f, -halfDepth),
            new Vector3(halfWidth, 0f, halfDepth),
            new Vector3(-halfWidth, 0f, halfDepth),
        ];

        Vector3[] normals = [Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY];

        uint[] indices = [0, 1, 2, 0, 2, 3];

        return new ProceduralMeshData { Positions = positions, Normals = normals, Indices = indices };
    }

    /// <summary>An axis-aligned box centered at the origin (24 vertices, one normal per face) sized <paramref name="size"/> on each axis.</summary>
    public static ProceduralMeshData CreateBox(Vector3 size)
    {
        if (size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "size components must all be positive.");
        }

        Vector3 half = size / 2f;

        (Vector3 Normal, Vector3 U, Vector3 V)[] faces =
        [
            (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),     // +Z
            (-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY),   // -Z
            (Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY),    // +X
            (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),    // -X
            (Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ),    // +Y
            (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),    // -Y
        ];

        Vector3[] positions = new Vector3[faces.Length * 4];
        Vector3[] normals = new Vector3[faces.Length * 4];
        uint[] indices = new uint[faces.Length * 6];

        for (int face = 0; face < faces.Length; face++)
        {
            (Vector3 normal, Vector3 u, Vector3 v) = faces[face];
            Vector3 center = normal * half;
            int vertexBase = face * 4;

            positions[vertexBase + 0] = center - (u * half) - (v * half);
            positions[vertexBase + 1] = center + (u * half) - (v * half);
            positions[vertexBase + 2] = center + (u * half) + (v * half);
            positions[vertexBase + 3] = center - (u * half) + (v * half);

            for (int i = 0; i < 4; i++)
            {
                normals[vertexBase + i] = normal;
            }

            int indexBase = face * 6;
            indices[indexBase + 0] = (uint)(vertexBase + 0);
            indices[indexBase + 1] = (uint)(vertexBase + 1);
            indices[indexBase + 2] = (uint)(vertexBase + 2);
            indices[indexBase + 3] = (uint)(vertexBase + 0);
            indices[indexBase + 4] = (uint)(vertexBase + 2);
            indices[indexBase + 5] = (uint)(vertexBase + 3);
        }

        return new ProceduralMeshData { Positions = positions, Normals = normals, Indices = indices };
    }

    /// <summary>
    /// A UV sphere centered at the origin. Poles are single vertices fanned to the first/last
    /// interior ring (rather than the naive "duplicate pole per column, quad the top/bottom band"
    /// approach) specifically so no triangle is ever degenerate.
    /// </summary>
    public static ProceduralMeshData CreateSphere(float radius)
    {
        if (radius <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "radius must be positive.");
        }

        const int latitudeSegments = 16;
        const int longitudeSegments = DefaultSegments;
        int ringStride = longitudeSegments + 1;
        int ringCount = latitudeSegments - 1;

        List<Vector3> positions = [];
        List<Vector3> normals = [];
        List<uint> indices = [];

        int northPoleIndex = positions.Count;
        positions.Add(new Vector3(0, radius, 0));
        normals.Add(Vector3.UnitY);

        int firstRingIndex = positions.Count;
        for (int lat = 1; lat < latitudeSegments; lat++)
        {
            float theta = lat * MathF.PI / latitudeSegments;
            float sinTheta = MathF.Sin(theta);
            float cosTheta = MathF.Cos(theta);
            for (int lon = 0; lon <= longitudeSegments; lon++)
            {
                float phi = lon * 2f * MathF.PI / longitudeSegments;
                Vector3 normal = new(MathF.Cos(phi) * sinTheta, cosTheta, MathF.Sin(phi) * sinTheta);
                positions.Add(normal * radius);
                normals.Add(normal);
            }
        }

        int southPoleIndex = positions.Count;
        positions.Add(new Vector3(0, -radius, 0));
        normals.Add(-Vector3.UnitY);

        for (int lon = 0; lon < longitudeSegments; lon++)
        {
            indices.Add((uint)northPoleIndex);
            indices.Add((uint)(firstRingIndex + lon));
            indices.Add((uint)(firstRingIndex + lon + 1));
        }

        for (int ring = 0; ring < ringCount - 1; ring++)
        {
            int ringStart = firstRingIndex + (ring * ringStride);
            AddQuadBand(indices, ringStart, ringStart + ringStride, longitudeSegments);
        }

        int lastRingStart = firstRingIndex + ((ringCount - 1) * ringStride);
        for (int lon = 0; lon < longitudeSegments; lon++)
        {
            indices.Add((uint)(lastRingStart + lon));
            indices.Add((uint)southPoleIndex);
            indices.Add((uint)(lastRingStart + lon + 1));
        }

        return new ProceduralMeshData { Positions = [.. positions], Normals = [.. normals], Indices = [.. indices] };
    }

    /// <summary>An axis-aligned (Y) capped cylinder centered at the origin.</summary>
    public static ProceduralMeshData CreateCylinder(float radius, float height)
    {
        if (radius <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "radius must be positive.");
        }

        if (height <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "height must be positive.");
        }

        const int segments = DefaultSegments;
        float halfHeight = height / 2f;

        List<Vector3> positions = [];
        List<Vector3> normals = [];
        List<uint> indices = [];

        int sideTopStart = AddRing(positions, normals, segments, radius, halfHeight, radial: true);
        int sideBottomStart = AddRing(positions, normals, segments, radius, -halfHeight, radial: true);
        AddQuadBand(indices, sideTopStart, sideBottomStart, segments);

        int topCenterIndex = positions.Count;
        positions.Add(new Vector3(0, halfHeight, 0));
        normals.Add(Vector3.UnitY);
        int topRingStart = AddRing(positions, normals, segments, radius, halfHeight, radial: false, capNormal: Vector3.UnitY);
        for (int i = 0; i < segments; i++)
        {
            indices.Add((uint)topCenterIndex);
            indices.Add((uint)(topRingStart + i));
            indices.Add((uint)(topRingStart + i + 1));
        }

        int bottomCenterIndex = positions.Count;
        positions.Add(new Vector3(0, -halfHeight, 0));
        normals.Add(-Vector3.UnitY);
        int bottomRingStart = AddRing(positions, normals, segments, radius, -halfHeight, radial: false, capNormal: -Vector3.UnitY);
        for (int i = 0; i < segments; i++)
        {
            indices.Add((uint)bottomCenterIndex);
            indices.Add((uint)(bottomRingStart + i + 1));
            indices.Add((uint)(bottomRingStart + i));
        }

        return new ProceduralMeshData { Positions = [.. positions], Normals = [.. normals], Indices = [.. indices] };
    }

    /// <summary>An axis-aligned (Y) cone centered at the origin: apex at +height/2, capped circular base at -height/2.</summary>
    public static ProceduralMeshData CreateCone(float radius, float height)
    {
        if (radius <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "radius must be positive.");
        }

        if (height <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "height must be positive.");
        }

        const int segments = DefaultSegments;
        float halfHeight = height / 2f;
        Vector3 apex = new(0, halfHeight, 0);

        List<Vector3> positions = [];
        List<Vector3> normals = [];
        List<uint> indices = [];

        // Flat-shaded side: each triangle gets its own three vertices (some duplication) so the
        // per-face normal (derived from the cross product, not interpolated) is exact.
        for (int i = 0; i < segments; i++)
        {
            float angle0 = i * 2f * MathF.PI / segments;
            float angle1 = (i + 1) * 2f * MathF.PI / segments;
            Vector3 p0 = new(MathF.Cos(angle0) * radius, -halfHeight, MathF.Sin(angle0) * radius);
            Vector3 p1 = new(MathF.Cos(angle1) * radius, -halfHeight, MathF.Sin(angle1) * radius);
            Vector3 faceNormal = Vector3.Normalize(Vector3.Cross(p1 - apex, p0 - apex));

            int baseIndex = positions.Count;
            positions.Add(apex); normals.Add(faceNormal);
            positions.Add(p0); normals.Add(faceNormal);
            positions.Add(p1); normals.Add(faceNormal);
            indices.Add((uint)baseIndex);
            indices.Add((uint)(baseIndex + 1));
            indices.Add((uint)(baseIndex + 2));
        }

        int baseCenterIndex = positions.Count;
        positions.Add(new Vector3(0, -halfHeight, 0));
        normals.Add(-Vector3.UnitY);
        int baseRingStart = AddRing(positions, normals, segments, radius, -halfHeight, radial: false, capNormal: -Vector3.UnitY);
        for (int i = 0; i < segments; i++)
        {
            indices.Add((uint)baseCenterIndex);
            indices.Add((uint)(baseRingStart + i + 1));
            indices.Add((uint)(baseRingStart + i));
        }

        return new ProceduralMeshData { Positions = [.. positions], Normals = [.. normals], Indices = [.. indices] };
    }

    /// <summary>A torus centered at the origin in the XZ plane. <paramref name="minorRadius"/> (tube radius) must be smaller than <paramref name="majorRadius"/> (ring radius) to avoid self-intersection.</summary>
    public static ProceduralMeshData CreateTorus(float majorRadius, float minorRadius)
    {
        if (majorRadius <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(majorRadius), "majorRadius must be positive.");
        }

        if (minorRadius <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(minorRadius), "minorRadius must be positive.");
        }

        if (minorRadius >= majorRadius)
        {
            throw new ArgumentOutOfRangeException(nameof(minorRadius), "minorRadius must be smaller than majorRadius to avoid self-intersection.");
        }

        const int majorSegments = DefaultSegments;
        const int minorSegments = DefaultMinorSegments;
        int ringStride = minorSegments + 1;

        List<Vector3> positions = [];
        List<Vector3> normals = [];
        List<uint> indices = [];

        for (int i = 0; i <= majorSegments; i++)
        {
            float theta = i * 2f * MathF.PI / majorSegments;
            float cosTheta = MathF.Cos(theta);
            float sinTheta = MathF.Sin(theta);

            for (int j = 0; j <= minorSegments; j++)
            {
                float phi = j * 2f * MathF.PI / minorSegments;
                float cosPhi = MathF.Cos(phi);
                float sinPhi = MathF.Sin(phi);
                float tubeRadius = majorRadius + (minorRadius * cosPhi);

                positions.Add(new Vector3(tubeRadius * cosTheta, minorRadius * sinPhi, tubeRadius * sinTheta));
                normals.Add(new Vector3(cosPhi * cosTheta, sinPhi, cosPhi * sinTheta));
            }
        }

        for (int i = 0; i < majorSegments; i++)
        {
            AddQuadBand(indices, i * ringStride, (i + 1) * ringStride, minorSegments);
        }

        return new ProceduralMeshData { Positions = [.. positions], Normals = [.. normals], Indices = [.. indices] };
    }

    /// <summary>A flat circular disk centered at the origin, Y=0, facing +Y.</summary>
    public static ProceduralMeshData CreateDisk(float radius)
    {
        if (radius <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "radius must be positive.");
        }

        const int segments = DefaultSegments;

        List<Vector3> positions = [Vector3.Zero];
        List<Vector3> normals = [Vector3.UnitY];
        List<uint> indices = [];

        int ringStart = AddRing(positions, normals, segments, radius, 0f, radial: false, capNormal: Vector3.UnitY);
        for (int i = 0; i < segments; i++)
        {
            indices.Add(0);
            indices.Add((uint)(ringStart + i));
            indices.Add((uint)(ringStart + i + 1));
        }

        return new ProceduralMeshData { Positions = [.. positions], Normals = [.. normals], Indices = [.. indices] };
    }

    /// <summary>
    /// A capped capsule (cylinder with hemispherical caps) centered at the origin, axis Y.
    /// <paramref name="height"/> is the total end-to-end height including both hemispheres, so it
    /// must exceed <c>2 * radius</c> for the cylindrical mid-section to have positive length.
    /// </summary>
    public static ProceduralMeshData CreateCapsule(float radius, float height)
    {
        if (radius <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "radius must be positive.");
        }

        if (height <= (2f * radius))
        {
            throw new ArgumentOutOfRangeException(nameof(height), "height must exceed 2*radius so the cylindrical section between the hemispherical caps has positive length.");
        }

        const int hemisphereRings = DefaultHemisphereRings;
        const int segments = DefaultSegments;
        int ringStride = segments + 1;
        float cylinderHalfHeight = (height / 2f) - radius;

        List<Vector3> positions = [];
        List<Vector3> normals = [];
        List<uint> indices = [];

        int topPoleIndex = positions.Count;
        positions.Add(new Vector3(0, cylinderHalfHeight + radius, 0));
        normals.Add(Vector3.UnitY);

        int topFirstRing = AddHemisphereRings(positions, normals, segments, hemisphereRings, radius, cylinderHalfHeight, upperHalf: true);

        int cylTopRing = AddRing(positions, normals, segments, radius, cylinderHalfHeight, radial: true);
        int cylBottomRing = AddRing(positions, normals, segments, radius, -cylinderHalfHeight, radial: true);

        int bottomFirstRing = AddHemisphereRings(positions, normals, segments, hemisphereRings, radius, -cylinderHalfHeight, upperHalf: false);

        int bottomPoleIndex = positions.Count;
        positions.Add(new Vector3(0, -(cylinderHalfHeight + radius), 0));
        normals.Add(-Vector3.UnitY);

        for (int lon = 0; lon < segments; lon++)
        {
            indices.Add((uint)topPoleIndex);
            indices.Add((uint)(topFirstRing + lon));
            indices.Add((uint)(topFirstRing + lon + 1));
        }

        for (int ring = 0; ring < hemisphereRings - 1; ring++)
        {
            int a0 = topFirstRing + (ring * ringStride);
            AddQuadBand(indices, a0, a0 + ringStride, segments);
        }

        AddQuadBand(indices, topFirstRing + ((hemisphereRings - 1) * ringStride), cylTopRing, segments);
        AddQuadBand(indices, cylTopRing, cylBottomRing, segments);
        AddQuadBand(indices, cylBottomRing, bottomFirstRing, segments);

        for (int ring = 0; ring < hemisphereRings - 1; ring++)
        {
            int a0 = bottomFirstRing + (ring * ringStride);
            AddQuadBand(indices, a0, a0 + ringStride, segments);
        }

        int lastBottomRing = bottomFirstRing + ((hemisphereRings - 1) * ringStride);
        for (int lon = 0; lon < segments; lon++)
        {
            indices.Add((uint)(lastBottomRing + lon));
            indices.Add((uint)bottomPoleIndex);
            indices.Add((uint)(lastBottomRing + lon + 1));
        }

        return new ProceduralMeshData { Positions = [.. positions], Normals = [.. normals], Indices = [.. indices] };
    }

    /// <summary>
    /// Appends <paramref name="hemisphereRings"/> interior latitude rings (excluding the pole and
    /// the equator, both added separately by the caller) for one hemisphere of <see cref="CreateCapsule"/>.
    /// </summary>
    private static int AddHemisphereRings(List<Vector3> positions, List<Vector3> normals, int segments, int hemisphereRings, float radius, float yOffset, bool upperHalf)
    {
        int firstRingIndex = positions.Count;
        for (int lat = 1; lat <= hemisphereRings; lat++)
        {
            float fraction = lat / (float)(hemisphereRings + 1); // (0, 1)
            float theta = upperHalf
                ? fraction * (MathF.PI / 2f)                  // (0, pi/2): pole -> equator
                : (MathF.PI / 2f) + (fraction * (MathF.PI / 2f)); // (pi/2, pi): equator -> pole
            float sinTheta = MathF.Sin(theta);
            float cosTheta = MathF.Cos(theta);

            for (int lon = 0; lon <= segments; lon++)
            {
                float phi = lon * 2f * MathF.PI / segments;
                Vector3 normal = new(MathF.Cos(phi) * sinTheta, cosTheta, MathF.Sin(phi) * sinTheta);
                positions.Add(new Vector3(normal.X * radius, yOffset + (normal.Y * radius), normal.Z * radius));
                normals.Add(normal);
            }
        }

        return firstRingIndex;
    }

    /// <summary>Appends one ring of <paramref name="segments"/>+1 vertices (a duplicate seam vertex closes the loop, keeping index math uniform with <see cref="AddQuadBand"/>). <paramref name="radial"/> selects a purely radial normal (cylinder side) vs. a caller-supplied flat <paramref name="capNormal"/> (cap/disk faces).</summary>
    private static int AddRing(List<Vector3> positions, List<Vector3> normals, int segments, float radius, float y, bool radial, Vector3 capNormal = default)
    {
        int ringStart = positions.Count;
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * 2f * MathF.PI / segments;
            float x = MathF.Cos(angle);
            float z = MathF.Sin(angle);
            positions.Add(new Vector3(x * radius, y, z * radius));
            normals.Add(radial ? new Vector3(x, 0, z) : capNormal);
        }

        return ringStart;
    }

    /// <summary>Quads a band between two same-length (<paramref name="segments"/>+1 vertex) rings.</summary>
    private static void AddQuadBand(List<uint> indices, int ringAStart, int ringBStart, int segments)
    {
        for (int i = 0; i < segments; i++)
        {
            int a = ringAStart + i;
            int b = a + 1;
            int c = ringBStart + i;
            int d = c + 1;
            indices.Add((uint)a);
            indices.Add((uint)c);
            indices.Add((uint)b);
            indices.Add((uint)b);
            indices.Add((uint)c);
            indices.Add((uint)d);
        }
    }
}
