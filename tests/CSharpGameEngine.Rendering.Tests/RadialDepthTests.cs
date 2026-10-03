using System.Numerics;
namespace CSharpGameEngine.Rendering.Tests;
/// <summary>M0099 Phase 2: the CPU definition of the radial depth curve, its encoding and derived planes (ADR-0076).</summary>
public sealed class RadialDepthTests {
    private const float FovY=MathF.PI/4f;
    private const float Aspect=16f/9f;
    private static IEnumerable<double>LogSpaced(double From,double To,int Count){
        for(var Index=0;Index<Count;Index++){
            yield return From*Math.Pow(To/From,Index/(double)(Count-1));
        }
    }
    /// <summary>Double-precision oracle: log2(1 + x), with the series where 1 + x would round.</summary>
    private static double Log2OnePlusOracle(double X)=>X<1e-8?(X-X*X/2d)/Math.Log(2d):Math.Log(1d+X)/Math.Log(2d);
    [Fact]
    public void Log2OnePlus_MatchesTheDoubleOracleFromTinyToHugeArguments(){
        foreach(var X in LogSpaced(1e-30,1e30,241)){
            var Actual=RadialDepthCurve.Log2OnePlus((float)X);
            var Expected=Log2OnePlusOracle((float)X);
            Assert.True(Math.Abs(Actual-Expected)<=Expected*6e-7,$"x={X:R} actual={Actual:R} expected={Expected:R}");
        }
        Assert.Equal(0f,RadialDepthCurve.Log2OnePlus(0f));
    }
    [Theory]
    [InlineData(1e-3f)]
    [InlineData(1f)]
    [InlineData(1e3f)]
    public void Curve_IsMonotonicAndLinearAtTheEye(float Transition){
        Assert.True(RadialDepthCurve.TryCreate(Transition,out var Curve));
        Assert.Equal(0f,Curve.G(0f));
        var Previous=0f;
        foreach(var Distance in LogSpaced(1e-9,9.2e18,400)){
            var Value=Curve.G((float)Distance);
            Assert.True(Value>=Previous,$"G({Distance:R})={Value:R} < {Previous:R}");
            Previous=Value;
        }
        var Near=Transition*1e-6f;
        Assert.Equal(1d/(Transition*Math.Log(2d)),Curve.G(Near)/Near,1d/(Transition*Math.Log(2d))*1e-5);
    }
    [Fact]
    public void Curve_RejectsNonPositiveAndNonFiniteTransitions(){
        Assert.False(RadialDepthCurve.TryCreate(0f,out _));
        Assert.False(RadialDepthCurve.TryCreate(-1f,out _));
        Assert.False(RadialDepthCurve.TryCreate(float.NaN,out _));
        Assert.False(RadialDepthCurve.TryCreate(float.PositiveInfinity,out _));
        Assert.Equal(1f,RadialDepthCurve.Default.TransitionMeters);
    }
    [Theory]
    [InlineData(0.1f,300f)]
    [InlineData(1e-9f,1e18f)]
    [InlineData(1e-3f,1e30f)]
    public void Encoding_StaysBelowTheClearedDepthAndIsMonotonic(float Near,float Far){
        Assert.True(RadialDepthProjection.TryCreate(RadialDepthCurve.Default,Near,Far,FovY,Aspect,out var Projection));
        Assert.Equal(0f,Projection.EncodeDepth(0f));
        var Corner=Projection.EncodeDepth(Projection.FrustumCornerDistance);
        Assert.True(Corner<=RadialDepthProjection.MaximumDepth&&Corner>0.9999f,$"corner depth {Corner:R}");
        Assert.True(Projection.EncodeDepth(Projection.FrustumCornerDistance*1e6f)<1f);
        var Previous=0f;
        foreach(var Distance in LogSpaced(1e-9,Projection.FrustumCornerDistance,300)){
            var Depth=Projection.EncodeDepth((float)Distance);
            Assert.True(Depth>=Previous);
            Previous=Depth;
        }
    }
    [Fact]
    public void Projection_RejectsInvalidCameras(){
        var Curve=RadialDepthCurve.Default;
        Assert.False(RadialDepthProjection.TryCreate(default,0.1f,300f,FovY,Aspect,out _));
        Assert.False(RadialDepthProjection.TryCreate(Curve,0f,300f,FovY,Aspect,out _));
        Assert.False(RadialDepthProjection.TryCreate(Curve,0.1f,0.1f,FovY,Aspect,out _));
        Assert.False(RadialDepthProjection.TryCreate(Curve,0.1f,float.PositiveInfinity,FovY,Aspect,out _));
        Assert.False(RadialDepthProjection.TryCreate(Curve,0.1f,300f,0f,Aspect,out _));
        Assert.False(RadialDepthProjection.TryCreate(Curve,0.1f,300f,MathF.PI,Aspect,out _));
        Assert.False(RadialDepthProjection.TryCreate(Curve,0.1f,300f,FovY,0f,out _));
        Assert.False(RadialDepthProjection.TryCreate(Curve,0.1f,float.NaN,FovY,Aspect,out _));
    }
    [Fact]
    public void Encoding_SeparatesSurfacesAtTheBudgetedRelativeDistanceOnEveryScale(){
        // ADR-0076: with a 2^-24 depth step, two surfaces are told apart once they are about
        // 2^-24 · ln2 · log2(rMax / T) · (1 + T / r) apart in relative distance (about 3e-6 for 0.1 m to 1e18 m).
        Assert.True(RadialDepthProjection.TryCreate(RadialDepthCurve.Default,0.1f,1e18f,FovY,Aspect,out var Projection));
        var Steps=Math.Log2(Projection.FrustumCornerDistance/Projection.Curve.TransitionMeters);
        foreach(var Distance in LogSpaced(0.2,1e17,60)){
            var Budget=Math.Pow(2d,-24d)*Math.Log(2d)*Steps*(1d+Projection.Curve.TransitionMeters/Distance);
            var Nearer=Projection.EncodeDepth((float)Distance);
            var Farther=Projection.EncodeDepth((float)(Distance*(1d+2d*Budget)));
            Assert.True(Farther>Nearer,$"r={Distance:R} budget={Budget:R}");
        }
        Assert.True(Steps*Math.Log(2d)*Math.Pow(2d,-24d)<3.2e-6);
    }
    [Fact]
    public void Observer_CompressesDistancesToTheTransitionTimesG(){
        Assert.True(RadialDepthCurve.TryCreate(2f,out var Curve));
        Assert.Equal(2f*Curve.G(1000f),Curve.Map(1000f));
        Assert.True(RadialDepthProjection.TryCreate(Curve,0.1f,1e18f,FovY,Aspect,out var Projection));
        Assert.Equal(Curve.Map(Projection.FrustumCornerDistance),Projection.ObservedWorldRadius);
        Assert.InRange(Projection.ObservedWorldRadius,100f,140f);
    }
    [Fact]
    public void StandardProjection_WithAHugeFar_CannotBeUnprojected(){
        // Why the radial mode needs an auxiliary view-projection (ADR-0076): once far/near passes about 2^24 the
        // float matrix's depth terms round so that NDC z = 1 unprojects to w = 0.
        var Projection=Matrix4x4.CreatePerspectiveFieldOfView(FovY,Aspect,0.1f,1e18f);
        Assert.True(Matrix4x4.Invert(Projection,out var Inverse));
        var Far=Vector4.Transform(new Vector4(0f,0f,1f,1f),Inverse);
        Assert.False(float.IsFinite(Far.Z/Far.W)&&MathF.Abs(Far.W)>1e-30f,$"far plane unprojected to {Far}");
    }
    [Theory]
    [InlineData(1e-9f,1e3f)]
    [InlineData(1e-9f,1e30f)]
    [InlineData(1e-3f,1e7f)]
    [InlineData(0.1f,1e18f)]
    [InlineData(1f,1e30f)]
    [InlineData(0.001f,0.005f)]
    public void AuxiliaryProjection_IsFiniteAndInvertible(float Near,float Far){
        Assert.True(RadialDepthProjection.TryCreate(RadialDepthCurve.Default,Near,Far,FovY,Aspect,out var Projection));
        Assert.True(Projection.AuxiliaryFarZ>Projection.AuxiliaryNearZ);
        Assert.True(Projection.AuxiliaryNearZ>=Near&&Projection.AuxiliaryFarZ<=Far);
        var Matrix=Projection.CreateAuxiliaryProjection(FovY,Aspect);
        Assert.True(Matrix4x4.Invert(Matrix,out var Inverse));
        foreach(var Z in new[]{0f,1f}){
            var Point=Vector4.Transform(new Vector4(1f,1f,Z,1f),Inverse);
            var Depth=-Point.Z/Point.W;
            var Expected=Z==0f?Projection.AuxiliaryNearZ:Projection.AuxiliaryFarZ;
            Assert.True(float.IsFinite(Depth)&&MathF.Abs(Depth-Expected)<=Expected*1e-3f,$"z={Z} depth={Depth} expected={Expected}");
        }
    }
    [Fact]
    public void AuxiliaryProjection_IsTheCameraProjectionForAnOrdinaryRange(){
        // A radial camera with a game-sized range gives the inverting passes exactly the standard matrix, so its
        // sky, sun cascades and ambient occlusion are the standard ones.
        Assert.True(RadialDepthProjection.TryCreate(RadialDepthCurve.Default,0.1f,300f,FovY,Aspect,out var Projection));
        Assert.Equal(Matrix4x4.CreatePerspectiveFieldOfView(FovY,Aspect,0.1f,300f),Projection.CreateAuxiliaryProjection(FovY,Aspect));
    }
}
