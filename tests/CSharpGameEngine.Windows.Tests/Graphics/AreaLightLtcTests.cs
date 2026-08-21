using System.Numerics;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

public sealed class AreaLightLtcTests {
    [Fact]
    public void EvaluateOneSidedProjectedSolidAngle_BackFace_IsZero(){
        float Factor=AreaLightLtc.EvaluateOneSidedProjectedSolidAngle(
            Center:Vector3.Zero,Forward:Vector3.UnitZ,Right:Vector3.UnitX,Up:Vector3.UnitY,
            HalfWidth:1f,HalfHeight:1f,
            ReceiverPosition:new Vector3(0f,0f,-2f),ReceiverNormal:Vector3.UnitZ);

        Assert.Equal(0f,Factor);
    }

    [Fact]
    public void EvaluateOneSidedProjectedSolidAngle_LargerRectangle_IncreasesIrradianceFactor(){
        float Small=AreaLightLtc.EvaluateOneSidedProjectedSolidAngle(
            Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,
            HalfWidth:0.5f,HalfHeight:0.5f,
            ReceiverPosition:new Vector3(0f,0f,10f),ReceiverNormal:-Vector3.UnitZ);
        float Large=AreaLightLtc.EvaluateOneSidedProjectedSolidAngle(
            Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,
            HalfWidth:1f,HalfHeight:1f,
            ReceiverPosition:new Vector3(0f,0f,10f),ReceiverNormal:-Vector3.UnitZ);

        Assert.True(Small>0f);
        Assert.True(Large>Small*3.5f,$"Expected a doubled width and height to approach four times the far-field irradiance factor; small={Small}, large={Large}.");
        Assert.InRange(Large,0f,MathF.PI);
    }

    [Fact]
    public void EvaluateOneSidedProjectedSolidAngle_FartherReceiver_DecreasesWithDistance(){
        float Near=AreaLightLtc.EvaluateOneSidedProjectedSolidAngle(
            Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,
            HalfWidth:0.5f,HalfHeight:0.5f,
            ReceiverPosition:new Vector3(0f,0f,5f),ReceiverNormal:-Vector3.UnitZ);
        float Far=AreaLightLtc.EvaluateOneSidedProjectedSolidAngle(
            Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,
            HalfWidth:0.5f,HalfHeight:0.5f,
            ReceiverPosition:new Vector3(0f,0f,10f),ReceiverNormal:-Vector3.UnitZ);

        Assert.True(Near>Far*3.5f,$"Expected the far-field projected solid angle to approach inverse-square falloff; near={Near}, far={Far}.");
    }

    [Fact]
    public void EvaluateOneSidedProjectedSolidAngle_RolledBasis_RotatesRectangleWithoutChangingFactor(){
        float Unrolled=AreaLightLtc.EvaluateOneSidedProjectedSolidAngle(
            Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,
            HalfWidth:2f,HalfHeight:0.5f,
            ReceiverPosition:new Vector3(1.5f,0f,5f),ReceiverNormal:-Vector3.UnitZ);
        float Rolled=AreaLightLtc.EvaluateOneSidedProjectedSolidAngle(
            Vector3.Zero,Vector3.UnitZ,Vector3.UnitY,-Vector3.UnitX,
            HalfWidth:2f,HalfHeight:0.5f,
            ReceiverPosition:new Vector3(0f,1.5f,5f),ReceiverNormal:-Vector3.UnitZ);

        Assert.True(Unrolled>0f);
        Assert.Equal(Unrolled,Rolled,precision:5);
    }

    [Fact]
    public void TryEvaluateOneSidedProjectedSolidAngle_InvalidValues_AreRejected(){
        bool ZeroForward=AreaLightLtc.TryEvaluateOneSidedProjectedSolidAngle(
            Vector3.Zero,Vector3.Zero,Vector3.UnitX,Vector3.UnitY,
            HalfWidth:1f,HalfHeight:1f,
            ReceiverPosition:new Vector3(0f,0f,2f),ReceiverNormal:-Vector3.UnitZ,out float ZeroForwardFactor);
        bool NegativeHalfWidth=AreaLightLtc.TryEvaluateOneSidedProjectedSolidAngle(
            Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,
            HalfWidth:-1f,HalfHeight:1f,
            ReceiverPosition:new Vector3(0f,0f,2f),ReceiverNormal:-Vector3.UnitZ,out float NegativeWidthFactor);
        bool LeftHandedBasis=AreaLightLtc.TryEvaluateOneSidedProjectedSolidAngle(
            Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,-Vector3.UnitY,
            HalfWidth:1f,HalfHeight:1f,
            ReceiverPosition:new Vector3(0f,0f,2f),ReceiverNormal:-Vector3.UnitZ,out float LeftHandedFactor);

        Assert.False(ZeroForward);
        Assert.False(NegativeHalfWidth);
        Assert.False(LeftHandedBasis);
        Assert.Equal(0f,ZeroForwardFactor);
        Assert.Equal(0f,NegativeWidthFactor);
        Assert.Equal(0f,LeftHandedFactor);
        Assert.Throws<ArgumentException>(()=>AreaLightLtc.EvaluateOneSidedProjectedSolidAngle(
            Vector3.Zero,Vector3.Zero,Vector3.UnitX,Vector3.UnitY,
            HalfWidth:1f,HalfHeight:1f,
            ReceiverPosition:new Vector3(0f,0f,2f),ReceiverNormal:-Vector3.UnitZ));
    }
}
