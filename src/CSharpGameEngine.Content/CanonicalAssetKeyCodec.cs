using System.Text;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Content;

/// <summary>Fixed canonical binary encoding shared by artifact envelopes and world-cell payloads.</summary>
public static class CanonicalAssetKeyCodec {
    public const int EncodedLength=69;

    public static void Write(Span<byte>Bytes,AssetKey Key){
        if(Bytes.Length<EncodedLength){ throw new ArgumentException("AssetKey output requires 69 bytes.",nameof(Bytes)); }
        if(!Key.IsValid){ throw new ArgumentException("A valid AssetKey is required.",nameof(Key)); }
        byte[]AssetText=Encoding.ASCII.GetBytes(Key.AssetId.ToString());
        if(AssetText.Length!=36){ throw new InvalidOperationException("Canonical AssetId text must contain 36 bytes."); }
        AssetText.CopyTo(Bytes.Slice(0,36));
        Bytes[36]=Key.SubAssetId.HasValue?(byte)1:(byte)0;
        Bytes.Slice(37,32).Clear();
        if(Key.SubAssetId is { }Sub){ Sub.Value.ToByteArray().CopyTo(Bytes.Slice(37,32)); }
    }

    public static bool TryRead(ReadOnlySpan<byte>Bytes,out AssetKey Key){
        Key=default;
        if(Bytes.Length<EncodedLength){ return false; }
        string AssetText=Encoding.ASCII.GetString(Bytes.Slice(0,36));
        if(!AssetId.TryParse(AssetText,out var Asset)||!Asset.IsValid){ return false; }
        byte HasSub=Bytes[36];
        if(HasSub>1){ return false; }
        Hash256 SubHash=Hash256.FromBytes(Bytes.Slice(37,32).ToArray());
        SubAssetId?Sub=null;
        if(HasSub==0){
            if(SubHash.IsValid){ return false; }
        } else {
            if(!SubHash.IsValid){ return false; }
            Sub=new SubAssetId(SubHash);
        }
        Key=new AssetKey(Asset,Sub);
        return true;
    }
}
