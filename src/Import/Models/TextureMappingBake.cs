using CSharpGameEngine.Interchange;
namespace CSharpGameEngine.Import.Models;
/// <summary>
/// The runtime material samples every texture through UV0. A texture whose reference carries a mapping relative to UV0 (glTF: another
/// KHR_texture_transform than the one baked into UV0) is resampled here, so that sampling the result at UV0 gives what sampling the
/// source at its own mapping gave. Bilinear with repeat addressing (the glTF default), in the stored encoding. Normal maps only reach here
/// without rotation or mirroring, where the tangent frame does not change.
/// </summary>
internal static class TextureMappingBake {
    internal static bool IsIdentity(InterchangeUvTransform Transform)=>
        Transform.Offset==System.Numerics.Vector2.Zero&&Transform.Scale==System.Numerics.Vector2.One&&Transform.Rotation==0f;
    /// <summary>RGBA8 rows from the top; the result has the same size.</summary>
    internal static byte[]Resample(int Width,int Height,byte[]Rgba,InterchangeUvTransform Transform){
        if(Width<=0||Height<=0||Rgba.Length!=Width*Height*4){ throw new ArgumentException("The image is not RGBA8 of its size.",nameof(Rgba)); }
        var (Sin,Cos)=MathF.SinCos(Transform.Rotation);
        var Result=new byte[Rgba.Length];
        for(var Y=0;Y<Height;Y++){
            for(var X=0;X<Width;X++){
                // R(v) = Offset + Rot(Scale * v), the same order as a KHR_texture_transform.
                var U=(X+0.5f)/Width*Transform.Scale.X;
                var V=(Y+0.5f)/Height*Transform.Scale.Y;
                var SourceU=Transform.Offset.X+Cos*U+Sin*V;
                var SourceV=Transform.Offset.Y-Sin*U+Cos*V;
                var PixelX=SourceU*Width-0.5f;
                var PixelY=SourceV*Height-0.5f;
                var X0=(int)MathF.Floor(PixelX);var Y0=(int)MathF.Floor(PixelY);
                var Fx=PixelX-X0;var Fy=PixelY-Y0;
                int Wrap(int Value,int Size)=>((Value%Size)+Size)%Size;
                int A=(Wrap(Y0,Height)*Width+Wrap(X0,Width))*4,B=(Wrap(Y0,Height)*Width+Wrap(X0+1,Width))*4;
                int C=(Wrap(Y0+1,Height)*Width+Wrap(X0,Width))*4,D=(Wrap(Y0+1,Height)*Width+Wrap(X0+1,Width))*4;
                var Target=(Y*Width+X)*4;
                for(var Channel=0;Channel<4;Channel++){
                    var Top=Rgba[A+Channel]+(Rgba[B+Channel]-Rgba[A+Channel])*Fx;
                    var Bottom=Rgba[C+Channel]+(Rgba[D+Channel]-Rgba[C+Channel])*Fx;
                    Result[Target+Channel]=(byte)Math.Clamp(MathF.Round(Top+(Bottom-Top)*Fy),0f,255f);
                }
            }
        }
        return Result;
    }
}
