using System;
using System.Collections.Concurrent;
using System.IO;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// ファイル拡張子で振り分け、<c>CSharpGameEngine.Extension</c> の CLI ビューアー (Program.cs) に合わせる。<see cref="ExternalFbxLoader"/> (ADR-0010) が扱う .fbx と <see cref="ExternalGltfLoader"/> (ADR-0019) が扱う .glb/.gltf はどちらも <see cref="ExternalBridgeProcess"/> 経由でプロセス外で解析するため、各形式のパーサーを devenv.exe に直接読み込むことはない。<see cref="AssetPreviewHwndHost"/> (ADR-0013、単一アセットのプレビュー) と <see cref="SceneHwndHost"/> (ADR-0020、シーンタブ) で共有し、どちらも同じ方法でモデルを読み込む。以前このコメントに記載していた <c>ViewerHwndHost</c> (ADR-0007/0008) は ADR-0021 で削除された。
/// 結果は devenv.exe のプロセスが存続する間キャッシュする。正規化済み絶対パスをキーとし、最終書き込み時刻で無効化するため、変更されていないアセットを再度開いたり読み込んだりしても（シーンタブの再読み込みごと、または同じプリミティブを形状パレットで繰り返し選択しても）ブリッジプロセスは起動しない。これは ADR-0010 追補の修正（ビルド済み exe を起動）を置き換えるのではなく補完する。キャッシュだけではシーンを初めて読み込む際のコールドロードは改善できないためである。
/// </summary>
internal static class MeshLoader {
    private static readonly ConcurrentDictionary<string,(SimpleMesh Mesh,DateTime LastWriteTimeUtc)>Cache=new(StringComparer.OrdinalIgnoreCase);

    public static SimpleMesh Load(string ModelPath,string RepoRoot){
        var FullPath=Path.GetFullPath(ModelPath);
        DateTime LastWriteTimeUtc=File.GetLastWriteTimeUtc(FullPath);

        if(Cache.TryGetValue(FullPath,out var Cached)&&Cached.LastWriteTimeUtc==LastWriteTimeUtc){
            return Cached.Mesh;
        }

        SimpleMesh Mesh=string.Equals(Path.GetExtension(FullPath),".fbx",StringComparison.OrdinalIgnoreCase)
            ?ExternalFbxLoader.Load(FullPath,RepoRoot)
            :ExternalGltfLoader.Load(FullPath,RepoRoot);

        Cache[FullPath]=(Mesh,LastWriteTimeUtc);
        return Mesh;
    }
}
