using System.Text.Json;

namespace CSharpGameEngine.FeatureStages;

public static class PlatformCompatibilityMatrixValidator {
    public const string Schema="csharp-game-engine-platform-compatibility-v1";
    private static readonly JsonSerializerOptions JsonOptions=new(){PropertyNameCaseInsensitive=true};
    private static readonly string[]ExpectedStages=[
        "platform.boundaries","platform.window-input","input.actions","audio.lifecycle","audio.mixer","audio.spatial","vsix.platform-debug",
    ];
    private static readonly IReadOnlyDictionary<string,(string Configuration,string Capability,string EvidenceMode,string CiCell)>ExpectedRequiredCells=
        new Dictionary<string,(string,string,string,string)>(StringComparer.Ordinal){
            ["platform-solution-debug"]=("Debug","PortableAndSubsystemContracts","SyntheticAndSilent","solution-debug"),
            ["platform-solution-release"]=("Release","PortableAndSubsystemContracts","SyntheticAndSilent","solution-release"),
            ["platform-windows-debug"]=("Debug","WindowsRuntime","SyntheticAndSilent","windows-tfm-debug"),
            ["platform-windows-release"]=("Release","WindowsRuntime","SyntheticAndSilent","windows-tfm-release"),
            ["platform-warp-debug"]=("Debug","GraphicsCorrectness","Warp","warp-debug"),
            ["platform-warp-release"]=("Release","GraphicsCorrectness","Warp","warp-release"),
        };
    private static readonly IReadOnlyDictionary<string,(string Capability,string EvidenceMode,string ManualEvidenceId)>ExpectedOptionalCells=
        new Dictionary<string,(string,string,string)>(StringComparer.Ordinal){
            ["platform-hardware-audio"]=("AudioHardware","XAudio2Hardware","hardware-audio"),
            ["platform-hardware-d3d12"]=("GpuHardware","D3D12Hardware","hardware-d3d12"),
        };

    public static IReadOnlyList<string>Validate(string RepositoryRoot,FeatureStageCatalog Catalog){
        ArgumentException.ThrowIfNullOrWhiteSpace(RepositoryRoot);
        ArgumentNullException.ThrowIfNull(Catalog);
        string Root=Path.GetFullPath(RepositoryRoot);
        var Errors=new List<string>();
        Errors.AddRange(CiMatrixValidator.Validate(Root,Catalog));
        Errors.AddRange(ManualEvidenceLedgerValidator.Validate(Root,false,DateTimeOffset.UtcNow));
        string MatrixPath=Path.Combine(Root,"Docs","Verification","Platform-Compatibility-Matrix.json");
        string CiPath=Path.Combine(Root,"Docs","Verification","CI-Matrix.json");
        string ManualPath=Path.Combine(Root,"Docs","Verification","Manual-Evidence.json");
        PlatformCompatibilityDocument?Document=Read<PlatformCompatibilityDocument>(MatrixPath,"Platform compatibility matrix",Errors);
        CiDocument?Ci=Read<CiDocument>(CiPath,"CI matrix",Errors);
        ManualDocument?Manual=Read<ManualDocument>(ManualPath,"Manual evidence ledger",Errors);
        if(Document is null||Ci is null||Manual is null){ return Finish(Errors); }
        if(Document.SchemaVersion!=Schema){ Errors.Add($"Platform compatibility schema must be {Schema}."); }
        if(Document.Platform is null||Document.Platform.Os!="Windows"||Document.Platform.MinimumVersion!="10.0.19041.0"||Document.Platform.Architecture!="x64"){
            Errors.Add("Platform compatibility target must be Windows 10.0.19041.0+ x64.");
        }
        ValidateStages(Document.RequiredStages,Catalog,Errors);
        RequiredCell?[]Required=Document.RequiredCells?.ToArray()??[];
        OptionalCell?[]Optional=Document.OptionalCells?.ToArray()??[];
        ValidateDuplicates(Required.Select(static Cell=>Cell?.Id),"required platform compatibility cell",Errors);
        ValidateDuplicates(Optional.Select(static Cell=>Cell?.Id),"optional platform compatibility cell",Errors);
        if(Required.Any(static Cell=>Cell is null)){ Errors.Add("Platform compatibility requiredCells contains null."); }
        if(Optional.Any(static Cell=>Cell is null)){ Errors.Add("Platform compatibility optionalCells contains null."); }
        IReadOnlyDictionary<string,CiCell>CiCells=UniqueById(Ci.Cells,"CI cell",Errors);
        IReadOnlyDictionary<string,ManualEntry>ManualEntries=UniqueById(Manual.Entries,"manual evidence entry",Errors);
        ValidateRequired(Required.Where(static Cell=>Cell is not null).Select(static Cell=>Cell!),CiCells,Errors);
        ValidateOptional(Optional.Where(static Cell=>Cell is not null).Select(static Cell=>Cell!),ManualEntries,Errors);
        return Finish(Errors);
    }

    private static void ValidateStages(IReadOnlyList<string?>?Stages,FeatureStageCatalog Catalog,List<string>Errors){
        string?[]Actual=Stages?.ToArray()??[];
        ValidateDuplicates(Actual,"required platform Stage",Errors);
        foreach(string Expected in ExpectedStages){
            if(!Actual.Contains(Expected,StringComparer.Ordinal)){ Errors.Add($"Missing required platform Stage: {Expected}"); }
        }
        foreach(string Extra in Actual.Where(static Id=>!string.IsNullOrWhiteSpace(Id)).Select(static Id=>Id!).Except(ExpectedStages,StringComparer.Ordinal)){
            Errors.Add($"Unregistered required platform Stage: {Extra}");
        }
        foreach(string Id in Actual.Where(static Id=>!string.IsNullOrWhiteSpace(Id)).Select(static Id=>Id!)){
            FeatureStageDefinition?Stage=Catalog.Stages.SingleOrDefault(Stage=>Stage.Id==Id);
            if(Stage is null){ Errors.Add($"Required platform Stage is absent from the catalog: {Id}"); }
            else if(Stage.Availability!=FeatureStageAvailability.Executable){ Errors.Add($"Required platform Stage must be executable: {Id}"); }
        }
    }

    private static void ValidateRequired(IEnumerable<RequiredCell>Cells,IReadOnlyDictionary<string,CiCell>CiCells,List<string>Errors){
        RequiredCell[]Actual=[.. Cells];
        foreach((string Id,(string Configuration,string Capability,string EvidenceMode,string CiCell)Expected) in ExpectedRequiredCells){
            RequiredCell?Cell=Actual.FirstOrDefault(Cell=>Cell.Id==Id);
            if(Cell is null){ Errors.Add($"Missing required platform compatibility cell: {Id}");continue; }
            Compare(Id,"configuration",Expected.Configuration,Cell.Configuration,Errors);
            Compare(Id,"capability",Expected.Capability,Cell.Capability,Errors);
            Compare(Id,"evidenceMode",Expected.EvidenceMode,Cell.EvidenceMode,Errors);
            Compare(Id,"ciCell",Expected.CiCell,Cell.CiCell,Errors);
            if(string.IsNullOrWhiteSpace(Cell.CiCell)){ Errors.Add($"Required platform cell {Id} has an empty CI cell reference.");continue; }
            if(!CiCells.TryGetValue(Cell.CiCell,out CiCell?Ci)){ Errors.Add($"Required platform cell {Id} references missing CI cell {Cell.CiCell}.");continue; }
            if(!Ci.Required){ Errors.Add($"Required platform cell {Id} cannot reference optional CI cell {Cell.CiCell}."); }
            if(Ci.Configuration!=Cell.Configuration){ Errors.Add($"Required platform cell {Id} and CI cell {Cell.CiCell} must use the same configuration."); }
            string ExpectedAdapter=Cell.EvidenceMode=="Warp"?"warp":"none";
            if(Ci.Adapter!=ExpectedAdapter){ Errors.Add($"CI cell {Cell.CiCell} adapter must be {ExpectedAdapter} for platform evidence mode {Cell.EvidenceMode}."); }
        }
        foreach(string Extra in Actual.Select(static Cell=>Cell.Id).Except(ExpectedRequiredCells.Keys,StringComparer.Ordinal)){
            Errors.Add($"Unregistered required platform compatibility cell: {Extra}");
        }
    }

    private static void ValidateOptional(IEnumerable<OptionalCell>Cells,IReadOnlyDictionary<string,ManualEntry>ManualEntries,List<string>Errors){
        OptionalCell[]Actual=[.. Cells];
        foreach((string Id,(string Capability,string EvidenceMode,string ManualEvidenceId)Expected) in ExpectedOptionalCells){
            OptionalCell?Cell=Actual.FirstOrDefault(Cell=>Cell.Id==Id);
            if(Cell is null){ Errors.Add($"Missing optional platform compatibility cell: {Id}");continue; }
            Compare(Id,"capability",Expected.Capability,Cell.Capability,Errors);
            Compare(Id,"evidenceMode",Expected.EvidenceMode,Cell.EvidenceMode,Errors);
            Compare(Id,"manualEvidenceId",Expected.ManualEvidenceId,Cell.ManualEvidenceId,Errors);
            if(string.IsNullOrWhiteSpace(Cell.ManualEvidenceId)){
                Errors.Add($"Optional platform cell {Id} has an empty manual evidence reference.");
            } else if(!ManualEntries.TryGetValue(Cell.ManualEvidenceId,out ManualEntry?Entry)){
                Errors.Add($"Optional platform cell {Id} references missing manual evidence {Cell.ManualEvidenceId}.");
            } else if(Entry.Owner is not("AudioInputPlatform" or "Rendering")){
                Errors.Add($"Optional platform evidence {Cell.ManualEvidenceId} has invalid owner {Entry.Owner}.");
            }
        }
        foreach(string Extra in Actual.Select(static Cell=>Cell.Id).Except(ExpectedOptionalCells.Keys,StringComparer.Ordinal)){
            Errors.Add($"Unregistered optional platform compatibility cell: {Extra}");
        }
    }

    private static T?Read<T>(string Path,string Name,List<string>Errors){
        if(!File.Exists(Path)){ Errors.Add($"{Name} is missing: {Path}");return default; }
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(Path),JsonOptions); }
        catch(Exception Ex)when(Ex is JsonException or IOException or UnauthorizedAccessException){ Errors.Add($"{Name} cannot be read: {Ex.Message}");return default; }
    }
    private static IReadOnlyDictionary<string,T>UniqueById<T>(IReadOnlyList<T?>?Items,string Kind,List<string>Errors)where T:class,IIdentified{
        var Result=new Dictionary<string,T>(StringComparer.Ordinal);
        foreach(T?Item in Items??[]){
            if(Item is null){ Errors.Add($"{Kind} list contains null.");continue; }
            if(string.IsNullOrWhiteSpace(Item.Id)){ Errors.Add($"{Kind} has an empty ID.");continue; }
            if(!Result.TryAdd(Item.Id,Item)){ Errors.Add($"Duplicate {Kind}: {Item.Id}"); }
        }
        return Result;
    }
    private static void ValidateDuplicates(IEnumerable<string?>Ids,string Kind,List<string>Errors){
        foreach(IGrouping<string?,string?>Group in Ids.GroupBy(static Id=>Id,StringComparer.Ordinal).Where(static Group=>string.IsNullOrWhiteSpace(Group.Key)||Group.Count()>1)){
            Errors.Add(string.IsNullOrWhiteSpace(Group.Key)?$"{Kind} has an empty ID.":$"Duplicate {Kind}: {Group.Key}");
        }
    }
    private static void Compare(string Id,string Field,string Expected,string Actual,List<string>Errors){
        if(Actual!=Expected){ Errors.Add($"Platform compatibility cell {Id} {Field} must be {Expected}."); }
    }
    private static IReadOnlyList<string>Finish(List<string>Errors)=>Errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private interface IIdentified { string Id{get;} }
    private sealed record PlatformCompatibilityDocument(string SchemaVersion,PlatformTarget?Platform,IReadOnlyList<string?>?RequiredStages,IReadOnlyList<RequiredCell?>?RequiredCells,IReadOnlyList<OptionalCell?>?OptionalCells);
    private sealed record PlatformTarget(string Os,string MinimumVersion,string Architecture);
    private sealed record RequiredCell(string Id,string Configuration,string Capability,string EvidenceMode,string CiCell):IIdentified;
    private sealed record OptionalCell(string Id,string Capability,string EvidenceMode,string ManualEvidenceId):IIdentified;
    private sealed record CiDocument(string Schema,string Runner,string Architecture,IReadOnlyList<CiCell?>?Cells);
    private sealed record CiCell(string Id,string Configuration,string Workload,string TargetFramework,string Adapter,bool Required):IIdentified;
    private sealed record ManualDocument(string SchemaVersion,IReadOnlyList<ManualEntry?>?Entries);
    private sealed record ManualEntry(string Id,string Requirement,string Owner,string Status,string Reason,string?Evidence,string?ObservedAtUtc,string?ExpiresAtUtc):IIdentified;
}
