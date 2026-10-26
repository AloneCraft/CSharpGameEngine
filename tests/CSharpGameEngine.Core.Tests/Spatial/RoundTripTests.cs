using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Blender;
using CSharpGameEngine.Spatial.Fbx;
using CSharpGameEngine.Spatial.Gltf;
using CSharpGameEngine.Spatial.Unity;
using CSharpGameEngine.Spatial.Unreal;

namespace CSharpGameEngine.Spatial.Tests;

/// <summary>
/// Docs/RUF-Semantic-Coordinate-Specification-Prompt.md の「Round Trip Tests」。RUF -&gt; XYZ -&gt; RUF と XYZ -&gt; RUF -&gt; XYZ は、すべてのプリセットで損失なく往復できなければならない。仕様の決定性要件に従い、シードを固定する。
/// </summary>
public class RoundTripTests {
    private const int SampleCount=50;
    private const int Seed=20260805;

    [Fact]
    public void Unity_RufToXyzToRuf_IsLossless()=>AssertRufRoundTrip<UnityCoordinateConvention>();

    [Fact]
    public void Unity_XyzToRufToXyz_IsLossless()=>AssertXyzRoundTrip<UnityCoordinateConvention>();

    [Fact]
    public void Unreal_RufToXyzToRuf_IsLossless()=>AssertRufRoundTrip<UnrealCoordinateConvention>();

    [Fact]
    public void Unreal_XyzToRufToXyz_IsLossless()=>AssertXyzRoundTrip<UnrealCoordinateConvention>();

    [Fact]
    public void Blender_RufToXyzToRuf_IsLossless()=>AssertRufRoundTrip<BlenderWorldConvention>();

    [Fact]
    public void Blender_XyzToRufToXyz_IsLossless()=>AssertXyzRoundTrip<BlenderWorldConvention>();

    [Fact]
    public void Gltf_RufToXyzToRuf_IsLossless()=>AssertRufRoundTrip<GltfCoordinateConvention>();

    [Fact]
    public void Gltf_XyzToRufToXyz_IsLossless()=>AssertXyzRoundTrip<GltfCoordinateConvention>();

    [Fact]
    public void Fbx_RufToXyzToRuf_IsLossless()=>AssertRufRoundTrip<FbxCoordinateConvention>();

    [Fact]
    public void Fbx_XyzToRufToXyz_IsLossless()=>AssertXyzRoundTrip<FbxCoordinateConvention>();

    private static void AssertRufRoundTrip<TConvention>()
        where TConvention:struct,ICoordinateConvention {
        Random Random=new(Seed);

        for(var I=0;I<SampleCount;I++){
            var Original=NextRuf(Random);
            var RoundTripped=TConvention.FromXyz(TConvention.ToXyz(Original));

            Assert.Equal(Original.R,RoundTripped.R,precision:4);
            Assert.Equal(Original.U,RoundTripped.U,precision:4);
            Assert.Equal(Original.F,RoundTripped.F,precision:4);
        }
    }

    private static void AssertXyzRoundTrip<TConvention>()
        where TConvention:struct,ICoordinateConvention {
        Random Random=new(Seed);

        for(var I=0;I<SampleCount;I++){
            var Original=NextXyz(Random);
            var RoundTripped=TConvention.ToXyz(TConvention.FromXyz(Original));

            Assert.Equal(Original.X,RoundTripped.X,precision:4);
            Assert.Equal(Original.Y,RoundTripped.Y,precision:4);
            Assert.Equal(Original.Z,RoundTripped.Z,precision:4);
        }
    }

    private static RufVector NextRuf(Random Random)=>new(NextComponent(Random),NextComponent(Random),NextComponent(Random));

    private static XyzVector NextXyz(Random Random)=>new(NextComponent(Random),NextComponent(Random),NextComponent(Random));

    private static float NextComponent(Random Random)=>(float)((Random.NextDouble()*200d)-100d);
}
