using CSharpGameEngine.FeatureStages;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class VsixReleaseEvidenceTests:IDisposable {
    private readonly string Root=Path.Combine(
        Path.GetTempPath(),"CgeVsixReleaseTests",Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MissingStrictCompatibilityAndLifecycleEvidenceFailsClosed(){
        Directory.CreateDirectory(this.Root);
        var Error=new StringWriter();

        int ExitCode=await VsixReleaseEvidence.RunAsync(
            this.Root,"Release",TextWriter.Null,Error,CancellationToken.None);

        Assert.Equal(1,ExitCode);
        Assert.NotEmpty(Error.ToString());
        string EvidenceRoot=Path.Combine(this.Root,".artifacts","VsixRelease");
        Assert.False(Directory.Exists(EvidenceRoot)
            &&Directory.EnumerateFileSystemEntries(EvidenceRoot).Any());
    }

    [Fact]
    public async Task RunPublishesOneSelfValidatingExactPackageClosure(){
        string ReportPath=await this.CreateEvidenceAsync();
        VsixReleaseReport Report=ReadReport(ReportPath);

        Assert.Empty(await VsixReleaseEvidence.ValidateAsync(this.Root,ReportPath));
        Assert.Equal("Release",Report.Configuration);
        Assert.Equal(7,Report.Observations.Count);
        Assert.Equal(2,Report.Observations.Count(static Item=>Item.Kind=="Automated"));
        Assert.Equal(5,Report.Observations.Count(static Item=>Item.Kind=="Manual"));
        Assert.All(Report.Observations,Item=>
            Assert.Equal(Report.Package.PackageSha256,Item.PackageSha256));
        Assert.Equal("CgeRelease",Report.Lifecycle.RootSuffix);
        Assert.Equal(0,Report.Lifecycle.RemainingInstallations);
        Assert.Equal(
            VsixReleaseEvidence.RequiredRepositoryInputPaths.Count(),Report.Inputs.Count);
    }

    [Fact]
    public async Task ChangedPackageLifecycleStageOrInputFailsValidation(){
        string ReportPath=await this.CreateEvidenceAsync();
        VsixReleaseReport Report=ReadReport(ReportPath);
        await File.AppendAllTextAsync(Resolve(this.Root,Report.Package.PackagePath),"changed");
        await File.AppendAllTextAsync(Resolve(this.Root,Report.Lifecycle.EvidencePath)," ");
        await File.AppendAllTextAsync(Resolve(this.Root,Report.Compatibility.ReportPath)," ");
        await File.AppendAllTextAsync(Resolve(this.Root,Report.Inputs[^1].Path),"changed");

        IReadOnlyList<string>Errors=await VsixReleaseEvidence.ValidateAsync(
            this.Root,ReportPath);

        Assert.Contains(Errors,Error=>Error.Contains("package",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains("lifecycle evidence file",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains("compatibility result file",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains("input changed",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ChangedObservationOrExtraReleaseFileFailsValidation(){
        string ReportPath=await this.CreateEvidenceAsync();
        VsixReleaseReport Report=ReadReport(ReportPath);
        await File.AppendAllTextAsync(Resolve(this.Root,Report.Observations[0].EvidencePath)," ");
        await File.WriteAllTextAsync(
            Path.Combine(Path.GetDirectoryName(ReportPath)!,"extra.txt"),"extra");

        IReadOnlyList<string>Errors=await VsixReleaseEvidence.ValidateAsync(
            this.Root,ReportPath);

        Assert.Contains(Errors,Error=>Error.Contains("observations",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains("missing or extra",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OrdinaryCompatibilityProfileCannotSubstituteForReleasePrerequisite(){
        this.CreateInputsAndEvidence();
        await this.SaveCompatibilityStageAsync("full");
        var Error=new StringWriter();

        int ExitCode=await VsixReleaseEvidence.RunAsync(
            this.Root,"Release",TextWriter.Null,Error,CancellationToken.None);

        Assert.Equal(1,ExitCode);
        Assert.Contains(VsixReleaseEvidence.CompatibilityProfile,Error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void LifecycleCaptureScriptIsIsolatedAndFailsClosed(){
        string Root=FeatureStageRepositoryLocator.Find();
        string Script=File.ReadAllText(Resolve(
            Root,"tools/CSharpGameEngine.VSExtension/Capture-CgeVsixReleaseLifecycle.ps1"));

        Assert.Contains("CgeRelease",Script,StringComparison.Ordinal);
        Assert.Contains("-WindowStyle Hidden",Script,StringComparison.Ordinal);
        Assert.Contains("/uninstall:",Script,StringComparison.Ordinal);
        Assert.Contains("finally",Script,StringComparison.Ordinal);
        Assert.Contains("remainingInstallations",Script,StringComparison.Ordinal);
        Assert.Contains("vsix-lifecycle.json",Script,StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManualObservationRecorderRequiresExactAssertionsAndRollsBack(){
        this.CreateInputsAndEvidence();
        string LedgerPath=Resolve(
            this.Root,"Docs/Verification/VSIX-Experimental-Baseline.json");
        byte[]Before=File.ReadAllBytes(LedgerPath);
        string InstalledDllPath=Resolve(
            this.Root,
            "tools/CSharpGameEngine.VSExtension/bin/Release/net472/CSharpGameEngine.VSExtension.dll");

        await Assert.ThrowsAsync<ArgumentException>(()=>
            VsixObservationLedger.RecordManualAsync(
                this.Root,"scene-designer",InstalledDllPath,
                [new("document-open","Only one assertion was observed.")],
                DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddDays(7)));

        Assert.Equal(Before,File.ReadAllBytes(LedgerPath));
    }

    [Fact]
    public async Task ManualObservationRecorderBindsCurrentPackageAndUpdatesLedger(){
        this.CreateInputsAndEvidence();
        string InstalledDllPath=Resolve(
            this.Root,
            "tools/CSharpGameEngine.VSExtension/bin/Release/net472/CSharpGameEngine.VSExtension.dll");
        DateTimeOffset ObservedUtc=DateTimeOffset.UtcNow.AddMinutes(-1);
        DateTimeOffset ExpiresUtc=ObservedUtc.AddDays(7);
        VsixObservationAssertion[]Assertions=[
            .. VsixObservationLedger.GetRequiredManualAssertionIds("scene-designer")
                .Select(Id=>new VsixObservationAssertion(Id,$"Observed {Id}.")),
        ];

        string Relative=await VsixObservationLedger.RecordManualAsync(
            this.Root,"scene-designer",InstalledDllPath,Assertions,
            ObservedUtc,ExpiresUtc);

        Assert.True(File.Exists(Resolve(this.Root,Relative)));
        JsonNode Ledger=JsonNode.Parse(File.ReadAllText(Resolve(
            this.Root,"Docs/Verification/VSIX-Experimental-Baseline.json")))!;
        JsonNode Row=Ledger["checks"]!.AsArray().Single(Item=>
            Item!["id"]!.GetValue<string>()=="scene-designer")!;
        Assert.Equal("Passed",Row["status"]!.GetValue<string>());
        Assert.Equal(ObservedUtc,Row["observedUtc"]!.GetValue<DateTimeOffset>());
        Assert.Empty(VsixCompatibilityMatrixValidator.Validate(
            this.Root,FeatureStageCatalog.CreateDefault(),"Release",false,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task AutomatedObservationRegistrationRejectsDifferentPackage(){
        this.CreateInputsAndEvidence();
        string EvidencePath=Path.Combine(
            this.Root,".artifacts","VsixObservations","shader-graph-1041.json");
        JsonNode Evidence=JsonNode.Parse(File.ReadAllText(EvidencePath))!;
        Evidence["packageSha256"]=new string('a',64);
        File.WriteAllText(EvidencePath,Evidence.ToJsonString());
        string LedgerPath=Resolve(
            this.Root,"Docs/Verification/VSIX-Experimental-Baseline.json");
        byte[]Before=File.ReadAllBytes(LedgerPath);

        await Assert.ThrowsAsync<InvalidDataException>(()=>
            VsixObservationLedger.RegisterAutomatedAsync(
                this.Root,"shader-graph-1041",EvidencePath,
                DateTimeOffset.UtcNow.AddDays(7),DateTimeOffset.UtcNow));

        Assert.Equal(Before,File.ReadAllBytes(LedgerPath));
    }

    [Fact]
    public void ReleaseNeverFallsBackToMutableBinWithoutFrozenInput(){
        Directory.CreateDirectory(this.Root);

        Assert.Throws<FileNotFoundException>(()=>
            VsixReleaseEvidence.ReadCurrentPackage(this.Root,"Release"));
    }

    [Fact]
    public void FrozenInputRemainsCurrentAfterItsSourceBinChanges(){
        this.CreateInputsAndEvidence();
        VsixReleasePackage Before=VsixReleaseEvidence.ReadCurrentPackage(
            this.Root,"Release");
        File.AppendAllText(
            Resolve(this.Root,
                "tools/CSharpGameEngine.VSExtension/bin/Release/net472/"
                +"CSharpGameEngine.VSExtension.vsix"),
            "mutable-bin-change");

        VsixReleasePackage After=VsixReleaseEvidence.ReadCurrentPackage(
            this.Root,"Release");

        Assert.Equal(Before.PackageSha256,After.PackageSha256);
        Assert.StartsWith(
            ".artifacts/VsixReleaseInputs/",After.PackagePath,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FrozenInputWhosePackageIsNotInTheCheckoutSaysHowToFreezeOne(){
        // 入力ドキュメントは追跡対象だが、パッケージはローカルの .artifacts ストアにある（新規クローンには含まれない）。
        this.CreateInputsAndEvidence();
        Directory.Delete(Path.Combine(this.Root,".artifacts","VsixReleaseInputs"),true);

        var Missing=Assert.Throws<FileNotFoundException>(()=>
            VsixReleaseEvidence.ReadCurrentPackage(this.Root,"Release"));

        Assert.Contains(".artifacts/VsixReleaseInputs/",Missing.Message,StringComparison.Ordinal);
        Assert.Contains("freeze-vsix-release-input",Missing.Message,StringComparison.Ordinal);
    }

    [Fact]
    public void CorruptFrozenInputIsRejected(){
        this.CreateInputsAndEvidence();
        VsixReleasePackage Package=VsixReleaseEvidence.ReadCurrentPackage(
            this.Root,"Release");
        File.AppendAllText(Resolve(this.Root,Package.PackagePath),"corrupt");

        Assert.Throws<InvalidDataException>(()=>
            VsixReleaseEvidence.ReadCurrentPackage(this.Root,"Release"));
    }

    public void Dispose(){
        if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,true); }
    }

    private async Task<string>CreateEvidenceAsync(){
        this.CreateInputsAndEvidence();
        await this.SaveCompatibilityStageAsync(VsixReleaseEvidence.CompatibilityProfile);
        var Error=new StringWriter();
        int ExitCode=await VsixReleaseEvidence.RunAsync(
            this.Root,"Release",TextWriter.Null,Error,CancellationToken.None);
        Assert.True(ExitCode==0,Error.ToString());
        return Assert.Single(Directory.EnumerateFiles(
            Path.Combine(this.Root,".artifacts","VsixRelease"),
            VsixReleaseEvidence.ReportFileName,SearchOption.AllDirectories));
    }

    private void CreateInputsAndEvidence(){
        string SourceRoot=FeatureStageRepositoryLocator.Find();
        foreach(string Relative in VsixReleaseEvidence.RequiredRepositoryInputPaths){
            if(Relative==VsixReleaseInputStore.ActiveRelativePath){ continue; }
            string Destination=Resolve(this.Root,Relative);
            Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
            File.Copy(Resolve(SourceRoot,Relative),Destination,true);
        }
        string OutputRoot=Path.Combine(
            this.Root,"tools","CSharpGameEngine.VSExtension","bin","Release","net472");
        Directory.CreateDirectory(OutputRoot);
        byte[]DllBytes=Enumerable.Range(0,512).Select(Index=>(byte)(Index%251)).ToArray();
        string BuildDllPath=Path.Combine(OutputRoot,"CSharpGameEngine.VSExtension.dll");
        File.WriteAllBytes(BuildDllPath,DllBytes);
        string PackagePath=Path.Combine(OutputRoot,"CSharpGameEngine.VSExtension.vsix");
        using(FileStream Stream=File.Create(PackagePath))
        using(var Archive=new ZipArchive(Stream,ZipArchiveMode.Create,false)){
            WriteEntry(Archive,"CSharpGameEngine.VSExtension.dll",DllBytes);
            WriteEntry(Archive,"extension.vsixmanifest",File.ReadAllBytes(Resolve(
                this.Root,"tools/CSharpGameEngine.VSExtension/source.extension.vsixmanifest")));
        }
        _=VsixReleaseInputStore.PublishCurrentAsync(
            this.Root,DateTimeOffset.UtcNow.AddMinutes(-11)).GetAwaiter().GetResult();
        string PackageHash=Hash(File.ReadAllBytes(PackagePath));
        string DllHash=Hash(DllBytes);
        DateTimeOffset ObservedUtc=DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTimeOffset ExpiresUtc=DateTimeOffset.UtcNow.AddDays(7);
        string MatrixPath=Resolve(
            this.Root,"Docs/Verification/VSIX-Compatibility-Matrix.json");
        JsonNode Matrix=JsonNode.Parse(File.ReadAllText(MatrixPath))!;
        Dictionary<string,(string Kind,string? Locale)>Contracts=Matrix["automation"]!["checks"]!
            .AsArray().ToDictionary(
                Item=>Item!["id"]!.GetValue<string>(),
                Item=>(Item!["kind"]!.GetValue<string>(),Item["locale"]?.GetValue<string>()),
                StringComparer.Ordinal);
        string LedgerPath=Resolve(
            this.Root,"Docs/Verification/VSIX-Experimental-Baseline.json");
        JsonNode Ledger=JsonNode.Parse(File.ReadAllText(LedgerPath))!;
        string ObservationRoot=Path.Combine(this.Root,".artifacts","VsixObservations");
        Directory.CreateDirectory(ObservationRoot);
        foreach(JsonNode?Check in Ledger["checks"]!.AsArray()){
            string Id=Check!["id"]!.GetValue<string>();
            (string Kind,string? Locale)=Contracts[Id];
            string EvidencePath=Path.Combine(ObservationRoot,Id+".json");
            Check["status"]="Passed";
            Check["evidencePath"]="../../.artifacts/VsixObservations/"+Id+".json";
            Check["observedUtc"]=ObservedUtc;
            Check["expiresUtc"]=ExpiresUtc;
            object Evidence=Kind=="Automated"
                ?new {
                    schemaVersion="cge-vsix-experimental-evidence-v1",
                    observedUtc=ObservedUtc,
                    configuration="Release",locale=Locale,rootSuffix="CgeAutomation",
                    status="Passed",visualStudioVersion="17.14.0",
                    packageSha256=PackageHash,packageDllSha256=DllHash,
                    buildDllSha256=DllHash,installedDllSha256=DllHash,
                    assertions=new[]{new{id="fixture",passed=true,detail="passed"}},
                }
                :new {
                    schemaVersion=VsixReleaseEvidence.ManualEvidenceSchema,checkId=Id,
                    status="Passed",observedUtc=ObservedUtc,expiresUtc=ExpiresUtc,
                    packageSha256=PackageHash,packageDllSha256=DllHash,
                    buildDllSha256=DllHash,installedDllSha256=DllHash,
                    assertions=new[]{new{id="fixture",passed=true,detail="observed"}},
                };
            WriteJson(EvidencePath,Evidence);
        }
        File.WriteAllText(LedgerPath,Ledger.ToJsonString());
        string ManualPath=Resolve(this.Root,"Docs/Verification/Manual-Evidence.json");
        JsonNode Manual=JsonNode.Parse(File.ReadAllText(ManualPath))!;
        JsonNode Summary=Manual["entries"]!.AsArray().Single(Item=>
            Item!["id"]!.GetValue<string>()=="vsix-experimental")!;
        Summary["status"]="Passed";
        Summary["evidence"]="../../.artifacts/VsixObservations";
        Summary["observedAtUtc"]=ObservedUtc;
        Summary["expiresAtUtc"]=ExpiresUtc;
        Summary.AsObject().Remove("reason");
        File.WriteAllText(ManualPath,Manual.ToJsonString());
        string LifecycleRoot=Path.Combine(
            this.Root,".artifacts","VsixReleaseLifecycle","fixture");
        Directory.CreateDirectory(LifecycleRoot);
        WriteJson(Path.Combine(LifecycleRoot,"vsix-lifecycle.json"),new {
            schemaVersion=VsixReleaseEvidence.LifecycleSchema,configuration="Release",
            extensionId="CSharpGameEngine.VSExtension.aec17804-e2e4-46e7-b373-de11af816203",
            rootSuffix="CgeRelease",startedUtc=ObservedUtc,finishedUtc=ObservedUtc.AddMinutes(2),
            packageSha256=PackageHash,packageDllSha256=DllHash,
            buildDllSha256=DllHash,installedDllSha256=DllHash,
            installExitCode=0,uninstallExitCode=0,remainingInstallations=0,
            assertions=new[]{
                new{id="install",passed=true,detail="exact package installed"},
                new{id="uninstall",passed=true,detail="no installation remains"},
            },
        });
    }

    private async Task SaveCompatibilityStageAsync(string Profile){
        FeatureStageDefinition Stage=FeatureStageCatalog.CreateDefault()
            .GetStage("vsix.compatibility");
        var Options=new FeatureStageExecutionOptions(
            this.Root,"Release",Profile,FeatureAdapter.Warp,
            Stage.Seed,Stage.Ticks,"unknown",true);
        FeatureStageAssertion[]Assertions=[.. Stage.Commands.Select(
            (Command,Index)=>new FeatureStageAssertion(
                $"command:{Index}",Command.FileName,true))];
        FeatureStageRunResult Result=FeatureStageRunResult.Create(
            Stage,Options,FeatureStageRunStatus.Passed,Stage.Oracle,
            Assertions,null,null);
        var Store=new FeatureRunResultStore(this.Root);
        _=await Store.SaveAsync(
            FeatureRunResultDocument.Create(Profile,[Result]),CancellationToken.None);
    }

    private static void WriteEntry(ZipArchive Archive,string Name,byte[]Bytes){
        ZipArchiveEntry Entry=Archive.CreateEntry(Name,CompressionLevel.NoCompression);
        using Stream Stream=Entry.Open();Stream.Write(Bytes);
    }

    private static void WriteJson<T>(string PathValue,T Value){
        File.WriteAllBytes(PathValue,JsonSerializer.SerializeToUtf8Bytes(Value,new JsonSerializerOptions {
            PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
            WriteIndented=true,
        }));
    }

    private static VsixReleaseReport ReadReport(string PathValue)=>
        JsonSerializer.Deserialize<VsixReleaseReport>(File.ReadAllBytes(PathValue),
            new JsonSerializerOptions { PropertyNamingPolicy=JsonNamingPolicy.CamelCase })
        ??throw new InvalidDataException("VSIX release report is empty.");
    private static string Resolve(string Root,string Relative)=>Path.Combine(
        Root,Relative.Replace('/',Path.DirectorySeparatorChar));
    private static string Hash(byte[]Bytes)=>
        Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();
}
