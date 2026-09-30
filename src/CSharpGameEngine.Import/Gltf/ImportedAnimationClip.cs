using System.Numerics;
namespace CSharpGameEngine.Import.Gltf;
/// <summary>One glTF animation in engine space, as local joint transform tracks addressed by joint name (ADR-0066).</summary>
public sealed class ImportedAnimationClip {
    public required string Name { get; init;}
    /// <summary>The last key time over every track.</summary>
    public required float DurationSeconds { get; init;}
    public required IReadOnlyList<ImportedJointTrack>Tracks { get; init;}
}
/// <summary>A joint's animated local translation, rotation and scale; an absent channel keeps the rest value.</summary>
public sealed class ImportedJointTrack {
    public required string JointName { get; init;}
    public ImportedKeys<Vector3>?Translation { get; init;}
    public ImportedKeys<Quaternion>?Rotation { get; init;}
    public ImportedKeys<Vector3>?Scale { get; init;}
}
/// <summary>Keys sorted by time. <see cref="Step"/> holds each value until the next key; otherwise values interpolate linearly (slerp for rotations).</summary>
public sealed class ImportedKeys<T> where T:struct {
    public required IReadOnlyList<float>Times { get; init;}
    public required IReadOnlyList<T>Values { get; init;}
    public required bool Step { get; init;}
}
/// <summary>Result of <see cref="GltfImporter.ImportAnimations"/>.</summary>
public sealed class GltfAnimationImportResult {
    private GltfAnimationImportResult(bool Success,IReadOnlyList<ImportedAnimationClip>Clips,IReadOnlyList<GltfImportDiagnostic>Diagnostics){
        this.Success=Success;
        this.Clips=Clips;
        this.Diagnostics=Diagnostics;
    }
    public bool Success { get;}
    /// <summary>Every animation in the file; empty when <see cref="Success"/> is false.</summary>
    public IReadOnlyList<ImportedAnimationClip>Clips { get;}
    public IReadOnlyList<GltfImportDiagnostic>Diagnostics { get;}
    public static GltfAnimationImportResult Succeeded(IReadOnlyList<ImportedAnimationClip>Clips,IReadOnlyList<GltfImportDiagnostic>Diagnostics)=>new(true,Clips,Diagnostics);
    public static GltfAnimationImportResult Failed(IReadOnlyList<GltfImportDiagnostic>Diagnostics)=>new(false,[],Diagnostics);
}
