using System.Globalization;
#if IMAGE_CODEGEN
namespace CSharpGameEngine.CodeAnalysis.ImageContract;
#else
namespace CSharpGameEngine.Contracts;
#endif
[AttributeUsage(AttributeTargets.Field|AttributeTargets.Property)]
public sealed class TextureUsageAttribute(TextureUsage Usage):Attribute { public TextureUsage Usage{get;}=Usage; }
public enum ImageOutputKind { Texture,Image }
public enum TextureUsage { Color,Emission,Normal,Mask,Metallic,Roughness,AmbientOcclusion,Packed }
/// <summary>1つのソースに対する名前付き解釈。Keyが識別情報を表し、Nameは表示用途に限る。</summary>
public sealed class ImageOutputSettings {
    public string Key{get;set;}="color";
    public string Name{get;set;}="Color";
    public ImageOutputKind Kind{get;set;}
    public TextureUsage Usage{get;set;}
    public bool FlipGreen{get;set;}
    public bool Mipmaps{get;set;}=true;
    public string Channel{get;set;}="r";
    public string OcclusionChannel{get;set;}="one";
    public string RoughnessChannel{get;set;}="one";
    public string MetallicChannel{get;set;}="zero";
    public bool InvertRoughness{get;set;}
    public bool IsSrgb=>this.Kind==ImageOutputKind.Texture&&this.Usage is TextureUsage.Color or TextureUsage.Emission;
    public static ImageOutputSettings For(TextureUsage Usage)=>new(){Key=Token(Usage),Name=Usage.ToString(),Usage=Usage};
    public static string Token(TextureUsage Usage)=>Usage==TextureUsage.AmbientOcclusion?"ao":Usage.ToString().ToLowerInvariant();
}
public sealed class ImageImportSettings {
    public const string Importer="cge.image";
    public string DefaultOutput{get;set;}="color";
    public ImageOutputSettings[]Outputs{get;set;}=[];
    public static bool Supports(string Path)=>System.IO.Path.GetExtension(Path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";
    public static ImageImportSettings Initial(string Path,TextureUsage?Target=null){
        var Name=System.IO.Path.GetFileNameWithoutExtension(Path).ToLowerInvariant();
        var Parts=Name.Split(new[]{'_','-','.'},StringSplitOptions.RemoveEmptyEntries);
        var Last=Parts.LastOrDefault()??string.Empty;
        var NormalConvention=Parts.Length>1&&Last is "dx" or "gl"&&Parts[Parts.Length-2] is "normal" or "nrm" or "n";
        var Usage=Target??(NormalConvention?TextureUsage.Normal:Last switch {
            "normal" or "nrm" or "n"=>TextureUsage.Normal,"mask"=>TextureUsage.Mask,
            "metallic" or "metalness"=>TextureUsage.Metallic,"roughness" or "rough"=>TextureUsage.Roughness,
            "ao" or "occlusion"=>TextureUsage.AmbientOcclusion,"orm" or "arm" or "mra"=>TextureUsage.Packed,
            "emission" or "emissive"=>TextureUsage.Emission,_=>TextureUsage.Color
        });
        var Output=ImageOutputSettings.For(Usage);
        Output.FlipGreen=Usage==TextureUsage.Normal&&NormalConvention&&Last=="dx";
        if(Usage==TextureUsage.Packed){
            Output.Key=Last is "orm" or "arm" or "mra"?Last:"packed";
            Output.Name=Output.Key.ToUpperInvariant();
            if(Last is "orm" or "arm"){ Output.OcclusionChannel="r";Output.RoughnessChannel="g";Output.MetallicChannel="b"; }
            if(Last=="mra"){ Output.OcclusionChannel="b";Output.RoughnessChannel="g";Output.MetallicChannel="r"; }
        }
        return new(){DefaultOutput=Output.Key,Outputs=[Output]};
    }
    public void Validate(){
        if(this.Outputs is null||this.Outputs.Length is <1 or >32){ throw new ArgumentException("IMAGE_OUTPUTS: expected 1 to 32 outputs."); }
        var Keys=new HashSet<string>(StringComparer.Ordinal);
        var Members=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"Default"};
        foreach(var Output in this.Outputs){
            if(Output is null||string.IsNullOrEmpty(Output.Key)||Output.Key.Length>64||!char.IsLetter(Output.Key[0])
                ||Output.Key.Any(C=>!(C is >= 'a' and <= 'z' or >= '0' and <= '9' or '_'))||!Keys.Add(Output.Key)){
                throw new ArgumentException("IMAGE_KEY: output keys must be unique lower-case identifiers.");
            }
            if(!Members.Add(Output.Key.Replace("_",string.Empty))){ throw new ArgumentException("IMAGE_KEY: output member names collide."); }
            if(string.IsNullOrWhiteSpace(Output.Name)||Output.Name.Length>256||Output.Name.Any(char.IsControl)){
                throw new ArgumentException("IMAGE_NAME: invalid output display name.");
            }
            if(!Enum.IsDefined(typeof(ImageOutputKind),Output.Kind)||!Enum.IsDefined(typeof(TextureUsage),Output.Usage)){
                throw new ArgumentException("IMAGE_USAGE: unsupported output kind/usage.");
            }
            foreach(var Channel in new[]{Output.Channel,Output.OcclusionChannel,Output.RoughnessChannel,Output.MetallicChannel}){
                if(Channel is not("r" or "g" or "b" or "a" or "zero" or "one")){ throw new ArgumentException("IMAGE_CHANNEL: invalid channel selector."); }
            }
        }
        if(!Keys.Contains(this.DefaultOutput)){ throw new ArgumentException("IMAGE_DEFAULT: default output must exist."); }
    }
    public IReadOnlyDictionary<string,string>ToOptions(){
        this.Validate();
        var Values=new SortedDictionary<string,string>(StringComparer.Ordinal){{"image.defaultOutput",this.DefaultOutput}};
        for(var Index=0;Index<this.Outputs.Length;Index++){
            var Output=this.Outputs[Index];var Prefix="image.outputs."+Index.ToString(CultureInfo.InvariantCulture)+".";
            Values.Add(Prefix+"key",Output.Key);Values.Add(Prefix+"name",Output.Name);
            Values.Add(Prefix+"kind",Output.Kind==ImageOutputKind.Image?"image":"texture");
            Values.Add(Prefix+"usage",ImageOutputSettings.Token(Output.Usage));
            Values.Add(Prefix+"flipGreen",Output.FlipGreen?"true":"false");Values.Add(Prefix+"mipmaps",Output.Mipmaps?"true":"false");
            Values.Add(Prefix+"channel",Output.Channel);Values.Add(Prefix+"occlusion",Output.OcclusionChannel);
            Values.Add(Prefix+"roughness",Output.RoughnessChannel);Values.Add(Prefix+"metallic",Output.MetallicChannel);
            Values.Add(Prefix+"invertRoughness",Output.InvertRoughness?"true":"false");
        }
        return Values;
    }
    public static ImageImportSettings FromOptions(IReadOnlyDictionary<string,string>Values){
        if(!Values.TryGetValue("image.defaultOutput",out var Default)){ throw new ArgumentException("IMAGE_DEFAULT: missing default output."); }
        var OutputList=new List<ImageOutputSettings>();var Consumed=new HashSet<string>(StringComparer.Ordinal){"image.defaultOutput"};
        for(var Index=0;Index<32;Index++){
            var Prefix="image.outputs."+Index.ToString(CultureInfo.InvariantCulture)+".";
            if(!Values.TryGetValue(Prefix+"key",out var Key)){ break; }
            string Read(string Name,string Fallback){ Consumed.Add(Prefix+Name);return Values.TryGetValue(Prefix+Name,out var Value)?Value:Fallback; }
            bool ReadBool(string Name,bool Fallback){ var Value=Read(Name,Fallback?"true":"false");return Value switch {"true"=>true,"false"=>false,_=>throw new ArgumentException("IMAGE_OPTION: invalid boolean "+Name)}; }
            var Kind=Read("kind","texture") switch {"texture"=>ImageOutputKind.Texture,"image"=>ImageOutputKind.Image,_=>throw new ArgumentException("IMAGE_KIND: unknown output kind.")};
            var UsageToken=Read("usage","color");
            var Usage=Enum.GetValues(typeof(TextureUsage)).Cast<TextureUsage>().Where(U=>ImageOutputSettings.Token(U)==UsageToken).ToArray();
            if(Usage.Length!=1){ throw new ArgumentException("IMAGE_USAGE: unknown usage."); }
            Consumed.Add(Prefix+"key");
            OutputList.Add(new(){Key=Key,Name=Read("name",Key),Kind=Kind,Usage=Usage[0],FlipGreen=ReadBool("flipGreen",false),Mipmaps=ReadBool("mipmaps",true),
                Channel=Read("channel","r"),OcclusionChannel=Read("occlusion","one"),RoughnessChannel=Read("roughness","one"),MetallicChannel=Read("metallic","zero"),InvertRoughness=ReadBool("invertRoughness",false)});
        }
        if(Values.Keys.Any(K=>!Consumed.Contains(K))){ throw new ArgumentException("IMAGE_OPTION: unknown or non-contiguous output option."); }
        var Settings=new ImageImportSettings{DefaultOutput=Default,Outputs=OutputList.ToArray()};Settings.Validate();return Settings;
    }
}
public sealed class AssetImageRequest {
    public int ImageCommand{get;set;}=1;
    public Guid RequestId{get;set;}
    public string Root{get;set;}=string.Empty;
    public string SourcePath{get;set;}=string.Empty;
    public string ExpectedMetadataHash{get;set;}=string.Empty;
    public ImageImportSettings?Settings{get;set;}
    public TextureUsage?TargetUsage{get;set;}
}
public sealed class AssetImageResult {
    public Guid RequestId{get;set;}
    public bool Success{get;set;}
    public string Diagnostic{get;set;}=string.Empty;
    public ImageImportSettings?Settings{get;set;}
    public string MetadataHash{get;set;}=string.Empty;
    public string MetadataJson{get;set;}=string.Empty;
    public string AssetId{get;set;}=string.Empty;
    public int Width{get;set;}
    public int Height{get;set;}
    public bool NormalCandidate{get;set;}
    public string NormalCandidateReason{get;set;}=string.Empty;
    public string ManifestPath{get;set;}=string.Empty;
}
