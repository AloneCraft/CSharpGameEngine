using System.Text.Json;
using System.Text.RegularExpressions;
namespace CSharpGameEngine.FeatureStages;
public static class FeatureCoverageValidator {
    public static IReadOnlyList<string>Validate(string RepositoryRoot,FeatureStageCatalog Catalog){
        ArgumentException.ThrowIfNullOrWhiteSpace(RepositoryRoot);
        ArgumentNullException.ThrowIfNull(Catalog);
        var Errors=new List<string>(Catalog.Validate());
        var LedgerPath=Path.Combine(RepositoryRoot,"Docs","Implementation-Status.md");
        if(!File.Exists(LedgerPath)){
            Errors.Add($"Implementation ledger does not exist: {LedgerPath}");
            return Errors;
        }
        var Ledger=File.ReadAllText(LedgerPath);
        var ManagedFeatureIds=new HashSet<string>(FeatureStageCatalog.NonPhysicsRoadmapFeatureIds.Concat(FeatureStageCatalog.WorldScaleRoadmapFeatureIds).Append(FeatureStageCatalog.ExtendedEcsMaskFeatureId),StringComparer.Ordinal);
        foreach(var Feature in Catalog.Features){
            var SectionMatch=Regex.Match(Ledger,$@"(?ms)^## {Regex.Escape(Feature.Id)}\b.*?(?=^## |\z)");
            var TableMatch=Regex.Match(Ledger,$@"(?m)^\|\s*{Regex.Escape(Feature.Id)}\s*\|");
            if(!SectionMatch.Success&&!TableMatch.Success){
                Errors.Add($"Implementation ledger is missing feature {Feature.Id}.");
                continue;
            }
            if(!SectionMatch.Success){ continue; }
            ValidateSection(Feature,SectionMatch.Value,Catalog,ManagedFeatureIds.Contains(Feature.Id),Errors);
        }
        foreach(var Stage in Catalog.Stages.Where(static Stage=>Stage.Mode==FeatureStageMode.Visual)){
            if(string.IsNullOrWhiteSpace(Stage.SceneProject)||string.IsNullOrWhiteSpace(Stage.SceneId)){
                Errors.Add($"Visual stage {Stage.Id} has no Scene project binding.");
                continue;
            }
            var ProjectPath=Path.Combine(RepositoryRoot,Stage.SceneProject.Replace('/',Path.DirectorySeparatorChar));
            if(!File.Exists(ProjectPath)){
                Errors.Add($"Scene project for {Stage.Id} does not exist: {Stage.SceneProject}");
                continue;
            }
            var ProgramPath=Path.Combine(Path.GetDirectoryName(ProjectPath)!,"Program.cs");
            if(!File.Exists(ProgramPath)||!File.ReadAllText(ProgramPath).Contains($"\"{Stage.SceneId}\"",StringComparison.Ordinal)){ Errors.Add($"Scene project for {Stage.Id} does not declare Scene ID {Stage.SceneId}."); }
        }
        return Errors.Distinct(StringComparer.Ordinal).OrderBy(static Error=>Error,StringComparer.Ordinal).ToArray();
    }
    private static void ValidateSection(FeatureDefinition Feature,string Section,FeatureStageCatalog Catalog,bool IsManaged,List<string>Errors){
        var StageLine=Regex.Match(Section,@"(?m)^- Stage:\s*(.+)$");
        if(StageLine.Success){
            var StageIdMatch=Regex.Match(StageLine.Groups[1].Value,"`([^`]+)`");
            if(!StageIdMatch.Success){
                Errors.Add($"Implementation ledger feature {Feature.Id} has no machine-readable Stage ID.");
            } else {
                var StageId=StageIdMatch.Groups[1].Value;
                var Stage=Catalog.Stages.FirstOrDefault(Stage=>Stage.Id==StageId);
                if(Stage is null){ Errors.Add($"Implementation ledger feature {Feature.Id} references unknown stage {StageId}."); }
                else if(!Stage.FeatureIds.Contains(Feature.Id,StringComparer.Ordinal)){ Errors.Add($"Implementation ledger stage {StageId} does not own feature {Feature.Id}."); }
            }
        } else if(IsManaged){
            Errors.Add($"Implementation ledger feature {Feature.Id} has no Stage field.");
        }
        if(!IsManaged){ return; }
        if(!Section.Contains($"- Status: {Feature.Status}",StringComparison.Ordinal)){ Errors.Add($"Implementation ledger status disagrees for {Feature.Id}; expected {Feature.Status}."); }
        var DependencyLine=Regex.Match(Section,@"(?m)^- Depends on:\s*(.+)$");
        if(!DependencyLine.Success){
            Errors.Add($"Implementation ledger feature {Feature.Id} has no Depends on field.");
            return;
        }
        var DocumentedDependencies=Regex.Matches(DependencyLine.Groups[1].Value,@"F-L\d+-\d+").Select(static Match=>Match.Value).Distinct(StringComparer.Ordinal).OrderBy(static Id=>Id,StringComparer.Ordinal).ToArray();
        var CatalogDependencies=Feature.DependsOnFeatureIds.OrderBy(static Id=>Id,StringComparer.Ordinal).ToArray();
        if(!DocumentedDependencies.SequenceEqual(CatalogDependencies,StringComparer.Ordinal)){ Errors.Add($"Implementation ledger dependencies disagree for {Feature.Id}; expected [{string.Join(',',CatalogDependencies)}]."); }
    }
}
public static class ManualEvidenceLedgerValidator {
    public const string SchemaVersion="csharp-game-engine-manual-evidence-v1";
    public static IReadOnlyList<string>RequiredEntryIds{get;}=["vsix-experimental","hardware-d3d12","hardware-audio","release-package"];
    public static IReadOnlyList<string>Validate(string RepositoryRoot,bool RequirePassing,DateTimeOffset NowUtc){
        ArgumentException.ThrowIfNullOrWhiteSpace(RepositoryRoot);
        var Errors=new List<string>();
        var LedgerPath=Path.Combine(RepositoryRoot,"Docs","Verification","Manual-Evidence.json");
        if(!File.Exists(LedgerPath)){
            Errors.Add($"Manual evidence ledger does not exist: {LedgerPath}");
            return Errors;
        }
        ManualEvidenceLedgerDocument?Document;
        try {
            Document=JsonSerializer.Deserialize<ManualEvidenceLedgerDocument>(File.ReadAllText(LedgerPath),new JsonSerializerOptions(){PropertyNameCaseInsensitive=true});
        } catch(JsonException Ex){
            Errors.Add($"Manual evidence ledger is invalid JSON: {Ex.Message}");
            return Errors;
        }
        if(Document is null){
            Errors.Add("Manual evidence ledger is empty.");
            return Errors;
        }
        if(Document.SchemaVersion!=SchemaVersion){ Errors.Add($"Manual evidence ledger schema must be {SchemaVersion}."); }
        var Entries=Document.Entries??[];
        foreach(var Duplicate in Entries.GroupBy(static Entry=>Entry.Id,StringComparer.Ordinal).Where(static Group=>Group.Count()>1)){ Errors.Add($"Duplicate manual evidence entry: {Duplicate.Key}"); }
        foreach(var Entry in Entries){ ValidateEntry(RepositoryRoot,Entry,RequirePassing,NowUtc,Errors); }
        var EntryIds=Entries.Select(static Entry=>Entry.Id).ToHashSet(StringComparer.Ordinal);
        foreach(var RequiredEntryId in RequiredEntryIds.Where(RequiredEntryId=>!EntryIds.Contains(RequiredEntryId))){ Errors.Add($"Missing required manual evidence entry: {RequiredEntryId}"); }
        foreach(var UnknownEntryId in EntryIds.Where(EntryId=>!RequiredEntryIds.Contains(EntryId,StringComparer.Ordinal))){ Errors.Add($"Unknown manual evidence entry: {UnknownEntryId}"); }
        return Errors.Distinct(StringComparer.Ordinal).OrderBy(static Error=>Error,StringComparer.Ordinal).ToArray();
    }
    private static void ValidateEntry(string RepositoryRoot,ManualEvidenceEntry Entry,bool RequirePassing,DateTimeOffset NowUtc,List<string>Errors){
        if(string.IsNullOrWhiteSpace(Entry.Id)){ Errors.Add("Manual evidence entry has no ID."); }
        if(string.IsNullOrWhiteSpace(Entry.Requirement)){ Errors.Add($"Manual evidence entry {Entry.Id} has no requirement."); }
        if(string.IsNullOrWhiteSpace(Entry.Owner)){ Errors.Add($"Manual evidence entry {Entry.Id} has no owner."); }
        if(!Enum.TryParse<ManualEvidenceStatus>(Entry.Status,true,out var Status)){
            Errors.Add($"Manual evidence entry {Entry.Id} has unknown status {Entry.Status}.");
            return;
        }
        if(Status==ManualEvidenceStatus.Passed){
            if(string.IsNullOrWhiteSpace(Entry.EvidencePath)){ Errors.Add($"Passed manual evidence entry {Entry.Id} has no evidence path."); }
            else if(!Uri.TryCreate(Entry.EvidencePath,UriKind.Absolute,out _)&&!File.Exists(Path.Combine(RepositoryRoot,Entry.EvidencePath.Replace('/',Path.DirectorySeparatorChar)))){ Errors.Add($"Passed manual evidence entry {Entry.Id} references missing evidence {Entry.EvidencePath}."); }
            if(Entry.ExpiresUtc is null){ Errors.Add($"Passed manual evidence entry {Entry.Id} has no expiry."); }
            else if(Entry.ExpiresUtc<=NowUtc){ Errors.Add($"Passed manual evidence entry {Entry.Id} is expired."); }
        } else if(string.IsNullOrWhiteSpace(Entry.Reason)){
            Errors.Add($"Manual evidence entry {Entry.Id} with status {Status} has no reason.");
        }
        if(RequirePassing&&Status!=ManualEvidenceStatus.Passed){ Errors.Add($"Manual evidence entry {Entry.Id} cannot pass release with status {Status}."); }
    }
    private enum ManualEvidenceStatus { Pending,Passed,Failed }
    private sealed record ManualEvidenceLedgerDocument(string SchemaVersion,IReadOnlyList<ManualEvidenceEntry>?Entries);
    private sealed record ManualEvidenceEntry(string Id,string Requirement,string Owner,string Status,string?EvidencePath,DateTimeOffset?ExpiresUtc,string?Reason);
}
