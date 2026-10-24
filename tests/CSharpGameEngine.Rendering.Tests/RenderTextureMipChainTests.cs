using System.Security.Cryptography;
namespace CSharpGameEngine.Rendering.Tests;
/// <summary>M0110: the CPU mip chain of material textures (box filter with exact area weights, sRGB averaged in linear light, deterministic).</summary>
public sealed class RenderTextureMipChainTests {
    [Theory]
    [InlineData(1,1,1)]
    [InlineData(2,1,2)]
    [InlineData(1,2,2)]
    [InlineData(5,3,3)]
    [InlineData(256,256,9)]
    [InlineData(255,256,9)]
    [InlineData(257,1,9)]
    [InlineData(1,16384,15)]
    public void LevelCount_IsFloorLog2OfTheLargerSidePlusOne(int Width,int Height,int Expected){
        Assert.Equal(Expected,RenderTextureMipChain.LevelCount(Width,Height));
    }
    [Fact]
    public void LevelSizes_HalveRoundingDownToOneTexel(){
        var Chain=RenderTextureMipChain.Build(5,3,new byte[5*3*4],RenderTextureColorSpace.Linear);
        Assert.Equal([(5,3),(2,1),(1,1)],Chain.Levels.Select(Level=>(Level.Width,Level.Height)).ToArray());
    }
    [Fact]
    public void OneTexel_HasOneLevelEqualToTheTexture(){
        byte[]Pixels=[10,20,30,40];
        var Chain=RenderTextureMipChain.Build(1,1,Pixels,RenderTextureColorSpace.Srgb);
        Assert.Single(Chain.Levels);
        Assert.Equal(Pixels,Chain.Levels[0].Pixels.ToArray());
    }
    [Fact]
    public void Linear_TwoByTwo_IsTheRoundedMean(){
        byte[]Pixels=[0,0,0,0, 255,1,10,255, 255,2,20,0, 0,3,31,255];
        var Chain=RenderTextureMipChain.Build(2,2,Pixels,RenderTextureColorSpace.Linear);
        Assert.Equal(2,Chain.Levels.Count);
        // (0+255+255+0)/4=127.5 rounds half up through the 16-bit carry; (0+1+2+3)/4=1.5; (0+10+20+31)/4=15.25; alpha 127.5.
        Assert.Equal(new byte[]{ 128,2,15,128 },Chain.Levels[1].Pixels.ToArray());
    }
    [Fact]
    public void Srgb_BlackAndWhite_AverageToMiddleGreyInLinearLight(){
        byte[]Pixels=[0,0,0,0, 255,255,255,255, 255,255,255,0, 0,0,0,255];
        var Srgb=RenderTextureMipChain.Build(2,2,Pixels,RenderTextureColorSpace.Srgb).Levels[1].Pixels.ToArray();
        var Linear=RenderTextureMipChain.Build(2,2,Pixels,RenderTextureColorSpace.Linear).Levels[1].Pixels.ToArray();
        // Half the light of white is sRGB code 188 (0.7354 encoded), not the 128 of an average of the codes; alpha stays linear.
        Assert.Equal(new byte[]{ 188,188,188,128 },Srgb);
        Assert.Equal(new byte[]{ 128,128,128,128 },Linear);
    }
    [Fact]
    public void NonPowerOfTwo_WeightsEachSourceTexelByTheAreaItCovers(){
        // 3 x 1 -> 1 x 1: every source texel covers a third of the target, so the target is the plain mean of three.
        byte[]Three=[0,0,0,0, 30,60,90,120, 60,120,180,240];
        Assert.Equal(new byte[]{ 30,60,90,120 },RenderTextureMipChain.Build(3,1,Three,RenderTextureColorSpace.Linear).Levels[1].Pixels.ToArray());
        // 5 x 1 -> 2 x 1: target 0 covers texels 0, 1 and half of 2; target 1 covers half of 2, 3 and 4.
        byte[]Five=[0,0,0,0, 100,0,0,0, 200,0,0,0, 50,0,0,0, 250,0,0,0];
        var Level=RenderTextureMipChain.Build(5,1,Five,RenderTextureColorSpace.Linear).Levels[1].Pixels.ToArray();
        Assert.Equal((byte)Math.Round((0+100+0.5*200)/2.5),Level[0]);
        Assert.Equal((byte)Math.Round((0.5*200+50+250)/2.5),Level[4]);
    }
    [Fact]
    public void EveryLevel_KeepsTheMeanOfTheTexture(){
        var Random=new Random(110);
        foreach(var (Width,Height) in new[]{ (7,5),(64,48),(33,1),(1,17),(100,61) }){
            var Pixels=new byte[Width*Height*4];
            Random.NextBytes(Pixels);
            var Chain=RenderTextureMipChain.Build(Width,Height,Pixels,RenderTextureColorSpace.Linear);
            var Expected=Mean(Pixels);
            foreach(var Level in Chain.Levels){
                // Each level rounds to 8 bits once from a 16-bit carry, so the mean moves by less than one code.
                Assert.InRange(Mean(Level.Pixels.ToArray())-Expected,-1.0,1.0);
            }
        }
    }
    [Fact]
    public void Build_IsDeterministicAndPinned(){
        var Pixels=new byte[37*23*4];
        new Random(2026).NextBytes(Pixels);
        string Hash(RenderTextureColorSpace Space){
            var Chain=RenderTextureMipChain.Build(37,23,Pixels,Space);
            using var Sha=SHA256.Create();
            foreach(var Level in Chain.Levels){
                var Bytes=Level.Pixels.ToArray();
                Sha.TransformBlock(Bytes,0,Bytes.Length,null,0);
            }
            Sha.TransformFinalBlock([],0,0);
            return Convert.ToHexString(Sha.Hash!);
        }
        Assert.Equal(Hash(RenderTextureColorSpace.Srgb),Hash(RenderTextureColorSpace.Srgb));
        Assert.NotEqual(Hash(RenderTextureColorSpace.Srgb),Hash(RenderTextureColorSpace.Linear));
        // The decoding table is part of the result: any change to it is a visible change of every sRGB mip.
        var Table=new byte[512];
        for(var Code=0;Code<256;Code++){
            BitConverter.TryWriteBytes(Table.AsSpan(Code*2,2),RenderTextureMipChain.DecodeSrgb((byte)Code));
        }
        Assert.Equal("FDB7AF3C01815A2118DB8E50AAC3C2304205CCC3F381F8976C588642C1AA60B6",Convert.ToHexString(SHA256.HashData(Table)));
    }
    [Fact]
    public void SrgbTable_RoundTripsEveryCode(){
        for(var Code=0;Code<256;Code++){
            Assert.Equal((byte)Code,RenderTextureMipChain.EncodeSrgb(RenderTextureMipChain.DecodeSrgb((byte)Code)));
        }
        Assert.Equal(0,RenderTextureMipChain.DecodeSrgb(0));
        Assert.Equal(65535,RenderTextureMipChain.DecodeSrgb(255));
        Assert.Equal(255,RenderTextureMipChain.EncodeSrgb(ushort.MaxValue));
    }
    [Fact]
    public void InvalidInput_Throws(){
        Assert.Throws<ArgumentOutOfRangeException>(()=>RenderTextureMipChain.Build(0,1,[],RenderTextureColorSpace.Linear));
        Assert.Throws<ArgumentOutOfRangeException>(()=>RenderTextureMipChain.Build(1,0,[],RenderTextureColorSpace.Linear));
        Assert.Throws<ArgumentException>(()=>RenderTextureMipChain.Build(2,2,new byte[15],RenderTextureColorSpace.Linear));
        Assert.Throws<ArgumentOutOfRangeException>(()=>RenderTextureMipChain.Build(1,1,new byte[4],(RenderTextureColorSpace)7));
        Assert.Throws<ArgumentOutOfRangeException>(()=>RenderTextureMipChain.LevelCount(0,4));
    }
    [Fact]
    public void TopLevelOnly_KeepsJustTheTexture(){
        var Texture=RenderArtifactCodec.CreateProceduralTexture(4,2,new byte[4*2*4]);
        var Chain=RenderTextureMipChain.TopLevelOnly(Texture);
        Assert.Single(Chain.Levels);
        Assert.Equal((4,2),(Chain.Levels[0].Width,Chain.Levels[0].Height));
    }
    private static double Mean(byte[]Pixels)=>Pixels.Average(Value=>(double)Value);
}
