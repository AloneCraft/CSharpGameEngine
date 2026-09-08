using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpGameEngine.FeatureStages;

public static class ReleaseCompatibilityMatrixValidator {
    public const string Schema="csharp-game-engine-release-compatibility-v1";
    public const string MatrixRelativePath=
        "Docs/Verification/Release-Compatibility-Matrix.json";
    private static readonly JsonSerializerOptions StrictJsonOptions=new(){
        PropertyNameCaseInsensitive=true,
        UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly JsonSerializerOptions SourceJsonOptions=new(){
        PropertyNameCaseInsensitive=true,
    };
    private static readonly string[]ExpectedStages=[
        "verification.ci-matrix","platform.compatibility","vsix.compatibility",
        "release.packages",
    ];
    private static readonly string[]ExpectedCiCells=[
        "solution-debug","solution-release","windows-tfm-debug",
        "windows-tfm-release","vsix-debug","vsix-release","warp-debug",
        "warp-release",
    ];
    private static readonly string[]ExpectedPlatformCells=[
        "platform-solution-debug","platform-solution-release",
        "platform-windows-debug","platform-windows-release",
        "platform-warp-debug","platform-warp-release",
    ];
    private static readonly string[]ExpectedVsixChecks=[
        "shader-graph-1041","shader-graph-1033","scene-designer","dpi-scaling",
        "escape-routing","physics-sidebar","startup-layout",
        "preview-render-audio",
    ];
    private static readonly string[]ExpectedOptionalHardwareCells=[
        "platform-hardware-audio","platform-hardware-d3d12",
    ];

    public static IReadOnlyList<string>Validate(
        string RepositoryRoot,FeatureStageCatalog Catalog,DateTimeOffset Now){
        ArgumentException.ThrowIfNullOrWhiteSpace(RepositoryRoot);
        ArgumentNullException.ThrowIfNull(Catalog);
        string Root=Path.GetFullPath(RepositoryRoot);
        var Errors=new List<string>();
        Errors.AddRange(CiMatrixValidator.Validate(Root,Catalog));
        Errors.AddRange(PlatformCompatibilityMatrixValidator.Validate(Root,Catalog));
        Errors.AddRange(VsixCompatibilityMatrixValidator.Validate(
            Root,Catalog,"Release",false,Now));
        ReleaseCompatibilityDocument?Document=Read<ReleaseCompatibilityDocument>(
            Path.Combine(Root,MatrixRelativePath.Replace(
                '/',Path.DirectorySeparatorChar)),
            "Release compatibility matrix",StrictJsonOptions,Errors);
        CiDocument?Ci=Read<CiDocument>(
            Path.Combine(Root,"Docs","Verification","CI-Matrix.json"),
            "CI matrix",SourceJsonOptions,Errors);
        PlatformDocument?Platform=Read<PlatformDocument>(
            Path.Combine(Root,"Docs","Verification",
                "Platform-Compatibility-Matrix.json"),
            "Platform compatibility matrix",SourceJsonOptions,Errors);
        VsixDocument?Vsix=Read<VsixDocument>(
            Path.Combine(Root,"Docs","Verification",
                "VSIX-Compatibility-Matrix.json"),
            "VSIX compatibility matrix",SourceJsonOptions,Errors);
        if(Document is null||Ci is null||Platform is null||Vsix is null){
            return Finish(Errors);
        }

        if(Document.SchemaVersion!=Schema){
            Errors.Add($"Release compatibility schema must be {Schema}.");
        }
        if(Document.FeatureId!="F-L7-046"){
            Errors.Add("Release compatibility featureId must be F-L7-046.");
        }
        ValidateScope(Document.Scope,Errors);
        ValidateSources(Document.Sources,Errors);
        ValidateExact(
            Document.RequiredStages,ExpectedStages,"required Stage",Errors);
        ValidateExact(
            Document.RequiredCiCells,ExpectedCiCells,"required CI cell",Errors);
        ValidateExact(
            Document.RequiredPlatformCells,ExpectedPlatformCells,
            "required Platform cell",Errors);
        ValidateExact(
            Document.RequiredVsixChecks,ExpectedVsixChecks,
            "required VSIX check",Errors);
        ValidateExact(
            Document.OptionalHardwareCells,ExpectedOptionalHardwareCells,
            "optional hardware cell",Errors);
        ValidateStages(Document.RequiredStages,Catalog,Errors);
        ValidateCiSources(Document.RequiredCiCells,Ci,Errors);
        ValidatePlatformSources(
            Document.RequiredPlatformCells,Document.OptionalHardwareCells,
            Platform,Errors);
        ValidateVsixSources(Document.RequiredVsixChecks,Vsix,Errors);
        return Finish(Errors);
    }

    private static void ValidateScope(
        ReleaseCompatibilityScope?Scope,List<string>Errors){
        if(Scope is null
            ||Scope.Id!="windows-x64-product"
            ||Scope.OperatingSystem!="Windows"
            ||Scope.MinimumVersion!="10.0.19041.0"
            ||Scope.Architecture!="x64"
            ||Scope.DotnetRuntime!="10.0"
            ||Scope.VsixTargetFramework!="net472"
            ||Scope.VisualCertificationAdapter!="warp"){
            Errors.Add(
                "Release compatibility scope must be Windows 10.0.19041.0+ x64, .NET 10, net472 VSIX, and WARP visual certification.");
        }
    }

    private static void ValidateSources(
        ReleaseCompatibilitySources?Sources,List<string>Errors){
        if(Sources is null
            ||Sources.CiMatrix!="Docs/Verification/CI-Matrix.json"
            ||Sources.PlatformMatrix!=
                "Docs/Verification/Platform-Compatibility-Matrix.json"
            ||Sources.VsixMatrix!=
                "Docs/Verification/VSIX-Compatibility-Matrix.json"
            ||Sources.ManualEvidence!=
                "Docs/Verification/Manual-Evidence.json"){
            Errors.Add(
                "Release compatibility sources must reference the canonical CI, Platform, VSIX, and manual-evidence truths.");
        }
    }

    private static void ValidateStages(
        IReadOnlyList<string?>?StageIds,FeatureStageCatalog Catalog,
        List<string>Errors){
        foreach(string StageId in Valid(StageIds)){
            FeatureStageDefinition?Stage=Catalog.Stages.FirstOrDefault(
                Stage=>Stage.Id==StageId);
            if(Stage is null){
                Errors.Add($"Required release compatibility Stage is absent: {StageId}");
            } else if(Stage.Availability!=FeatureStageAvailability.Executable){
                Errors.Add(
                    $"Required release compatibility Stage must be executable: {StageId}");
            }
        }
    }

    private static void ValidateCiSources(
        IReadOnlyList<string?>?Aggregate,CiDocument Source,List<string>Errors){
        IReadOnlyDictionary<string,CiCell>Cells=Unique(
            Source.Cells,"CI source cell",Errors);
        foreach(string Id in Valid(Aggregate)){
            if(!Cells.TryGetValue(Id,out CiCell?Cell)||!Cell.Required){
                Errors.Add($"CI source is missing required aggregate cell {Id}.");
            }
        }
    }

    private static void ValidatePlatformSources(
        IReadOnlyList<string?>?Required,IReadOnlyList<string?>?Optional,
        PlatformDocument Source,List<string>Errors){
        IReadOnlyDictionary<string,IdentifiedCell>RequiredSource=Unique(
            Source.RequiredCells,"Platform required source cell",Errors);
        IReadOnlyDictionary<string,IdentifiedCell>OptionalSource=Unique(
            Source.OptionalCells,"Platform optional source cell",Errors);
        foreach(string Id in Valid(Required)){
            if(!RequiredSource.ContainsKey(Id)){
                Errors.Add(
                    $"Platform source is missing required aggregate cell {Id}.");
            }
        }
        foreach(string Id in Valid(Optional)){
            if(!OptionalSource.ContainsKey(Id)){
                Errors.Add(
                    $"Platform source is missing optional aggregate cell {Id}.");
            }
        }
    }

    private static void ValidateVsixSources(
        IReadOnlyList<string?>?Aggregate,VsixDocument Source,List<string>Errors){
        IReadOnlyDictionary<string,VsixCheck>Checks=Unique(
            Source.Automation?.Checks,"VSIX source check",Errors);
        foreach(string Id in Valid(Aggregate)){
            if(!Checks.TryGetValue(Id,out VsixCheck?Check)){
                Errors.Add($"VSIX source is missing aggregate check {Id}.");
            } else if(!Check.RequiredForRelease){
                Errors.Add(
                    $"VSIX source no longer marks aggregate check {Id} as required.");
            }
        }
    }

    private static void ValidateExact(
        IReadOnlyList<string?>?Actual,IReadOnlyList<string>Expected,string Kind,
        List<string>Errors){
        string?[]Items=Actual?.ToArray()??[];
        foreach(IGrouping<string?,string?>Group in Items.GroupBy(
            static Item=>Item,StringComparer.Ordinal)){
            if(string.IsNullOrWhiteSpace(Group.Key)){
                Errors.Add($"Release compatibility {Kind} has an empty ID.");
            } else if(Group.Count()>1){
                Errors.Add($"Duplicate {Kind}: {Group.Key}");
            }
        }
        foreach(string Id in Expected){
            if(!Items.Contains(Id,StringComparer.Ordinal)){
                Errors.Add($"Missing {Kind}: {Id}");
            }
        }
        foreach(string Id in Valid(Items).Except(Expected,StringComparer.Ordinal)){
            Errors.Add($"Unregistered {Kind}: {Id}");
        }
    }

    private static IReadOnlyDictionary<string,T>Unique<T>(
        IReadOnlyList<T?>?Items,string Kind,List<string>Errors)
        where T:class,IIdentified {
        var Result=new Dictionary<string,T>(StringComparer.Ordinal);
        foreach(T?Item in Items??[]){
            if(Item is null){ Errors.Add($"{Kind} list contains null.");continue; }
            if(string.IsNullOrWhiteSpace(Item.Id)){
                Errors.Add($"{Kind} has an empty ID.");continue;
            }
            if(!Result.TryAdd(Item.Id,Item)){
                Errors.Add($"Duplicate {Kind}: {Item.Id}");
            }
        }
        return Result;
    }

    private static IEnumerable<string>Valid(IEnumerable<string?>?Items)=>
        (Items??[]).Where(static Item=>!string.IsNullOrWhiteSpace(Item))
        .Select(static Item=>Item!);

    private static T?Read<T>(
        string Path,string Name,JsonSerializerOptions Options,List<string>Errors){
        if(!File.Exists(Path)){ Errors.Add($"{Name} is missing: {Path}");return default; }
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(Path),Options); }
        catch(Exception Ex)when(Ex is JsonException or IOException
            or UnauthorizedAccessException){
            Errors.Add($"{Name} cannot be read: {Ex.Message}");
            return default;
        }
    }

    private static IReadOnlyList<string>Finish(List<string>Errors)=>
        Errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private interface IIdentified { string Id{get;} }
    private sealed record ReleaseCompatibilityDocument(
        string SchemaVersion,string FeatureId,
        ReleaseCompatibilityScope?Scope,ReleaseCompatibilitySources?Sources,
        IReadOnlyList<string?>?RequiredStages,
        IReadOnlyList<string?>?RequiredCiCells,
        IReadOnlyList<string?>?RequiredPlatformCells,
        IReadOnlyList<string?>?RequiredVsixChecks,
        IReadOnlyList<string?>?OptionalHardwareCells);
    private sealed record ReleaseCompatibilityScope(
        string Id,string OperatingSystem,string MinimumVersion,
        string Architecture,string DotnetRuntime,string VsixTargetFramework,
        string VisualCertificationAdapter);
    private sealed record ReleaseCompatibilitySources(
        string CiMatrix,string PlatformMatrix,string VsixMatrix,
        string ManualEvidence);
    private sealed record CiDocument(
        string Schema,string Runner,string Architecture,
        IReadOnlyList<CiCell?>?Cells);
    private sealed record CiCell(
        string Id,string Configuration,string Workload,string TargetFramework,
        string Adapter,bool Required):IIdentified;
    private sealed record PlatformDocument(
        string SchemaVersion,JsonElement Platform,
        IReadOnlyList<string?>?RequiredStages,
        IReadOnlyList<IdentifiedCell?>?RequiredCells,
        IReadOnlyList<IdentifiedCell?>?OptionalCells);
    private sealed record IdentifiedCell(string Id):IIdentified;
    private sealed record VsixDocument(
        string SchemaVersion,VsixAutomation?Automation);
    private sealed record VsixAutomation(IReadOnlyList<VsixCheck?>?Checks);
    private sealed record VsixCheck(string Id,bool RequiredForRelease):IIdentified;
}
