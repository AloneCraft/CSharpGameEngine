using CSharpGameEngine.Content.Models;
namespace CSharpGameEngine.Runtime.Animation;
/// <summary>描画と姿勢評価で同じ保存済みモデル・スキン対応表を共有する。</summary>
public static class SkeletalAssetCache {
    private static readonly Lock Gate=new();
    private static readonly Dictionary<string,ModelAssetBundle>Assets=new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string,Skeleton>Skeletons=new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string Path,string?Selector,Skeleton Skeleton),AnimationClip>Clips=[];
    private static string CacheKey(string FullPath)=>Path.GetFullPath(CSharpGameEngine.Entities.ContentRoot.ModelDirectory)+"|"+FullPath;
    public static string ResolvePath(string AssetPath)=>CSharpGameEngine.Entities.ContentRoot.Resolve(AssetPath);
    public static void Clear(){
        lock(Gate){ foreach(var Bundle in Assets.Values){ Bundle.Dispose(); }Assets.Clear();Skeletons.Clear();Clips.Clear(); }
    }
    public static ModelAssetBundle Asset(string AssetPath){
        var FullPath=ResolvePath(AssetPath);
        var Key=CacheKey(FullPath);
        lock(Gate){
            if(Assets.TryGetValue(Key,out var Cached)){ return Cached; }
            var Root=CSharpGameEngine.Entities.ContentRoot.Directory;
            var SourceUri=Path.GetRelativePath(Root,FullPath).Replace('\\','/');
            var Bundle=ModelAssetBundle.OpenAsync(CSharpGameEngine.Entities.ContentRoot.ModelDirectory,SourceUri).GetAwaiter().GetResult();
            if(Bundle.Model.Skins.Length!=1){ Bundle.Dispose();throw new InvalidDataException("A skeletal model must contain exactly one normalized skin."); }
            Assets.Add(Key,Bundle);return Bundle;
        }
    }
    public static Skeleton Skeleton(string AssetPath){
        var FullPath=ResolvePath(AssetPath);var Key=CacheKey(FullPath);var Bundle=Asset(AssetPath);
        lock(Gate){
            if(!Skeletons.TryGetValue(Key,out var Cached)){
                var Skin=Bundle.Model.Skins[0];Cached=Animation.Skeleton.FromModel(Bundle.Skeletons[Skin.SkeletonIndex],Skin);Skeletons.Add(Key,Cached);
            }
            return Cached;
        }
    }
    public static AnimationClip Clip(string AssetPath,Skeleton Skeleton){
        var Separator=AssetPath.LastIndexOf(':');
        var HasSelector=Separator>1&&Separator>AssetPath.LastIndexOfAny(['\\','/']);
        var Source=HasSelector?AssetPath[..Separator]:AssetPath;var Selector=HasSelector?AssetPath[(Separator+1)..]:null;
        var FullPath=ResolvePath(Source);
        var Key=CacheKey(FullPath);
        lock(Gate){
            if(Clips.TryGetValue((Key,Selector,Skeleton),out var Cached)){ return Cached; }
            var Root=CSharpGameEngine.Entities.ContentRoot.Directory;
            var SourceUri=Path.GetRelativePath(Root,FullPath).Replace('\\','/');
            using var Bundle=ModelAssetBundle.OpenAsync(CSharpGameEngine.Entities.ContentRoot.ModelDirectory,SourceUri).GetAwaiter().GetResult();
            var Data=Selector is null?Bundle.Clips.FirstOrDefault():Bundle.Clips.SingleOrDefault(Value=>Value.Name==Selector);
            if(Data is null){ throw new InvalidDataException("The saved animation model has no selected clip."); }
            var Bound=AnimationClip.Bind(Data,Skeleton);
            if(Bound.BoundTrackCount==0){ throw new InvalidDataException("The saved animation clip animates none of the skeleton joints."); }
            Clips.Add((Key,Selector,Skeleton),Bound);return Bound;
        }
    }
}
