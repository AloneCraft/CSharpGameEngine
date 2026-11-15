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
    /// <summary>フォントの指標（em 単位）。見本を実フォントと同じ行の高さ・位置で描くために使う。</summary>
    public float Ascent{get;set;}
    public float Descent{get;set;}
    public float LineGap{get;set;}
    public float SpreadEm{get;set;}
    /// <summary>ベイク済みの字形（文字コードの昇順）。ここにある文字が「描ける文字」で、無い文字は実行時に <c>?</c> で描かれる。</summary>
    public AssetFontGlyph[]Glyphs{get;set;}=[];
    public const int MaximumGlyphs=65_536;
    /// <summary>応答の形・範囲の検査。問題がなければ null。</summary>
    public static string?Validate(AssetFontPreview?Preview){
        if(Preview is null){ return "The font preview is missing."; }
        if(Preview.Width is <=0 or >512||Preview.Height is <=0 or >512||Preview.SourceWidth is <=0 or >8192||Preview.SourceHeight is <=0 or >8192){ return "The font atlas size is out of range."; }
        if(Preview.GlyphCount is <=0 or >MaximumGlyphs){ return "The font glyph count is out of range."; }
        if(Preview.Distance is null||Preview.Distance.Length!=Preview.Width*Preview.Height){ return "The font distance image does not match its size."; }
        if(!Finite(Preview.Ascent)||!Finite(Preview.Descent)||!Finite(Preview.LineGap)||!Finite(Preview.SpreadEm)||Math.Abs(Preview.Ascent)>64f||Math.Abs(Preview.Descent)>64f||Math.Abs(Preview.LineGap)>64f||Preview.SpreadEm<0f||Preview.SpreadEm>64f){ return "The font metrics are out of range."; }
        if(Preview.Glyphs is null||Preview.Glyphs.Length>MaximumGlyphs||Preview.Glyphs.Length!=Preview.GlyphCount){ return "The font glyph list does not match its count."; }
        var Previous=-1;
        foreach(var Glyph in Preview.Glyphs){
            if(Glyph is null||Glyph.CodePoint<=Previous||Glyph.CodePoint>0x10FFFF){ return "The font glyphs must have ascending, unique code points."; }
            Previous=Glyph.CodePoint;
            if(!Finite(Glyph.Advance)||Math.Abs(Glyph.Advance)>64f||!Finite(Glyph.PlaneLeft)||!Finite(Glyph.PlaneBottom)||!Finite(Glyph.PlaneRight)||!Finite(Glyph.PlaneTop)
                ||Math.Abs(Glyph.PlaneLeft)>64f||Math.Abs(Glyph.PlaneBottom)>64f||Math.Abs(Glyph.PlaneRight)>64f||Math.Abs(Glyph.PlaneTop)>64f){ return "A font glyph has an out-of-range size."; }
            if(!Unit(Glyph.U0)||!Unit(Glyph.V0)||!Unit(Glyph.U1)||!Unit(Glyph.V1)){ return "A font glyph has texture coordinates outside 0..1."; }
        }
        return null;
    }
    private static bool Finite(float Value)=>!float.IsNaN(Value)&&!float.IsInfinity(Value);
    private static bool Unit(float Value)=>Finite(Value)&&Value>=0f&&Value<=1f;
}
/// <summary>ベイク済みフォントの字形 1 つ分（<c>FontAtlas</c> の <c>FontGlyph</c> と同じ値）。矩形は em 単位、UV は 0..1 の正規化座標。</summary>
public sealed class AssetFontGlyph {
    public int CodePoint{get;set;}
    public float Advance{get;set;}
    public float PlaneLeft{get;set;}
    public float PlaneBottom{get;set;}
    public float PlaneRight{get;set;}
    public float PlaneTop{get;set;}
    public float U0{get;set;}
    public float V0{get;set;}
    public float U1{get;set;}
    public float V1{get;set;}
    /// <summary>空白など、送り幅だけで矩形のない字形か。</summary>
    public bool HasQuad=>this.PlaneRight>this.PlaneLeft&&this.PlaneTop>this.PlaneBottom;
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
