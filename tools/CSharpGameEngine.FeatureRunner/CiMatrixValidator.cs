using System.Text.Json;
namespace CSharpGameEngine.FeatureStages;

public static class CiMatrixValidator {
    public const string Schema="csharp-game-engine-ci-matrix-v1";
    private static readonly JsonSerializerOptions JsonOptions=new(){PropertyNameCaseInsensitive=true};
    private static readonly string[]Configurations=["Debug","Release"];
    private static readonly IReadOnlyDictionary<string,(string TargetFramework,string Adapter)>RequiredWorkloads=new Dictionary<string,(string,string)>(StringComparer.Ordinal){
        ["solution"]=("net10.0","none"),
        ["windows-tfm"]=("net10.0-windows10.0.19041.0","none"),
        ["vsix"]=("net472","none"),
        ["warp"]=("net10.0-windows10.0.19041.0","warp"),
    };

    public static IReadOnlyList<string>Validate(string RepositoryRoot,FeatureStageCatalog Catalog){
        ArgumentException.ThrowIfNullOrWhiteSpace(RepositoryRoot);
        ArgumentNullException.ThrowIfNull(Catalog);
        string Root=Path.GetFullPath(RepositoryRoot);
        string ManifestPath=Path.Combine(Root,"Docs","Verification","CI-Matrix.json");
        string WorkflowPath=Path.Combine(Root,".github","workflows","ci.yml");
        var Errors=new List<string>();
        if(!File.Exists(ManifestPath)){ Errors.Add("CI matrix manifest is missing: Docs/Verification/CI-Matrix.json");return Errors; }
        if(!File.Exists(WorkflowPath)){ Errors.Add("Required CI workflow is missing: .github/workflows/ci.yml");return Errors; }
        CiMatrixDocument?Document;
        try { Document=JsonSerializer.Deserialize<CiMatrixDocument>(File.ReadAllText(ManifestPath),JsonOptions); }
        catch(Exception Ex)when(Ex is JsonException or IOException or UnauthorizedAccessException){ Errors.Add($"CI matrix manifest cannot be read: {Ex.Message}");return Errors; }
        if(Document is null){ Errors.Add("CI matrix manifest is empty.");return Errors; }
        if(Document.Schema!=Schema){ Errors.Add($"CI matrix schema must be {Schema}."); }
        if(Document.Runner!="windows-2022"){ Errors.Add("Required CI runner must be windows-2022."); }
        if(Document.Architecture!="x64"){ Errors.Add("Required CI architecture must be x64."); }
        CiMatrixCell?[]Cells=Document.Cells?.ToArray()??[];
        var ExpectedIds=new HashSet<string>(RequiredWorkloads.Keys.SelectMany(Workload=>Configurations.Select(Configuration=>$"{Workload}-{Configuration.ToLowerInvariant()}")),StringComparer.Ordinal);
        foreach(var Duplicate in Cells.Where(static Cell=>Cell is not null).GroupBy(static Cell=>Cell!.Id,StringComparer.Ordinal).Where(static Group=>Group.Count()>1)){ Errors.Add($"Duplicate CI cell ID: {Duplicate.Key}"); }
        foreach((string Workload,(string TargetFramework,string Adapter)Contract) in RequiredWorkloads){
            foreach(string Configuration in Configurations){
                string ExpectedId=$"{Workload}-{Configuration.ToLowerInvariant()}";
                CiMatrixCell?Cell=Cells.FirstOrDefault(Cell=>Cell is not null&&Cell.Id==ExpectedId);
                if(Cell is null){ Errors.Add($"Missing required CI cell: {ExpectedId}");continue; }
                if(Cell.Configuration!=Configuration){ Errors.Add($"CI cell {ExpectedId} configuration must be {Configuration}."); }
                if(Cell.Workload!=Workload){ Errors.Add($"CI cell {ExpectedId} workload must be {Workload}."); }
                if(Cell.TargetFramework!=Contract.TargetFramework){ Errors.Add($"CI cell {ExpectedId} targetFramework must be {Contract.TargetFramework}."); }
                if(Cell.Adapter!=Contract.Adapter){ Errors.Add($"CI cell {ExpectedId} adapter must be {Contract.Adapter}."); }
                if(!Cell.Required){ Errors.Add($"CI cell {ExpectedId} cannot be optional."); }
            }
        }
        if(Cells.Any(static Cell=>Cell is null)){ Errors.Add("CI matrix contains a null cell."); }
        foreach(CiMatrixCell Cell in Cells.Where(static Cell=>Cell is not null).Select(static Cell=>Cell!)){
            if(string.IsNullOrWhiteSpace(Cell.Workload)||string.IsNullOrWhiteSpace(Cell.Configuration)||!RequiredWorkloads.ContainsKey(Cell.Workload)||!Configurations.Contains(Cell.Configuration,StringComparer.Ordinal)){ Errors.Add($"Unknown required CI cell contract: {Cell.Id}"); }
            if(!ExpectedIds.Contains(Cell.Id)){ Errors.Add($"Unregistered CI cell ID: {Cell.Id}"); }
        }
        string Workflow=File.ReadAllText(WorkflowPath);
        IReadOnlyDictionary<string,IReadOnlyDictionary<string,string>>WorkflowCells=ParseWorkflowCells(Workflow,Errors);
        foreach(CiMatrixCell Cell in Cells.Where(static Cell=>Cell is not null).Select(static Cell=>Cell!)){
            if(!WorkflowCells.TryGetValue(Cell.Id,out var WorkflowCell)){ Errors.Add($"Workflow matrix is missing cell {Cell.Id}.");continue; }
            Compare(Cell.Id,"configuration",Cell.Configuration,WorkflowCell,Errors);
            Compare(Cell.Id,"workload",Cell.Workload,WorkflowCell,Errors);
            Compare(Cell.Id,"targetFramework",Cell.TargetFramework,WorkflowCell,Errors);
            Compare(Cell.Id,"adapter",Cell.Adapter,WorkflowCell,Errors);
        }
        foreach(string Extra in WorkflowCells.Keys.Except(Cells.Where(static Cell=>Cell is not null).Select(static Cell=>Cell!.Id),StringComparer.Ordinal)){ Errors.Add($"Workflow matrix has an unregistered cell: {Extra}"); }
        RequireToken(Workflow,"runs-on: windows-2022","windows-2022 runner",Errors);
        RequireToken(Workflow,"global-json-file: global.json","global.json SDK selection",Errors);
        RequireToken(Workflow,"dotnet run --project tools/CSharpGameEngine.FeatureRunner/CSharpGameEngine.FeatureRunner.csproj -- verify-ci-matrix","matrix self-validation",Errors);
        RequireToken(Workflow,"dotnet test CSharpGameEngine.slnx","solution test workload",Errors);
        RequireToken(Workflow,"--arch x64","solution x64 architecture",Errors);
        RequireToken(Workflow,"tests/CSharpGameEngine.Windows.Tests/CSharpGameEngine.Windows.Tests.csproj","Windows TFM workload",Errors);
        RequireToken(Workflow,"tools/CSharpGameEngine.VSExtension/CSharpGameEngine.VSExtension.csproj","net472 VSIX workload",Errors);
        RequireToken(Workflow,"suite visual","visual certification suite",Errors);
        RequireToken(Workflow,"--adapter warp","mandatory WARP adapter",Errors);
        if(Workflow.Contains("continue-on-error: true",StringComparison.OrdinalIgnoreCase)){ Errors.Add("Required CI workflow cannot continue on error."); }
        FeatureStageDefinition[]VisualStages=[.. Catalog.Stages.Where(static Stage=>Stage.Availability==FeatureStageAvailability.Executable&&Stage.Mode==FeatureStageMode.Visual)];
        if(VisualStages.Length==0){ Errors.Add("The catalog has no executable Visual Stage for WARP certification."); }
        foreach(FeatureStageDefinition Stage in VisualStages.Where(static Stage=>Stage.AdapterRequirement!=FeatureAdapterRequirement.WarpRequiredHardwareOptional)){ Errors.Add($"Executable Visual Stage {Stage.Id} does not require WARP certification."); }
        return Errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyDictionary<string,IReadOnlyDictionary<string,string>>ParseWorkflowCells(string Workflow,List<string>Errors){
        var Result=new Dictionary<string,IReadOnlyDictionary<string,string>>(StringComparer.Ordinal);
        Dictionary<string,string>?Current=null;
        string?CurrentId=null;
        foreach(string RawLine in Workflow.Replace(((char)13).ToString(),string.Empty,StringComparison.Ordinal).Split((char)10)){
            string Line=RawLine.Trim();
            if(Line=="steps:"){ break; }
            if(Line.StartsWith("- cell:",StringComparison.Ordinal)){
                CurrentId=Line[7..].Trim();
                Current=new Dictionary<string,string>(StringComparer.Ordinal);
                if(!Result.TryAdd(CurrentId,Current)){ Errors.Add($"Workflow matrix duplicates cell {CurrentId}."); }
                continue;
            }
            if(Current is null){ continue; }
            foreach(string Key in new[]{"configuration","workload","targetFramework","adapter"}){
                string Prefix=Key+":";
                if(Line.StartsWith(Prefix,StringComparison.Ordinal)){ Current[Key]=Line[Prefix.Length..].Trim();break; }
            }
        }
        if(Result.Count==0){ Errors.Add("Workflow matrix include list could not be parsed."); }
        return Result;
    }

    private static void Compare(string Id,string Key,string Expected,IReadOnlyDictionary<string,string>Actual,List<string>Errors){
        if(!Actual.TryGetValue(Key,out string?Value)||Value!=Expected){ Errors.Add($"Workflow cell {Id} {Key} must be {Expected}."); }
    }
    private static void RequireToken(string Workflow,string Token,string Contract,List<string>Errors){
        if(!Workflow.Contains(Token,StringComparison.Ordinal)){ Errors.Add($"CI workflow is missing {Contract}."); }
    }
    private sealed record CiMatrixDocument(string Schema,string Runner,string Architecture,IReadOnlyList<CiMatrixCell?>?Cells);
    private sealed record CiMatrixCell(string Id,string Configuration,string Workload,string TargetFramework,string Adapter,bool Required);
}
