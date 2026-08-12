using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.CLSCTX;
using static TerraFX.Interop.Windows.COINIT;
using static TerraFX.Interop.Windows.WICBitmapDitherType;
using static TerraFX.Interop.Windows.WICBitmapPaletteType;
using static TerraFX.Interop.Windows.WICDecodeOptions;
using static TerraFX.Interop.Windows.Windows;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>Tightly-packed RGBA8 pixel data (stride = Width * 4).</summary>
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
/// Decodes PNG/JPEG (or anything else WIC's installed codecs understand) bytes to raw RGBA8 pixels
/// via the Windows Imaging Component, using the TerraFX.Interop.Windows bindings already used
/// throughout this project - no new package dependency. Windows-only.
/// </summary>
public static unsafe class WicImageDecoder {
    // RPC_E_CHANGEDMODE: COM was already initialized on this thread with a different concurrency
    // model. Not fatal - proceed without owning the CoInitializeEx call (skip CoUninitialize).
    private const int RPC_E_CHANGEDMODE=unchecked((int)0x80010106);

    public static WicDecodeResult Decode(byte[]EncodedBytes){
        ArgumentNullException.ThrowIfNull(EncodedBytes);
        if(EncodedBytes.Length==0){
            return WicDecodeResult.Failed("Encoded image byte array is empty.");
        }

        HRESULT CoInitHr=CoInitializeEx(null,(uint)COINIT_APARTMENTTHREADED);
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
            Guid FactoryClsid=CLSID.CLSID_WICImagingFactory;
            HRESULT Hr=CoCreateInstance(&FactoryClsid,null,(uint)CLSCTX_INPROC_SERVER,__uuidof<IWICImagingFactory>(),(void**)&Factory);
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

            Guid RgbaFormat=GUID.GUID_WICPixelFormat32bppRGBA;
            Hr=Converter->Initialize((IWICBitmapSource*)Frame,&RgbaFormat,WICBitmapDitherTypeNone,null,0.0,WICBitmapPaletteTypeCustom);
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

            var Stride=Width*4;
            var Pixels=new byte[Stride*Height];
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
