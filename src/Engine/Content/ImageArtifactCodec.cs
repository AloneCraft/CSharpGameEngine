using System.Text;
namespace CSharpGameEngine.Content;
public sealed record ImageData(int Width,int Height,string MediaType,byte[]EncodedBytes);
public sealed record TextureMipData(int Width,int Height,byte[]Pixels);
public sealed record TextureOutputData(bool Srgb,TextureMipData[]Levels);
/// <summary>サイズ上限のあるエンジン所有ペイロード。Imageを読み込んでもデコードやGPUテクスチャの割り当ては行わない。</summary>
public static class ImageArtifactCodec {
    public static RuntimeAssetCodec<ImageData>CreateImageRuntimeCodec()=>new(Kind=>Kind==ContentArtifactKind.Image,
        (Artifact,Cancellation)=>{ Cancellation.ThrowIfCancellationRequested();return ValueTask.FromResult(ReadImage(Artifact.Payload.AsSpan())); });
    public static RuntimeAssetCodec<TextureOutputData>CreateTextureRuntimeCodec()=>new(Kind=>Kind==ContentArtifactKind.Texture,
        (Artifact,Cancellation)=>{ Cancellation.ThrowIfCancellationRequested();return ValueTask.FromResult(ReadTexture(Artifact.Payload.AsSpan())); });
    private const int MaximumBytes=384*1024*1024;
    public static byte[]WriteImage(ImageData Image){
        ValidateSize(Image.Width,Image.Height);
        if(Image.MediaType is not("image/png" or "image/jpeg")||Image.EncodedBytes.Length is <1 or >MaximumBytes){ throw new InvalidDataException("Invalid encoded image."); }
        using var Stream=new MemoryStream();using var Writer=new BinaryWriter(Stream,Encoding.UTF8,true);
        Writer.Write(0x31494743);Writer.Write(Image.Width);Writer.Write(Image.Height);Writer.Write(Image.MediaType);Writer.Write(Image.EncodedBytes.Length);Writer.Write(Image.EncodedBytes);return Stream.ToArray();
    }
    public static ImageData ReadImage(ReadOnlySpan<byte>Payload){
        using var Stream=Open(Payload);using var Reader=new BinaryReader(Stream,Encoding.UTF8,true);
        if(Reader.ReadInt32()!=0x31494743){ throw new InvalidDataException("Unknown image payload."); }
        var Width=Reader.ReadInt32();var Height=Reader.ReadInt32();ValidateSize(Width,Height);
        var Media=Reader.ReadString();if(Media is not("image/png" or "image/jpeg")){ throw new InvalidDataException("Unknown image media type."); }
        var Length=Reader.ReadInt32();if(Length<1||Length>MaximumBytes||Length!=Stream.Length-Stream.Position){ throw new InvalidDataException("Invalid image length."); }
        return new(Width,Height,Media,Reader.ReadBytes(Length));
    }
    public static byte[]WriteTexture(TextureOutputData Texture){
        if(Texture.Levels is null||Texture.Levels.Length is <1 or >14){ throw new InvalidDataException("Invalid mip count."); }
        using var Stream=new MemoryStream();using var Writer=new BinaryWriter(Stream,Encoding.UTF8,true);
        Writer.Write(0x31544743);Writer.Write(Texture.Srgb);Writer.Write(Texture.Levels.Length);
        var PreviousWidth=0;var PreviousHeight=0;
        foreach(var Level in Texture.Levels){
            ValidateSize(Level.Width,Level.Height);
            if(Level.Pixels.Length!=checked(Level.Width*Level.Height*4)||PreviousWidth>0&&(Level.Width!=Math.Max(1,PreviousWidth/2)||Level.Height!=Math.Max(1,PreviousHeight/2))){ throw new InvalidDataException("Invalid mip geometry."); }
            if(PreviousWidth==1&&PreviousHeight==1){ throw new InvalidDataException("Extra mip after 1x1."); }
            Writer.Write(Level.Width);Writer.Write(Level.Height);Writer.Write(Level.Pixels.Length);Writer.Write(Level.Pixels);PreviousWidth=Level.Width;PreviousHeight=Level.Height;
        }
        if(Stream.Length>MaximumBytes){ throw new InvalidDataException("Texture exceeds payload bound."); }
        return Stream.ToArray();
    }
    public static TextureOutputData ReadTexture(ReadOnlySpan<byte>Payload){
        using var Stream=Open(Payload);using var Reader=new BinaryReader(Stream,Encoding.UTF8,true);
        if(Reader.ReadInt32()!=0x31544743){ throw new InvalidDataException("Unknown texture payload."); }
        var Flag=Reader.ReadByte();if(Flag>1){ throw new InvalidDataException("Invalid colour encoding."); }
        var Count=Reader.ReadInt32();if(Count is <1 or >14){ throw new InvalidDataException("Invalid mip count."); }
        var Levels=new TextureMipData[Count];
        for(var I=0;I<Count;I++){
            var Width=Reader.ReadInt32();var Height=Reader.ReadInt32();ValidateSize(Width,Height);var Length=Reader.ReadInt32();
            if(Length!=checked(Width*Height*4)||Length>Stream.Length-Stream.Position){ throw new InvalidDataException("Invalid mip length."); }
            Levels[I]=new(Width,Height,Reader.ReadBytes(Length));
        }
        if(Stream.Position!=Stream.Length){ throw new InvalidDataException("Trailing texture payload."); }
        var Texture=new TextureOutputData(Flag==1,Levels);_=WriteTexture(Texture);return Texture;
    }
    public static void ValidateSize(int Width,int Height){
        if(Width is <1 or >8192||Height is <1 or >8192){ throw new InvalidDataException("IMAGE_SIZE: maximum image edge is 8192."); }
    }
    private static MemoryStream Open(ReadOnlySpan<byte>Bytes){
        if(Bytes.Length is <13 or >MaximumBytes){ throw new InvalidDataException("Invalid image/texture payload size."); }
        return new(Bytes.ToArray(),false);
    }
}
