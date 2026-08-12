using TerraFX.Interop.DirectX;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;

namespace CSharpGameEngine.Graphics.D3D12;

public sealed record TriangleVerificationResult(bool Success,PixelColor CenterColor,IReadOnlyList<PixelColor>CornerColors,string Details);

/// <summary>
/// Pure, GPU-free pixel checks for the triangle vertical slice: the frame center must differ
/// from the background clear color (something was drawn) and the corners must not (nothing
/// bleeds outside the triangle). No GPU access required, so this is fully unit-testable.
/// </summary>
public static class TrianglePixelVerifier {
    public const byte DefaultTolerance=24;

    public static TriangleVerificationResult Verify(PixelReadbackResult Readback,PixelColor BackgroundColor,byte Tolerance=DefaultTolerance){
        ArgumentNullException.ThrowIfNull(Readback);

        if(Readback.Width<=0||Readback.Height<=0){
            return new TriangleVerificationResult(false,default,[],"Readback has no pixels (width or height is zero).");
        }

        const double EdgeInset=0.05;
        var InsetX=Math.Max(1,(int)(Readback.Width*EdgeInset));
        var InsetY=Math.Max(1,(int)(Readback.Height*EdgeInset));

        PixelColor Center=ReadPixel(Readback,Readback.Width/2,Readback.Height/2);

        (int X,int Y)[]CornerPositions=[
            (InsetX,InsetY),
            (Readback.Width-1-InsetX,InsetY),
            (InsetX,Readback.Height-1-InsetY),
            (Readback.Width-1-InsetX,Readback.Height-1-InsetY),
        ];

        PixelColor[]Corners=new PixelColor[CornerPositions.Length];
        for(var I=0;I<CornerPositions.Length;I++){
            Corners[I]=ReadPixel(Readback,CornerPositions[I].X,CornerPositions[I].Y);
        }

        var CenterDiffersFromBackground=!IsClose(Center,BackgroundColor,Tolerance);

        List<string>CornerFailures=[];
        for(var I=0;I<Corners.Length;I++){
            if(!IsClose(Corners[I],BackgroundColor,Tolerance)){
                CornerFailures.Add($"corner[{I}] at {CornerPositions[I]} = {Corners[I]} is not close to background {BackgroundColor}");
            }
        }

        var Success=CenterDiffersFromBackground&&CornerFailures.Count==0;

        var Details=Success
            ? $"center={Center} differs from background={BackgroundColor}; all corners match background within tolerance {Tolerance}."
            :BuildFailureDetails(CenterDiffersFromBackground,Center,BackgroundColor,CornerFailures);

        return new TriangleVerificationResult(Success,Center,Corners,Details);
    }

    private static string BuildFailureDetails(bool CenterDiffersFromBackground,PixelColor Center,PixelColor BackgroundColor,List<string>CornerFailures){
        List<string>Parts=[];
        if(!CenterDiffersFromBackground){
            Parts.Add($"center={Center} is too close to background={BackgroundColor}; nothing appears to have been drawn.");
        }
        Parts.AddRange(CornerFailures);
        return string.Join(" ",Parts);
    }

    private static bool IsClose(PixelColor A,PixelColor B,byte Tolerance)=>
        Math.Abs(A.R-B.R)<=Tolerance&&
        Math.Abs(A.G-B.G)<=Tolerance&&
        Math.Abs(A.B-B.B)<=Tolerance;

    private static PixelColor ReadPixel(PixelReadbackResult Readback,int X,int Y){
        const int BytesPerPixel=4;
        var Offset=(long)Y*Readback.RowPitch+(X*BytesPerPixel);

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
