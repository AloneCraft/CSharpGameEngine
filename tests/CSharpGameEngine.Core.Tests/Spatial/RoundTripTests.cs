using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Blender;
using CSharpGameEngine.Spatial.Fbx;
using CSharpGameEngine.Spatial.Gltf;
using CSharpGameEngine.Spatial.Unity;
using CSharpGameEngine.Spatial.Unreal;

namespace CSharpGameEngine.Spatial.Tests;

/// <summary>
/// Docs/RUF-Semantic-Coordinate-Specification-Prompt.md "Round Trip Tests": RUF -> XYZ -> RUF and
/// XYZ -> RUF -> XYZ must be lossless for every preset. Fixed seed per the spec's determinism
/// requirement.
/// </summary>
public class RoundTripTests
{
    private const int SampleCount = 50;
    private const int Seed = 20260805;

    [Fact]
    public void Unity_RufToXyzToRuf_IsLossless() => AssertRufRoundTrip<UnityCoordinateConvention>();

    [Fact]
    public void Unity_XyzToRufToXyz_IsLossless() => AssertXyzRoundTrip<UnityCoordinateConvention>();

    [Fact]
    public void Unreal_RufToXyzToRuf_IsLossless() => AssertRufRoundTrip<UnrealCoordinateConvention>();

    [Fact]
    public void Unreal_XyzToRufToXyz_IsLossless() => AssertXyzRoundTrip<UnrealCoordinateConvention>();

    [Fact]
    public void Blender_RufToXyzToRuf_IsLossless() => AssertRufRoundTrip<BlenderWorldConvention>();

    [Fact]
    public void Blender_XyzToRufToXyz_IsLossless() => AssertXyzRoundTrip<BlenderWorldConvention>();

    [Fact]
    public void Gltf_RufToXyzToRuf_IsLossless() => AssertRufRoundTrip<GltfCoordinateConvention>();

    [Fact]
    public void Gltf_XyzToRufToXyz_IsLossless() => AssertXyzRoundTrip<GltfCoordinateConvention>();

    [Fact]
    public void Fbx_RufToXyzToRuf_IsLossless() => AssertRufRoundTrip<FbxCoordinateConvention>();

    [Fact]
    public void Fbx_XyzToRufToXyz_IsLossless() => AssertXyzRoundTrip<FbxCoordinateConvention>();

    private static void AssertRufRoundTrip<TConvention>()
        where TConvention : struct, ICoordinateConvention
    {
        Random random = new(Seed);

        for (int i = 0; i < SampleCount; i++)
        {
            RufVector original = NextRuf(random);
            RufVector roundTripped = TConvention.FromXyz(TConvention.ToXyz(original));

            Assert.Equal(original.R, roundTripped.R, precision: 4);
            Assert.Equal(original.U, roundTripped.U, precision: 4);
            Assert.Equal(original.F, roundTripped.F, precision: 4);
        }
    }

    private static void AssertXyzRoundTrip<TConvention>()
        where TConvention : struct, ICoordinateConvention
    {
        Random random = new(Seed);

        for (int i = 0; i < SampleCount; i++)
        {
            XyzVector original = NextXyz(random);
            XyzVector roundTripped = TConvention.ToXyz(TConvention.FromXyz(original));

            Assert.Equal(original.X, roundTripped.X, precision: 4);
            Assert.Equal(original.Y, roundTripped.Y, precision: 4);
            Assert.Equal(original.Z, roundTripped.Z, precision: 4);
        }
    }

    private static RufVector NextRuf(Random random) => new(NextComponent(random), NextComponent(random), NextComponent(random));

    private static XyzVector NextXyz(Random random) => new(NextComponent(random), NextComponent(random), NextComponent(random));

    private static float NextComponent(Random random) => (float)((random.NextDouble() * 200d) - 100d);
}
