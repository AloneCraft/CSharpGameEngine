using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.CLSCTX;
using static TerraFX.Interop.Windows.COINIT;
using static TerraFX.Interop.Windows.WICBitmapDitherType;
using static TerraFX.Interop.Windows.WICBitmapPaletteType;
using static TerraFX.Interop.Windows.WICDecodeOptions;
using static TerraFX.Interop.Windows.Windows;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>Tightly-packed RGBA8 pixel data (stride = Width * 4).</summary>
public sealed record DecodedImage(int Width, int Height, byte[] Pixels);

public sealed class WicDecodeResult
{
    private WicDecodeResult(bool success, DecodedImage? image, string? error)
    {
        Success = success;
        Image = image;
        Error = error;
    }

    public bool Success { get; }

    public DecodedImage? Image { get; }

    public string? Error { get; }

    public static WicDecodeResult Succeeded(DecodedImage image) => new(true, image, null);

    public static WicDecodeResult Failed(string error) => new(false, null, error);
}

/// <summary>
/// Decodes PNG/JPEG (or anything else WIC's installed codecs understand) bytes to raw RGBA8 pixels
/// via the Windows Imaging Component, using the TerraFX.Interop.Windows bindings already used
/// throughout this project - no new package dependency. Windows-only.
/// </summary>
public static unsafe class WicImageDecoder
{
    // RPC_E_CHANGEDMODE: COM was already initialized on this thread with a different concurrency
    // model. Not fatal - proceed without owning the CoInitializeEx call (skip CoUninitialize).
    private const int RPC_E_CHANGEDMODE = unchecked((int)0x80010106);

    public static WicDecodeResult Decode(byte[] encodedBytes)
    {
        ArgumentNullException.ThrowIfNull(encodedBytes);
        if (encodedBytes.Length == 0)
        {
            return WicDecodeResult.Failed("Encoded image byte array is empty.");
        }

        HRESULT coInitHr = CoInitializeEx(null, (uint)COINIT_APARTMENTTHREADED);
        if (coInitHr.FAILED && coInitHr.Value != RPC_E_CHANGEDMODE)
        {
            return WicDecodeResult.Failed($"CoInitializeEx failed. HRESULT=0x{coInitHr.Value:X8}");
        }
        bool ownsComInit = coInitHr.SUCCEEDED;

        IWICImagingFactory* factory = null;
        IStream* stream = null;
        IWICBitmapDecoder* decoder = null;
        IWICBitmapFrameDecode* frame = null;
        IWICFormatConverter* converter = null;

        try
        {
            Guid factoryClsid = CLSID.CLSID_WICImagingFactory;
            HRESULT hr = CoCreateInstance(&factoryClsid, null, (uint)CLSCTX_INPROC_SERVER, __uuidof<IWICImagingFactory>(), (void**)&factory);
            if (hr.FAILED)
            {
                return WicDecodeResult.Failed($"CoCreateInstance(CLSID_WICImagingFactory) failed. HRESULT=0x{hr.Value:X8}");
            }

            fixed (byte* pBytes = encodedBytes)
            {
                stream = SHCreateMemStream(pBytes, (uint)encodedBytes.Length);
            }
            if (stream == null)
            {
                return WicDecodeResult.Failed("SHCreateMemStream returned null.");
            }

            hr = factory->CreateDecoderFromStream(stream, null, WICDecodeMetadataCacheOnDemand, &decoder);
            if (hr.FAILED)
            {
                return WicDecodeResult.Failed($"IWICImagingFactory::CreateDecoderFromStream failed (unrecognized/corrupt image data). HRESULT=0x{hr.Value:X8}");
            }

            hr = decoder->GetFrame(0, &frame);
            if (hr.FAILED)
            {
                return WicDecodeResult.Failed($"IWICBitmapDecoder::GetFrame failed. HRESULT=0x{hr.Value:X8}");
            }

            hr = factory->CreateFormatConverter(&converter);
            if (hr.FAILED)
            {
                return WicDecodeResult.Failed($"IWICImagingFactory::CreateFormatConverter failed. HRESULT=0x{hr.Value:X8}");
            }

            Guid rgbaFormat = GUID.GUID_WICPixelFormat32bppRGBA;
            hr = converter->Initialize((IWICBitmapSource*)frame, &rgbaFormat, WICBitmapDitherTypeNone, null, 0.0, WICBitmapPaletteTypeCustom);
            if (hr.FAILED)
            {
                return WicDecodeResult.Failed($"IWICFormatConverter::Initialize failed. HRESULT=0x{hr.Value:X8}");
            }

            uint width, height;
            hr = converter->GetSize(&width, &height);
            if (hr.FAILED)
            {
                return WicDecodeResult.Failed($"IWICFormatConverter::GetSize failed. HRESULT=0x{hr.Value:X8}");
            }
            if (width == 0 || height == 0)
            {
                return WicDecodeResult.Failed($"Decoded image has zero size ({width}x{height}).");
            }

            uint stride = width * 4;
            byte[] pixels = new byte[stride * height];
            fixed (byte* pPixels = pixels)
            {
                hr = converter->CopyPixels(null, stride, (uint)pixels.Length, pPixels);
            }
            if (hr.FAILED)
            {
                return WicDecodeResult.Failed($"IWICFormatConverter::CopyPixels failed. HRESULT=0x{hr.Value:X8}");
            }

            return WicDecodeResult.Succeeded(new DecodedImage((int)width, (int)height, pixels));
        }
        finally
        {
            if (converter != null)
            {
                _ = converter->Release();
            }
            if (frame != null)
            {
                _ = frame->Release();
            }
            if (decoder != null)
            {
                _ = decoder->Release();
            }
            if (stream != null)
            {
                _ = stream->Release();
            }
            if (factory != null)
            {
                _ = factory->Release();
            }
            if (ownsComInit)
            {
                CoUninitialize();
            }
        }
    }
}
