using System;
using System.Collections.Concurrent;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension;

/// <summary>glTF は共通モデル成果物から読み、FBX は専用の外部ブリッジで読む。VS 本体にソース解析器を読み込まない。シーン単位の Prepare は依存変更を再評価し、成果物ハッシュで形状を共有する。</summary>
internal static class MeshLoader {
    private static readonly ConcurrentDictionary<string,(SimpleMesh Mesh,DateTime LastWriteTimeUtc)>Cache=new(StringComparer.OrdinalIgnoreCase);
    internal static void Prepare(IEnumerable<string>Paths,string RepoRoot){
        var Sources=Paths.Select(Path.GetFullPath).Where(PathValue=>Path.GetExtension(PathValue).ToLowerInvariant() is ".glb" or ".gltf").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        for(var Offset=0;Offset<Sources.Length;Offset+=16){
            var Selected=Sources.Skip(Offset).Take(16).ToArray();
            var Result=AssetAuthoringHostClient.Model(new AssetModelRequest{ModelCommand=4,SourcePath=Selected[0],PreviewSourcePaths=Selected});
            if(!Result.Success){ throw new InvalidDataException(Result.Diagnostic); }
            foreach(var Source in Result.PreparedSources){
                var FullPath=Path.GetFullPath(Source.SourcePath);var Stamp=File.GetLastWriteTimeUtc(FullPath);
                var Key=Source.ModelHash;
                if(!PreparedCache.TryGetValue(Key,out var Mesh)){ Mesh=AssetAuthoringHostClient.Geometry(Source,RepoRoot);PreparedCache[Key]=Mesh; }
                Cache[FullPath]=(Mesh,Stamp);
            }
        }
    }
    private static readonly ConcurrentDictionary<string,SimpleMesh>PreparedCache=new(StringComparer.Ordinal);

    public static SimpleMesh Load(string ModelPath,string RepoRoot){
        var FullPath=Path.GetFullPath(ModelPath);
        DateTime LastWriteTimeUtc=File.GetLastWriteTimeUtc(FullPath);

        if(Cache.TryGetValue(FullPath,out var Cached)&&Cached.LastWriteTimeUtc==LastWriteTimeUtc){
            return Cached.Mesh;
        }

        SimpleMesh Mesh=string.Equals(Path.GetExtension(FullPath),".fbx",StringComparison.OrdinalIgnoreCase)
            ?ExternalFbxLoader.Load(FullPath,RepoRoot)
            :LoadPrepared(FullPath,RepoRoot);

        Cache[FullPath]=(Mesh,LastWriteTimeUtc);
        return Mesh;
    }
    private static SimpleMesh LoadPrepared(string FullPath,string RepoRoot){ Prepare([FullPath],RepoRoot);return Cache[FullPath].Mesh; }
}
