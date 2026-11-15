using System;
using System.Collections.Generic;
using System.IO;
namespace CSharpGameEngine.VSExtension;
/// <summary>アセットエディターの種類（UE5 のアセットエディターに対応する）。</summary>
internal enum AssetEditorKind { StaticMesh, Texture, SoundWave, Font, DataAsset }
/// <summary>1 つの拡張子を担当するエディターの記述。<see cref="UsesTextBuffer"/> は DocData が JSON テキストバッファーであることを表す。</summary>
internal sealed class AssetEditorDescriptor(string Extension,AssetEditorKind Kind,bool UsesTextBuffer) {
    internal string Extension { get;}=Extension;
    internal AssetEditorKind Kind { get;}=Kind;
    internal bool UsesTextBuffer { get;}=UsesTextBuffer;
}
/// <summary>
/// ダブルクリックで専用アセットエディターを開く拡張子の対応表。登録属性（<c>ProvideEditorExtension</c>）と
/// <see cref="CSharpGameEngine.Contracts.AssetWorkspaceClassification"/> の分類はテストでこの表と照合する。
/// シーン JSON とシェーダーグラフは別のエディター（Scene Editor / Shader Graph Editor）が担当するため含めない。
/// </summary>
internal static class AssetEditorRegistry {
    /// <summary>VS 標準の JSON エディター・画像エディター（0x32）より優先する。</summary>
    internal const int ExtensionPriority=0x40;
    private static readonly AssetEditorDescriptor[]Descriptors=[
        new(".glb",AssetEditorKind.StaticMesh,false),
        new(".gltf",AssetEditorKind.StaticMesh,false),
        new(".fbx",AssetEditorKind.StaticMesh,false),
        new(".png",AssetEditorKind.Texture,false),
        new(".jpg",AssetEditorKind.Texture,false),
        new(".jpeg",AssetEditorKind.Texture,false),
        new(".dds",AssetEditorKind.Texture,false),
        new(".tga",AssetEditorKind.Texture,false),
        new(".hdr",AssetEditorKind.Texture,false),
        new(".exr",AssetEditorKind.Texture,false),
        new(".wav",AssetEditorKind.SoundWave,false),
        new(".ogg",AssetEditorKind.SoundWave,false),
        new(".mp3",AssetEditorKind.SoundWave,false),
        new(".flac",AssetEditorKind.SoundWave,false),
        new(".cgefont",AssetEditorKind.Font,false),
        new(".ttf",AssetEditorKind.Font,false),
        new(".otf",AssetEditorKind.Font,false),
        new(".cgeasset",AssetEditorKind.DataAsset,true),
    ];
    internal static IReadOnlyList<AssetEditorDescriptor>All=>Descriptors;
    internal static bool TryGet(string?DocumentPath,out AssetEditorDescriptor Descriptor){
        Descriptor=null!;
        if(string.IsNullOrEmpty(DocumentPath)){ return false; }
        string Extension;
        try { Extension=Path.GetExtension(DocumentPath); } catch(ArgumentException){ return false; }
        foreach(var Candidate in Descriptors){
            if(string.Equals(Candidate.Extension,Extension,StringComparison.OrdinalIgnoreCase)){
                Descriptor=Candidate;
                return true;
            }
        }
        return false;
    }
    internal static bool Accepts(string?DocumentPath)=>TryGet(DocumentPath,out _);
}
