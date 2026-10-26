using CSharpGameEngine.Import.Gltf;
namespace CSharpGameEngine.Runtime.Animation;
/// <summary>
/// 各スケルタルglTFとクリップをプロセスごとに一度だけインポートする（ADR-0066）。レンダラーの登録処理とアニメーションシステムは
/// 同じインポート済みスキンを読み取るため、ジョイント順とパレットサイズが必ず一致する。パスはStaticMeshと同様に
/// <see cref="CSharpGameEngine.Entities.ContentRoot"/>を基準に解決する。
/// </summary>
public static class SkeletalAssetCache {
    private static readonly Lock Gate=new();
    private static readonly Dictionary<string,ImportedScene>Scenes=new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string,Skeleton>Skeletons=new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string Path,Skeleton Skeleton),AnimationClip>Clips=[];
    public static string ResolvePath(string AssetPath)=>CSharpGameEngine.Entities.ContentRoot.Resolve(AssetPath);
    /// <summary>スケルタルメッシュアセットのインポート済みシーン。スキンを正確に1つ含む必要がある。</summary>
    public static ImportedScene Scene(string AssetPath){
        var FullPath=ResolvePath(AssetPath);
        lock(Gate){
            if(Scenes.TryGetValue(FullPath,out var Cached)){
                return Cached;
            }
            var Result=GltfImporter.ImportScene(FullPath);
            if(!Result.Success||Result.Scene is not{ }Imported){
                throw new InvalidDataException($"SkeletalMesh '{FullPath}' could not be imported: {string.Join("; ",Result.Diagnostics)}");
            }
            if(Imported.Skins.Count!=1){
                throw new InvalidDataException($"SkeletalMesh '{FullPath}' must contain exactly one skin, found {Imported.Skins.Count}.");
            }
            Scenes.Add(FullPath,Imported);
            return Imported;
        }
    }
    public static Skeleton Skeleton(string AssetPath){
        var FullPath=ResolvePath(AssetPath);
        var Imported=Scene(AssetPath);
        lock(Gate){
            if(!Skeletons.TryGetValue(FullPath,out var Cached)){
                Cached=Animation.Skeleton.FromImported(Imported.Skins[0]);
                Skeletons.Add(FullPath,Cached);
            }
            return Cached;
        }
    }
    /// <summary>クリップファイル内の最初のアニメーションを<paramref name="Skeleton"/>へバインドしたもの。</summary>
    public static AnimationClip Clip(string AssetPath,Skeleton Skeleton){
        var FullPath=ResolvePath(AssetPath);
        lock(Gate){
            if(Clips.TryGetValue((FullPath,Skeleton),out var Cached)){
                return Cached;
            }
            var Result=GltfImporter.ImportAnimations(FullPath);
            if(!Result.Success||Result.Clips.Count==0){
                throw new InvalidDataException($"Animation '{FullPath}' has no clip: {string.Join("; ",Result.Diagnostics)}");
            }
            var Bound=AnimationClip.Bind(Result.Clips[0],Skeleton);
            if(Bound.BoundTrackCount==0){
                throw new InvalidDataException($"Animation '{FullPath}' animates none of the skeleton's joints.");
            }
            Clips.Add((FullPath,Skeleton),Bound);
            return Bound;
        }
    }
}
