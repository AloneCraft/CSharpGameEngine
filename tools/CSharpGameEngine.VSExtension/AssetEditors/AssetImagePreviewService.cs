using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// Texture Editor 用の大きい派生プレビュー（最大 <see cref="AssetThumbnailRequest.MaximumImageEdge"/> px）。
/// devenv はソース画像のデコーダーを持たない方針なので、Assets のサムネイルと同じく L7 Cook worker の <c>thumbnail-image</c> が
/// ソースを読み、縮小した PNG だけを返す。ソースにもメタデータにも書かない（読み取り専用の派生物）。呼び出しはバックグラウンドスレッドで行う。
/// </summary>
internal static class AssetImagePreviewService {
    /// <summary>派生 PNG の受け入れ上限（ピクセルあたり 8 バイト）。</summary>
    private const long BytesPerPixelBound=8;
    internal static BitmapSource Render(string SourcePath,int MaxEdge,CancellationToken Cancellation){
        var Context=CgeProjectContext.Resolve(SourcePath)??throw new InvalidOperationException(VsixText.Get("Common_GameProjectNotFound"));
        var Request=new AssetThumbnailRequest { ImagePath=SourcePath,MaxEdge=MaxEdge };
        var Error=AssetThumbnailRequest.Validate(Request);
        if(Error is not null){ throw new InvalidDataException(Error); }
        var Temp=Path.Combine(Path.GetTempPath(),"CgeAssetPreviews",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Temp);
        try {
            var Input=Path.Combine(Temp,"request.json");
            var Output=Path.Combine(Temp,"preview.png");
            File.WriteAllText(Input,JsonSerializer.Serialize(Request));
            var Worker=BridgeExecutableLocator.FindExecutable(Context.WorkspaceRoot,@"tools\CSharpGameEngine.Cook.Cli")
                ??throw new FileNotFoundException("The packaged Cook preview worker is unavailable.");
            if(!Worker.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)){ throw new FileNotFoundException("The packaged Cook preview worker executable is unavailable."); }
            ExternalBridgeProcess.RunExecutableCommandProducingFile(Worker,"CSharpGameEngine.Cook.Cli",["thumbnail-image",Input,Output],Output,Context.WorkspaceRoot,TimeSpan.FromSeconds(30),false,Cancellation);
            Cancellation.ThrowIfCancellationRequested();
            using var Stream=File.OpenRead(Output);
            if(Stream.Length>(long)MaxEdge*MaxEdge*BytesPerPixelBound){ throw new InvalidDataException("Preview PNG exceeds its bound."); }
            var Bitmap=new BitmapImage();
            Bitmap.BeginInit();
            Bitmap.CacheOption=BitmapCacheOption.OnLoad;
            Bitmap.StreamSource=Stream;
            Bitmap.EndInit();
            Bitmap.Freeze();
            if(Bitmap.PixelWidth is<1||Bitmap.PixelHeight is<1||Bitmap.PixelWidth>MaxEdge||Bitmap.PixelHeight>MaxEdge){ throw new InvalidDataException("Preview dimensions are invalid."); }
            return Bitmap;
        } finally {
            try { Directory.Delete(Temp,true); } catch(IOException){ } catch(UnauthorizedAccessException){ }
        }
    }
    /// <summary>フレームを BGRA32 のピクセル列にする。</summary>
    internal static byte[] ToBgra(BitmapSource Source,out int Width,out int Height){
        var Converted=new FormatConvertedBitmap(Source,PixelFormats.Bgra32,null,0);
        Width=Converted.PixelWidth;
        Height=Converted.PixelHeight;
        var Pixels=new byte[Width*Height*4];
        Converted.CopyPixels(Pixels,Width*4,0);
        return Pixels;
    }
}
