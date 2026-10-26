using CSharpGameEngine.Rendering;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

public class ModelPixelVerifierTests {
    private static readonly PixelColor Background=new(5,5,20,255);
    private static readonly PixelColor Foreground=new(200,150,100,255);

    private static PixelReadbackResult BuildFrame(int Width,int Height,uint ExtraRowPitchPadding,RenderPixelFormat Format,Func<int,int,bool>IsForeground){
        var BytesPerPixel=4;
        var RowPitch=(uint)(Width*BytesPerPixel)+ExtraRowPitchPadding;
        var Pixels=new byte[RowPitch*Height];

        for(var Y=0;Y<Height;Y++){
            for(var X=0;X<Width;X++){
                var Color=IsForeground(X,Y)?Foreground:Background;
                var Offset=((long)Y*RowPitch)+(X*BytesPerPixel);

                if(Format==RenderPixelFormat.Bgra8Unorm){
                    Pixels[Offset]=Color.B;
                    Pixels[Offset+1]=Color.G;
                    Pixels[Offset+2]=Color.R;
                    Pixels[Offset+3]=Color.A;
                } else {
                    Pixels[Offset]=Color.R;
                    Pixels[Offset+1]=Color.G;
                    Pixels[Offset+2]=Color.B;
                    Pixels[Offset+3]=Color.A;
                }
            }
        }

        return new PixelReadbackResult {
            Width=Width,
            Height=Height,
            RowPitch=RowPitch,
            Format=Format,
            Pixels=Pixels,
        };
    }

    [Fact]
    public void Verify_PlausibleCoverage_Succeeds(){
        // 100x100フレームの約20%を覆う、中央配置の円盤。
        var Frame=BuildFrame(100,100,ExtraRowPitchPadding:0,RenderPixelFormat.Rgba8Unorm,
                             (X,Y)=>(X-50)*(X-50)+(Y-50)*(Y-50)<32*32);

        var Result=ModelPixelVerifier.Verify(Frame,Background);

        Assert.True(Result.Success,Result.Details);
        Assert.Equal(Foreground,Result.CenterColor);
        Assert.InRange(Result.ForegroundRatio,0.05,0.98);
    }

    [Fact]
    public void Verify_EntirelyBackground_Fails(){
        var Frame=BuildFrame(64,64,ExtraRowPitchPadding:0,RenderPixelFormat.Rgba8Unorm,(_,_)=>false);

        var Result=ModelPixelVerifier.Verify(Frame,Background);

        Assert.False(Result.Success);
        Assert.Equal(0.0,Result.ForegroundRatio);
        Assert.Contains("nothing appears to have been drawn",Result.Details);
    }

    [Fact]
    public void Verify_EntirelyForeground_Fails(){
        var Frame=BuildFrame(64,64,ExtraRowPitchPadding:0,RenderPixelFormat.Rgba8Unorm,(_,_)=>true);

        var Result=ModelPixelVerifier.Verify(Frame,Background);

        Assert.False(Result.Success);
        Assert.Equal(1.0,Result.ForegroundRatio);
        Assert.Contains("plausible band",Result.Details);
    }

    [Fact]
    public void Verify_HandlesRowPitchPaddingLargerThanTightWidth(){
        var Frame=BuildFrame(17,13,ExtraRowPitchPadding:231,RenderPixelFormat.Rgba8Unorm,
                             (X,Y)=>Math.Abs(X-8)<4&&Math.Abs(Y-6)<4);

        var Result=ModelPixelVerifier.Verify(Frame,Background);

        Assert.True(Result.Success,Result.Details);
    }

    [Fact]
    public void Verify_BgraFormat_ReadsChannelsInCorrectOrder(){
        var Frame=BuildFrame(64,64,ExtraRowPitchPadding:0,RenderPixelFormat.Bgra8Unorm,
                             (X,Y)=>Math.Abs(X-32)<10&&Math.Abs(Y-32)<10);

        var Result=ModelPixelVerifier.Verify(Frame,Background);

        Assert.True(Result.Success,Result.Details);
        Assert.Equal(Foreground,Result.CenterColor);
    }

    [Fact]
    public void Verify_ZeroSizedFrame_FailsCleanly(){
        PixelReadbackResult Frame=new(){
            Width=0,
            Height=0,
            RowPitch=0,
            Format=RenderPixelFormat.Rgba8Unorm,
            Pixels=[],
        };

        var Result=ModelPixelVerifier.Verify(Frame,Background);

        Assert.False(Result.Success);
    }
}
