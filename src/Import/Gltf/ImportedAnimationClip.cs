using System.Numerics;
namespace CSharpGameEngine.Import.Gltf;
/// <summary>glTF内の1つのアニメーションをエンジン空間で表す。ジョイント名を参照先とするローカルジョイント変換トラック（ADR-0066）。</summary>
public sealed class ImportedAnimationClip {
    public required string Name { get; init;}
    /// <summary>すべてのトラックを通じた最後のキー時刻。</summary>
    public required float DurationSeconds { get; init;}
    public required IReadOnlyList<ImportedJointTrack>Tracks { get; init;}
}
/// <summary>ジョイントのアニメーション対象となるローカル平行移動／回転／スケール。チャンネルがない場合はレスト値を維持する。</summary>
public sealed class ImportedJointTrack {
    public required string JointName { get; init;}
    public ImportedKeys<Vector3>?Translation { get; init;}
    public ImportedKeys<Quaternion>?Rotation { get; init;}
    public ImportedKeys<Vector3>?Scale { get; init;}
}
/// <summary>時間順に並んだキー。<see cref="Step"/>では次のキーまで値を保持し、それ以外では値を線形補間する（回転はslerp）。</summary>
public sealed class ImportedKeys<T> where T:struct {
    public required IReadOnlyList<float>Times { get; init;}
    public required IReadOnlyList<T>Values { get; init;}
    public required bool Step { get; init;}
}
/// <summary><see cref="GltfImporter.ImportAnimations"/> の結果。</summary>
public sealed class GltfAnimationImportResult {
    private GltfAnimationImportResult(bool Success,IReadOnlyList<ImportedAnimationClip>Clips,IReadOnlyList<GltfImportDiagnostic>Diagnostics){
        this.Success=Success;
        this.Clips=Clips;
        this.Diagnostics=Diagnostics;
    }
    public bool Success { get;}
    /// <summary>ファイル内のすべてのアニメーション。<see cref="Success"/>がfalseの場合は空。</summary>
    public IReadOnlyList<ImportedAnimationClip>Clips { get;}
    public IReadOnlyList<GltfImportDiagnostic>Diagnostics { get;}
    public static GltfAnimationImportResult Succeeded(IReadOnlyList<ImportedAnimationClip>Clips,IReadOnlyList<GltfImportDiagnostic>Diagnostics)=>new(true,Clips,Diagnostics);
    public static GltfAnimationImportResult Failed(IReadOnlyList<GltfImportDiagnostic>Diagnostics)=>new(false,[],Diagnostics);
}
