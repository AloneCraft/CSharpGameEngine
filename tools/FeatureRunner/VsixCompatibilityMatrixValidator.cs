using System.Text.Json;
using System.Xml.Linq;

namespace CSharpGameEngine.FeatureStages;

public static class VsixCompatibilityMatrixValidator {
    public const string SchemaVersion="csharp-game-engine-vsix-compatibility-v1";
    private const string ExperimentalLedgerSchema="cge-vsix-experimental-baseline-v1";
    private const string ManualLedgerSchema="csharp-game-engine-manual-evidence-v1";
    private const string ExperimentalEvidenceSchema="cge-vsix-experimental-evidence-v1";
    private static readonly JsonSerializerOptions JsonOptions=new(){PropertyNameCaseInsensitive=true};

    public static IReadOnlyList<string>Validate(
        string RepositoryRoot,
        FeatureStageCatalog Catalog,
        string Configuration,
        bool RequirePassingEvidence,
        DateTimeOffset NowUtc){
        ArgumentException.ThrowIfNullOrWhiteSpace(RepositoryRoot);
        ArgumentNullException.ThrowIfNull(Catalog);
        if(Configuration is not("Debug" or "Release")){ throw new ArgumentException("Configuration must be Debug or Release.",nameof(Configuration)); }
        string Root=Path.GetFullPath(RepositoryRoot);
        string MatrixPath=Path.Combine(Root,"Docs","Verification","VSIX-Compatibility-Matrix.json");
        var Errors=new List<string>();
        VsixCompatibilityDocument?Document=ReadJson<VsixCompatibilityDocument>(MatrixPath,"VSIX compatibility matrix",Errors);
        if(Document is null){ return Errors; }
        if(Document.SchemaVersion!=SchemaVersion){ Errors.Add($"VSIX compatibility schema must be {SchemaVersion}."); }
        if(Document.FeatureId!="F-L8-028"){ Errors.Add("VSIX compatibility featureId must be F-L8-028."); }
        ValidatePackage(Root,Document.Package,Errors);
        ValidateVisualStudio(Document.VisualStudio,Errors);
        ValidateToolchain(Root,Document.Package,Document.Toolchain,Errors);
        ValidateCiMatrix(Root,Errors);
        ValidateAutomation(Root,Catalog,Document,Configuration,RequirePassingEvidence,NowUtc,Errors);
        ValidateCatalog(Catalog,Errors);
        return Errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static void ValidatePackage(string Root,VsixPackageContract?Contract,List<string>Errors){
        if(Contract is null){ Errors.Add("VSIX compatibility package contract is missing.");return; }
        if(Contract.TargetFramework!="net472"){ Errors.Add("VSIX package targetFramework must be net472."); }
        if(Contract.Architecture!="amd64"){ Errors.Add("VSIX package architecture must be amd64."); }
        if(Contract.NeutralLanguage!="en-US"){ Errors.Add("VSIX package neutralLanguage must be en-US."); }
        if(!ExactSet(Contract.SatelliteCultures,["ja-JP"])){ Errors.Add("VSIX package satelliteCultures must contain exactly ja-JP."); }
        if(Contract.FrameworkDependencyId!="Microsoft.Framework.NDP"||Contract.FrameworkDependencyVersion!="[4.7.2,)"){
            Errors.Add("VSIX package must require Microsoft.Framework.NDP [4.7.2,).");
        }
        string?ProjectPath=ResolveCheckedInPath(Root,Contract.ProjectPath,"VSIX project",Errors);
        string?ManifestPath=ResolveCheckedInPath(Root,Contract.SourceManifestPath,"VSIX source manifest",Errors);
        if(ProjectPath is null||ManifestPath is null){ return; }
        XDocument?Project=ReadXml(ProjectPath,"VSIX project",Errors);
        XDocument?Manifest=ReadXml(ManifestPath,"VSIX source manifest",Errors);
        if(Project is null||Manifest is null){ return; }
        RequireProjectProperty(Project,"TargetFramework",Contract.TargetFramework,"VSIX project",Errors);
        RequireProjectProperty(Project,"NeutralLanguage",Contract.NeutralLanguage,"VSIX project",Errors);
        RequireProjectProperty(Project,"VSSDKTargetPlatformRegRootSuffix","CgeFixed","VSIX project",Errors);
        string ProjectText=File.ReadAllText(ProjectPath);
        RequireToken(ProjectText,"ja-JP\\Extension.vsixlangpack","Japanese VSIX language pack",Errors);
        RequireToken(ProjectText,"VSPackage.ja-JP.resx","Japanese package resources",Errors);
        RequireToken(ProjectText,"<Culture>1033</Culture>","English template culture",Errors);
        RequireToken(ProjectText,"<Culture>1041</Culture>","Japanese template culture",Errors);

        XNamespace Namespace="http://schemas.microsoft.com/developer/vsx-schema/2011";
        XElement?RootElement=Manifest.Root;
        XElement?Identity=RootElement?.Element(Namespace+"Metadata")?.Element(Namespace+"Identity");
        if((string?)Identity?.Attribute("Language")!=Contract.NeutralLanguage){ Errors.Add($"VSIX source manifest language must be {Contract.NeutralLanguage}."); }
        XElement[]Targets=RootElement?.Element(Namespace+"Installation")?.Elements(Namespace+"InstallationTarget").ToArray()??[];
        if(Targets.Length!=1){ Errors.Add("VSIX source manifest must contain exactly one installation target."); }
        foreach(XElement Target in Targets){
            if((string?)Target.Attribute("Id")!="Microsoft.VisualStudio.Community"){ Errors.Add("VSIX installation target ID must be Microsoft.VisualStudio.Community."); }
            if((string?)Target.Attribute("Version")!="[17.14,)"){ Errors.Add("VSIX installation target version must be [17.14,)."); }
            if(Target.Element(Namespace+"ProductArchitecture")?.Value!=Contract.Architecture){ Errors.Add($"VSIX installation target architecture must be {Contract.Architecture}."); }
        }
        XElement[]Dependencies=RootElement?.Element(Namespace+"Dependencies")?.Elements(Namespace+"Dependency")
            .Where(Element=>(string?)Element.Attribute("Id")==Contract.FrameworkDependencyId).ToArray()??[];
        if(Dependencies.Length!=1||(string?)Dependencies[0].Attribute("Version")!=Contract.FrameworkDependencyVersion){ Errors.Add("VSIX source manifest framework dependency disagrees with the compatibility matrix."); }
        XElement[]Prerequisites=RootElement?.Element(Namespace+"Prerequisites")?.Elements(Namespace+"Prerequisite")
            .Where(Element=>(string?)Element.Attribute("Id")=="Microsoft.VisualStudio.Component.CoreEditor").ToArray()??[];
        if(Prerequisites.Length!=1||(string?)Prerequisites[0].Attribute("Version")!="[17.14,)"){ Errors.Add("VSIX source manifest Core Editor prerequisite must be [17.14,)."); }
    }

    private static void ValidateVisualStudio(VisualStudioContract?Contract,List<string>Errors){
        if(Contract is null){ Errors.Add("VSIX compatibility Visual Studio contract is missing.");return; }
        if(Contract.MinimumVersion!="17.14"){ Errors.Add("VSIX minimum Visual Studio version must be 17.14."); }
        string ExpectedRange=$"[{Contract.MinimumVersion},)";
        if(Contract.InstallationVersionRange!=ExpectedRange){ Errors.Add($"VSIX installationVersionRange must be {ExpectedRange}."); }
        if(!ExactSet(Contract.InstallationTargetIds,["Microsoft.VisualStudio.Community"])){ Errors.Add("VSIX installationTargetIds must contain exactly Microsoft.VisualStudio.Community."); }
        if(Contract.ProductArchitecture!="amd64"){ Errors.Add("VSIX Visual Studio productArchitecture must be amd64."); }
        if(Contract.CoreEditorPrerequisiteId!="Microsoft.VisualStudio.Component.CoreEditor"||Contract.CoreEditorPrerequisiteVersion!=ExpectedRange){
            Errors.Add($"VSIX core editor prerequisite must be Microsoft.VisualStudio.Component.CoreEditor {ExpectedRange}.");
        }
    }

    private static void ValidateToolchain(string Root,VsixPackageContract?Package,VsixToolchainContract?Contract,List<string>Errors){
        if(Package is null||Contract is null){ if(Contract is null){ Errors.Add("VSIX compatibility toolchain contract is missing."); }return; }
        string?ProjectPath=ResolveCheckedInPath(Root,Package.ProjectPath,"VSIX project",Errors);
        if(ProjectPath is null){ return; }
        XDocument?Project=ReadXml(ProjectPath,"VSIX project",Errors);
        if(Project is null){ return; }
        RequirePackageVersion(Project,"Microsoft.VisualStudio.SDK",Contract.VisualStudioSdkVersion,Errors);
        RequirePackageVersion(Project,"Microsoft.CodeAnalysis.CSharp",Contract.RoslynVersion,Errors);
        RequirePackageVersion(Project,"Microsoft.CodeAnalysis.CSharp.Workspaces",Contract.RoslynVersion,Errors);
        RequirePackageVersion(Project,"Microsoft.VisualStudio.LanguageServices",Contract.RoslynVersion,Errors);
    }

    private static void ValidateCiMatrix(string Root,List<string>Errors){
        string CiMatrixPath=Path.Combine(Root,"Docs","Verification","CI-Matrix.json");
        CiMatrixDocument?CiMatrix=ReadJson<CiMatrixDocument>(CiMatrixPath,"CI matrix",Errors);
        if(CiMatrix is null){ return; }
        CiMatrixCell?[]Cells=CiMatrix.Cells?.ToArray()??[];
        foreach(string Configuration in new[]{"Debug","Release"}){
            string Id=$"vsix-{Configuration.ToLowerInvariant()}";
            CiMatrixCell[]Matching=Cells.Where(Cell=>Cell is not null&&Cell.Id==Id).Select(static Cell=>Cell!).ToArray();
            if(Matching.Length!=1){ Errors.Add($"CI matrix must contain exactly one required VSIX cell {Id}.");continue; }
            CiMatrixCell Cell=Matching[0];
            if(Cell.Configuration!=Configuration||Cell.Workload!="vsix"||Cell.TargetFramework!="net472"||Cell.Adapter!="none"||!Cell.Required){
                Errors.Add($"CI matrix cell {Id} must be required {Configuration} vsix/net472 with adapter none.");
            }
        }
    }

    private static void ValidateAutomation(
        string Root,
        FeatureStageCatalog Catalog,
        VsixCompatibilityDocument Document,
        string Configuration,
        bool RequirePassingEvidence,
        DateTimeOffset NowUtc,
        List<string>Errors){
        VsixAutomationContract?Contract=Document.Automation;
        if(Contract is null){ Errors.Add("VSIX compatibility automation contract is missing.");return; }
        if(Contract.TargetFramework!="net472"){ Errors.Add("VSIX automation targetFramework must be net472."); }
        if(Contract.DevelopmentRootSuffix!="CgeFixed"){ Errors.Add("VSIX development root suffix must be CgeFixed."); }
        if(Contract.EvidenceRootSuffix!="CgeAutomation"){ Errors.Add("VSIX evidence root suffix must be CgeAutomation."); }
        if(Contract.Scenario!="shader-graph"){ Errors.Add("VSIX compatibility automation scenario must be shader-graph."); }
        if(Contract.LocaleMode!="auto"){ Errors.Add("VSIX compatibility locale mode must be auto."); }
        IReadOnlyList<string>DeclaredLocales=Contract.Locales??[];
        if(!ExactSet(DeclaredLocales,["1033","1041"])){ Errors.Add("VSIX compatibility locales must contain exactly 1033 and 1041."); }
        string?AutomationProjectPath=ResolveCheckedInPath(Root,Contract.ProjectPath,"VSIX automation project",Errors);
        if(AutomationProjectPath is not null){
            XDocument?AutomationProject=ReadXml(AutomationProjectPath,"VSIX automation project",Errors);
            if(AutomationProject is not null){ RequireProjectProperty(AutomationProject,"TargetFramework",Contract.TargetFramework,"VSIX automation project",Errors); }
        }
        FeatureStageDefinition?AutomationStage=Catalog.Stages.SingleOrDefault(static Stage=>Stage.Id=="vsix.experimental-automation");
        FeatureStageCommand?AutomationCommand=AutomationStage?.Commands.SingleOrDefault();
        if(AutomationStage is null||AutomationStage.Availability!=FeatureStageAvailability.Executable||AutomationCommand is null){
            Errors.Add("Executable vsix.experimental-automation Stage is missing.");
        } else {
            RequireArgument(AutomationCommand.Arguments,"--scenario",Contract.Scenario,Errors);
            RequireArgument(AutomationCommand.Arguments,"--root-suffix",Contract.EvidenceRootSuffix,Errors);
            RequireArgument(AutomationCommand.Arguments,"--locale",Contract.LocaleMode,Errors);
        }
        VsixCheckContract?[]Checks=Contract.Checks?.ToArray()??[];
        if(Checks.Any(static Check=>Check is null)){ Errors.Add("VSIX compatibility checks cannot contain null."); }
        VsixCheckContract[]DefinedChecks=Checks.Where(static Check=>Check is not null).Select(static Check=>Check!).ToArray();
        foreach(IGrouping<string,VsixCheckContract>Duplicate in DefinedChecks.GroupBy(static Check=>Check.Id,StringComparer.Ordinal).Where(static Group=>Group.Count()>1)){ Errors.Add($"Duplicate VSIX compatibility check: {Duplicate.Key}"); }
        if(!DefinedChecks.Any(static Check=>Check.Kind=="Automated")||!DefinedChecks.Any(static Check=>Check.Kind=="Manual")){ Errors.Add("VSIX compatibility checks must include Automated and Manual evidence."); }
        foreach(VsixCheckContract Check in DefinedChecks){
            if(string.IsNullOrWhiteSpace(Check.Id)||string.IsNullOrWhiteSpace(Check.Owner)){ Errors.Add("VSIX compatibility check IDs and owners must be non-empty."); }
            if(Check.Kind is not("Automated" or "Manual")){ Errors.Add($"VSIX compatibility check {Check.Id} has unknown kind {Check.Kind}."); }
            if(Check.Kind=="Automated"&&!DeclaredLocales.Contains(Check.Locale??string.Empty,StringComparer.Ordinal)){ Errors.Add($"Automated VSIX compatibility check {Check.Id} must bind a declared locale."); }
            if(Check.Kind=="Automated"&&Check.Configuration!="Release"){ Errors.Add($"Automated VSIX compatibility check {Check.Id} must bind Release evidence."); }
            if(Check.Kind=="Manual"&&(Check.Locale is not null||Check.Configuration is not null)){ Errors.Add($"Manual VSIX compatibility check {Check.Id} cannot claim an automated configuration or locale."); }
        }
        string?ExperimentalLedgerPath=ResolveCheckedInPath(Root,Contract.ExperimentalLedgerPath,"VSIX Experimental ledger",Errors);
        string?ManualLedgerPath=ResolveCheckedInPath(Root,Contract.ManualLedgerPath,"manual evidence ledger",Errors);
        if(ExperimentalLedgerPath is null||ManualLedgerPath is null){ return; }
        ExperimentalLedger?Experimental=ReadJson<ExperimentalLedger>(ExperimentalLedgerPath,"VSIX Experimental ledger",Errors);
        ManualLedger?Manual=ReadJson<ManualLedger>(ManualLedgerPath,"manual evidence ledger",Errors);
        if(Experimental is null||Manual is null){ return; }
        if(Experimental.SchemaVersion!=ExperimentalLedgerSchema){ Errors.Add($"VSIX Experimental ledger schema must be {ExperimentalLedgerSchema}."); }
        if(Manual.SchemaVersion!=ManualLedgerSchema){ Errors.Add($"Manual evidence ledger schema must be {ManualLedgerSchema}."); }
        ExperimentalCheck?[]LedgerChecks=Experimental.Checks?.ToArray()??[];
        if(LedgerChecks.Any(static Check=>Check is null)){ Errors.Add("VSIX Experimental ledger cannot contain null checks."); }
        ExperimentalCheck[]DefinedLedgerChecks=LedgerChecks.Where(static Check=>Check is not null).Select(static Check=>Check!).ToArray();
        foreach(IGrouping<string,ExperimentalCheck>Duplicate in DefinedLedgerChecks.GroupBy(static Check=>Check.Id,StringComparer.Ordinal).Where(static Group=>Group.Count()>1)){ Errors.Add($"Duplicate VSIX Experimental ledger check: {Duplicate.Key}"); }
        var ContractById=DefinedChecks.Where(static Check=>!string.IsNullOrWhiteSpace(Check.Id)).GroupBy(static Check=>Check.Id,StringComparer.Ordinal).ToDictionary(static Group=>Group.Key,static Group=>Group.First(),StringComparer.Ordinal);
        var LedgerById=DefinedLedgerChecks.Where(static Check=>!string.IsNullOrWhiteSpace(Check.Id)).GroupBy(static Check=>Check.Id,StringComparer.Ordinal).ToDictionary(static Group=>Group.Key,static Group=>Group.First(),StringComparer.Ordinal);
        foreach(string Missing in ContractById.Keys.Except(LedgerById.Keys,StringComparer.Ordinal)){ Errors.Add($"VSIX Experimental ledger is missing compatibility check {Missing}."); }
        foreach(string Extra in LedgerById.Keys.Except(ContractById.Keys,StringComparer.Ordinal)){ Errors.Add($"VSIX Experimental ledger has unregistered compatibility check {Extra}."); }
        foreach((string Id,VsixCheckContract CheckContract) in ContractById){
            if(!LedgerById.TryGetValue(Id,out ExperimentalCheck?LedgerCheck)){ continue; }
            if(LedgerCheck.Owner!=CheckContract.Owner){ Errors.Add($"VSIX Experimental check {Id} owner must be {CheckContract.Owner}."); }
            ValidateExperimentalCheck(Path.GetDirectoryName(ExperimentalLedgerPath)!,Document.VisualStudio,Contract,CheckContract,LedgerCheck,RequirePassingEvidence,NowUtc,Errors);
        }
        ManualEntry?[]ManualEntries=Manual.Entries?.ToArray()??[];
        if(ManualEntries.Any(static Entry=>Entry is null)){ Errors.Add("Manual evidence ledger cannot contain null entries."); }
        ManualEntry[]DefinedManualEntries=ManualEntries.Where(static Entry=>Entry is not null).Select(static Entry=>Entry!).ToArray();
        foreach(IGrouping<string,ManualEntry>Duplicate in DefinedManualEntries.GroupBy(static Entry=>Entry.Id,StringComparer.Ordinal).Where(static Group=>Group.Count()>1)){ Errors.Add($"Duplicate manual evidence entry: {Duplicate.Key}"); }
        ManualEntry?Summary=DefinedManualEntries.FirstOrDefault(Entry=>Entry.Id==Contract.ManualSummaryEntryId);
        if(Summary is null){ Errors.Add($"Manual evidence ledger is missing VSIX summary {Contract.ManualSummaryEntryId}."); }
        else {
            string ExpectedStatus=DefinedLedgerChecks.Length>0&&DefinedLedgerChecks.All(static Check=>Check.Status=="Passed")?"Passed":DefinedLedgerChecks.Any(static Check=>Check.Status=="Failed")?"Failed":"Pending";
            if(Summary.Status!=ExpectedStatus){ Errors.Add($"Manual VSIX summary status must be {ExpectedStatus} while detailed Experimental checks have their current states."); }
            if(RequirePassingEvidence&&Summary.Status!="Passed"){ Errors.Add("Release VSIX compatibility requires the manual VSIX summary to be Passed."); }
        }
    }

    private static void ValidateExperimentalCheck(
        string LedgerDirectory,
        VisualStudioContract?VisualStudio,
        VsixAutomationContract Automation,
        VsixCheckContract Contract,
        ExperimentalCheck Check,
        bool RequirePassingEvidence,
        DateTimeOffset NowUtc,
        List<string>Errors){
        if(Check.Status is not("Pending" or "Passed" or "Failed")){ Errors.Add($"VSIX Experimental check {Check.Id} has unknown status {Check.Status}.");return; }
        if(RequirePassingEvidence&&Contract.RequiredForRelease&&Check.Status!="Passed"){ Errors.Add($"Release VSIX compatibility requires check {Check.Id} to be Passed."); }
        if(Check.Status=="Pending"){ return; }
        if(Check.ObservedUtc is null){ Errors.Add($"VSIX Experimental check {Check.Id} requires observedUtc."); }
        if(string.IsNullOrWhiteSpace(Check.EvidencePath)){ Errors.Add($"VSIX Experimental check {Check.Id} requires evidencePath.");return; }
        if(Check.Status=="Passed"){
            if(Check.ExpiresUtc is null){ Errors.Add($"Passed VSIX Experimental check {Check.Id} requires expiresUtc."); }
            else if(RequirePassingEvidence&&Check.ExpiresUtc<=NowUtc){ Errors.Add($"Release VSIX compatibility requires unexpired evidence for check {Check.Id} (expired {Check.ExpiresUtc:u})."); }
            if(Check.ObservedUtc is not null&&Check.ExpiresUtc is not null&&Check.ExpiresUtc<=Check.ObservedUtc){ Errors.Add($"VSIX Experimental check {Check.Id} expiry must follow observation."); }
        }
        string EvidencePath=Path.IsPathRooted(Check.EvidencePath)?Path.GetFullPath(Check.EvidencePath):Path.GetFullPath(Path.Combine(LedgerDirectory,Check.EvidencePath));
        if(!File.Exists(EvidencePath)){
            // 実行の証拠は未追跡の .artifacts ルートにあるため、新規チェックアウトには存在しない (ADR-0061)。鮮度と同様に、
            // 必須とするのはリリース検証だけであり、通常のビルドでは記録済みの結果を保持する。
            if(RequirePassingEvidence){ Errors.Add($"VSIX Experimental check {Check.Id} evidence does not exist: {Check.EvidencePath}"); }
            return;
        }
        if(Contract.Kind!="Automated"||Check.Status!="Passed"){ return; }
        ExperimentalEvidence?Evidence=ReadJson<ExperimentalEvidence>(EvidencePath,$"VSIX Experimental evidence {Check.Id}",Errors);
        if(Evidence is null){ return; }
        if(Evidence.SchemaVersion!=ExperimentalEvidenceSchema){ Errors.Add($"VSIX Experimental evidence {Check.Id} schema must be {ExperimentalEvidenceSchema}."); }
        if(Evidence.Status!="Passed"){ Errors.Add($"VSIX Experimental evidence {Check.Id} status must be Passed."); }
        if(Evidence.Configuration!=Contract.Configuration){ Errors.Add($"VSIX Experimental evidence {Check.Id} configuration must be {Contract.Configuration}."); }
        if(Evidence.RootSuffix!=Automation.EvidenceRootSuffix){ Errors.Add($"VSIX Experimental evidence {Check.Id} rootSuffix must be {Automation.EvidenceRootSuffix}."); }
        if(Evidence.Locale!=Contract.Locale){ Errors.Add($"VSIX Experimental evidence {Check.Id} locale must be {Contract.Locale}."); }
        if(!Version.TryParse(Evidence.VisualStudioVersion,out Version?ObservedVersion)||!Version.TryParse(VisualStudio?.MinimumVersion,out Version?MinimumVersion)||ObservedVersion<MinimumVersion){ Errors.Add($"VSIX Experimental evidence {Check.Id} does not prove Visual Studio {VisualStudio?.MinimumVersion}+."); }
        foreach((string Name,string Hash) in new[]{("package",Evidence.PackageSha256),("package DLL",Evidence.PackageDllSha256),("build DLL",Evidence.BuildDllSha256),("installed DLL",Evidence.InstalledDllSha256)}){
            if(!IsSha256(Hash)){ Errors.Add($"VSIX Experimental evidence {Check.Id} {Name} hash is invalid."); }
        }
        if(Evidence.Assertions is null||Evidence.Assertions.Count==0||Evidence.Assertions.Any(static Assertion=>Assertion is null||!Assertion.Passed)){ Errors.Add($"VSIX Experimental evidence {Check.Id} must contain only passing assertions."); }
    }

    private static void ValidateCatalog(FeatureStageCatalog Catalog,List<string>Errors){
        FeatureDefinition?Feature=Catalog.Features.SingleOrDefault(static Feature=>Feature.Id=="F-L8-028");
        if(Feature?.Status!=FeatureLifecycleStatus.Implemented){ Errors.Add("F-L8-028 must be Implemented when the VSIX compatibility validator is active."); }
        FeatureStageDefinition?Stage=Catalog.Stages.SingleOrDefault(static Stage=>Stage.Id=="vsix.compatibility");
        if(Stage?.Availability!=FeatureStageAvailability.Executable){ Errors.Add("vsix.compatibility Stage must be executable."); }
    }

    private static T?ReadJson<T>(string PathValue,string Name,List<string>Errors){
        if(!File.Exists(PathValue)){ Errors.Add($"{Name} is missing: {PathValue}");return default; }
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(PathValue),JsonOptions); }
        catch(Exception Ex)when(Ex is JsonException or IOException or UnauthorizedAccessException){ Errors.Add($"{Name} cannot be read: {Ex.Message}");return default; }
    }
    private static XDocument?ReadXml(string PathValue,string Name,List<string>Errors){
        try { return XDocument.Load(PathValue); }
        catch(Exception Ex)when(Ex is IOException or UnauthorizedAccessException or System.Xml.XmlException){ Errors.Add($"{Name} cannot be read: {Ex.Message}");return null; }
    }
    private static string?ResolveCheckedInPath(string Root,string?RelativePath,string Name,List<string>Errors){
        if(string.IsNullOrWhiteSpace(RelativePath)||Path.IsPathRooted(RelativePath)){ Errors.Add($"{Name} path must be a repository-relative path.");return null; }
        string Resolved=Path.GetFullPath(Path.Combine(Root,RelativePath.Replace('/',Path.DirectorySeparatorChar)));
        string RootPrefix=Root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!Resolved.StartsWith(RootPrefix,StringComparison.OrdinalIgnoreCase)){ Errors.Add($"{Name} path escapes the repository.");return null; }
        if(!File.Exists(Resolved)){ Errors.Add($"{Name} is missing: {RelativePath}");return null; }
        return Resolved;
    }
    private static void RequireProjectProperty(XDocument Project,string Name,string Expected,string ProjectName,List<string>Errors){
        string?Actual=Project.Descendants().FirstOrDefault(Element=>Element.Name.LocalName==Name)?.Value;
        if(Actual!=Expected){ Errors.Add($"{ProjectName} {Name} must be {Expected}."); }
    }
    private static void RequirePackageVersion(XDocument Project,string PackageId,string Expected,List<string>Errors){
        XElement[]References=Project.Descendants().Where(Element=>Element.Name.LocalName=="PackageReference"&&(string?)Element.Attribute("Include")==PackageId).ToArray();
        if(References.Length!=1||(string?)References[0].Attribute("Version")!=Expected){ Errors.Add($"VSIX package {PackageId} version must be {Expected}."); }
    }
    private static void RequireArgument(IReadOnlyList<string>Arguments,string Name,string Expected,List<string>Errors){
        int Index=-1;
        for(int Candidate=0;Candidate<Arguments.Count;Candidate++){
            if(Arguments[Candidate]==Name){ Index=Candidate;break; }
        }
        if(Index<0||Index+1>=Arguments.Count||Arguments[Index+1]!=Expected){ Errors.Add($"vsix.experimental-automation {Name} must be {Expected}."); }
    }
    private static void RequireToken(string Text,string Token,string Name,List<string>Errors){
        if(!Text.Contains(Token,StringComparison.Ordinal)){ Errors.Add($"VSIX project is missing {Name}."); }
    }
    private static bool ExactSet(IReadOnlyList<string>?Actual,IReadOnlyList<string>Expected)=>Actual is not null&&Actual.Count==Expected.Count&&Actual.ToHashSet(StringComparer.Ordinal).SetEquals(Expected);
    private static bool IsSha256(string?Value)=>Value?.Length==64&&Value.All(static Character=>(Character>='0'&&Character<='9')||(Character>='a'&&Character<='f'));

    private sealed record VsixCompatibilityDocument(string SchemaVersion,string FeatureId,VsixPackageContract?Package,VisualStudioContract?VisualStudio,VsixToolchainContract?Toolchain,VsixAutomationContract?Automation);
    private sealed record VsixPackageContract(string ProjectPath,string SourceManifestPath,string TargetFramework,string Architecture,string NeutralLanguage,IReadOnlyList<string>?SatelliteCultures,string FrameworkDependencyId,string FrameworkDependencyVersion);
    private sealed record VisualStudioContract(string MinimumVersion,string InstallationVersionRange,IReadOnlyList<string>?InstallationTargetIds,string ProductArchitecture,string CoreEditorPrerequisiteId,string CoreEditorPrerequisiteVersion);
    private sealed record VsixToolchainContract(string VisualStudioSdkVersion,string RoslynVersion);
    private sealed record VsixAutomationContract(string ProjectPath,string TargetFramework,string DevelopmentRootSuffix,string EvidenceRootSuffix,string Scenario,string LocaleMode,IReadOnlyList<string>?Locales,string ExperimentalLedgerPath,string ManualLedgerPath,string ManualSummaryEntryId,IReadOnlyList<VsixCheckContract?>?Checks);
    private sealed record VsixCheckContract(string Id,string Owner,string Kind,string?Configuration,string?Locale,bool RequiredForRelease);
    private sealed record ExperimentalLedger(string SchemaVersion,IReadOnlyList<ExperimentalCheck?>?Checks);
    private sealed record ExperimentalCheck(string Id,string Owner,string Status,string?EvidencePath,DateTimeOffset?ObservedUtc,DateTimeOffset?ExpiresUtc);
    private sealed record ManualLedger(string SchemaVersion,IReadOnlyList<ManualEntry?>?Entries);
    private sealed record ManualEntry(string Id,string Owner,string Status);
    private sealed record ExperimentalEvidence(string SchemaVersion,string Configuration,string Locale,string RootSuffix,string Status,string VisualStudioVersion,string PackageSha256,string PackageDllSha256,string BuildDllSha256,string InstalledDllSha256,IReadOnlyList<ExperimentalAssertion?>?Assertions);
    private sealed record ExperimentalAssertion(string Id,bool Passed);
    private sealed record CiMatrixDocument(IReadOnlyList<CiMatrixCell?>?Cells);
    private sealed record CiMatrixCell(string Id,string Configuration,string Workload,string TargetFramework,string Adapter,bool Required);
}
