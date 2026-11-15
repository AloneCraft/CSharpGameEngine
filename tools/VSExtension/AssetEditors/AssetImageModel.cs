using System;
using System.IO;
namespace CSharpGameEngine.VSExtension;
/// <summary>Texture Editor のチャンネル表示。<see cref="Color"/> はアルファを含むそのままの表示（市松模様の上に合成される）。</summary>
internal enum AssetImageChannelMode { Color, ColorOpaque, Red, Green, Blue, Alpha }
/// <summary>
/// PNG／JPEG のヘッダーから元画像の寸法だけを読む。devenv はソース画像のデコーダーを持たない方針（Cook worker の派生プレビューだけを読む）
/// なので、ピクセルには触れず、有限の先頭バイトだけを読んで寸法を得る。
/// </summary>
internal static class AssetImageHeader {
    /// <summary>JPEG は SOF マーカーまでのセグメントを走査する。EXIF 等で大きくなり得るので上限を設ける。</summary>
    internal const int MaximumHeaderBytes=256*1024;
    internal static bool TryRead(ReadOnlySpan<byte>Header,out int Width,out int Height){
        Width=0;
        Height=0;
        if(Header.Length>=24&&Header[0]==0x89&&Header[1]==0x50&&Header[2]==0x4E&&Header[3]==0x47&&Header[4]==0x0D&&Header[5]==0x0A&&Header[6]==0x1A&&Header[7]==0x0A
            &&Header[12]==(byte)'I'&&Header[13]==(byte)'H'&&Header[14]==(byte)'D'&&Header[15]==(byte)'R'){
            var PngWidth=ReadBigEndian32(Header,16);
            var PngHeight=ReadBigEndian32(Header,20);
            if(PngWidth<=0||PngHeight<=0){ return false; }
            Width=PngWidth;
            Height=PngHeight;
            return true;
        }
        return Header.Length>=4&&Header[0]==0xFF&&Header[1]==0xD8&&TryReadJpeg(Header,out Width,out Height);
    }
    internal static bool TryReadFile(string Path,out int Width,out int Height){
        Width=0;
        Height=0;
        try {
            using var Stream=new FileStream(Path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            var Buffer=new byte[(int)Math.Min(Stream.Length,MaximumHeaderBytes)];
            var Read=0;
            while(Read<Buffer.Length){
                var Count=Stream.Read(Buffer,Read,Buffer.Length-Read);
                if(Count==0){ break; }
                Read+=Count;
            }
            return TryRead(new ReadOnlySpan<byte>(Buffer,0,Read),out Width,out Height);
        } catch(Exception Error) when(Error is IOException or UnauthorizedAccessException){
            return false;
        }
    }
    private static bool TryReadJpeg(ReadOnlySpan<byte>Data,out int Width,out int Height){
        Width=0;
        Height=0;
        var Position=2;
        while(Position+4<=Data.Length){
            if(Data[Position]!=0xFF){ return false; }
            var Marker=Data[Position+1];
            if(Marker==0xFF){ Position++; continue; }
            if(Marker is 0xD8 or 0x01||Marker is>=0xD0 and<=0xD7){ Position+=2; continue; }
            if(Marker==0xD9||Marker==0xDA){ return false; }
            var Length=(Data[Position+2]<<8)|Data[Position+3];
            if(Length<2){ return false; }
            var IsFrame=Marker is>=0xC0 and<=0xCF&&Marker is not(0xC4 or 0xC8 or 0xCC);
            if(IsFrame){
                if(Position+9>Data.Length){ return false; }
                Height=(Data[Position+5]<<8)|Data[Position+6];
                Width=(Data[Position+7]<<8)|Data[Position+8];
                return Width>0&&Height>0;
            }
            Position+=2+Length;
        }
        return false;
    }
    private static int ReadBigEndian32(ReadOnlySpan<byte>Data,int Offset)=>
        (Data[Offset]<<24)|(Data[Offset+1]<<16)|(Data[Offset+2]<<8)|Data[Offset+3];
}
/// <summary>BGRA32 ピクセル列からチャンネル表示用のピクセル列を作る。</summary>
internal static class AssetImageChannels {
    internal static byte[] Extract(byte[]Bgra,AssetImageChannelMode Mode){
        if(Bgra is null){ throw new ArgumentNullException(nameof(Bgra)); }
        if(Bgra.Length%4!=0){ throw new ArgumentException("BGRA32 pixel data must be a multiple of 4 bytes.",nameof(Bgra)); }
        var Result=new byte[Bgra.Length];
        for(var Index=0;Index<Bgra.Length;Index+=4){
            var B=Bgra[Index];
            var G=Bgra[Index+1];
            var R=Bgra[Index+2];
            var A=Bgra[Index+3];
            switch(Mode){
                case AssetImageChannelMode.Color:
                    Result[Index]=B;Result[Index+1]=G;Result[Index+2]=R;Result[Index+3]=A;
                    break;
                case AssetImageChannelMode.ColorOpaque:
                    Result[Index]=B;Result[Index+1]=G;Result[Index+2]=R;Result[Index+3]=255;
                    break;
                case AssetImageChannelMode.Red:
                    Result[Index]=R;Result[Index+1]=R;Result[Index+2]=R;Result[Index+3]=255;
                    break;
                case AssetImageChannelMode.Green:
                    Result[Index]=G;Result[Index+1]=G;Result[Index+2]=G;Result[Index+3]=255;
                    break;
                case AssetImageChannelMode.Blue:
                    Result[Index]=B;Result[Index+1]=B;Result[Index+2]=B;Result[Index+3]=255;
                    break;
                case AssetImageChannelMode.Alpha:
                    Result[Index]=A;Result[Index+1]=A;Result[Index+2]=A;Result[Index+3]=255;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(Mode));
            }
        }
        return Result;
    }
}
/// <summary>Texture Editor のズーム計算。倍率は元画像のピクセルに対する比（100% = 1）。</summary>
internal static class AssetImageZoom {
    internal const float MinimumZoom=0.05f;
    internal const float MaximumZoom=32f;
    private const float NotchFactor=1.1f;
    /// <summary>画像全体が表示領域に収まる倍率。</summary>
    internal static float Fit(float ViewportWidth,float ViewportHeight,int ImageWidth,int ImageHeight){
        if(ImageWidth<=0||ImageHeight<=0||ViewportWidth<=0f||ViewportHeight<=0f){ return 1f; }
        return Clamp(Math.Min(ViewportWidth/ImageWidth,ViewportHeight/ImageHeight));
    }
    /// <summary>一度に反映する目盛りの上限。<c>FloatMath.Pow</c> は float に収まらない結果を例外にするので、極端なホイール量でも例外にならないようにする。</summary>
    internal const int MaximumNotches=64;
    internal static float Step(float Current,int WheelNotches)=>Clamp(Current*FloatMath.Pow(NotchFactor,Math.Max(-MaximumNotches,Math.Min(MaximumNotches,WheelNotches))));
    /// <summary>拡大時は画素を見分けられるように最近傍補間にする。</summary>
    internal static bool UseNearestNeighbor(float Zoom)=>Zoom>=2f;
    /// <summary>
    /// 派生プレビューが元画像より小さい場合（最大辺が 2048px に縮小される）の、プレビュー上の表示倍率。
    /// 元画像の寸法が不明、またはプレビューが元と同じ大きさなら <paramref name="Zoom"/> のまま。
    /// </summary>
    internal static float DisplayScale(float Zoom,int OriginalWidth,int PreviewWidth)=>
        OriginalWidth>0&&PreviewWidth>0?Zoom*OriginalWidth/PreviewWidth:Zoom;
    private static float Clamp(float Value)=>Math.Min(Math.Max(Value,MinimumZoom),MaximumZoom);
}
