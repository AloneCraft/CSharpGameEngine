using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.CLSCTX;
using static TerraFX.Interop.Windows.COINIT;
using static TerraFX.Interop.Windows.WICBitmapDitherType;
using static TerraFX.Interop.Windows.WICBitmapPaletteType;
using static TerraFX.Interop.Windows.WICDecodeOptions;
using static TerraFX.Interop.Windows.Windows;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>隙間なく配置されたRGBA8ピクセルデータ（stride = Width * 4）。</summary>
public sealed record DecodedImage(int Width,int Height,byte[]Pixels);

public sealed class WicDecodeResult {
    private WicDecodeResult(bool Success,DecodedImage?Image,string?Error){
        this.Success=Success;
        this.Image=Image;
        this.Error=Error;
    }

    public bool Success { get;}

    public DecodedImage?Image { get;}

    public string?Error { get;}

    public static WicDecodeResult Succeeded(DecodedImage Image)=>new(true,Image,null);

    public static WicDecodeResult Failed(string Error)=>new(false,null,Error);
}

/// <summary>
/// Windows Imaging Componentを介し、WindowsにインストールされたWICコーデックが解釈できるPNG／JPEGなどのバイト列を、生のRGBA8ピクセルへデコードする。
/// プロジェクト全体で既に使用しているTerraFX.Interop.Windowsバインディングを使い、新しいパッケージ依存を追加しない。Windows専用。
/// </summary>
public static unsafe class WicImageDecoder {
    // RPC_E_CHANGEDMODE：このスレッドでは別の並行モデルでCOMが初期化済み。
    // 致命的ではない。CoInitializeEx呼び出しの所有者にはならず（CoUninitializeを省略）、処理を続ける。
    private const int RPC_E_CHANGEDMODE=unchecked((int)0x80010106);
    /// <summary>D3D12_REQ_TEXTURE2D_U_OR_V_DIMENSION.</summary>
    private const uint MaxTextureSide=16384;

    public static WicDecodeResult Decode(byte[]EncodedBytes){
        ArgumentNullException.ThrowIfNull(EncodedBytes);
        if(EncodedBytes.Length==0){
            return WicDecodeResult.Failed("Encoded image byte array is empty.");
        }

        var CoInitHr=CoInitializeEx(null,(uint)COINIT_APARTMENTTHREADED);
        if(CoInitHr.FAILED&&CoInitHr.Value!=RPC_E_CHANGEDMODE){
            return WicDecodeResult.Failed($"CoInitializeEx failed. HRESULT=0x{CoInitHr.Value:X8}");
        }
        var OwnsComInit=CoInitHr.SUCCEEDED;

        IWICImagingFactory*Factory=null;
        IStream*Stream=null;
        IWICBitmapDecoder*Decoder=null;
        IWICBitmapFrameDecode*Frame=null;
        IWICFormatConverter*Converter=null;

        try {
            var FactoryClsid=CLSID.CLSID_WICImagingFactory;
            var Hr=CoCreateInstance(&FactoryClsid,null,(uint)CLSCTX_INPROC_SERVER,__uuidof<IWICImagingFactory>(),(void**)&Factory);
            if(Hr.FAILED){
                return WicDecodeResult.Failed($"CoCreateInstance(CLSID_WICImagingFactory) failed. HRESULT=0x{Hr.Value:X8}");
            }

            fixed(byte*PBytes=EncodedBytes){
                Stream=SHCreateMemStream(PBytes,(uint)EncodedBytes.Length);
            }
            if(Stream==null){
                return WicDecodeResult.Failed("SHCreateMemStream returned null.");
            }

            Hr=Factory->CreateDecoderFromStream(Stream,null,WICDecodeMetadataCacheOnDemand,&Decoder);
            if(Hr.FAILED){
                return WicDecodeResult.Failed($"IWICImagingFactory::CreateDecoderFromStream failed (unrecognized/corrupt image data). HRESULT=0x{Hr.Value:X8}");
            }

            Hr=Decoder->GetFrame(0,&Frame);
            if(Hr.FAILED){
                return WicDecodeResult.Failed($"IWICBitmapDecoder::GetFrame failed. HRESULT=0x{Hr.Value:X8}");
            }

            Hr=Factory->CreateFormatConverter(&Converter);
            if(Hr.FAILED){
                return WicDecodeResult.Failed($"IWICImagingFactory::CreateFormatConverter failed. HRESULT=0x{Hr.Value:X8}");
            }

            var RgbaFormat=GUID.GUID_WICPixelFormat32bppRGBA;
            Hr=Converter->Initialize((IWICBitmapSource*)Frame,&RgbaFormat,WICBitmapDitherTypeNone,null,0f,WICBitmapPaletteTypeCustom);
            if(Hr.FAILED){
                return WicDecodeResult.Failed($"IWICFormatConverter::Initialize failed. HRESULT=0x{Hr.Value:X8}");
            }

            uint Width,Height;
            Hr=Converter->GetSize(&Width,&Height);
            if(Hr.FAILED){
                return WicDecodeResult.Failed($"IWICFormatConverter::GetSize failed. HRESULT=0x{Hr.Value:X8}");
            }
            if(Width==0||Height==0){
                return WicDecodeResult.Failed($"Decoded image has zero size ({Width}x{Height}).");
            }
            // 画像はDirect3D 12テクスチャとなり、各辺は最大16384。これを超える画像は利用できない。また、
            // WICはピクセルデータがないヘッダーだけでもデコードするため、20000x20000と申告した画像では後で失敗する前に1.6 GBを消費する。
            if(Width>MaxTextureSide||Height>MaxTextureSide){
                return WicDecodeResult.Failed($"Decoded image ({Width}x{Height}) exceeds the {MaxTextureSide}-pixel side of a Direct3D 12 texture.");
            }

            // 最大サイズは16384 x 16384 x 4バイト（1 GiB）。1つの配列に収まる。
            var Size=(ulong)Width*Height*4;
            var Stride=Width*4;
            var Pixels=new byte[Size];
            fixed(byte*PPixels=Pixels){
                Hr=Converter->CopyPixels(null,Stride,(uint)Pixels.Length,PPixels);
            }
            if(Hr.FAILED){
                return WicDecodeResult.Failed($"IWICFormatConverter::CopyPixels failed. HRESULT=0x{Hr.Value:X8}");
            }

            return WicDecodeResult.Succeeded(new DecodedImage((int)Width,(int)Height,Pixels));
        } finally {
            if(Converter!=null){
                _=Converter->Release();
            }
            if(Frame!=null){
                _=Frame->Release();
            }
            if(Decoder!=null){
                _=Decoder->Release();
            }
            if(Stream!=null){
                _=Stream->Release();
            }
            if(Factory!=null){
                _=Factory->Release();
            }
            if(OwnsComInit){
                CoUninitialize();
            }
        }
    }
}
