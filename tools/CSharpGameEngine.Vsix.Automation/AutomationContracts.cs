using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace CSharpGameEngine.Vsix.Automation;
public enum AutomationExitCode { Success=0,OracleMismatch=1,InfrastructureError=2 }
public enum AutomationCommand { ValidateLedger,Run }
public sealed class AutomationParseResult {
    internal AutomationParseResult(AutomationOptions?Options,string Error) { this.Options=Options;this.Error=Error; }
    public bool Success=>this.Options is not null;
    public AutomationOptions?Options { get; }
    public string Error { get; }
}
public sealed class AutomationOptions {
    private AutomationOptions() { }
    public AutomationCommand Command { get; private set; }
    public string ManifestPath { get; private set; }=string.Empty;
    public string Scenario { get; private set; }=string.Empty;
    public string Configuration { get; private set; }=string.Empty;
    public string RootSuffix { get; private set; }=string.Empty;
    public int TimeoutSeconds { get; private set; }
    public string EvidenceDirectory { get; private set; }=string.Empty;
    public string Locale { get; private set; }="auto";
    public static AutomationParseResult Parse(IReadOnlyList<string>Arguments) {
        if(Arguments.Count==0) { return Fail("A command is required."); }
        var Values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        for(var Index=1;Index<Arguments.Count;Index+=2) {
            if(Index+1>=Arguments.Count||!Arguments[Index].StartsWith("--",StringComparison.Ordinal)) { return Fail("Options must use --name value pairs."); }
            Values[Arguments[Index].Substring(2)]=Arguments[Index+1];
        }
        if(string.Equals(Arguments[0],"validate-ledger",StringComparison.OrdinalIgnoreCase)) {
            if(!Values.TryGetValue("manifest",out var Manifest)||string.IsNullOrWhiteSpace(Manifest)) { return Fail("--manifest is required."); }
            return Success(new AutomationOptions { Command=AutomationCommand.ValidateLedger,ManifestPath=Manifest });
        }
        if(!string.Equals(Arguments[0],"run",StringComparison.OrdinalIgnoreCase)) { return Fail("Unknown command."); }
        Values.TryGetValue("scenario",out var Scenario);
        Values.TryGetValue("configuration",out var Configuration);
        Values.TryGetValue("root-suffix",out var RootSuffix);
        Values.TryGetValue("timeout-seconds",out var Timeout);
        Values.TryGetValue("evidence-dir",out var EvidenceDirectory);
        Values.TryGetValue("locale",out var Locale);
        if(!string.Equals(Scenario,"shader-graph",StringComparison.Ordinal)) { return Fail("Only the shader-graph scenario is supported."); }
        if(Configuration!="Debug"&&Configuration!="Release") { return Fail("--configuration must be Debug or Release."); }
        if(!string.Equals(RootSuffix,"CgeAutomation",StringComparison.Ordinal)) { return Fail("Automation is restricted to the CgeAutomation root suffix."); }
        if(!int.TryParse(Timeout,NumberStyles.None,CultureInfo.InvariantCulture,out var TimeoutSeconds)||TimeoutSeconds<30||TimeoutSeconds>7200) { return Fail("--timeout-seconds must be from 30 through 7200."); }
        if(string.IsNullOrWhiteSpace(EvidenceDirectory)) { return Fail("--evidence-dir is required."); }
        if(string.IsNullOrWhiteSpace(Locale)) { Locale="auto"; }
        if(Locale!="auto"&&Locale!="1033"&&Locale!="1041") { return Fail("--locale must be auto, 1033, or 1041."); }
        return Success(new AutomationOptions { Command=AutomationCommand.Run,Scenario=Scenario!,Configuration=Configuration!,RootSuffix=RootSuffix!,TimeoutSeconds=TimeoutSeconds,EvidenceDirectory=EvidenceDirectory!,Locale=Locale! });
    }
    private static AutomationParseResult Fail(string Error)=>new AutomationParseResult(null,Error);
    private static AutomationParseResult Success(AutomationOptions Options)=>new AutomationParseResult(Options,string.Empty);
}
public sealed class LedgerValidationResult {
    internal LedgerValidationResult(IReadOnlyList<string>Errors,int PassedCount,int PendingCount,int FailedCount) { this.Errors=Errors;this.PassedCount=PassedCount;this.PendingCount=PendingCount;this.FailedCount=FailedCount; }
    public bool IsValid=>this.Errors.Count==0;
    public IReadOnlyList<string>Errors { get; }
    public int PassedCount { get; }
    public int PendingCount { get; }
    public int FailedCount { get; }
}
public static class VsixExperimentalLedgerValidator {
    public static LedgerValidationResult Validate(string ManifestPath,DateTimeOffset Now) {
        var Errors=new List<string>();
        VsixExperimentalLedger?Ledger;
        try { Ledger=JsonSerializer.Deserialize<VsixExperimentalLedger>(File.ReadAllText(ManifestPath),JsonOptions); } catch(Exception Exception) { return new LedgerValidationResult(["Ledger cannot be read: "+Exception.Message],0,0,0); }
        if(Ledger is null||Ledger.SchemaVersion!="cge-vsix-experimental-baseline-v1") { Errors.Add("Unknown ledger schemaVersion."); }
        var Checks=Ledger?.Checks??[];
        var Duplicate=Checks.GroupBy(Check=>Check.Id,StringComparer.Ordinal).FirstOrDefault(Group=>string.IsNullOrWhiteSpace(Group.Key)||Group.Count()!=1);
        if(Duplicate is not null) { Errors.Add("Ledger check IDs must be non-empty and unique."); }
        foreach(var Check in Checks) {
            if(string.IsNullOrWhiteSpace(Check.Owner)) { Errors.Add(Check.Id+": owner is required."); }
            if(Check.Status=="Pending") { continue; }
            if(Check.Status!="Passed"&&Check.Status!="Failed") { Errors.Add(Check.Id+": unknown status.");continue; }
            if(!Check.ObservedUtc.HasValue) { Errors.Add(Check.Id+": observedUtc is required."); }
            if(string.IsNullOrWhiteSpace(Check.EvidencePath)) { Errors.Add(Check.Id+": evidencePath is required."); }
            else {
                var EvidencePath=Path.IsPathRooted(Check.EvidencePath)?Check.EvidencePath:Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ManifestPath))!,Check.EvidencePath));
                if(!File.Exists(EvidencePath)) { Errors.Add(Check.Id+": evidence does not exist."); }
            }
            if(Check.Status=="Passed") {
                if(!Check.ExpiresUtc.HasValue) { Errors.Add(Check.Id+": expiresUtc is required for Passed."); }
                else if(Check.ExpiresUtc.Value<=Now) { Errors.Add(Check.Id+": Passed evidence is expired."); }
                if(Check.ObservedUtc.HasValue&&Check.ExpiresUtc.HasValue&&Check.ExpiresUtc<=Check.ObservedUtc) { Errors.Add(Check.Id+": expiry must follow observation."); }
            }
        }
        return new LedgerValidationResult(Errors,Checks.Count(Check=>Check.Status=="Passed"),Checks.Count(Check=>Check.Status=="Pending"),Checks.Count(Check=>Check.Status=="Failed"));
    }
    private static readonly JsonSerializerOptions JsonOptions=new JsonSerializerOptions { PropertyNameCaseInsensitive=true };
}
public sealed class VsixExperimentalLedger {
    [JsonPropertyName("schemaVersion")]public string SchemaVersion { get; set; }=string.Empty;
    [JsonPropertyName("checks")]public List<VsixExperimentalLedgerCheck>Checks { get; set; }=[];
}
public sealed class VsixExperimentalLedgerCheck {
    [JsonPropertyName("id")]public string Id { get; set; }=string.Empty;
    [JsonPropertyName("owner")]public string Owner { get; set; }=string.Empty;
    [JsonPropertyName("status")]public string Status { get; set; }="Pending";
    [JsonPropertyName("evidencePath")]public string?EvidencePath { get; set; }
    [JsonPropertyName("observedUtc")]public DateTimeOffset?ObservedUtc { get; set; }
    [JsonPropertyName("expiresUtc")]public DateTimeOffset?ExpiresUtc { get; set; }
}
public sealed class VisualStudioCandidate {
    public VisualStudioCandidate(string InstallationPath,string ProductPath,string Version) { this.InstallationPath=InstallationPath;this.ProductPath=ProductPath;this.Version=Version; }
    public string InstallationPath { get; }
    public string ProductPath { get; }
    public string Version { get; }
}
public static class VisualStudioCandidateSelector {
    public static VisualStudioCandidate?Select(IEnumerable<VisualStudioCandidate>Candidates,Func<string,bool>FileExists)=>Candidates.Where(Candidate=>
        Candidate.ProductPath.EndsWith("devenv.exe",StringComparison.OrdinalIgnoreCase)&&
        Candidate.InstallationPath.IndexOf("SQL Server Management Studio",StringComparison.OrdinalIgnoreCase)<0&&
        FileExists(Candidate.ProductPath)).OrderByDescending(Candidate=>ParseVersion(Candidate.Version)).FirstOrDefault();
    private static Version ParseVersion(string Value)=>Version.TryParse(Value,out var VersionValue)?VersionValue:new Version();
}
public static class RootSuffixProcessPolicy {
    private static readonly Regex AutomationHive=new Regex(@"(?:^|\s)/rootsuffix\s+(?:""CgeAutomation""|CgeAutomation)(?=\s|$)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    public static bool IsOwnedAutomationHive(string?CommandLine)=>!string.IsNullOrWhiteSpace(CommandLine)&&AutomationHive.IsMatch(CommandLine);
}
public static class OwnedProcessShutdownPolicy {
    public static bool CanForceTerminate(int LaunchedProcessId,int CandidateProcessId,bool GracePeriodExpired)=>
        GracePeriodExpired&&LaunchedProcessId>0&&CandidateProcessId==LaunchedProcessId;
}
public sealed class VsixExperimentalEvidence {
    [JsonPropertyName("schemaVersion")]public string SchemaVersion { get; set; }="cge-vsix-experimental-evidence-v1";
    [JsonPropertyName("runId")]public string RunId { get; set; }=string.Empty;
    [JsonPropertyName("observedUtc")]public DateTimeOffset ObservedUtc { get; set; }=DateTimeOffset.UtcNow;
    [JsonPropertyName("configuration")]public string Configuration { get; set; }=string.Empty;
    [JsonPropertyName("locale")]public string Locale { get; set; }=string.Empty;
    [JsonPropertyName("rootSuffix")]public string RootSuffix { get; set; }=string.Empty;
    [JsonPropertyName("status")]public string Status { get; set; }=string.Empty;
    [JsonPropertyName("visualStudioVersion")]public string VisualStudioVersion { get; set; }=string.Empty;
    [JsonPropertyName("processId")]public int ProcessId { get; set; }
    [JsonPropertyName("forcedTermination")]public bool ForcedTermination { get; set; }
    [JsonPropertyName("packageSha256")]public string PackageSha256 { get; set; }=string.Empty;
    [JsonPropertyName("packageDllSha256")]public string PackageDllSha256 { get; set; }=string.Empty;
    [JsonPropertyName("buildDllSha256")]public string BuildDllSha256 { get; set; }=string.Empty;
    [JsonPropertyName("installedDllSha256")]public string InstalledDllSha256 { get; set; }=string.Empty;
    [JsonPropertyName("activityLogPath")]public string ActivityLogPath { get; set; }=string.Empty;
    [JsonPropertyName("screenshotPath")]public string ScreenshotPath { get; set; }=string.Empty;
    [JsonPropertyName("assertions")]public List<VsixEvidenceAssertion>Assertions { get; set; }=[];
}
public sealed class VsixEvidenceAssertion {
    [JsonPropertyName("id")]public string Id { get; set; }=string.Empty;
    [JsonPropertyName("passed")]public bool Passed { get; set; }
    [JsonPropertyName("detail")]public string Detail { get; set; }=string.Empty;
}
public static class VsixEvidenceWriter {
    private static readonly JsonSerializerOptions Options=new JsonSerializerOptions { WriteIndented=true };
    public static string Write(string PathValue,VsixExperimentalEvidence Evidence) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(PathValue))!);
        File.WriteAllText(PathValue,JsonSerializer.Serialize(Evidence,Options)+Environment.NewLine);
        return ComputeSha256(PathValue);
    }
    public static string ComputeSha256(string PathValue) {
        using(var Stream=File.OpenRead(PathValue))using(var Sha=SHA256.Create()) { return string.Concat(Sha.ComputeHash(Stream).Select(Byte=>Byte.ToString("x2",CultureInfo.InvariantCulture))); }
    }
}
