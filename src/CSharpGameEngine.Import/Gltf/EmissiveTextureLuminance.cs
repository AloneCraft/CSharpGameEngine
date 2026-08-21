using System.IO.Compression;

namespace CSharpGameEngine.Import.Gltf;

/// <summary>
/// Computes a deterministic scalar for direct lighting from a glTF emissive texture. Color
/// textures in glTF are sRGB, so supported PNG samples are linearized before their Rec.709
/// luminance is averaged. Missing or unsupported encodings deliberately preserve the material
/// factor/strength result with the identity scale instead of adding an image-decoder dependency to
/// the cross-platform import assembly.
/// </summary>
internal static class EmissiveTextureLuminance {
    private const int PngSignatureLength=8;
    private const int MaximumPixels=16*1024*1024;
    private const int MaximumCompressedBytes=64*1024*1024;
    private const uint Ihdr=0x49484452;
    private const uint Idat=0x49444154;
    private const uint Iend=0x49454e44;
    private const uint Plte=0x504c5445;

    /// <summary>
    /// Returns mean linear Rec.709 luminance for a supported PNG, or one when the texture is
    /// absent or its encoding is not safely decodable in this assembly.
    /// </summary>
    public static float CalculateOrIdentity(byte[]?EncodedBytes)=>
        TryCalculatePng(EncodedBytes,out float Luminance)?Luminance:1f;

    internal static bool TryCalculatePng(byte[]?EncodedBytes,out float Luminance){
        Luminance=1f;
        if(EncodedBytes is null||EncodedBytes.Length<PngSignatureLength
            ||!HasPngSignature(EncodedBytes)){
            return false;
        }

        try {
            return TryCalculatePngCore(EncodedBytes,out Luminance);
        } catch(Exception Exception) when(Exception is ArgumentException
            ||Exception is InvalidDataException
            ||Exception is IOException
            ||Exception is OverflowException){
            Luminance=1f;
            return false;
        }
    }

    private static bool TryCalculatePngCore(byte[]EncodedBytes,out float Luminance){
        Luminance=1f;
        var Offset=PngSignatureLength;
        var HasHeader=false;
        var HasEnd=false;
        var Width=0;
        var Height=0;
        byte ColorType=0;
        byte[]?Palette=null;
        List<byte>Compressed=[];

        while(Offset<EncodedBytes.Length){
            if(EncodedBytes.Length-Offset<12){
                return false;
            }

            uint RawLength=ReadUInt32BigEndian(EncodedBytes,Offset);
            if(RawLength>int.MaxValue){
                return false;
            }
            var Length=(int)RawLength;
            uint Type=ReadUInt32BigEndian(EncodedBytes,Offset+4);
            var DataOffset=Offset+8;
            if(Length>EncodedBytes.Length-DataOffset-4){
                return false;
            }
            Offset=DataOffset+Length+4; // data plus CRC; the decoded bytes are authoritative.

            if(!HasHeader&&Type!=Ihdr){
                return false;
            }

            if(Type==Ihdr){
                if(HasHeader||Length!=13){
                    return false;
                }
                uint RawWidth=ReadUInt32BigEndian(EncodedBytes,DataOffset);
                uint RawHeight=ReadUInt32BigEndian(EncodedBytes,DataOffset+4);
                if(RawWidth==0||RawHeight==0||RawWidth>int.MaxValue||RawHeight>int.MaxValue
                    ||EncodedBytes[DataOffset+8]!=8 // bit depth
                    ||EncodedBytes[DataOffset+10]!=0 // compression method
                    ||EncodedBytes[DataOffset+11]!=0 // filter method
                    ||EncodedBytes[DataOffset+12]!=0){ // Adam7 interlace has nontrivial pass layout
                    return false;
                }
                ColorType=EncodedBytes[DataOffset+9];
                if(ColorType is not(0 or 2 or 3 or 4 or 6)){
                    return false;
                }
                Width=(int)RawWidth;
                Height=(int)RawHeight;
                if((long)Width*Height>MaximumPixels){
                    return false;
                }
                HasHeader=true;
            } else if(Type==Plte){
                if(!HasHeader||Length==0||Length%3!=0||Length>256*3){
                    return false;
                }
                Palette=EncodedBytes.AsSpan(DataOffset,Length).ToArray();
            } else if(Type==Idat){
                if(!HasHeader||Length>MaximumCompressedBytes-Compressed.Count){
                    return false;
                }
                Compressed.AddRange(EncodedBytes.AsSpan(DataOffset,Length).ToArray());
            } else if(Type==Iend){
                if(Length!=0){
                    return false;
                }
                HasEnd=true;
                break;
            }
        }

        if(!HasHeader||!HasEnd||Compressed.Count==0||(ColorType==3&&Palette is null)){
            return false;
        }

        int BytesPerPixel=ColorType switch {
            0=>1, // gray
            2=>3, // RGB
            3=>1, // indexed RGB
            4=>2, // gray + alpha (alpha is unused by glTF emissive)
            6=>4, // RGBA (alpha is unused by glTF emissive)
            _=>throw new InvalidOperationException(),
        };
        int RowBytes=checked(Width*BytesPerPixel);
        int DecodedLength=checked((RowBytes+1)*Height);
        if(DecodedLength>MaximumPixels*5){
            return false;
        }

        byte[]Decoded=new byte[DecodedLength];
        using(var CompressedStream=new MemoryStream(Compressed.ToArray(),writable:false))
        using(var Inflater=new ZLibStream(CompressedStream,CompressionMode.Decompress)){
            var Written=0;
            while(Written<Decoded.Length){
                int Read=Inflater.Read(Decoded,Written,Decoded.Length-Written);
                if(Read==0){
                    return false;
                }
                Written+=Read;
            }
        }

        byte[]PreviousRow=new byte[RowBytes];
        byte[]CurrentRow=new byte[RowBytes];
        double Sum=0d;
        var ScanlineLength=RowBytes+1;
        for(var Row=0;Row<Height;Row++){
            var ScanlineOffset=Row*ScanlineLength;
            Buffer.BlockCopy(Decoded,ScanlineOffset+1,CurrentRow,0,RowBytes);
            if(!TryUnfilter(Decoded[ScanlineOffset],CurrentRow,PreviousRow,BytesPerPixel)){
                return false;
            }

            for(var Column=0;Column<Width;Column++){
                if(!TryGetSrgb(CurrentRow,Column*BytesPerPixel,ColorType,Palette,out byte Red,out byte Green,out byte Blue)){
                    return false;
                }
                Sum+=(0.2126d*SrgbToLinear(Red))
                    +(0.7152d*SrgbToLinear(Green))
                    +(0.0722d*SrgbToLinear(Blue));
            }
            (PreviousRow,CurrentRow)=(CurrentRow,PreviousRow);
        }

        Luminance=(float)Math.Clamp(Sum/((double)Width*Height),0d,1d);
        return true;
    }

    private static bool TryGetSrgb(
        ReadOnlySpan<byte>Row,
        int Offset,
        byte ColorType,
        byte[]?Palette,
        out byte Red,
        out byte Green,
        out byte Blue){
        switch(ColorType){
            case 0:
            case 4:
                Red=Green=Blue=Row[Offset];
                return true;
            case 2:
            case 6:
                Red=Row[Offset];
                Green=Row[Offset+1];
                Blue=Row[Offset+2];
                return true;
            case 3:
                if(Palette is null){
                    break;
                }
                var PaletteOffset=Row[Offset]*3;
                if(PaletteOffset+2>=Palette.Length){
                    break;
                }
                Red=Palette[PaletteOffset];
                Green=Palette[PaletteOffset+1];
                Blue=Palette[PaletteOffset+2];
                return true;
        }

        Red=Green=Blue=0;
        return false;
    }

    private static bool TryUnfilter(byte Filter,Span<byte>Current,ReadOnlySpan<byte>Previous,int BytesPerPixel){
        switch(Filter){
            case 0:
                return true;
            case 1:
                for(var Index=0;Index<Current.Length;Index++){
                    var Left=Index>=BytesPerPixel?Current[Index-BytesPerPixel]:0;
                    Current[Index]=unchecked((byte)(Current[Index]+Left));
                }
                return true;
            case 2:
                for(var Index=0;Index<Current.Length;Index++){
                    Current[Index]=unchecked((byte)(Current[Index]+Previous[Index]));
                }
                return true;
            case 3:
                for(var Index=0;Index<Current.Length;Index++){
                    var Left=Index>=BytesPerPixel?Current[Index-BytesPerPixel]:0;
                    Current[Index]=unchecked((byte)(Current[Index]+((Left+Previous[Index])/2)));
                }
                return true;
            case 4:
                for(var Index=0;Index<Current.Length;Index++){
                    var Left=Index>=BytesPerPixel?Current[Index-BytesPerPixel]:0;
                    var UpperLeft=Index>=BytesPerPixel?Previous[Index-BytesPerPixel]:0;
                    Current[Index]=unchecked((byte)(Current[Index]+Paeth(Left,Previous[Index],UpperLeft)));
                }
                return true;
            default:
                return false;
        }
    }

    private static int Paeth(int Left,int Above,int UpperLeft){
        int Predictor=Left+Above-UpperLeft;
        int LeftDistance=Math.Abs(Predictor-Left);
        int AboveDistance=Math.Abs(Predictor-Above);
        int UpperLeftDistance=Math.Abs(Predictor-UpperLeft);
        return LeftDistance<=AboveDistance&&LeftDistance<=UpperLeftDistance
            ?Left
            :AboveDistance<=UpperLeftDistance?Above:UpperLeft;
    }

    private static double SrgbToLinear(byte Value){
        double Srgb=Value/255d;
        return Srgb<=0.04045d
            ?Srgb/12.92d
            :Math.Pow((Srgb+0.055d)/1.055d,2.4d);
    }

    private static bool HasPngSignature(IReadOnlyList<byte>Bytes)=>
        Bytes[0]==137&&Bytes[1]==80&&Bytes[2]==78&&Bytes[3]==71
        &&Bytes[4]==13&&Bytes[5]==10&&Bytes[6]==26&&Bytes[7]==10;

    private static uint ReadUInt32BigEndian(IReadOnlyList<byte>Bytes,int Offset)=>
        ((uint)Bytes[Offset]<<24)|((uint)Bytes[Offset+1]<<16)|((uint)Bytes[Offset+2]<<8)|Bytes[Offset+3];
}
