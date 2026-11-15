using System.IO.Compression;

namespace CSharpGameEngine.Import.Gltf;

/// <summary>
/// glTFの発光テクスチャから、直接照明に使う決定的なスカラー値を計算する。
/// glTFのカラー画像はsRGBなので、対応するPNGサンプルを線形化してからRec.709輝度を平均する。
/// クロスプラットフォームのImportアセンブリに画像デコーダー依存を追加しないため、画像がない、または非対応形式の場合は、
/// スケールを恒等値のままにしてマテリアルの係数／強度による結果を維持する。
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
    /// 対応PNGの線形Rec.709輝度の平均を返す。テクスチャがない場合、またはこのアセンブリで安全にデコードできない形式の場合は1。
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
            Offset=DataOffset+Length+4; // データとCRC。デコード済みのバイト列を正本として扱う。

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
                    ||EncodedBytes[DataOffset+8]!=8 // ビット深度
                    ||EncodedBytes[DataOffset+10]!=0 // 圧縮方式
                    ||EncodedBytes[DataOffset+11]!=0 // フィルター方式
                    ||EncodedBytes[DataOffset+12]!=0){ // Adam7インターレースには複雑なパス配置がある。
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
            0=>1, // グレースケール
            2=>3, // RGB
            3=>1, // インデックス付きRGB
            4=>2, // グレースケール＋アルファ形式（glTFの発光ではアルファを使わない）。
            6=>4, // RGBA形式（glTFの発光ではアルファを使わない）。
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
        float Sum=0f;
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
                Sum+=(0.2126f*SrgbToLinear(Red))
                    +(0.7152f*SrgbToLinear(Green))
                    +(0.0722f*SrgbToLinear(Blue));
            }
            (PreviousRow,CurrentRow)=(CurrentRow,PreviousRow);
        }

        Luminance=Math.Clamp(Sum/((float)Width*Height),0f,1f);
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

    private static float SrgbToLinear(byte Value){
        float Srgb=Value/255f;
        return Srgb<=0.04045f
            ?Srgb/12.92f
            :MathF.Pow((Srgb+0.055f)/1.055f,2.4f);
    }

    private static bool HasPngSignature(IReadOnlyList<byte>Bytes)=>
        Bytes[0]==137&&Bytes[1]==80&&Bytes[2]==78&&Bytes[3]==71
        &&Bytes[4]==13&&Bytes[5]==10&&Bytes[6]==26&&Bytes[7]==10;

    private static uint ReadUInt32BigEndian(IReadOnlyList<byte>Bytes,int Offset)=>
        ((uint)Bytes[Offset]<<24)|((uint)Bytes[Offset+1]<<16)|((uint)Bytes[Offset+2]<<8)|Bytes[Offset+3];
}
