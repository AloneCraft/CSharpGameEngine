using CSharpGameEngine.Rendering;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>Windows 用インポートアダプター。この呼び出しで符号化済みソースバイト列をデコードし、破棄する。</summary>
public static class RenderTextureArtifactAdapter {
    public static RenderTextureResource?DecodeOptional(ReadOnlySpan<byte>EncodedBytes){
        if(EncodedBytes.IsEmpty){ return null; }
        WicDecodeResult Result=WicImageDecoder.Decode(EncodedBytes.ToArray());
        if(!Result.Success||Result.Image is not{ }Image){
            throw new InvalidDataException($"Texture decode failed: {Result.Error}");
        }
        return RenderArtifactCodec.CreateProceduralTexture(
            Image.Width,Image.Height,Image.Pixels);
    }
}
