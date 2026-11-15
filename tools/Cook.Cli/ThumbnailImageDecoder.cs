using CSharpGameEngine.Contracts;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.CLSCTX;
using static TerraFX.Interop.Windows.COINIT;
using static TerraFX.Interop.Windows.WICBitmapDitherType;
using static TerraFX.Interop.Windows.WICBitmapPaletteType;
using static TerraFX.Interop.Windows.WICDecodeOptions;
using static TerraFX.Interop.Windows.Windows;
namespace CSharpGameEngine.Cook.Cli;
/// <summary>Tightly-packed RGBA8 pixel data (stride = Width * 4).</summary>
internal sealed record ThumbnailImage(int Width,int Height,byte[]Pixels);
internal sealed class ThumbnailDecodeResult {
    private ThumbnailDecodeResult(bool Success,ThumbnailImage?Image,string?Error){
        this.Success=Success;
        this.Image=Image;
        this.Error=Error;
    }
    public bool Success { get;}
    public ThumbnailImage?Image { get;}
    public string?Error { get;}
    public static ThumbnailDecodeResult Succeeded(ThumbnailImage Image)=>new(true,Image,null);
    public static ThumbnailDecodeResult Failed(string Error)=>new(false,null,Error);
}
/// <summary>
/// Decodes source images only inside the L7 Cook worker, using bounded WIC scaling to RGBA8 pixels
/// via the Windows Imaging Component, using the TerraFX.Interop.Windows bindings already used
/// by the engine. The provider types remain private to this L7 executable. Windows-only.
/// </summary>
internal static unsafe class ThumbnailImageDecoder {
    // RPC_E_CHANGEDMODE: COM was already initialized on this thread with a different concurrency
    // model. Not fatal - proceed without owning the CoInitializeEx call (skip CoUninitialize).
    private const int RPC_E_CHANGEDMODE=unchecked((int)0x80010106);
    /// <summary>Bound source dimensions before asking a native codec for pixels.</summary>
    private const uint MaxTextureSide=16384;
    /// <summary>WIC scales before copying pixels into managed memory; thumbnail requests never allocate a full-size managed image.</summary>
    internal static ThumbnailDecodeResult DecodeThumbnail(byte[]EncodedBytes,int MaximumEdge){
        if(MaximumEdge is <1 or >AssetThumbnailRequest.MaximumImageEdge){ throw new ArgumentOutOfRangeException(nameof(MaximumEdge)); }
        if(!OperatingSystem.IsWindowsVersionAtLeast(6,1)){ return ThumbnailDecodeResult.Failed("Windows WIC thumbnail decoding is unavailable on this platform."); }
        return DecodeCore(EncodedBytes,MaximumEdge);
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
    private static ThumbnailDecodeResult DecodeCore(byte[]EncodedBytes,int MaximumEdge){
        ArgumentNullException.ThrowIfNull(EncodedBytes);
        if(EncodedBytes.Length==0){
            return ThumbnailDecodeResult.Failed("Encoded image byte array is empty.");
        }
        var CoInitHr=CoInitializeEx(null,(uint)COINIT_APARTMENTTHREADED);
        if(CoInitHr.FAILED&&CoInitHr.Value!=RPC_E_CHANGEDMODE){
            return ThumbnailDecodeResult.Failed($"CoInitializeEx failed. HRESULT=0x{CoInitHr.Value:X8}");
        }
        var OwnsComInit=CoInitHr.SUCCEEDED;
        IWICImagingFactory*Factory=null;
        IStream*Stream=null;
        IWICBitmapDecoder*Decoder=null;
        IWICBitmapFrameDecode*Frame=null;
        IWICFormatConverter*Converter=null;
        IWICBitmapScaler*Scaler=null;
        try {
            var FactoryClsid=CLSID.CLSID_WICImagingFactory;
            var Hr=CoCreateInstance(&FactoryClsid,null,(uint)CLSCTX_INPROC_SERVER,__uuidof<IWICImagingFactory>(),(void**)&Factory);
            if(Hr.FAILED){
                return ThumbnailDecodeResult.Failed($"CoCreateInstance(CLSID_WICImagingFactory) failed. HRESULT=0x{Hr.Value:X8}");
            }
            fixed(byte*PBytes=EncodedBytes){
                Stream=SHCreateMemStream(PBytes,(uint)EncodedBytes.Length);
            }
            if(Stream==null){
                return ThumbnailDecodeResult.Failed("SHCreateMemStream returned null.");
            }
            Hr=Factory->CreateDecoderFromStream(Stream,null,WICDecodeMetadataCacheOnDemand,&Decoder);
            if(Hr.FAILED){
                return ThumbnailDecodeResult.Failed($"IWICImagingFactory::CreateDecoderFromStream failed (unrecognized/corrupt image data). HRESULT=0x{Hr.Value:X8}");
            }
            Hr=Decoder->GetFrame(0,&Frame);
            if(Hr.FAILED){
                return ThumbnailDecodeResult.Failed($"IWICBitmapDecoder::GetFrame failed. HRESULT=0x{Hr.Value:X8}");
            }
            uint Width,Height;
            Hr=Frame->GetSize(&Width,&Height);
            if(Hr.FAILED){
                return ThumbnailDecodeResult.Failed($"IWICBitmapFrameDecode::GetSize failed. HRESULT=0x{Hr.Value:X8}");
            }
            if(Width==0||Height==0){
                return ThumbnailDecodeResult.Failed($"Decoded image has zero size ({Width}x{Height}).");
            }
            // Reject implausible source dimensions before invoking native scaling.
            // WIC can read dimensions even when the pixel stream is absent.
            if(Width>MaxTextureSide||Height>MaxTextureSide||(ulong)Width*Height>64UL*1024*1024){
                return ThumbnailDecodeResult.Failed($"Decoded image ({Width}x{Height}) exceeds the thumbnail source budget (edge {MaxTextureSide}, area 64 megapixels).");
            }
            if(MaximumEdge>0&&(Width>(uint)MaximumEdge||Height>(uint)MaximumEdge)){
                var Scale=(float)MaximumEdge/Math.Max(Width,Height);
                Width=Math.Max(1,(uint)(Width*Scale));Height=Math.Max(1,(uint)(Height*Scale));
                Hr=Factory->CreateBitmapScaler(&Scaler);
                if(Hr.FAILED){ return ThumbnailDecodeResult.Failed("WIC thumbnail scaler is unavailable."); }
                Hr=Scaler->Initialize((IWICBitmapSource*)Frame,Width,Height,WICBitmapInterpolationMode.WICBitmapInterpolationModeFant);
                if(Hr.FAILED){ return ThumbnailDecodeResult.Failed("WIC thumbnail scaling failed."); }
            }
            // A WIC scaler may choose a different pixel format. Convert its result to the wire's explicit RGBA order.
            Hr=Factory->CreateFormatConverter(&Converter);
            if(Hr.FAILED){ return ThumbnailDecodeResult.Failed($"IWICImagingFactory::CreateFormatConverter failed. HRESULT=0x{Hr.Value:X8}"); }
            var RgbaFormat=GUID.GUID_WICPixelFormat32bppRGBA;
            Hr=Converter->Initialize(Scaler==null?(IWICBitmapSource*)Frame:(IWICBitmapSource*)Scaler,&RgbaFormat,WICBitmapDitherTypeNone,null,0f,WICBitmapPaletteTypeCustom);
            if(Hr.FAILED){ return ThumbnailDecodeResult.Failed($"IWICFormatConverter::Initialize failed. HRESULT=0x{Hr.Value:X8}"); }
            var Size=(ulong)Width*Height*4;
            var Stride=Width*4;
            var Pixels=new byte[Size];
            fixed(byte*PPixels=Pixels){
                Hr=Converter->CopyPixels(null,Stride,(uint)Pixels.Length,PPixels);
            }
            if(Hr.FAILED){
                return ThumbnailDecodeResult.Failed($"IWICFormatConverter::CopyPixels failed. HRESULT=0x{Hr.Value:X8}");
            }
            return ThumbnailDecodeResult.Succeeded(new ThumbnailImage((int)Width,(int)Height,Pixels));
        } finally {
            if(Scaler!=null){ _=Scaler->Release(); }
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
