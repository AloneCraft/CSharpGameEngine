namespace CSharpGameEngine.Contracts;
public sealed class ModelImportSettings {
    public ModelClipRange[]Clips{get;set;}=[];
    public bool Meshes{get;set;}=true;
    public bool Materials{get;set;}=true;
    public bool Skeleton{get;set;}=true;
    public bool Animations{get;set;}=true;
    public string Rig{get;set;}="generic";
    public IReadOnlyDictionary<string,string>ToOptions(){
        if(this.Clips is null||this.Clips.Length>64||this.Clips.Any(C=>C is null||string.IsNullOrWhiteSpace(C.Name)||string.IsNullOrWhiteSpace(C.Take)||C.Name.Length>256||C.Name.Any(char.IsControl)||C.StartFrame<0||C.EndFrame<=C.StartFrame)
            ||this.Clips.Select(C=>C.Name).Distinct(StringComparer.Ordinal).Count()!=this.Clips.Length){ throw new ArgumentException("MODEL_CLIP_RANGE: invalid named frame range."); }
        var Values=new SortedDictionary<string,string>(StringComparer.Ordinal){
        ["model.meshes"]=this.Meshes?"true":"false",["model.materials"]=this.Materials?"true":"false",
        ["model.skeleton"]=this.Skeleton?"true":"false",["model.animations"]=this.Animations?"true":"false",["model.rig"]=this.Rig
        };
        for(var I=0;I<this.Clips.Length;I++){
            var Prefix="model.clips."+I.ToString(System.Globalization.CultureInfo.InvariantCulture)+".";var Clip=this.Clips[I];
            Values.Add(Prefix+"name",Clip.Name);Values.Add(Prefix+"take",Clip.Take);
            Values.Add(Prefix+"startFrame",Clip.StartFrame.ToString(System.Globalization.CultureInfo.InvariantCulture));Values.Add(Prefix+"endFrame",Clip.EndFrame.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return Values;
    }
    public static ModelImportSettings FromOptions(IReadOnlyDictionary<string,string>Options){
        bool Read(string Key)=>!Options.TryGetValue(Key,out var Value)||Value switch {"true"=>true,"false"=>false,_=>throw new ArgumentException("MODEL_OPTION: invalid boolean "+Key)};
        var Result=new ModelImportSettings{Meshes=Read("model.meshes"),Materials=Read("model.materials"),Skeleton=Read("model.skeleton"),Animations=Read("model.animations"),Rig=Options.TryGetValue("model.rig",out var Rig)?Rig:"generic"};
        if(Result.Rig!="generic"){ throw new ArgumentException("MODEL_RIG: only explicit Generic import is currently supported; Humanoid requires a configured retargeting processor."); }
        var Ranges=new List<ModelClipRange>();
        for(var I=0;I<64;I++){
            var Prefix="model.clips."+I.ToString(System.Globalization.CultureInfo.InvariantCulture)+".";
            if(!Options.TryGetValue(Prefix+"name",out var Name)){ break; }
            if(!Options.TryGetValue(Prefix+"take",out var Take)||!Options.TryGetValue(Prefix+"startFrame",out var Start)||!Options.TryGetValue(Prefix+"endFrame",out var End)
                ||!int.TryParse(Start,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var First)||!int.TryParse(End,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var Last)){
                throw new ArgumentException("MODEL_CLIP_RANGE: incomplete frame range.");
            }
            Ranges.Add(new(){Name=Name,Take=Take,StartFrame=First,EndFrame=Last});
        }
        Result.Clips=Ranges.ToArray();_=Result.ToOptions();return Result;
    }
}
public sealed class ModelClipRange {
    public string Name{get;set;}=string.Empty;
    public string Take{get;set;}=string.Empty;
    public int StartFrame{get;set;}
    public int EndFrame{get;set;}
}
public sealed class AssetModelRequest {
    public ModelImportSettings?Settings{get;set;}
    public string ExpectedMetadataHash{get;set;}=string.Empty;
    public int ModelCommand{get;set;}=1;
    public Guid RequestId{get;set;}
    public string Root{get;set;}=string.Empty;
    public string SourcePath{get;set;}=string.Empty;
}
public sealed class ModelSubassetSummary {
    public string Name{get;set;}=string.Empty;
    public string Kind{get;set;}=string.Empty;
    public string Id{get;set;}=string.Empty;
}
public sealed class AssetModelResult {
    public Guid RequestId{get;set;}
    public bool Success{get;set;}
    public string Diagnostic{get;set;}=string.Empty;
    public ModelImportSettings?Settings{get;set;}
    public string MetadataJson{get;set;}=string.Empty;
    public string MetadataHash{get;set;}=string.Empty;
    public string ManifestPath{get;set;}=string.Empty;
    public ModelSubassetSummary[]Subassets{get;set;}=[];
}
