using Xunit;
using CSharpGameEngine.Rendering.Streaming;
namespace CSharpGameEngine.Rendering.Tests;
/// <summary>Hi-Z の段の決まり（M0131 単位 1）。段の大きさ（段 0 は 2 のべき乗、その後はちょうど半分）、空は無限に遠い、縁の画素も漏れない、を確かめる。GPU との一致は HiZGpuTests。</summary>
public sealed class HiZPyramidTests {
    [Theory]
    [InlineData(1,1,new[]{ 1 },new[]{ 1 })]
    [InlineData(2,2,new[]{ 1 },new[]{ 1 })]
    [InlineData(5,3,new[]{ 4,2,1 },new[]{ 2,1,1 })]
    [InlineData(317,181,new[]{ 256,128,64,32,16,8,4,2,1 },new[]{ 128,64,32,16,8,4,2,1,1 })]
    [InlineData(320,180,new[]{ 256,128,64,32,16,8,4,2,1 },new[]{ 128,64,32,16,8,4,2,1,1 })]
    public void LevelZeroIsAPowerOfTwoAndEachLevelIsExactlyHalf(int Width,int Height,int[]Widths,int[]Heights){
        var Sizes=HiZPyramid.LevelSizes(Width,Height);
        Assert.Equal(Widths,Sizes.Select(static Size=>Size.Width));
        Assert.Equal(Heights,Sizes.Select(static Size=>Size.Height));
    }
    [Fact]
    public void TheSkyIsInfinitelyFar(){
        var Levels=HiZPyramid.Build([1f,2f,0f,4f],2,2);
        Assert.Single(Levels);
        Assert.Equal(float.PositiveInfinity,Levels[0][0]);
        var Ground=HiZPyramid.Build([1f,2f,3f,4f],2,2);
        Assert.Equal(4f,Ground[0][0]);
    }
    /// <summary>奇数の大きさでも、最後の行と列の画素がどこかの段に入り、最後の 1 画素は全体のいちばん遠い値になる。画面の外は 0 で、遠い値に効かない。</summary>
    [Theory]
    [InlineData(5,3)]
    [InlineData(7,7)]
    [InlineData(33,1)]
    public void EveryPixelReachesTheLastLevel(int Width,int Height){
        for(var Far=0;Far<Width*Height;Far++){
            var Distances=Enumerable.Repeat(1f,Width*Height).ToArray();
            Distances[Far]=9f;
            var Levels=HiZPyramid.Build(Distances,Width,Height);
            Assert.Equal(9f,Levels[^1][0]);
            Assert.Equal(1,Levels[0].Count(static Value=>Value==9f));
        }
    }
    [Fact]
    public void EachTexelIsTheFarthestOfItsSourceBlock(){
        var Random=new Random(131);
        const int Width=13;
        const int Height=9;
        var Distances=Enumerable.Range(0,Width*Height).Select(_=>(float)Random.NextDouble()*100f+1f).ToArray();
        var Levels=HiZPyramid.Build(Distances,Width,Height);
        var Sizes=HiZPyramid.LevelSizes(Width,Height);
        for(var Y=0;Y<Sizes[0].Height;Y++){
            for(var X=0;X<Sizes[0].Width;X++){
                var Expected=0f;
                for(var Dy=0;Dy<2;Dy++){
                    for(var Dx=0;Dx<2;Dx++){
                        int Sx=X*2+Dx,Sy=Y*2+Dy;
                        if(Sx<Width&&Sy<Height){ Expected=MathF.Max(Expected,Distances[Sy*Width+Sx]); }
                    }
                }
                Assert.Equal(Expected,Levels[0][Y*Sizes[0].Width+X]);
            }
        }
    }
}
