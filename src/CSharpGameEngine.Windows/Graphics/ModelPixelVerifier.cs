using TerraFX.Interop.DirectX;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;

namespace CSharpGameEngine.Graphics.D3D12;

public sealed record ModelVerificationResult(bool Success,PixelColor CenterColor,double ForegroundRatio,string Details);

/// <summary>
/// Pure, GPU-free pixel checks for the model viewer vertical slice. Unlike
/// <see cref="TrianglePixelVerifier"/> (which knows a small triangle sits dead-center with
/// background at every edge), an arbitrary auto-framed mesh's silhouette isn't a fixed shape, so
/// this scans the whole frame and checks the fraction of non-background pixels falls in a
/// plausible band - proving real geometry was rasterized without covering the entire frame.
/// </summary>
public static class ModelPixelVerifier {
    public const byte DefaultTolerance=24;
    public const double DefaultMinForegroundRatio=0.05;
    public const double DefaultMaxForegroundRatio=0.98;

    public static ModelVerificationResult Verify(
        PixelReadbackResult Readback,
        PixelColor BackgroundColor,
        byte Tolerance=DefaultTolerance,
        double MinForegroundRatio=DefaultMinForegroundRatio,
        double MaxForegroundRatio=DefaultMaxForegroundRatio){
        ArgumentNullException.ThrowIfNull(Readback);

        if(Readback.Width<=0||Readback.Height<=0){
            return new ModelVerificationResult(false,default,0.0,"Readback has no pixels (width or height is zero).");
        }

        var Center=ReadPixel(Readback,Readback.Width/2,Readback.Height/2);

        long ForegroundCount=0;
        var TotalCount=(long)Readback.Width*Readback.Height;

        for(var Y=0;Y<Readback.Height;Y++){
            for(var X=0;X<Readback.Width;X++){
                if(!IsClose(ReadPixel(Readback,X,Y),BackgroundColor,Tolerance)){
                    ForegroundCount++;
                }
            }
        }

        var ForegroundRatio=(double)ForegroundCount/TotalCount;
        var CenterDiffersFromBackground=!IsClose(Center,BackgroundColor,Tolerance);
        var RatioInBand=ForegroundRatio>=MinForegroundRatio&&ForegroundRatio<=MaxForegroundRatio;

        var Success=CenterDiffersFromBackground&&RatioInBand;

        var Details=Success
            ? $"center={Center} differs from background={BackgroundColor}; foreground ratio={ForegroundRatio:P2} is within [{MinForegroundRatio:P0},{MaxForegroundRatio:P0}]."
            :BuildFailureDetails(CenterDiffersFromBackground,RatioInBand,Center,BackgroundColor,ForegroundRatio,MinForegroundRatio,MaxForegroundRatio);

        return new ModelVerificationResult(Success,Center,ForegroundRatio,Details);
    }

    private static string BuildFailureDetails(bool CenterDiffersFromBackground,bool RatioInBand,PixelColor Center,PixelColor BackgroundColor,double ForegroundRatio,double MinForegroundRatio,double MaxForegroundRatio){
        List<string>Parts=[];
        if(!CenterDiffersFromBackground){
            Parts.Add($"center={Center} is too close to background={BackgroundColor}; nothing appears to have been drawn there.");
        }
        if(!RatioInBand){
            Parts.Add($"foreground ratio={ForegroundRatio:P2} is outside the plausible band [{MinForegroundRatio:P0},{MaxForegroundRatio:P0}].");
        }
        return string.Join(" ",Parts);
    }

    private static bool IsClose(PixelColor A,PixelColor B,byte Tolerance)=>
        Math.Abs(A.R-B.R)<=Tolerance&&
        Math.Abs(A.G-B.G)<=Tolerance&&
        Math.Abs(A.B-B.B)<=Tolerance;

    private static PixelColor ReadPixel(PixelReadbackResult Readback,int X,int Y){
        const int BytesPerPixel=4;
        var Offset=((long)Y*Readback.RowPitch)+(X*BytesPerPixel);

        var B0=Readback.Pixels[Offset];
        var B1=Readback.Pixels[Offset+1];
        var B2=Readback.Pixels[Offset+2];
        var B3=Readback.Pixels[Offset+3];

        return Readback.Format switch {
            DXGI_FORMAT_B8G8R8A8_UNORM=>new PixelColor(B2,B1,B0,B3),
            _ =>new PixelColor(B0,B1,B2,B3),
        };
    }
}
