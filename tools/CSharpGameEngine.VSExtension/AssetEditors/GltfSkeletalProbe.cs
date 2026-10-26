using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
namespace CSharpGameEngine.VSExtension;
/// <summary>glTF がスキンとアニメーションを持つかどうか。VSIX は glTF を読み込まない（ADR-0019/0020、ADR-0090）ので、JSON だけを見て編集画面の種類を決める。</summary>
internal sealed class GltfSkeletalInfo(int SkinCount,IReadOnlyList<string>AnimationNames) {
    internal int SkinCount { get;}=SkinCount;
    internal IReadOnlyList<string>AnimationNames { get;}=AnimationNames;
    /// <summary>スキンを正確に 1 つ持つ（ランタイムのスケルタルメッシュと同じ条件）。</summary>
    internal bool IsSkeletalMesh=>this.SkinCount==1;
    /// <summary>スキンを持たず、アニメーションだけを持つ。プレビューするメッシュを別に選ぶ。</summary>
    internal bool IsAnimationOnly=>this.SkinCount==0&&this.AnimationNames.Count>0;
}
/// <summary>.glb（JSON チャンクだけ）または .gltf を読み、<c>skins</c> と <c>animations</c> の数を調べる。バイナリチャンクは読まない。</summary>
internal static class GltfSkeletalProbe {
    /// <summary>JSON として読む上限。これを超える（またはヘッダーが壊れている）ファイルは調べられない扱い。</summary>
    internal const int MaximumJsonBytes=64*1024*1024;
    private const uint GlbMagic=0x46546C67;
    private const uint JsonChunk=0x4E4F534A;
    /// <summary>調べられたとき（.glb／.gltf で JSON が読めた）に結果を返す。それ以外は null。</summary>
    internal static GltfSkeletalInfo?Probe(string Path){
        try {
            var Extension=System.IO.Path.GetExtension(Path);
            if(string.Equals(Extension,".glb",StringComparison.OrdinalIgnoreCase)){
                using var Stream=new FileStream(Path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
                var Json=ReadGlbJson(Stream);
                return Json is null?null:Parse(Json);
            }
            if(string.Equals(Extension,".gltf",StringComparison.OrdinalIgnoreCase)){
                var Info=new FileInfo(Path);
                if(!Info.Exists||Info.Length>MaximumJsonBytes){ return null; }
                return Parse(File.ReadAllBytes(Path));
            }
        } catch(IOException) {
        } catch(UnauthorizedAccessException) {
        }
        return null;
    }
    /// <summary>.glb の先頭チャンク（JSON）の中身。ヘッダーが glTF 2 の形でなければ null。</summary>
    internal static byte[]?ReadGlbJson(Stream Stream){
        var Header=new byte[20];
        if(ReadFully(Stream,Header)!=Header.Length){ return null; }
        if(BitConverter.ToUInt32(Header,0)!=GlbMagic||BitConverter.ToUInt32(Header,4)!=2u){ return null; }
        var Length=BitConverter.ToUInt32(Header,12);
        if(BitConverter.ToUInt32(Header,16)!=JsonChunk||Length==0||Length>MaximumJsonBytes){ return null; }
        var Json=new byte[Length];
        return ReadFully(Stream,Json)==Json.Length?Json:null;
    }
    /// <summary>glTF の JSON から <c>skins</c> の数と <c>animations</c> の名前を取り出す。JSON として読めなければ null。</summary>
    internal static GltfSkeletalInfo?Parse(byte[]Json){
        try {
            using var Document=JsonDocument.Parse(Json,new JsonDocumentOptions { AllowTrailingCommas=true });
            var Root=Document.RootElement;
            if(Root.ValueKind!=JsonValueKind.Object){ return null; }
            var Skins=Root.TryGetProperty("skins",out var SkinArray)&&SkinArray.ValueKind==JsonValueKind.Array?SkinArray.GetArrayLength():0;
            var Names=new List<string>();
            if(Root.TryGetProperty("animations",out var Animations)&&Animations.ValueKind==JsonValueKind.Array){
                var Index=0;
                foreach(var Animation in Animations.EnumerateArray()){
                    var Name=Animation.ValueKind==JsonValueKind.Object&&Animation.TryGetProperty("name",out var Value)&&Value.ValueKind==JsonValueKind.String?Value.GetString():null;
                    Names.Add(string.IsNullOrEmpty(Name)?"animation"+Index:Name!);
                    Index++;
                }
            }
            return new GltfSkeletalInfo(Skins,Names);
        } catch(JsonException) {
            return null;
        }
    }
    private static int ReadFully(Stream Stream,byte[]Buffer){
        var Total=0;
        while(Total<Buffer.Length){
            var Read=Stream.Read(Buffer,Total,Buffer.Length-Total);
            if(Read<=0){ break; }
            Total+=Read;
        }
        return Total;
    }
}
