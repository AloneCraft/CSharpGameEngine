using System;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension;
/// <summary>文書名変更時のスケルタル入力の付け替え。外部のプレビューメッシュは維持する。</summary>
internal static class SkeletalAssetSourcePolicy {
    internal static RenderPreviewAssetSource?Retarget(RenderPreviewAssetSource?Source,string PreviousPath,string CurrentPath)=>Source is null?null:new RenderPreviewAssetSource {
        MeshPath=string.Equals(Source.MeshPath,PreviousPath,StringComparison.OrdinalIgnoreCase)?CurrentPath:Source.MeshPath,
        AnimationPath=string.Equals(Source.AnimationPath,PreviousPath,StringComparison.OrdinalIgnoreCase)?CurrentPath:Source.AnimationPath,
    };
}
