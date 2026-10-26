namespace CSharpGameEngine.Contracts;
public enum AssetWorkspaceKind { Scene,Model,Texture,Audio,ShaderGraph,Data,Font,Other,Image }
public enum AssetWorkspaceState { Source,Ready,MissingMetadata,InvalidMetadata,MissingSource,ReadOnly,ImportFailed }
[Flags]
public enum AssetWorkspaceCapabilities { None=0,Open=1,Inspect=2,Duplicate=4,Edit=8,Organize=16,RegisterMetadata=32,ConvertLegacyMetadata=64 }
public sealed class AssetWorkspaceSource {
    public string ProjectPath{get;set;}=string.Empty;
    public string ProjectRoot{get;set;}=string.Empty;
    public string MetadataRoot{get;set;}=string.Empty;
    public string SourcePath{get;set;}=string.Empty;
    public string RelativePath{get;set;}=string.Empty;
    public string Provider{get;set;}=string.Empty;
    public bool ReadOnly{get;set;}
}
public sealed class AssetWorkspaceItem {
    public AssetWorkspaceSource Source{get;set;}=new();
    public AssetWorkspaceKind Kind{get;set;}
    public AssetWorkspaceState State{get;set;}
    public ImageImportSettings?ImageSettings{get;set;}
    public string AssetId{get;set;}=string.Empty;
    public AssetKey?GetKey()=>Contracts.AssetId.TryParse(this.AssetId,out var Identity)?new AssetKey(Identity,null):null;
    public AssetWorkspaceCapabilities GetCapabilities()=>this.State==AssetWorkspaceState.MissingSource?AssetWorkspaceCapabilities.Inspect:
        AssetWorkspaceCapabilities.Open|AssetWorkspaceCapabilities.Inspect|AssetWorkspaceCapabilities.Duplicate
        |(this.Source.ReadOnly?AssetWorkspaceCapabilities.None:AssetWorkspaceCapabilities.Edit|AssetWorkspaceCapabilities.Organize
            |((this.Kind==AssetWorkspaceKind.Model||ImageImportSettings.Supports(this.Source.SourcePath))&&this.State==AssetWorkspaceState.MissingMetadata?AssetWorkspaceCapabilities.RegisterMetadata:AssetWorkspaceCapabilities.None)
            |(this.Kind==AssetWorkspaceKind.Model&&this.State==AssetWorkspaceState.InvalidMetadata?AssetWorkspaceCapabilities.ConvertLegacyMetadata:AssetWorkspaceCapabilities.None));
    public string MetadataSha256{get;set;}=string.Empty;
    public string Diagnostic{get;set;}=string.Empty;
    public long SourceLength{get;set;}
    public long ModifiedUtcTicks{get;set;}
}
public sealed class AssetWorkspaceRequest {
    public int WorkspaceCommand{get;set;}=1;
    public int ProtocolVersion{get;set;}=1;
    public Guid RequestId{get;set;}
    public AssetWorkspaceSource[]Sources{get;set;}=[];
}
public sealed class AssetWorkspaceResult {
    public int ProtocolVersion{get;set;}=1;
    public Guid RequestId{get;set;}
    public bool Success{get;set;}
    public AssetWorkspaceItem[]Items{get;set;}=[];
    public AssetFontPreview?FontPreview{get;set;}
    public AssetAudioPreview?AudioPreview{get;set;}
    public string Diagnostic{get;set;}=string.Empty;
}
/// <summary>Sound Wave Editor の波形と形式の要約（<c>WorkspaceCommand=3</c>）。<see cref="Peaks"/> は 0..1 に正規化した区間ごとの最大振幅で、再生用のデータではない。</summary>
public sealed class AssetAudioPreview {
    public const int MaximumPeaks=2048;
    public int SampleRate{get;set;}
    public int Channels{get;set;}
    public int BitsPerSample{get;set;}
    public int FrameCount{get;set;}
    public float DurationSeconds{get;set;}
    public float[]Peaks{get;set;}=[];
}
public sealed class AssetFontPreview {
    public int SourceWidth{get;set;}
    public int SourceHeight{get;set;}
    public int GlyphCount{get;set;}
    public int Width{get;set;}
    public int Height{get;set;}
    public byte[]Distance{get;set;}=[];
}
/// <summary>Visual Studioの階層表示とスタンドアロンのコンテンツ一覧で共有する分類。</summary>
public static class AssetWorkspaceClassification {
    public static AssetWorkspaceKind?Classify(string Path){
        if(string.IsNullOrEmpty(Path)){ return null; }
        var Normalized=Path.Replace('\\','/');
        var Extension=System.IO.Path.GetExtension(Path).ToLowerInvariant();
        if(Normalized.EndsWith(".schema.json",StringComparison.OrdinalIgnoreCase)){ return null; }
        return Extension switch {
            ".json" when ("/"+Normalized).IndexOf("/Scenes/",StringComparison.OrdinalIgnoreCase)>=0=>AssetWorkspaceKind.Scene,
            ".glb" or ".gltf" or ".fbx"=>AssetWorkspaceKind.Model,
            ".png" or ".jpg" or ".jpeg" or ".dds" or ".tga" or ".hdr" or ".exr"=>AssetWorkspaceKind.Texture,
            ".wav" or ".ogg" or ".mp3" or ".flac"=>AssetWorkspaceKind.Audio,
            ".cgeshadergraph" or ".cgesubgraph"=>AssetWorkspaceKind.ShaderGraph,
            ".cgeasset"=>AssetWorkspaceKind.Data,
            ".cgefont" or ".ttf" or ".otf"=>AssetWorkspaceKind.Font,
            ".cgenav" or ".cgephysics"=>AssetWorkspaceKind.Other,
            _=>null,
        };
    }
    public static bool IsExcluded(string Path)=>Path.Replace('\\','/').Split('/').Any(static Part=>
        Part.Equals("bin",StringComparison.OrdinalIgnoreCase)||Part.Equals("obj",StringComparison.OrdinalIgnoreCase)
        ||Part.Equals(".git",StringComparison.OrdinalIgnoreCase)||Part.Equals(".vs",StringComparison.OrdinalIgnoreCase)
        ||Part.Equals(".artifacts",StringComparison.OrdinalIgnoreCase));
}
