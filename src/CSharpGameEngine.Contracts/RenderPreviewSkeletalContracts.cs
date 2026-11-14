namespace CSharpGameEngine.Contracts;
/// <summary>制作側が生成済みのモデル参照。表示ホストは元ファイルを読まない。</summary>
public sealed class RenderPreviewModelSource {
    public string SourcePath { get;set; }=string.Empty;
    public string ContentDirectory { get;set; }=string.Empty;
    public string SourceUri { get;set; }=string.Empty;
    public string ModelHash { get;set; }=string.Empty;
}

/// <summary>
/// 制作側が用意したスケルタルモデルの参照（プロトコル 14、ADR-0097）。
/// 表示ホストはゲームと同じ生成済みモデルを読み、ソース解析・テクスチャ生成を行わない。
/// メッシュ・マテリアル・オブジェクトは <see cref="RenderPreviewScene"/> の明示分の後ろに足され、原点に置かれる。
/// </summary>
public sealed class RenderPreviewAssetSource {
    public const int MaximumPathLength=1_024;
    /// <summary>画面上で選択したソースの絶対パス。表示ホストのファイル入力には使わない。</summary>
    public string MeshPath { get;set; }=string.Empty;
    /// <summary>クリップを読む .glb／.gltf の絶対パス。空なら <see cref="MeshPath"/> のクリップを使う。ジョイント名が <see cref="MeshPath"/> のスキンと一致するものだけが評価される。</summary>
    public string AnimationPath { get;set; }=string.Empty;
    public RenderPreviewModelSource?MeshModel { get;set; }
    public RenderPreviewModelSource?AnimationModel { get;set; }
}

/// <summary>Open／ReplaceScene の応答でホストが返す、読み込んだスケルタルアセットの要約。</summary>
public sealed class RenderPreviewAssetSummary {
    /// <summary>ジョイント名（スキンの順）。</summary>
    public string[]JointNames { get;set; }=[];
    /// <summary>各ジョイントの親のインデックス。ルートは -1。</summary>
    public int[]JointParents { get;set; }=[];
    /// <summary>スキニング対象のメッシュ数。</summary>
    public int SkinnedMeshCount { get;set; }
    public RenderPreviewClipSummary[]Clips { get;set; }=[];
    /// <summary>レスト姿勢における全描画ノードの境界と、参照プリミティブの統計。</summary>
    public RenderPreviewAssetBounds?RestBounds { get;set; }
}

public sealed class RenderPreviewClipSummary {
    public string Name { get;set; }=string.Empty;
    public float DurationSeconds { get;set; }
    /// <summary>スケルトンのジョイントに結び付いたトラック数。0 のクリップはこのスケルトンでは再生できない。</summary>
    public int BoundTrackCount { get;set; }
}

/// <summary>フレームごとのアニメーション姿勢。ホストが <see cref="TimeSeconds"/> でクリップを評価し、スキンのパレットへ反映する。</summary>
public sealed class RenderPreviewAnimation {
    /// <summary>応答の <see cref="RenderPreviewAssetSummary.Clips"/> の番号。-1 ならバインド姿勢（レスト）。</summary>
    public int ClipIndex { get;set; }=-1;
    public float TimeSeconds { get;set; }
    public bool Loop { get;set; }=true;
}

public static class RenderPreviewSkeletalValidation {
    public const float MaximumTimeSeconds=86_400f;
    public static string?Validate(RenderPreviewAssetSource?Asset){
        if(Asset is null){ return null; }
        string?Error=ValidatePath(Asset.MeshPath,"Asset mesh path",required:true);
        if(Error is not null){ return Error; }
        Error=ValidatePath(Asset.AnimationPath,"Asset animation path",required:false);
        if(Error is not null){ return Error; }
        Error=ValidateModel(Asset.MeshModel,Asset.MeshPath);if(Error is not null){ return Error; }
        if(!string.IsNullOrEmpty(Asset.AnimationPath)){ return ValidateModel(Asset.AnimationModel,Asset.AnimationPath); }
        return Asset.AnimationModel is null?null:"An animation model reference requires its source identity.";
    }
    public static string?ValidateModel(RenderPreviewModelSource?Source,string ExpectedPath){
        if(Source is null){ return "A prepared model reference is required."; }
        if(!string.Equals(Source.SourcePath,ExpectedPath,System.StringComparison.OrdinalIgnoreCase)){ return "Prepared model source identity differs from the selected asset."; }
        var Directory=Source.ContentDirectory;
        if(string.IsNullOrEmpty(Directory)||Directory.Length>RenderPreviewAssetSource.MaximumPathLength||Directory.IndexOf('\0')>=0){ return "Model content directory is invalid."; }
        var Rooted=Directory.Length>=3&&Directory[1]==':'&&(Directory[2]=='\\'||Directory[2]=='/')||Directory.StartsWith("\\\\",System.StringComparison.Ordinal);
        if(!Rooted){ return "Model content directory must be absolute."; }
        var Uri=Source.SourceUri;
        if(string.IsNullOrEmpty(Uri)||Uri.Length>4096||Uri.IndexOf('\\')>=0||Uri.IndexOf(':')>=0||!Uri.IsNormalized(System.Text.NormalizationForm.FormC)){ return "Model source URI is invalid."; }
        foreach(var Character in Uri){ if(char.IsControl(Character)){ return "Model source URI is invalid."; } }
        foreach(var Part in Uri.Split('/')){ if(Part is "" or "." or ".."){ return "Model source URI must remain inside the project."; } }
        if(!Hash256.TryParse(Source.ModelHash,out var Hash)||!Hash.IsValid){ return "Model content hash is invalid."; }
        return null;
    }
    public static string?Validate(RenderPreviewFrame Frame){
        if(Frame.SelectedJoint<-1||Frame.SelectedJoint>=ushort.MaxValue){ return "Frame selected joint is out of range."; }
        if(Frame.Animation is not{ }Animation){ return null; }
        if(Animation.ClipIndex<-1||Animation.ClipIndex>=4_096){ return "Frame animation clip index is out of range."; }
        float Time=Animation.TimeSeconds;
        if(float.IsNaN(Time)||float.IsInfinity(Time)||Time<0f||Time>MaximumTimeSeconds){
            return "Frame animation time must be finite and between 0 and "+MaximumTimeSeconds+" seconds.";
        }
        return null;
    }
    internal static string?ValidatePath(string?Path,string Name,bool required){
        if(string.IsNullOrEmpty(Path)){ return required?Name+" is required.":null; }
        if(Path!.Length>RenderPreviewAssetSource.MaximumPathLength||Path.IndexOf('\0')>=0){ return Name+" is invalid."; }
        bool Rooted=Path.Length>=3&&Path[1]==':'&&(Path[2]=='\\'||Path[2]=='/')||Path.StartsWith("\\\\",System.StringComparison.Ordinal);
        if(!Rooted){ return Name+" must be an absolute path."; }
        bool Gltf=Path.EndsWith(".glb",System.StringComparison.OrdinalIgnoreCase)||Path.EndsWith(".gltf",System.StringComparison.OrdinalIgnoreCase);
        return Gltf?null:Name+" must be a .glb or .gltf file.";
    }
}
