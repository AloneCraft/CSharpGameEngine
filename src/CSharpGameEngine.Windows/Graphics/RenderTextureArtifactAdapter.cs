using CSharpGameEngine.Rendering;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>Windows import adapter. Encoded source bytes are decoded and discarded at this call.</summary>
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
