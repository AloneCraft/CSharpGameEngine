using TerraFX.Interop.DirectX;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

public class TrianglePixelVerifierTests {
    private static readonly PixelColor Background=new(5,5,20,255);
    private static readonly PixelColor Foreground=new(220,30,30,255);

    private static PixelReadbackResult BuildFrame(int Width,int Height,uint ExtraRowPitchPadding,DXGI_FORMAT Format,Func<int,int,bool>IsForeground){
        var BytesPerPixel=4;
        var RowPitch=(uint)(Width*BytesPerPixel)+ExtraRowPitchPadding;
        var Pixels=new byte[RowPitch*Height];

        for(var Y=0;Y<Height;Y++){
            for(var X=0;X<Width;X++){
                PixelColor Color=IsForeground(X,Y)?Foreground:Background;
                var Offset=(long)Y*RowPitch+(X*BytesPerPixel);

                if(Format==DXGI_FORMAT_B8G8R8A8_UNORM){
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
    public void Verify_CenterFilledCornersBackground_Succeeds(){
        PixelReadbackResult Frame=BuildFrame(256,256,ExtraRowPitchPadding:0,DXGI_FORMAT_R8G8B8A8_UNORM,
            (X,Y)=>Math.Abs(X-128)<40&&Math.Abs(Y-128)<40);

        TriangleVerificationResult Result=TrianglePixelVerifier.Verify(Frame,Background);

        Assert.True(Result.Success,Result.Details);
        Assert.Equal(Foreground,Result.CenterColor);
    }

    [Fact]
    public void Verify_EntirelyBackground_FailsBecauseCenterDoesNotDiffer(){
        PixelReadbackResult Frame=BuildFrame(256,256,ExtraRowPitchPadding:0,DXGI_FORMAT_R8G8B8A8_UNORM,(_,_)=>false);

        TriangleVerificationResult Result=TrianglePixelVerifier.Verify(Frame,Background);

        Assert.False(Result.Success);
        Assert.Contains("nothing appears to have been drawn",Result.Details);
    }

    [Fact]
    public void Verify_ForegroundBleedsIntoCorners_Fails(){
        PixelReadbackResult Frame=BuildFrame(256,256,ExtraRowPitchPadding:0,DXGI_FORMAT_R8G8B8A8_UNORM,(_,_)=>true);

        TriangleVerificationResult Result=TrianglePixelVerifier.Verify(Frame,Background);

        Assert.False(Result.Success);
        Assert.Contains("corner",Result.Details);
    }

    [Fact]
    public void Verify_HandlesRowPitchPaddingLargerThanTightWidth(){
        PixelReadbackResult Frame=BuildFrame(17,13,ExtraRowPitchPadding:231,DXGI_FORMAT_R8G8B8A8_UNORM,
            (X,Y)=>Math.Abs(X-8)<3&&Math.Abs(Y-6)<3);

        TriangleVerificationResult Result=TrianglePixelVerifier.Verify(Frame,Background);

        Assert.True(Result.Success,Result.Details);
    }

    [Fact]
    public void Verify_BgraFormat_ReadsChannelsInCorrectOrder(){
        PixelReadbackResult Frame=BuildFrame(64,64,ExtraRowPitchPadding:0,DXGI_FORMAT_B8G8R8A8_UNORM,
            (X,Y)=>Math.Abs(X-32)<10&&Math.Abs(Y-32)<10);

        TriangleVerificationResult Result=TrianglePixelVerifier.Verify(Frame,Background);

        Assert.True(Result.Success,Result.Details);
        Assert.Equal(Foreground,Result.CenterColor);
    }

    [Fact]
    public void Verify_ZeroSizedFrame_FailsCleanly(){
        PixelReadbackResult Frame=new(){
            Width=0,
            Height=0,
            RowPitch=0,
            Format=DXGI_FORMAT_R8G8B8A8_UNORM,
            Pixels=[],
        };

        TriangleVerificationResult Result=TrianglePixelVerifier.Verify(Frame,Background);

        Assert.False(Result.Success);
    }
}
