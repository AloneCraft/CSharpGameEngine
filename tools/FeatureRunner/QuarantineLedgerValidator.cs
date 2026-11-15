using System.Text.Json;

namespace CSharpGameEngine.FeatureStages;

public static class QuarantineLedgerValidator {
    public const string Schema="csharp-game-engine-quarantine-v1";
    public static readonly TimeSpan MaximumDuration=TimeSpan.FromDays(14);
    private static readonly JsonSerializerOptions JsonOptions=new(){PropertyNameCaseInsensitive=true};

    public static IReadOnlyList<string>Validate(string RepositoryRoot,FeatureStageCatalog Catalog,DateTimeOffset NowUtc){
        ArgumentException.ThrowIfNullOrWhiteSpace(RepositoryRoot);
        ArgumentNullException.ThrowIfNull(Catalog);
        string Root=Path.GetFullPath(RepositoryRoot);
        string LedgerPath=Path.Combine(Root,"Docs","Verification","Quarantines.json");
        var Errors=new List<string>();
        if(!File.Exists(LedgerPath)){ return ["Quarantine ledger is missing: Docs/Verification/Quarantines.json"]; }
        QuarantineDocument?Document;
        try { Document=JsonSerializer.Deserialize<QuarantineDocument>(File.ReadAllText(LedgerPath),JsonOptions); }
        catch(Exception Ex)when(Ex is JsonException or IOException or UnauthorizedAccessException){ return [$"Quarantine ledger cannot be read: {Ex.Message}"]; }
        if(Document is null){ return ["Quarantine ledger is empty."]; }
        if(Document.Schema!=Schema){ Errors.Add($"Quarantine ledger schema must be {Schema}."); }
        QuarantineEntry?[]Entries=Document.Entries?.ToArray()??[];
        foreach(var Duplicate in Entries.Where(static Entry=>Entry is not null).GroupBy(static Entry=>Entry!.Id,StringComparer.Ordinal).Where(static Group=>Group.Count()>1)){ Errors.Add($"Duplicate quarantine ID: {Duplicate.Key}"); }
        foreach(var DuplicateScope in Entries.Where(static Entry=>Entry is not null).GroupBy(static Entry=>$"{Entry!.StageId}|{Entry.Configuration}|{Entry.Adapter}",StringComparer.Ordinal).Where(static Group=>Group.Count()>1)){ Errors.Add($"Duplicate quarantine scope: {DuplicateScope.Key}"); }
        foreach(QuarantineEntry?Entry in Entries){ ValidateEntry(Root,Catalog,NowUtc,Entry,Errors); }
        return Errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static void ValidateEntry(string Root,FeatureStageCatalog Catalog,DateTimeOffset NowUtc,QuarantineEntry?Entry,List<string>Errors){
        if(Entry is null){ Errors.Add("Quarantine ledger contains a null entry.");return; }
        string Label=string.IsNullOrWhiteSpace(Entry.Id)?"<missing-id>":Entry.Id;
        if(string.IsNullOrWhiteSpace(Entry.Id)){ Errors.Add("Quarantine ID is required."); }
        if(string.IsNullOrWhiteSpace(Entry.Owner)){ Errors.Add($"Quarantine {Label} owner is required."); }
        if(string.IsNullOrWhiteSpace(Entry.Reason)){ Errors.Add($"Quarantine {Label} reason is required."); }
        if(string.IsNullOrWhiteSpace(Entry.FailureSignature)){ Errors.Add($"Quarantine {Label} failureSignature is required."); }
        FeatureStageDefinition?Stage=Catalog.Stages.FirstOrDefault(Stage=>Stage.Id==Entry.StageId);
        if(Stage is null){ Errors.Add($"Quarantine {Label} references unknown Stage {Entry.StageId}."); }
        else if(Stage.Availability!=FeatureStageAvailability.Executable){ Errors.Add($"Quarantine {Label} cannot target NotImplemented Stage {Entry.StageId}."); }
        if(Entry.Configuration is not("Any" or "Debug" or "Release")){ Errors.Add($"Quarantine {Label} configuration must be Any, Debug, or Release."); }
        if(Entry.Adapter is not("Any" or "Warp" or "Hardware")){ Errors.Add($"Quarantine {Label} adapter must be Any, Warp, or Hardware."); }
        if(Entry.CreatedUtc>NowUtc.AddMinutes(5)){ Errors.Add($"Quarantine {Label} createdUtc is in the future."); }
        if(Entry.ExpiresUtc<=Entry.CreatedUtc){ Errors.Add($"Quarantine {Label} expiresUtc must be after createdUtc."); }
        if(Entry.ExpiresUtc-Entry.CreatedUtc>MaximumDuration){ Errors.Add($"Quarantine {Label} exceeds the fourteen-day maximum duration."); }
        if(Entry.ExpiresUtc<=NowUtc){ Errors.Add($"Quarantine {Label} is expired."); }
        if(string.IsNullOrWhiteSpace(Entry.IssueOrEvidence)){ Errors.Add($"Quarantine {Label} issueOrEvidence is required."); }
        else if(Uri.TryCreate(Entry.IssueOrEvidence,UriKind.Absolute,out Uri?UriValue)){
            if(UriValue.Scheme is not("https" or "http")){ Errors.Add($"Quarantine {Label} issueOrEvidence URI must use HTTP or HTTPS."); }
        } else {
            try {
                string FullPath=Path.GetFullPath(Path.Combine(Root,Entry.IssueOrEvidence.Replace('/',Path.DirectorySeparatorChar)));
                string Prefix=Root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(!FullPath.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase)||!File.Exists(FullPath)||ContainsReparsePoint(Root,FullPath)){ Errors.Add($"Quarantine {Label} issueOrEvidence path is missing, linked, or outside the repository."); }
            } catch(Exception Ex)when(Ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException){ Errors.Add($"Quarantine {Label} issueOrEvidence path is invalid: {Ex.Message}"); }
        }
    }

    private static bool ContainsReparsePoint(string Root,string FullPath){
        string?Current=FullPath;
        while(Current is not null&&!Current.Equals(Root,StringComparison.OrdinalIgnoreCase)){
            if((File.Exists(Current)||Directory.Exists(Current))&&(File.GetAttributes(Current)&FileAttributes.ReparsePoint)!=0){ return true; }
            Current=Path.GetDirectoryName(Current);
        }
        return false;
    }

    private sealed record QuarantineDocument(string Schema,IReadOnlyList<QuarantineEntry?>?Entries);
    private sealed record QuarantineEntry(string Id,string StageId,string Configuration,string Adapter,string Owner,string IssueOrEvidence,string Reason,string FailureSignature,DateTimeOffset CreatedUtc,DateTimeOffset ExpiresUtc);
}
