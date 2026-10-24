using System.Text.Json;
namespace CSharpGameEngine.FeatureStages;
public sealed class FeatureRunnerApplication {
    private readonly FeatureStageCatalog Catalog;
    private readonly IFeatureStageExecutor Executor;
    private readonly IFeatureRunResultStore ResultStore;
    private readonly IChangedPathProvider ChangedPathProvider;
    private readonly TextWriter Output;
    private readonly TextWriter Error;
    public FeatureRunnerApplication(FeatureStageCatalog Catalog,IFeatureStageExecutor Executor,IFeatureRunResultStore ResultStore,IChangedPathProvider ChangedPathProvider,TextWriter Output,TextWriter Error){
        this.Catalog=Catalog;
        this.Executor=Executor;
        this.ResultStore=ResultStore;
        this.ChangedPathProvider=ChangedPathProvider;
        this.Output=Output;
        this.Error=Error;
    }
    public async Task<int>RunAsync(string[]Args,CancellationToken CancellationToken){
        if(Args.Length==0||IsHelp(Args[0])){
            this.WriteHelp();
            return 0;
        }
        try {
            return Args[0].ToLowerInvariant()switch {
                "list"=>this.RunList(Args),
                "export-showcase"=>this.RunExportShowcase(Args),
                "verify-coverage"=>this.RunVerifyCoverage(Args),
                "verify-manual-ledger"=>this.RunVerifyManualLedger(Args),
                "verify-goldens"=>this.RunVerifyGoldens(Args),
                "verify-performance-framework"=>await this.RunVerifyPerformanceFrameworkAsync(Args,CancellationToken).ConfigureAwait(false),
                "verify-rendering-performance"=>await this.RunVerifyRenderingPerformanceAsync(Args,CancellationToken).ConfigureAwait(false),
                "verify-rendering-release"=>await this.RunVerifyRenderingReleaseAsync(Args,CancellationToken).ConfigureAwait(false),
                "verify-thirdperson-character"=>await this.RunVerifyThirdPersonCharacterAsync(Args,CancellationToken).ConfigureAwait(false),
                "verify-template-package"=>await this.RunVerifyTemplatePackageAsync(Args,CancellationToken).ConfigureAwait(false),
                "verify-plugin-package"=>await this.RunVerifyPluginPackageAsync(Args,CancellationToken).ConfigureAwait(false),
                "verify-content-performance"=>await this.RunVerifyContentPerformanceAsync(Args,CancellationToken).ConfigureAwait(false),
                "verify-ecs-performance"=>await this.RunVerifyEcsPerformanceAsync(Args,CancellationToken).ConfigureAwait(false),
                "verify-ecs-release"=>await this.RunVerifyEcsReleaseAsync(Args,CancellationToken).ConfigureAwait(false),
                "verify-content-release"=>await this.RunVerifyContentReleaseAsync(Args,CancellationToken).ConfigureAwait(false),
                "verify-release-package"=>await this.RunVerifyReleasePackageAsync(Args,CancellationToken).ConfigureAwait(false),
                "verify-release-candidate"=>await this.RunVerifyReleaseCandidateAsync(Args,CancellationToken).ConfigureAwait(false),
                "run-release-candidate"=>await this.RunReleaseCandidateAsync(Args,CancellationToken).ConfigureAwait(false),
                "verify-release-manifest"=>await this.RunVerifyReleaseManifestAsync(Args,CancellationToken).ConfigureAwait(false),
                "verify-ci-matrix"=>this.RunVerifyCiMatrix(Args),
                "verify-release-compatibility"=>this.RunVerifyReleaseCompatibility(Args),
                "verify-platform-compatibility"=>this.RunVerifyPlatformCompatibility(Args),
                "verify-platform-release"=>await this.RunVerifyPlatformReleaseAsync(Args,CancellationToken).ConfigureAwait(false),
                "verify-vsix-compatibility"=>this.RunVerifyVsixCompatibility(Args),
                "verify-vsix-release"=>await this.RunVerifyVsixReleaseAsync(Args,CancellationToken).ConfigureAwait(false),
                "register-vsix-automated-evidence"=>await this.RunRegisterVsixAutomatedEvidenceAsync(Args,CancellationToken).ConfigureAwait(false),
                "record-vsix-manual-evidence"=>await this.RunRecordVsixManualEvidenceAsync(Args,CancellationToken).ConfigureAwait(false),
                "freeze-vsix-release-input"=>await this.RunFreezeVsixReleaseInputAsync(Args,CancellationToken).ConfigureAwait(false),
                "verify-failure-policy"=>this.RunVerifyFailurePolicy(Args),
                "verify-stress-contract"=>await this.RunVerifyStressContractAsync(Args,CancellationToken).ConfigureAwait(false),
                "verify-clean-build"=>await this.RunVerifyCleanBuildAsync(Args,CancellationToken).ConfigureAwait(false),
                "run"=>await this.RunStageAsync(Args,CancellationToken).ConfigureAwait(false),
                "run-feature"=>await this.RunFeatureAsync(Args,CancellationToken).ConfigureAwait(false),
                "suite"=>await this.RunSuiteAsync(Args,CancellationToken).ConfigureAwait(false),
                "resume"=>await this.ResumeAsync(Args,CancellationToken).ConfigureAwait(false),
                _=>this.UnknownCommand(Args[0]),
            };
        } catch(OperationCanceledException)when(CancellationToken.IsCancellationRequested){
            await this.Error.WriteLineAsync("Feature run was cancelled.").ConfigureAwait(false);
            return 130;
        } catch(Exception Ex)when(Ex is ArgumentException or FormatException or KeyNotFoundException or InvalidDataException or DirectoryNotFoundException){
            await this.Error.WriteLineAsync(Ex.Message).ConfigureAwait(false);
            return 2;
        }
    }
    private int RunExportShowcase(string[]Args){
        if(Args.Length!=3||!Args[1].Equals("--output",StringComparison.OrdinalIgnoreCase)){ throw new ArgumentException("export-showcase requires --output <path>."); }
        ShowcaseManifestExporter.Export(this.Catalog,Args[2]);
        this.Output.WriteLine(Path.GetFullPath(Args[2]));
        return 0;
    }
    private int RunVerifyCoverage(string[]Args){
        var Options=ParseVerificationOptions(Args,false);
        var Errors=FeatureCoverageValidator.Validate(Options.RepositoryRoot,this.Catalog);
        return this.WriteVerificationResult("Feature coverage",Errors);
    }
    private int RunVerifyManualLedger(string[]Args){
        var Options=ParseVerificationOptions(Args,true);
        var Errors=ManualEvidenceLedgerValidator.Validate(Options.RepositoryRoot,Options.RequirePassing,DateTimeOffset.UtcNow);
        return this.WriteVerificationResult(Options.RequirePassing?"Release manual evidence":"Manual evidence ledger",Errors);
    }
    private int RunVerifyGoldens(string[]Args){
        var Options=ParseVerificationOptions(Args,false);
        var Errors=GoldenArtifactPolicyValidator.Validate(Options.RepositoryRoot);
        return this.WriteVerificationResult("Golden artifact policy",Errors);
    }
    private Task<int>RunVerifyPerformanceFrameworkAsync(string[]Args,CancellationToken CancellationToken){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        string Configuration="Debug";
        for(int Index=1;Index<Args.Length;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Length){ throw new ArgumentException($"Missing value for {Argument}."); }
            string Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--repository-root": RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);break;
                case "--configuration" when Value is "Debug" or "Release": Configuration=Value;break;
                case "--configuration": throw new ArgumentException("Configuration must be Debug or Release.");
                default: throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        return PerformanceFrameworkProbe.RunAsync(RepositoryRoot,Configuration,this.Output,this.Error,CancellationToken);
    }
    private Task<int>RunVerifyContentPerformanceAsync(string[]Args,CancellationToken CancellationToken){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        string Configuration="Debug";
        for(int Index=1;Index<Args.Length;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Length){ throw new ArgumentException($"Missing value for {Argument}."); }
            string Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--repository-root": RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);break;
                case "--configuration" when Value is "Debug" or "Release": Configuration=Value;break;
                case "--configuration": throw new ArgumentException("Configuration must be Debug or Release.");
                default: throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        return ContentPerformanceProbe.RunAsync(
            RepositoryRoot,Configuration,this.Output,this.Error,CancellationToken);
    }
    private Task<int>RunVerifyEcsPerformanceAsync(string[]Args,CancellationToken CancellationToken){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        string Configuration="Debug";
        for(int Index=1;Index<Args.Length;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Length){ throw new ArgumentException($"Missing value for {Argument}."); }
            string Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--repository-root": RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);break;
                case "--configuration" when Value is "Debug" or "Release": Configuration=Value;break;
                case "--configuration": throw new ArgumentException("Configuration must be Debug or Release.");
                default: throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        return EcsPerformanceProbe.RunAsync(
            RepositoryRoot,Configuration,this.Output,this.Error,CancellationToken);
    }
    private Task<int>RunVerifyEcsReleaseAsync(string[]Args,CancellationToken CancellationToken){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        string Configuration="Debug";
        for(int Index=1;Index<Args.Length;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Length){ throw new ArgumentException($"Missing value for {Argument}."); }
            string Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--repository-root": RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);break;
                case "--configuration" when Value is "Debug" or "Release": Configuration=Value;break;
                case "--configuration": throw new ArgumentException("Configuration must be Debug or Release.");
                default: throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        return EcsReleaseEvidence.RunAsync(
            RepositoryRoot,Configuration,this.Output,this.Error,CancellationToken);
    }
    private Task<int>RunVerifyRenderingPerformanceAsync(string[]Args,CancellationToken CancellationToken){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        string Configuration="Debug";
        string Adapter="warp";
        for(int Index=1;Index<Args.Length;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Length){ throw new ArgumentException($"Missing value for {Argument}."); }
            string Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--repository-root": RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);break;
                case "--configuration" when Value is "Debug" or "Release": Configuration=Value;break;
                case "--configuration": throw new ArgumentException("Configuration must be Debug or Release.");
                case "--adapter" when Value is "warp" or "hardware": Adapter=Value;break;
                case "--adapter": throw new ArgumentException("Adapter must be warp or hardware.");
                default: throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        return RenderingPerformanceProbe.RunAsync(
            RepositoryRoot,Configuration,Adapter,this.Output,this.Error,CancellationToken);
    }
    private Task<int>RunVerifyRenderingReleaseAsync(string[]Args,CancellationToken CancellationToken){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        string Configuration="Debug";
        for(int Index=1;Index<Args.Length;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Length){ throw new ArgumentException($"Missing value for {Argument}."); }
            string Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--repository-root": RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);break;
                case "--configuration" when Value is "Debug" or "Release": Configuration=Value;break;
                case "--configuration": throw new ArgumentException("Configuration must be Debug or Release.");
                default: throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        return RenderingReleaseEvidence.RunAsync(
            RepositoryRoot,Configuration,this.Output,this.Error,CancellationToken);
    }
    private Task<int>RunVerifyTemplatePackageAsync(string[]Args,CancellationToken CancellationToken){
        // --template is read here and removed, so the shared parser keeps rejecting unknown options.
        var Template="ThirdPerson";
        List<string>Rest=[];
        for(int Index=0;Index<Args.Length;Index++){
            if(Index>0&&string.Equals(Args[Index],"--template",StringComparison.OrdinalIgnoreCase)){
                if(Index+1>=Args.Length){ throw new ArgumentException("Missing value for --template."); }
                Template=Args[++Index] is "ThirdPerson" or "FirstPerson" or "TopDown" or "Vehicle"?Args[Index]:throw new ArgumentException("Template must be ThirdPerson, FirstPerson, TopDown or Vehicle.");
                continue;
            }
            Rest.Add(Args[Index]);
        }
        var (RepositoryRoot,Configuration,Adapter)=ParseTemplateRunArguments([.. Rest]);
        return TemplatePackageVerification.RunAsync(RepositoryRoot,Configuration,Adapter,this.Output,this.Error,CancellationToken,Template);
    }
    private Task<int>RunVerifyPluginPackageAsync(string[]Args,CancellationToken CancellationToken){
        var (RepositoryRoot,Configuration,Adapter)=ParseTemplateRunArguments(Args);
        return PluginPackageVerification.RunAsync(RepositoryRoot,Configuration,Adapter,this.Output,this.Error,CancellationToken);
    }
    private static (string RepositoryRoot,string Configuration,string Adapter)ParseTemplateRunArguments(string[]Args){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        string Configuration="Debug";
        string Adapter="warp";
        for(int Index=1;Index<Args.Length;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Length){ throw new ArgumentException($"Missing value for {Argument}."); }
            string Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--repository-root": RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);break;
                case "--configuration" when Value is "Debug" or "Release": Configuration=Value;break;
                case "--configuration": throw new ArgumentException("Configuration must be Debug or Release.");
                case "--adapter" when Value is "warp" or "hardware": Adapter=Value;break;
                case "--adapter": throw new ArgumentException("Adapter must be warp or hardware.");
                default: throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        return (RepositoryRoot,Configuration,Adapter);
    }
    private Task<int>RunVerifyThirdPersonCharacterAsync(string[]Args,CancellationToken CancellationToken){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        string Configuration="Debug";
        string Adapter="warp";
        string Template="ThirdPerson";
        for(int Index=1;Index<Args.Length;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Length){ throw new ArgumentException($"Missing value for {Argument}."); }
            string Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--repository-root": RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);break;
                case "--configuration" when Value is "Debug" or "Release": Configuration=Value;break;
                case "--configuration": throw new ArgumentException("Configuration must be Debug or Release.");
                case "--adapter" when Value is "warp" or "hardware": Adapter=Value;break;
                case "--adapter": throw new ArgumentException("Adapter must be warp or hardware.");
                case "--template" when Value is "ThirdPerson" or "FirstPerson" or "TopDown" or "Vehicle": Template=Value;break;
                case "--template": throw new ArgumentException("Template must be ThirdPerson, FirstPerson, TopDown or Vehicle.");
                default: throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        return ThirdPersonCharacterVerification.RunAsync(
            RepositoryRoot,Configuration,Adapter,this.Output,this.Error,CancellationToken,Template);
    }
    private Task<int>RunVerifyContentReleaseAsync(string[]Args,CancellationToken CancellationToken){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        string Configuration="Debug";
        for(int Index=1;Index<Args.Length;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Length){ throw new ArgumentException($"Missing value for {Argument}."); }
            string Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--repository-root": RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);break;
                case "--configuration" when Value is "Debug" or "Release": Configuration=Value;break;
                case "--configuration": throw new ArgumentException("Configuration must be Debug or Release.");
                default: throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        return ContentReleaseEvidence.RunAsync(
            RepositoryRoot,Configuration,this.Output,this.Error,CancellationToken);
    }
    private Task<int>RunVerifyReleasePackageAsync(string[]Args,CancellationToken CancellationToken){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        string Configuration="Debug";
        for(int Index=1;Index<Args.Length;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Length){ throw new ArgumentException($"Missing value for {Argument}."); }
            string Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--repository-root": RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);break;
                case "--configuration" when Value is "Debug" or "Release": Configuration=Value;break;
                case "--configuration": throw new ArgumentException("Configuration must be Debug or Release.");
                default: throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        return ReleasePackageEvidence.RunAsync(
            RepositoryRoot,Configuration,this.Output,this.Error,CancellationToken);
    }
    private Task<int>RunVerifyReleaseCandidateAsync(
        string[]Args,CancellationToken CancellationToken){
        ReleaseCandidateOptions Options=ParseReleaseCandidateOptions(Args);
        return ReleaseCandidateEvidence.RunAsync(
            Options.RepositoryRoot,this.Catalog,Options.ReachedLevel,
            ReleaseCandidateEvidence.ProductTracks,this.Output,this.Error,CancellationToken);
    }
    private async Task<int>RunReleaseCandidateAsync(
        string[]Args,CancellationToken CancellationToken){
        ReleaseCandidateOptions Options=ParseReleaseCandidateOptions(Args);
        ReleaseCandidateScope Scope=ReleaseCandidateEvidence.SelectScope(
            this.Catalog,Options.ReachedLevel,ReleaseCandidateEvidence.ProductTracks);
        FeatureStageDefinition[]Stages=[.. Scope.RequiredStageIds
            .Select(this.Catalog.GetStage)
            .OrderBy(Stage=>Stage.FeatureIds
                .Where(Scope.RequiredFeatureIds.Contains)
                .Select(FeatureId=>this.Catalog.GetFeature(FeatureId).DependencyLevel)
                .DefaultIfEmpty(0).Max())
            .ThenBy(static Stage=>Stage.Id,StringComparer.Ordinal)];
        var Parsed=new ParsedExecutionOptions(
            Options.RepositoryRoot,"Release",
            ReleaseCandidateEvidence.PrerequisiteProfile,FeatureAdapter.Warp,null,null);
        int PrerequisiteExitCode=await this.ExecuteAndStoreAsync(
            Stages,Parsed,CancellationToken).ConfigureAwait(false);
        if(PrerequisiteExitCode!=0){ return PrerequisiteExitCode; }
        return await ReleaseCandidateEvidence.RunAsync(
            Options.RepositoryRoot,this.Catalog,Options.ReachedLevel,
            ReleaseCandidateEvidence.ProductTracks,this.Output,this.Error,CancellationToken)
            .ConfigureAwait(false);
    }
    private Task<int>RunVerifyReleaseManifestAsync(
        string[]Args,CancellationToken CancellationToken){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        for(int Index=1;Index<Args.Length;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Length){
                throw new ArgumentException($"Missing value for {Argument}.");
            }
            string Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--repository-root":
                    RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);
                    break;
                case "--configuration" when Value=="Release":
                    break;
                case "--configuration":
                    throw new ArgumentException(
                        "Formal release configuration must be Release.");
                default:
                    throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        return ReleaseManifestEvidence.RunAsync(
            RepositoryRoot,this.Catalog,this.Output,this.Error,CancellationToken);
    }
    private int RunVerifyCiMatrix(string[]Args){
        var Options=ParseVerificationOptions(Args,false);
        var Errors=CiMatrixValidator.Validate(Options.RepositoryRoot,this.Catalog);
        return this.WriteVerificationResult("CI matrix",Errors);
    }
    private int RunVerifyReleaseCompatibility(string[]Args){
        var Options=ParseVerificationOptions(Args,false);
        var Errors=ReleaseCompatibilityMatrixValidator.Validate(
            Options.RepositoryRoot,this.Catalog,DateTimeOffset.UtcNow);
        return this.WriteVerificationResult("Release compatibility matrix",Errors);
    }
    private int RunVerifyPlatformCompatibility(string[]Args){
        var Options=ParseVerificationOptions(Args,false);
        var Errors=PlatformCompatibilityMatrixValidator.Validate(Options.RepositoryRoot,this.Catalog);
        return this.WriteVerificationResult("Platform compatibility matrix",Errors);
    }
    private Task<int>RunVerifyPlatformReleaseAsync(string[]Args,CancellationToken CancellationToken){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        string Configuration="Debug";
        for(int Index=1;Index<Args.Length;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Length){ throw new ArgumentException($"Missing value for {Argument}."); }
            string Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--repository-root": RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);break;
                case "--configuration" when Value is "Debug" or "Release": Configuration=Value;break;
                case "--configuration": throw new ArgumentException("Configuration must be Debug or Release.");
                default: throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        return PlatformReleaseEvidence.RunAsync(RepositoryRoot,Configuration,this.Output,this.Error,CancellationToken);
    }
    private int RunVerifyVsixCompatibility(string[]Args){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        string Configuration="Debug";
        bool RequirePassingEvidence=false;
        for(int Index=1;Index<Args.Length;Index++){
            string Argument=Args[Index];
            if(Argument.Equals("--require-passing-evidence",StringComparison.OrdinalIgnoreCase)){
                RequirePassingEvidence=true;
                continue;
            }
            if(Index+1>=Args.Length){ throw new ArgumentException($"Missing value for {Argument}."); }
            string Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--repository-root": RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);break;
                case "--configuration" when Value is "Debug" or "Release": Configuration=Value;break;
                case "--configuration": throw new ArgumentException("Configuration must be Debug or Release.");
                default: throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        var Errors=VsixCompatibilityMatrixValidator.Validate(RepositoryRoot,this.Catalog,Configuration,RequirePassingEvidence,DateTimeOffset.UtcNow);
        return this.WriteVerificationResult(RequirePassingEvidence?"Release VSIX compatibility":"VSIX compatibility matrix",Errors);
    }
    private Task<int>RunVerifyVsixReleaseAsync(string[]Args,CancellationToken CancellationToken){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        string Configuration="Release";
        for(int Index=1;Index<Args.Length;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Length){ throw new ArgumentException($"Missing value for {Argument}."); }
            string Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--repository-root": RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);break;
                case "--configuration" when Value=="Release": Configuration=Value;break;
                case "--configuration": throw new ArgumentException("VSIX release configuration must be Release.");
                default: throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        return VsixReleaseEvidence.RunAsync(
            RepositoryRoot,Configuration,this.Output,this.Error,CancellationToken);
    }
    private async Task<int>RunRegisterVsixAutomatedEvidenceAsync(
        string[]Args,CancellationToken CancellationToken){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        string?CheckId=null;
        string?EvidencePath=null;
        int ValidDays=7;
        for(int Index=1;Index<Args.Length;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Length){ throw new ArgumentException($"Missing value for {Argument}."); }
            string Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--repository-root": RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);break;
                case "--check": CheckId=Value;break;
                case "--evidence": EvidencePath=Value;break;
                case "--valid-days" when int.TryParse(Value,out int Days)
                    &&Days is>=1 and<=14: ValidDays=Days;break;
                case "--valid-days": throw new ArgumentException("Validity must be 1 through 14 days.");
                default: throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        if(string.IsNullOrWhiteSpace(CheckId)||string.IsNullOrWhiteSpace(EvidencePath)){
            throw new ArgumentException("--check and --evidence are required.");
        }
        DateTimeOffset NowUtc=DateTimeOffset.UtcNow;
        string Registered=await VsixObservationLedger.RegisterAutomatedAsync(
            RepositoryRoot,CheckId,EvidencePath,NowUtc.AddDays(ValidDays),
            NowUtc,CancellationToken).ConfigureAwait(false);
        await this.Output.WriteLineAsync(
            $"Registered automated VSIX evidence: {Registered}").ConfigureAwait(false);
        return 0;
    }
    private async Task<int>RunRecordVsixManualEvidenceAsync(
        string[]Args,CancellationToken CancellationToken){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        string?CheckId=null;
        string?InstalledDllPath=null;
        int ValidDays=7;
        var Assertions=new List<VsixObservationAssertion>();
        for(int Index=1;Index<Args.Length;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Length){ throw new ArgumentException($"Missing value for {Argument}."); }
            string Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--repository-root": RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);break;
                case "--check": CheckId=Value;break;
                case "--installed-dll": InstalledDllPath=Value;break;
                case "--assert":{
                    int Separator=Value.IndexOf('=');
                    if(Separator<=0||Separator==Value.Length-1){
                        throw new ArgumentException("Assertions must use <id>=<observed detail>.");
                    }
                    Assertions.Add(new VsixObservationAssertion(
                        Value[..Separator],Value[(Separator+1)..]));
                    break;
                }
                case "--valid-days" when int.TryParse(Value,out int Days)
                    &&Days is>=1 and<=14: ValidDays=Days;break;
                case "--valid-days": throw new ArgumentException("Validity must be 1 through 14 days.");
                default: throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        if(string.IsNullOrWhiteSpace(CheckId)||string.IsNullOrWhiteSpace(InstalledDllPath)){
            throw new ArgumentException("--check and --installed-dll are required.");
        }
        DateTimeOffset ObservedUtc=DateTimeOffset.UtcNow;
        string Recorded=await VsixObservationLedger.RecordManualAsync(
            RepositoryRoot,CheckId,InstalledDllPath,Assertions,
            ObservedUtc,ObservedUtc.AddDays(ValidDays),CancellationToken)
            .ConfigureAwait(false);
        await this.Output.WriteLineAsync(
            $"Recorded manual VSIX evidence: {Recorded}").ConfigureAwait(false);
        return 0;
    }
    private async Task<int>RunFreezeVsixReleaseInputAsync(
        string[]Args,CancellationToken CancellationToken){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        string?ProductArchive=null;
        for(int Index=1;Index<Args.Length;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Length){ throw new ArgumentException($"Missing value for {Argument}."); }
            string Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--repository-root": RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);break;
                case "--product-archive": ProductArchive=Value;break;
                default: throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        DateTimeOffset CreatedUtc=DateTimeOffset.UtcNow;
        VsixReleaseInputDocument Document=string.IsNullOrWhiteSpace(ProductArchive)
            ?await VsixReleaseInputStore.PublishCurrentAsync(
                RepositoryRoot,CreatedUtc,CancellationToken).ConfigureAwait(false)
            :await VsixReleaseInputStore.PublishProductArchiveAsync(
                RepositoryRoot,ProductArchive,CreatedUtc,CancellationToken)
                .ConfigureAwait(false);
        await this.Output.WriteLineAsync(
            $"Frozen VSIX release input: {Document.PackagePath} "
            +$"sha256={Document.PackageSha256}").ConfigureAwait(false);
        return 0;
    }
    private int RunVerifyFailurePolicy(string[]Args){
        var Options=ParseVerificationOptions(Args,false);
        var Errors=QuarantineLedgerValidator.Validate(Options.RepositoryRoot,this.Catalog,DateTimeOffset.UtcNow);
        return this.WriteVerificationResult("Failure and quarantine policy",Errors);
    }
    private Task<int>RunVerifyStressContractAsync(string[]Args,CancellationToken CancellationToken){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        string Configuration="Debug";
        uint Seed=5489;
        int Ticks=600;
        for(int Index=1;Index<Args.Length;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Length){ throw new ArgumentException($"Missing value for {Argument}."); }
            string Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--repository-root": RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);break;
                case "--configuration" when Value is "Debug" or "Release": Configuration=Value;break;
                case "--configuration": throw new ArgumentException("Configuration must be Debug or Release.");
                case "--seed" when uint.TryParse(Value,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out uint ParsedSeed)&&ParsedSeed!=0: Seed=ParsedSeed;break;
                case "--seed": throw new FormatException("Seed must be a positive UInt32.");
                case "--ticks" when int.TryParse(Value,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out int ParsedTicks)&&ParsedTicks>0: Ticks=ParsedTicks;break;
                case "--ticks": throw new FormatException("Ticks must be a positive Int32.");
                default: throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        return StressFrameworkProbe.RunAsync(RepositoryRoot,Configuration,Seed,Ticks,this.Output,this.Error,CancellationToken);
    }
    private Task<int>RunVerifyCleanBuildAsync(string[]Args,CancellationToken CancellationToken){
        var Options=ParseVerificationOptions(Args,false);
        return HermeticBuildVerifier.RunAsync(
            Options.RepositoryRoot,this.Output,this.Error,CancellationToken);
    }
    private int WriteVerificationResult(string Name,IReadOnlyList<string>Errors){
        if(Errors.Count==0){
            this.Output.WriteLine($"{Name} verification passed.");
            return 0;
        }
        foreach(var ValidationError in Errors){ this.Error.WriteLine(ValidationError); }
        return 1;
    }
    private int RunList(string[]Args){
        var Json=Args.Skip(1).Any(static Argument=>Argument.Equals("--json",StringComparison.OrdinalIgnoreCase));
        if(Json){
            var Features=this.Catalog.Features.Select(static Feature=>new {id=Feature.Id,name=Feature.Name,layer=Feature.Layer,status=Feature.Status.ToString(),track=Feature.Track.ToString(),dependencyLevel=Feature.DependencyLevel,dependsOnFeatureIds=Feature.DependsOnFeatureIds});
            this.Output.WriteLine(JsonSerializer.Serialize(new {schema=FeatureStageCatalog.CatalogSchemaVersion,features=Features,stages=this.Catalog.Stages},FeatureRunResultStore.CreateJsonOptions()));
            return 0;
        }
        foreach(var Stage in this.Catalog.Stages){
            this.Output.WriteLine($"{Stage.Id,-42} {Stage.Availability,-14} {Stage.Pack,-20} {string.Join(',',Stage.FeatureIds)}");
        }
        return 0;
    }
    private async Task<int>RunStageAsync(string[]Args,CancellationToken CancellationToken){
        if(Args.Length<2){ throw new ArgumentException("run requires a stage ID."); }
        var Stage=this.Catalog.GetStage(Args[1]);
        var Parsed=ParseOptions(Args,2,"single");
        return await this.ExecuteAndStoreAsync([Stage],Parsed,CancellationToken).ConfigureAwait(false);
    }
    private async Task<int>RunFeatureAsync(string[]Args,CancellationToken CancellationToken){
        if(Args.Length<2){ throw new ArgumentException("run-feature requires a feature ID."); }
        var Feature=this.Catalog.GetFeature(Args[1]);
        var Stages=this.Catalog.Stages.Where(Stage=>Stage.FeatureIds.Contains(Feature.Id,StringComparer.Ordinal)).ToArray();
        var Parsed=ParseOptions(Args,2,"feature");
        return await this.ExecuteAndStoreAsync(Stages,Parsed,CancellationToken).ConfigureAwait(false);
    }
    private async Task<int>RunSuiteAsync(string[]Args,CancellationToken CancellationToken){
        if(Args.Length<2){ throw new ArgumentException("suite requires changed, smoke, full, stress, visual, or vsix."); }
        var Suite=Args[1].ToLowerInvariant();
        if(Suite is not("changed" or "smoke" or "full" or "stress" or "visual" or "vsix")){ throw new ArgumentException($"Unknown suite: {Args[1]}"); }
        var Parsed=ParseOptions(Args,2,Suite);
        FeatureStageDefinition[]Stages;
        if(Suite=="changed"){
            var ChangedPaths=await this.ChangedPathProvider.GetChangedPathsAsync(Parsed.RepositoryRoot,CancellationToken).ConfigureAwait(false);
            Stages=[.. ChangedStageSelector.Select(this.Catalog,ChangedPaths)];
        } else if(Suite=="visual"){
            Stages=[.. this.Catalog.Stages.Where(static Stage=>Stage.Availability==FeatureStageAvailability.Executable&&Stage.Mode==FeatureStageMode.Visual)];
        } else {
            Stages=[.. this.Catalog.Stages.Where(static Stage=>Stage.Availability==FeatureStageAvailability.Executable).Where(Stage=>Stage.Profiles.Contains(Suite,StringComparer.OrdinalIgnoreCase))];
        }
        return await this.ExecuteAndStoreAsync(Stages,Parsed,CancellationToken).ConfigureAwait(false);
    }
    private async Task<int>ResumeAsync(string[]Args,CancellationToken CancellationToken){
        if(Args.Length<2){ throw new ArgumentException("resume requires a run-result.json path."); }
        var Previous=await this.ResultStore.LoadAsync(Args[1],CancellationToken).ConfigureAwait(false);
        var StageIds=Previous.Results.Where(static Result=>Result.Status!=FeatureStageRunStatus.Passed).Select(static Result=>Result.StageId).Distinct(StringComparer.Ordinal).ToArray();
        if(StageIds.Length==0){
            await this.Output.WriteLineAsync("All stages in the result already passed.").ConfigureAwait(false);
            return 0;
        }
        var Stages=StageIds.Select(this.Catalog.GetStage).ToArray();
        var Parsed=ParseOptions(Args,2,Previous.Profile);
        return await this.ExecuteAndStoreAsync(Stages,Parsed,CancellationToken).ConfigureAwait(false);
    }
    private async Task<int>ExecuteAndStoreAsync(IReadOnlyList<FeatureStageDefinition>Stages,ParsedExecutionOptions Parsed,CancellationToken CancellationToken){
        if(Stages.Count==0){ throw new ArgumentException("No stages matched the request."); }
        (string Commit,bool IsDirty)Provenance;
        try {
            Provenance=await GitChangedPathProvider.ReadProvenanceAsync(Parsed.RepositoryRoot,CancellationToken).ConfigureAwait(false);
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Provenance=("unknown",true);
            await this.Error.WriteLineAsync($"Warning: git provenance is unavailable: {Ex.Message}").ConfigureAwait(false);
        }
        var Results=new List<FeatureStageRunResult>();
        foreach(var Stage in Stages){
            var Options=new FeatureStageExecutionOptions(Parsed.RepositoryRoot,Parsed.Configuration,Parsed.Profile,Parsed.Adapter,Parsed.Seed??Stage.Seed,Parsed.Ticks??Stage.Ticks,Provenance.Commit,Provenance.IsDirty);
            await this.Output.WriteLineAsync($"RUN {Stage.Id} [{Options.Configuration}/{Options.Profile}/{Options.Adapter}] seed={Options.Seed} ticks={Options.Ticks}").ConfigureAwait(false);
            FeatureStageRunResult Result;
            try {
                Result=await this.Executor.ExecuteAsync(Stage,Options,CancellationToken).ConfigureAwait(false);
            } catch(Exception Ex)when(Ex is not OperationCanceledException){
                Result=FeatureStageRunResult.Create(Stage,Options,FeatureStageRunStatus.InfrastructureError,"Executor failed before producing a result.",[],null,null,[Ex.ToString()]);
            }
            Results.Add(Result);
            await this.Output.WriteLineAsync($"{Result.Status} {Stage.Id}: {Result.Summary}").ConfigureAwait(false);
        }
        var Document=FeatureRunResultDocument.Create(Parsed.Profile,Results);
        var ResultPath=await this.ResultStore.SaveAsync(Document,CancellationToken).ConfigureAwait(false);
        await this.Output.WriteLineAsync($"Result: {ResultPath}").ConfigureAwait(false);
        return ExitCode(Results);
    }
    private static int ExitCode(IReadOnlyList<FeatureStageRunResult>Results){
        if(Results.All(static Result=>Result.Status==FeatureStageRunStatus.Passed)){ return 0; }
        if(Results.Any(static Result=>Result.Status==FeatureStageRunStatus.TimedOut)){ return 4; }
        if(Results.All(static Result=>Result.Status is FeatureStageRunStatus.Passed or FeatureStageRunStatus.NotImplemented)){ return 3; }
        return 1;
    }
    /// <summary>
    /// The repository whose <c>.artifacts/FeatureStages</c> keeps a command's results: the one <c>--repository-root</c>
    /// names, where the stages run and where release evidence looks for their results; otherwise the one around the
    /// current directory.
    /// </summary>
    public static string ResultsRepositoryRoot(IReadOnlyList<string>Args){
        for(var Index=1;Index+1<Args.Count;Index++){
            if(Args[Index].Equals("--repository-root",StringComparison.OrdinalIgnoreCase)){
                return FeatureStageRepositoryLocator.Find(Args[Index+1]);
            }
        }
        return FeatureStageRepositoryLocator.Find();
    }
    private static ParsedExecutionOptions ParseOptions(string[]Args,int StartIndex,string DefaultProfile){
        var RepositoryRoot=FeatureStageRepositoryLocator.Find();
        var Configuration="Debug";
        var Profile=DefaultProfile;
        var Adapter=FeatureAdapter.Warp;
        uint?Seed=null;
        int?Ticks=null;
        for(var Index=StartIndex;Index<Args.Length;Index++){
            var Argument=Args[Index];
            if(Argument is "--json"){ continue; }
            if(Index+1>=Args.Length){ throw new ArgumentException($"Missing value for {Argument}."); }
            var Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--configuration":
                    if(Value is not("Debug" or "Release")){ throw new ArgumentException("Configuration must be Debug or Release."); }
                    Configuration=Value;
                    break;
                case "--profile":
                    Profile=Value;
                    break;
                case "--adapter":
                    if(!Enum.TryParse(Value,true,out Adapter)){ throw new ArgumentException("Adapter must be warp or hardware."); }
                    break;
                case "--seed":
                    if(!uint.TryParse(Value,out var ParsedSeed)||ParsedSeed==0){ throw new FormatException("Seed must be a positive UInt32."); }
                    Seed=ParsedSeed;
                    break;
                case "--ticks":
                    if(!int.TryParse(Value,out var ParsedTicks)||ParsedTicks<=0){ throw new FormatException("Ticks must be a positive Int32."); }
                    Ticks=ParsedTicks;
                    break;
                case "--repository-root":
                    RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        return new ParsedExecutionOptions(RepositoryRoot,Configuration,Profile,Adapter,Seed,Ticks);
    }
    private static VerificationOptions ParseVerificationOptions(string[]Args,bool AllowRelease){
        var RepositoryRoot=FeatureStageRepositoryLocator.Find();
        var RequirePassing=false;
        for(var Index=1;Index<Args.Length;Index++){
            var Argument=Args[Index];
            if(AllowRelease&&Argument.Equals("--release",StringComparison.OrdinalIgnoreCase)){
                RequirePassing=true;
                continue;
            }
            if(!Argument.Equals("--repository-root",StringComparison.OrdinalIgnoreCase)){ throw new ArgumentException($"Unknown option: {Argument}"); }
            if(Index+1>=Args.Length){ throw new ArgumentException("Missing value for --repository-root."); }
            RepositoryRoot=Path.GetFullPath(Args[++Index]);
        }
        return new VerificationOptions(RepositoryRoot,RequirePassing);
    }
    private static ReleaseCandidateOptions ParseReleaseCandidateOptions(string[]Args){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        int ReachedLevel=7;
        for(int Index=1;Index<Args.Length;Index++){
            string Argument=Args[Index];
            if(Index+1>=Args.Length){
                throw new ArgumentException($"Missing value for {Argument}.");
            }
            string Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--repository-root":
                    RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);
                    break;
                case "--configuration" when Value=="Release":
                    break;
                case "--configuration":
                    throw new ArgumentException(
                        "Release candidate configuration must be Release.");
                case "--level" when int.TryParse(
                    Value,System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int ParsedLevel)&&ParsedLevel is>=0 and<=7:
                    ReachedLevel=ParsedLevel;
                    break;
                case "--level":
                    throw new FormatException(
                        "Reached Level must be between zero and seven.");
                default:
                    throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        return new ReleaseCandidateOptions(RepositoryRoot,ReachedLevel);
    }
    private static bool IsHelp(string Argument)=>Argument is "help" or "--help" or "-h";
    private int UnknownCommand(string Command){
        this.Error.WriteLine($"Unknown command: {Command}");
        this.WriteHelp();
        return 2;
    }
    private void WriteHelp(){
        this.Output.WriteLine("C# Game Engine Feature Runner");
        this.Output.WriteLine("Usage:");
        this.Output.WriteLine("  list [--json]");
        this.Output.WriteLine("  export-showcase --output <path>");
        this.Output.WriteLine("  verify-coverage [--repository-root <path>]");
        this.Output.WriteLine("  verify-manual-ledger [--repository-root <path>] [--release]");
        this.Output.WriteLine("  verify-goldens [--repository-root <path>]");
        this.Output.WriteLine("  verify-performance-framework [--repository-root <path>] [--configuration Debug|Release]");
        this.Output.WriteLine("  verify-rendering-performance [--repository-root <path>] [--configuration Debug|Release] [--adapter warp|hardware]");
        this.Output.WriteLine("  verify-rendering-release [--repository-root <path>] [--configuration Debug|Release]");
        this.Output.WriteLine("  verify-thirdperson-character [--repository-root <path>] [--configuration Debug|Release] [--adapter warp|hardware] [--template ThirdPerson|FirstPerson|TopDown|Vehicle]");
        this.Output.WriteLine("  verify-template-package [--repository-root <path>] [--configuration Debug|Release] [--adapter warp|hardware] [--template ThirdPerson|FirstPerson|TopDown|Vehicle]");
        this.Output.WriteLine("  verify-plugin-package [--repository-root <path>] [--configuration Debug|Release] [--adapter warp|hardware]");
        this.Output.WriteLine("  verify-content-performance [--repository-root <path>] [--configuration Debug|Release]");
        this.Output.WriteLine("  verify-ecs-performance [--repository-root <path>] [--configuration Debug|Release]");
        this.Output.WriteLine("  verify-ecs-release [--repository-root <path>] [--configuration Debug|Release]");
        this.Output.WriteLine("  verify-content-release [--repository-root <path>] [--configuration Debug|Release]");
        this.Output.WriteLine("  verify-release-package [--repository-root <path>] [--configuration Debug|Release]");
        this.Output.WriteLine("  verify-release-candidate [--repository-root <path>] [--configuration Release] [--level 0..7]");
        this.Output.WriteLine("  run-release-candidate [--repository-root <path>] [--configuration Release] [--level 0..7]");
        this.Output.WriteLine("  verify-release-manifest [--repository-root <path>] [--configuration Release]");
        this.Output.WriteLine("  verify-ci-matrix [--repository-root <path>]");
        this.Output.WriteLine("  verify-release-compatibility [--repository-root <path>]");
        this.Output.WriteLine("  verify-platform-compatibility [--repository-root <path>]");
        this.Output.WriteLine("  verify-platform-release [--repository-root <path>] [--configuration Debug|Release]");
         this.Output.WriteLine("  verify-vsix-compatibility [--repository-root <path>] [--configuration Debug|Release] [--require-passing-evidence]");
         this.Output.WriteLine("  verify-vsix-release [--repository-root <path>] [--configuration Release]");
         this.Output.WriteLine("  register-vsix-automated-evidence --check <id> --evidence <path> [--valid-days 1..14]");
         this.Output.WriteLine("  record-vsix-manual-evidence --check <id> --installed-dll <path> --assert <id>=<detail> [--assert ...] [--valid-days 1..14]");
         this.Output.WriteLine("  freeze-vsix-release-input [--product-archive <release.zip>]");
        this.Output.WriteLine("  verify-failure-policy [--repository-root <path>]");
        this.Output.WriteLine("  verify-stress-contract [--repository-root <path>] [--configuration Debug|Release] [--seed <uint>] [--ticks <int>]");
        this.Output.WriteLine("  verify-clean-build [--repository-root <path>]");
        this.Output.WriteLine("  run <stage-id> [options]");
        this.Output.WriteLine("  run-feature <feature-id> [options]");
        this.Output.WriteLine("  suite <changed|smoke|full|stress|visual|vsix> [options]");
        this.Output.WriteLine("  resume <run-result.json> [options]");
        this.Output.WriteLine("Options: --configuration Debug|Release --profile <name> --adapter warp|hardware --seed <uint> --ticks <int> --repository-root <path>");
        this.Output.WriteLine("Visual certification uses WARP by default; hardware is an optional additional adapter pass.");
    }
    private sealed record ParsedExecutionOptions(string RepositoryRoot,string Configuration,string Profile,FeatureAdapter Adapter,uint?Seed,int?Ticks);
    private sealed record VerificationOptions(string RepositoryRoot,bool RequirePassing);
    private sealed record ReleaseCandidateOptions(string RepositoryRoot,int ReachedLevel);
}
