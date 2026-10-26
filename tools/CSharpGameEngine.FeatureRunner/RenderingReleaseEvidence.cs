using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CSharpGameEngine.FeatureStages;

public sealed record RenderingReleaseInput(
    string Role,string Path,string Sha256,long ByteLength);
public sealed record RenderingReleaseStageEvidence(
    string StageId,string ReportPath,string FileSha256,string ResultArtifactSha256,
    DateTimeOffset FinishedUtc,string Profile,string Adapter);
public sealed record RenderingReleasePerformanceEvidence(
    string PlanId,string ReportPath,string FileSha256,string ReportSha256,
    string RawReportSha256,string ShaderArtifactSha256,string AdapterDescription,
    long MedianNanoseconds,long P95Nanoseconds,long AllocationP95Bytes);
public sealed record RenderingReleaseRawEvidence(
    string ReportPath,string FileSha256,string Schema,string Adapter,
    string AdapterDescription,string RuntimeMode,string ShaderArtifactSha256,
    int DebugErrorCount,bool PixelVerified);
public sealed record RenderingReleasePayload(
    string Schema,DateTimeOffset CreatedUtc,string Configuration,string Commit,bool IsDirty,
    string TargetOs,string MinimumOsVersion,string Architecture,
    PerformanceMachineFingerprint Machine,RenderingReleaseStageEvidence PerformanceStage,
    RenderingReleaseStageEvidence PreviewStage,RenderingReleaseRawEvidence RawReport,
    IReadOnlyList<RenderingReleasePerformanceEvidence>PerformanceReports,
    IReadOnlyList<RenderingReleaseInput>Inputs);
public sealed record RenderingReleaseReport(
    string Schema,string ReportSha256,DateTimeOffset CreatedUtc,string Configuration,
    string Commit,bool IsDirty,string TargetOs,string MinimumOsVersion,string Architecture,
    PerformanceMachineFingerprint Machine,RenderingReleaseStageEvidence PerformanceStage,
    RenderingReleaseStageEvidence PreviewStage,RenderingReleaseRawEvidence RawReport,
    IReadOnlyList<RenderingReleasePerformanceEvidence>PerformanceReports,
    IReadOnlyList<RenderingReleaseInput>Inputs);

/// <summary>
/// Produces one Rendering-track release input for later product-wide aggregation. It binds
/// existing Stage and performance truths without becoming a package or release manifest.
/// </summary>
public static class RenderingReleaseEvidence {
    public const string ReportSchema="csharp-game-engine-rendering-release-v1";
    public const string ReportFileName="rendering-release.json";
    public const string PerformanceProfile="rendering-release-performance";
    public const string PreviewProfile="rendering-release-preview";
    private static readonly TimeSpan MaximumEvidenceAge=TimeSpan.FromHours(2);
    private static readonly string[]RequiredPerformancePlanIds=[
        "rendering.visibility-100k","rendering.warp-frame",
    ];
    private static readonly InputDefinition[]InputDefinitions=[
        new("rendering.specification","Docs/Specifications/Rendering.md"),
        new("release.specification","Docs/Specifications/Testing-CI-Release.md"),
        new("toolchain","global.json"),
        new("shader.source","assets/Shaders/Stage.cgeshadergraph"),
        new("shader.depth","assets/Shaders/Depth.cgesubgraph"),
        new("shader.pbr","assets/Shaders/Pbr.cgesubgraph"),
        new("shader.shadow","assets/Shaders/Shadow.cgesubgraph"),
        new("shader.pcss","assets/Shaders/Pcss.cgesubgraph"),
        new("shader.aces","assets/Shaders/Aces.cgesubgraph"),
        new("shader.presentation","src/CSharpGameEngine.ShaderArtifacts/StagePresentationShaderSource.cs"),
        new("shader.build-target","build/CSharpGameEngine.ShaderGraph.targets"),
        new("shader.abi","src/CSharpGameEngine.ShaderArtifacts/ShaderArtifactAbi.cs"),
        new("render.graph","src/CSharpGameEngine.Rendering/RenderGraph.cs"),
        new("render.snapshot","src/CSharpGameEngine.Rendering/RenderWorldSnapshot.cs"),
        new("render.device-state","src/CSharpGameEngine.Rendering/RenderDeviceLifecycle.cs"),
        new("render.server","src/CSharpGameEngine.Rendering/Server/RenderServer.cs"),
        new("preview.contract","src/CSharpGameEngine.Contracts/RenderPreviewHostContracts.cs"),
        new("preview.host","tools/CSharpGameEngine.EditorPreview.Host/RenderPreviewRenderSession.cs"),
        new("preview.client","tools/CSharpGameEngine.VSExtension/RenderPreviewHostClient.cs"),
        new("test-host.project","tools/CSharpGameEngine.TestHost/CSharpGameEngine.TestHost.csproj"),
        new("performance.capture","tools/CSharpGameEngine.TestHost/RenderingPerformanceRawCapture.cs"),
        new("performance.probe","tools/CSharpGameEngine.FeatureRunner/RenderingPerformanceProbe.cs"),
        new("performance.framework","tools/CSharpGameEngine.FeatureRunner/PerformanceMeasurementFramework.cs"),
        new("release.evidence","tools/CSharpGameEngine.FeatureRunner/RenderingReleaseEvidence.cs"),
        new("stage.catalog","tools/CSharpGameEngine.FeatureRunner/FeatureStageCatalog.cs"),
    ];
    private static readonly JsonSerializerOptions JsonOptions=
        FeatureRunResultStore.CreateJsonOptions();
    private static readonly Regex PerformancePathPattern=new(
        @"report=(?<path>[^\r\n]+?\.performance\.json)",
        RegexOptions.Compiled|RegexOptions.CultureInvariant);
    private static readonly Regex RawPathPattern=new(
        @"Rendering performance raw report: (?<path>[^\r\n]+?\.raw\.json)",
        RegexOptions.Compiled|RegexOptions.CultureInvariant);

    public static IEnumerable<string>RequiredRepositoryInputPaths=>
        InputDefinitions.Select(static Definition=>Definition.Path);

    public static async Task<int>RunAsync(
        string RepositoryRoot,string Configuration,TextWriter Output,TextWriter Error,
        CancellationToken CancellationToken){
        string Root=Path.GetFullPath(RepositoryRoot);
        ValidateConfiguration(Configuration);
        DateTimeOffset CreatedUtc=DateTimeOffset.UtcNow;
        (string Commit,bool IsDirty)Provenance;
        try { Provenance=await GitChangedPathProvider.ReadProvenanceAsync(
            Root,CancellationToken).ConfigureAwait(false); }
        catch(Exception Ex)when(Ex is not OperationCanceledException){
            Provenance=("unknown",true);
            await Error.WriteLineAsync(
                $"Warning: git provenance is unavailable: {Ex.Message}").ConfigureAwait(false);
        }
        string EvidenceRoot=Path.Combine(
            Root,".artifacts","RenderingRelease",
            $"{CreatedUtc:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        string ReportPath=Path.Combine(EvidenceRoot,ReportFileName);
        bool Completed=false;
        try {
            FeatureStageCatalog Catalog=FeatureStageCatalog.CreateDefault();
            RenderingReleaseStageEvidence PerformanceStage=FindStageEvidence(
                Root,"rendering.performance",PerformanceProfile,Configuration,
                Provenance,CreatedUtc,Catalog);
            RenderingReleaseStageEvidence PreviewStage=FindStageEvidence(
                Root,"vsix.rendering-preview",PreviewProfile,Configuration,
                Provenance,CreatedUtc,Catalog);
            RenderingReleaseRawEvidence RawReport=ReadRawEvidence(
                Root,PerformanceStage,Configuration);
            RenderingReleasePerformanceEvidence[]PerformanceReports=
                ReadPerformanceEvidence(
                    Root,PerformanceStage,RawReport,Configuration,Provenance);
            RenderingReleaseInput[]Inputs=ReadInputs(Root);
            PerformanceMachineFingerprint Machine=
                PerformanceMeasurementFramework.CaptureMachineFingerprint();
            var Payload=new RenderingReleasePayload(
                ReportSchema,CreatedUtc,Configuration,Provenance.Commit,Provenance.IsDirty,
                "Windows","10.0.19041.0","x64",Machine,PerformanceStage,PreviewStage,
                RawReport,PerformanceReports,Inputs);
            string Hash=Sha256(JsonSerializer.SerializeToUtf8Bytes(Payload,JsonOptions));
            var Report=new RenderingReleaseReport(
                Payload.Schema,Hash,Payload.CreatedUtc,Payload.Configuration,Payload.Commit,
                Payload.IsDirty,Payload.TargetOs,Payload.MinimumOsVersion,Payload.Architecture,
                Payload.Machine,Payload.PerformanceStage,Payload.PreviewStage,
                Payload.RawReport,Payload.PerformanceReports,Payload.Inputs);
            Directory.CreateDirectory(EvidenceRoot);
            await WriteCanonicalAsync(ReportPath,Report,CancellationToken).ConfigureAwait(false);
            IReadOnlyList<string>Errors=await ValidateAsync(
                Root,ReportPath,CancellationToken).ConfigureAwait(false);
            foreach(string ValidationError in Errors){
                await Error.WriteLineAsync(ValidationError).ConfigureAwait(false);
            }
            if(Errors.Count!=0){ return 1; }
            Completed=true;
            await Output.WriteLineAsync(
                $"renderingRelease inputs={Inputs.Length} performanceReports={PerformanceReports.Length} "
                +$"shaderArtifact={PerformanceReports[0].ShaderArtifactSha256} machine={Machine.FingerprintSha256}")
                .ConfigureAwait(false);
            await Output.WriteLineAsync(ReportPath).ConfigureAwait(false);
            return 0;
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            await Error.WriteLineAsync(
                $"Rendering release verification failed: {Ex.Message}").ConfigureAwait(false);
            return 1;
        } finally {
            if(!Completed&&Directory.Exists(EvidenceRoot)){ Directory.Delete(EvidenceRoot,true); }
        }
    }

    public static async Task<IReadOnlyList<string>>ValidateAsync(
        string RepositoryRoot,string ReportPath,CancellationToken CancellationToken=default){
        var Errors=new List<string>();
        string Root;
        string FullReportPath;
        byte[]Bytes;
        RenderingReleaseReport?Report;
        try {
            Root=Path.GetFullPath(RepositoryRoot);
            FullReportPath=Path.GetFullPath(ReportPath);
            if(!IsContained(Path.Combine(Root,".artifacts","RenderingRelease"),FullReportPath)){
                return ["Rendering release report must be inside .artifacts/RenderingRelease."];
            }
            EnsureNoReparsePoints(Root,FullReportPath);
            Bytes=await File.ReadAllBytesAsync(FullReportPath,CancellationToken).ConfigureAwait(false);
            Report=JsonSerializer.Deserialize<RenderingReleaseReport>(Bytes,JsonOptions);
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            return [$"Rendering release report cannot be read: {Ex.Message}"];
        }
        if(Report is null){ return ["Rendering release report is empty."]; }
        try {
            ValidateConfiguration(Report.Configuration);
            if(!Bytes.AsSpan().SequenceEqual(CanonicalBytes(Report))){
                Errors.Add("Rendering release report is not canonically encoded.");
            }
            if(Report.Schema!=ReportSchema){
                Errors.Add($"Rendering release report schema must be {ReportSchema}.");
            }
            var Payload=new RenderingReleasePayload(
                Report.Schema,Report.CreatedUtc,Report.Configuration,Report.Commit,
                Report.IsDirty,Report.TargetOs,Report.MinimumOsVersion,Report.Architecture,
                Report.Machine,Report.PerformanceStage,Report.PreviewStage,
                Report.RawReport,Report.PerformanceReports,Report.Inputs);
            if(!FixedHashEquals(
                Sha256(JsonSerializer.SerializeToUtf8Bytes(Payload,JsonOptions)),
                Report.ReportSha256)){
                Errors.Add("Rendering release report SHA-256 mismatch.");
            }
            if(Report.TargetOs!="Windows"||Report.MinimumOsVersion!="10.0.19041.0"
                ||Report.Architecture!="x64"){
                Errors.Add("Rendering release target must be Windows 10.0.19041.0+ x64.");
            }
            if(!FixedHashEquals(
                PerformanceMeasurementFramework.HashFingerprint(Report.Machine),
                Report.Machine.FingerprintSha256)){
                Errors.Add("Rendering release machine fingerprint mismatch.");
            }
            if(Report.Machine.OsArchitecture!="X64"
                ||Report.Machine.ProcessArchitecture!="X64"){
                Errors.Add("Rendering release evidence must be captured by an x64 OS and process.");
            }
            ValidateInputs(Root,Report.Inputs,Errors);
            ValidateStageEvidence(
                Root,Report,Report.PerformanceStage,"rendering.performance",
                PerformanceProfile,Errors);
            ValidateStageEvidence(
                Root,Report,Report.PreviewStage,"vsix.rendering-preview",
                PreviewProfile,Errors);
            ValidateRawEvidence(Root,Report,Errors);
            ValidatePerformanceEvidence(Root,Report,Errors);
            string[]Files=Directory.EnumerateFiles(
                Path.GetDirectoryName(FullReportPath)!,"*",SearchOption.AllDirectories).ToArray();
            if(Files.Length!=1||!string.Equals(
                Path.GetFullPath(Files[0]),FullReportPath,StringComparison.OrdinalIgnoreCase)){
                Errors.Add("Rendering release evidence directory contains missing or extra files.");
            }
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Errors.Add($"Rendering release evidence validation failed: {Ex.Message}");
        }
        return Errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static RenderingReleaseStageEvidence FindStageEvidence(
        string Root,string StageId,string Profile,string Configuration,
        (string Commit,bool IsDirty)Provenance,DateTimeOffset CreatedUtc,
        FeatureStageCatalog Catalog){
        string StageRoot=Path.Combine(Root,".artifacts","FeatureStages");
        if(!Directory.Exists(StageRoot)){
            throw new InvalidDataException(
                $"Rendering release requires a recent passing {StageId} Stage result.");
        }
        var Candidates=new List<(string Path,FeatureStageRunResult Result)>();
        foreach(string PathValue in Directory.EnumerateFiles(
            StageRoot,"*.run-result.json",SearchOption.TopDirectoryOnly)){
            try {
                EnsureNoReparsePoints(Root,PathValue);
                FeatureRunResultDocument?Document=
                    JsonSerializer.Deserialize<FeatureRunResultDocument>(
                        File.ReadAllBytes(PathValue),JsonOptions);
                if(Document?.SchemaVersion!=FeatureRunResultDocument.CurrentSchemaVersion){ continue; }
                Candidates.AddRange(Document.Results.Where(Result=>
                    Result.StageId==StageId&&Result.Configuration==Configuration
                    &&Result.Profile==Profile&&Result.Status==FeatureStageRunStatus.Passed
                    &&Result.Adapter=="Warp"&&Result.GitCommit==Provenance.Commit
                    &&Result.IsDirty==Provenance.IsDirty&&Result.FinishedUtc<=CreatedUtc
                    &&CreatedUtc-Result.FinishedUtc<=MaximumEvidenceAge
                    &&ValidateStageResult(Result,StageId,Catalog).Count==0)
                    .Select(Result=>(PathValue,Result)));
            } catch(Exception Ex)when(Ex is not OperationCanceledException
                &&Ex is not OutOfMemoryException&&Ex is not StackOverflowException){ }
        }
        (string Path,FeatureStageRunResult Result)Match=Candidates
            .OrderByDescending(static Candidate=>Candidate.Result.FinishedUtc)
            .ThenByDescending(static Candidate=>Candidate.Path,StringComparer.Ordinal)
            .FirstOrDefault();
        if(Match.Result is null){
            throw new InvalidDataException(
                $"Rendering release requires a recent passing {StageId} result for {Configuration} "
                +$"with {Profile} profile, matching Git provenance, and Warp adapter.");
        }
        return new RenderingReleaseStageEvidence(
            StageId,Relative(Root,Match.Path),Sha256(File.ReadAllBytes(Match.Path)),
            Match.Result.ArtifactSha256,Match.Result.FinishedUtc,
            Match.Result.Profile,Match.Result.Adapter);
    }

    private static RenderingReleaseRawEvidence ReadRawEvidence(
        string Root,RenderingReleaseStageEvidence Stage,string Configuration){
        FeatureStageRunResult Result=ReadBoundStageResult(Root,Stage);
        string[]Paths=[.. RawPathPattern.Matches(Result.StandardOutput)
            .Select(static Match=>Match.Groups["path"].Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        if(Paths.Length!=1){
            throw new InvalidDataException(
                "Rendering performance Stage must publish exactly one raw report.");
        }
        string FullPath=Path.GetFullPath(Paths[0]);
        if(!IsContained(Path.Combine(Root,".artifacts","RenderingPerformance"),FullPath)){
            throw new InvalidDataException(
                "Rendering raw report escaped .artifacts/RenderingPerformance.");
        }
        EnsureNoReparsePoints(Root,FullPath);
        byte[]Bytes=File.ReadAllBytes(FullPath);
        RenderingRawPerformanceReport Raw=
            JsonSerializer.Deserialize<RenderingRawPerformanceReport>(Bytes,JsonOptions)
            ??throw new InvalidDataException("Rendering raw report is empty.");
        IReadOnlyList<string>Errors=RenderingPerformanceProbe.ValidateRawReport(
            Raw,"warp",Configuration);
        if(Errors.Count!=0){
            throw new InvalidDataException(
                "Rendering raw report failed validation: "+string.Join(" ",Errors));
        }
        return new RenderingReleaseRawEvidence(
            Relative(Root,FullPath),Sha256(Bytes),Raw.Schema,Raw.Adapter,
            Raw.AdapterDescription,Raw.RuntimeMode,Raw.ShaderArtifactSha256,
            Raw.DebugErrorCount,Raw.PixelVerified);
    }

    private static RenderingReleasePerformanceEvidence[]ReadPerformanceEvidence(
        string Root,RenderingReleaseStageEvidence Stage,
        RenderingReleaseRawEvidence RawReport,string Configuration,
        (string Commit,bool IsDirty)Provenance){
        FeatureStageRunResult Result=ReadBoundStageResult(Root,Stage);
        string[]Paths=[.. PerformancePathPattern.Matches(Result.StandardOutput)
            .Select(static Match=>Match.Groups["path"].Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        if(Paths.Length!=RequiredPerformancePlanIds.Length){
            throw new InvalidDataException(
                "Rendering performance Stage must publish exactly two performance reports.");
        }
        RenderingReleasePerformanceEvidence[]Evidence=[.. Paths.Select(PathValue=>
            ReadPerformanceReport(Root,PathValue,Configuration,Provenance))
            .OrderBy(static Entry=>Entry.PlanId,StringComparer.Ordinal)];
        if(!Evidence.Select(static Entry=>Entry.PlanId)
            .SequenceEqual(RequiredPerformancePlanIds,StringComparer.Ordinal)){
            throw new InvalidDataException("Rendering performance report closure is incomplete.");
        }
        if(Evidence.Select(static Entry=>Entry.ShaderArtifactSha256)
            .Distinct(StringComparer.Ordinal).Count()!=1){
            throw new InvalidDataException(
                "Rendering performance reports do not bind one Shader Artifact.");
        }
        if(Evidence.Any(Entry=>Entry.RawReportSha256!=RawReport.FileSha256
            ||Entry.ShaderArtifactSha256!=RawReport.ShaderArtifactSha256)){
            throw new InvalidDataException(
                "Rendering performance reports do not match the bound raw report.");
        }
        return Evidence;
    }

    private static void ValidateRawEvidence(
        string Root,RenderingReleaseReport Report,List<string>Errors){
        try {
            RenderingReleaseRawEvidence Expected=ReadRawEvidence(
                Root,Report.PerformanceStage,Report.Configuration);
            if(Report.RawReport.ReportPath!=Expected.ReportPath
                ||!FixedHashEquals(Report.RawReport.FileSha256,Expected.FileSha256)){
                Errors.Add("Rendering raw report file SHA-256 mismatch.");
            } else if(Report.RawReport!=Expected){
                Errors.Add("Rendering raw report evidence changed.");
            }
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Errors.Add($"Rendering raw report cannot be validated: {Ex.Message}");
        }
    }

    private static RenderingReleasePerformanceEvidence ReadPerformanceReport(
        string Root,string PathValue,string Configuration,
        (string Commit,bool IsDirty)Provenance){
        string FullPath=Path.GetFullPath(PathValue);
        if(!IsContained(Path.Combine(Root,".artifacts","Performance"),FullPath)){
            throw new InvalidDataException("Rendering performance report escaped .artifacts/Performance.");
        }
        EnsureNoReparsePoints(Root,FullPath);
        IReadOnlyList<string>IntegrityErrors=PerformanceReportStore.Validate(FullPath);
        if(IntegrityErrors.Count!=0){
            throw new InvalidDataException(
                "Rendering performance report failed integrity validation: "
                +string.Join(" ",IntegrityErrors));
        }
        byte[]Bytes=File.ReadAllBytes(FullPath);
        PerformanceReport Report=JsonSerializer.Deserialize<PerformanceReport>(Bytes,JsonOptions)
            ??throw new InvalidDataException("Rendering performance report is empty.");
        if(Report.Plan.Configuration!=Configuration||Report.Commit!=Provenance.Commit
            ||Report.IsDirty!=Provenance.IsDirty||!Report.Analysis.Passed){
            throw new InvalidDataException(
                "Rendering performance report provenance or verdict does not match release.");
        }
        IReadOnlyDictionary<string,string>Metadata=Report.Plan.Workload;
        if(!Metadata.TryGetValue("adapter",out string?Adapter)||Adapter!="warp"
            ||!Metadata.TryGetValue("adapterDescription",out string?Description)
            ||string.IsNullOrWhiteSpace(Description)
            ||!Metadata.TryGetValue("runtimeMode",out string?RuntimeMode)
            ||RuntimeMode!=RenderingPerformanceProbe.RequiredRuntimeMode
            ||!Metadata.TryGetValue("rawReportSha256",out string?RawHash)||!IsSha256(RawHash)
            ||!Metadata.TryGetValue("shaderArtifactSha256",out string?ShaderHash)
            ||!IsSha256(ShaderHash)||Metadata.GetValueOrDefault("debugLayer")!="enabled"
            ||Metadata.GetValueOrDefault("pixelOracle")!="triangle-readback"
            ||Metadata.GetValueOrDefault("resolution")!="1280x720"){
            throw new InvalidDataException(
                "Rendering performance workload metadata is incomplete or non-canonical.");
        }
        return new RenderingReleasePerformanceEvidence(
            Report.Plan.Id,Relative(Root,FullPath),Sha256(Bytes),Report.ReportSha256,
            RawHash,ShaderHash,Description,Report.Analysis.ElapsedNanoseconds.Median,
            Report.Analysis.ElapsedNanoseconds.P95,Report.Analysis.AllocatedBytes.P95);
    }

    private static void ValidatePerformanceEvidence(
        string Root,RenderingReleaseReport Release,List<string>Errors){
        if(Release.PerformanceReports is null
            ||Release.PerformanceReports.Count!=RequiredPerformancePlanIds.Length){
            Errors.Add("Rendering release performance report closure is incomplete.");return;
        }
        RenderingReleasePerformanceEvidence[]Expected;
        try {
            Expected=ReadPerformanceEvidence(
                Root,Release.PerformanceStage,Release.RawReport,Release.Configuration,
                (Release.Commit,Release.IsDirty));
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Errors.Add($"Rendering performance reports cannot be validated: {Ex.Message}");return;
        }
        RenderingReleasePerformanceEvidence[]Actual=[.. Release.PerformanceReports
            .OrderBy(static Entry=>Entry.PlanId,StringComparer.Ordinal)];
        for(int Index=0;Index<Expected.Length;Index++){
            if(Actual[Index].ReportPath!=Expected[Index].ReportPath
                ||!FixedHashEquals(Actual[Index].FileSha256,Expected[Index].FileSha256)){
                Errors.Add($"Rendering performance report file SHA-256 mismatch: {Actual[Index].PlanId}.");
            } else if(Actual[Index]!=Expected[Index]){
                Errors.Add($"Rendering performance report evidence changed: {Actual[Index].PlanId}.");
            }
        }
        if(Expected.Select(static Entry=>Entry.ShaderArtifactSha256)
            .Distinct(StringComparer.Ordinal).Count()!=1){
            Errors.Add("Rendering release does not bind one Shader Artifact SHA-256.");
        }
        foreach(RenderingReleasePerformanceEvidence Entry in Expected){
            try {
                PerformanceReport Report=JsonSerializer.Deserialize<PerformanceReport>(
                    File.ReadAllBytes(ResolveRelative(Root,Entry.ReportPath)),JsonOptions)!;
                if(Report.Machine.FingerprintSha256!=Release.Machine.FingerprintSha256){
                    Errors.Add($"Rendering performance machine does not match release: {Entry.PlanId}.");
                }
            } catch(Exception Ex)when(Ex is not OperationCanceledException){
                Errors.Add($"Rendering performance machine cannot be validated: {Ex.Message}");
            }
        }
    }

    private static void ValidateStageEvidence(
        string Root,RenderingReleaseReport Report,RenderingReleaseStageEvidence Evidence,
        string StageId,string Profile,List<string>Errors){
        try {
            if(Evidence.StageId!=StageId){ Errors.Add($"Rendering {StageId} evidence ID mismatch.");return; }
            string PathValue=ResolveRelative(Root,Evidence.ReportPath);
            if(!IsContained(Path.Combine(Root,".artifacts","FeatureStages"),PathValue)){
                throw new InvalidDataException($"{StageId} result escaped .artifacts/FeatureStages.");
            }
            EnsureNoReparsePoints(Root,PathValue);
            byte[]Bytes=File.ReadAllBytes(PathValue);
            if(!FixedHashEquals(Sha256(Bytes),Evidence.FileSha256)){
                Errors.Add($"Rendering {StageId} result file SHA-256 mismatch.");return;
            }
            FeatureStageRunResult Result=ReadBoundStageResult(Root,Evidence);
            Errors.AddRange(ValidateStageResult(
                Result,StageId,FeatureStageCatalog.CreateDefault()));
            if(Result.Configuration!=Report.Configuration||Result.GitCommit!=Report.Commit
                ||Result.IsDirty!=Report.IsDirty||Result.FinishedUtc!=Evidence.FinishedUtc
                ||Result.Profile!=Profile||Result.Profile!=Evidence.Profile
                ||Result.Adapter!="Warp"||Result.Adapter!=Evidence.Adapter
                ||Result.FinishedUtc>Report.CreatedUtc
                ||Report.CreatedUtc-Result.FinishedUtc>MaximumEvidenceAge){
                Errors.Add($"Rendering {StageId} result provenance does not match release report.");
            }
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Errors.Add($"Rendering {StageId} result cannot be validated: {Ex.Message}");
        }
    }

    private static FeatureStageRunResult ReadBoundStageResult(
        string Root,RenderingReleaseStageEvidence Evidence){
        string PathValue=ResolveRelative(Root,Evidence.ReportPath);
        FeatureRunResultDocument?Document=JsonSerializer.Deserialize<FeatureRunResultDocument>(
            File.ReadAllBytes(PathValue),JsonOptions);
        if(Document?.SchemaVersion!=FeatureRunResultDocument.CurrentSchemaVersion){
            throw new InvalidDataException("Rendering Stage result schema mismatch.");
        }
        return Document.Results.FirstOrDefault(Result=>
            Result.StageId==Evidence.StageId
            &&Result.ArtifactSha256==Evidence.ResultArtifactSha256)
            ??throw new InvalidDataException($"Bound {Evidence.StageId} result is missing.");
    }

    private static IReadOnlyList<string>ValidateStageResult(
        FeatureStageRunResult Result,string StageId,FeatureStageCatalog Catalog){
        var Errors=new List<string>();
        FeatureStageDefinition Stage=Catalog.GetStage(StageId);
        if(Result.Status!=FeatureStageRunStatus.Passed
            ||Result.Pack!=Stage.Pack.ToString()||Result.Adapter!="Warp"
            ||!Result.FeatureIds.SequenceEqual(Stage.FeatureIds,StringComparer.Ordinal)
            ||Result.Errors.Count!=0||Result.Assertions.Count!=Stage.Commands.Count
            ||Result.Assertions.Any(static Assertion=>!Assertion.Passed)){
            Errors.Add($"Bound {StageId} result is not a complete passing Warp verdict.");
        }
        if(!FixedHashEquals(ComputeStageArtifactHash(Result),Result.ArtifactSha256)){
            Errors.Add($"Bound {StageId} result artifact SHA-256 mismatch.");
        }
        if(Result.FinishedUtc<Result.StartedUtc||Result.DurationMilliseconds<0){
            Errors.Add($"Bound {StageId} result has invalid timing.");
        }
        return Errors;
    }

    private static RenderingReleaseInput[]ReadInputs(string Root)=>[.. InputDefinitions.Select(
        Definition=>{
            string PathValue=ResolveRelative(Root,Definition.Path);
            EnsureNoReparsePoints(Root,PathValue);
            byte[]Bytes=File.ReadAllBytes(PathValue);
            return new RenderingReleaseInput(
                Definition.Role,Definition.Path,Sha256(Bytes),Bytes.LongLength);
        })];

    private static void ValidateInputs(
        string Root,IReadOnlyList<RenderingReleaseInput>?Inputs,List<string>Errors){
        if(Inputs is null||Inputs.Count!=InputDefinitions.Length){
            Errors.Add("Rendering release input closure is incomplete.");return;
        }
        for(int Index=0;Index<InputDefinitions.Length;Index++){
            InputDefinition Definition=InputDefinitions[Index];
            RenderingReleaseInput Entry=Inputs[Index];
            if(Entry.Role!=Definition.Role||Entry.Path!=Definition.Path){
                Errors.Add($"Rendering release input {Index} is not canonical.");continue;
            }
            try {
                string PathValue=ResolveRelative(Root,Entry.Path);
                EnsureNoReparsePoints(Root,PathValue);
                byte[]Bytes=File.ReadAllBytes(PathValue);
                if(Entry.ByteLength!=Bytes.LongLength
                    ||!FixedHashEquals(Sha256(Bytes),Entry.Sha256)){
                    Errors.Add($"Rendering release input changed: {Entry.Path}.");
                }
            } catch(Exception Ex)when(Ex is not OperationCanceledException){
                Errors.Add($"Rendering release input cannot be validated: {Entry.Path}: {Ex.Message}");
            }
        }
    }

    private static async Task WriteCanonicalAsync(
        string PathValue,RenderingReleaseReport Report,CancellationToken CancellationToken){
        string Temporary=PathValue+".tmp";
        try {
            await File.WriteAllBytesAsync(
                Temporary,CanonicalBytes(Report),CancellationToken).ConfigureAwait(false);
            File.Move(Temporary,PathValue,true);
        } finally { if(File.Exists(Temporary)){ File.Delete(Temporary); } }
    }

    private static byte[]CanonicalBytes(RenderingReleaseReport Report){
        byte[]Json=JsonSerializer.SerializeToUtf8Bytes(Report,JsonOptions);
        byte[]Result=new byte[Json.Length+1];
        Json.CopyTo(Result,0);Result[^1]=(byte)'\n';return Result;
    }
    private static string ComputeStageArtifactHash(FeatureStageRunResult Result){
        string Evidence=string.Join("\n",[Result.StageId,Result.Status.ToString(),
            Result.Summary,Result.StateHash??string.Empty,Result.EventHash??string.Empty,
            Result.StandardOutput,Result.StandardError,
            .. Result.Assertions.Select(static Assertion=>
                $"{Assertion.Name}:{Assertion.Passed}:{Assertion.Details}"),.. Result.Errors]);
        return Sha256(Encoding.UTF8.GetBytes(Evidence));
    }
    private static string ResolveRelative(string Root,string RelativePath){
        if(string.IsNullOrWhiteSpace(RelativePath)||Path.IsPathRooted(RelativePath)
            ||RelativePath.Contains('\\')){
            throw new InvalidDataException(
                "Evidence path must be a canonical forward-slash relative path.");
        }
        string[]Segments=RelativePath.Split('/');
        if(Segments.Any(static Segment=>Segment.Length==0||Segment is "." or "..")){
            throw new InvalidDataException("Evidence path contains an invalid segment.");
        }
        string FullPath=Path.GetFullPath(Path.Combine(
            Path.GetFullPath(Root),RelativePath.Replace('/',Path.DirectorySeparatorChar)));
        if(!IsContained(Root,FullPath)){
            throw new InvalidDataException("Evidence path escaped its root.");
        }
        return FullPath;
    }
    private static bool IsContained(string Root,string Candidate){
        string FullRoot=Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar)
            +Path.DirectorySeparatorChar;
        return Path.GetFullPath(Candidate).StartsWith(
            FullRoot,StringComparison.OrdinalIgnoreCase);
    }
    private static void EnsureNoReparsePoints(string Root,string PathValue){
        string FullRoot=Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar);
        string Current=Path.GetFullPath(PathValue);
        if(!IsContained(FullRoot,Current)
            &&!string.Equals(FullRoot,Current,StringComparison.OrdinalIgnoreCase)){
            throw new InvalidDataException("Evidence path escaped its validation root.");
        }
        while(Current.Length>=FullRoot.Length){
            if((File.Exists(Current)||Directory.Exists(Current))
                &&(File.GetAttributes(Current)&FileAttributes.ReparsePoint)!=0){
                throw new InvalidDataException(
                    "Evidence paths cannot contain filesystem reparse points.");
            }
            if(string.Equals(Current,FullRoot,StringComparison.OrdinalIgnoreCase)){ break; }
            Current=Path.GetDirectoryName(Current)
                ??throw new InvalidDataException("Evidence path has no validation parent.");
        }
    }
    private static string Relative(string Root,string PathValue)=>Path.GetRelativePath(
        Path.GetFullPath(Root),Path.GetFullPath(PathValue)).Replace('\\','/');
    private static string Sha256(byte[]Bytes)=>Convert.ToHexString(
        SHA256.HashData(Bytes)).ToLowerInvariant();
    private static bool IsSha256(string?Value)=>Value is {Length:64}
        &&Value.All(static Character=>Character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool FixedHashEquals(string?Left,string?Right){
        if(Left is null||Right is null||Left.Length!=64||Right.Length!=64){ return false; }
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Left.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(Right.ToLowerInvariant()));
    }
    private static void ValidateConfiguration(string Configuration){
        if(Configuration is not("Debug" or "Release")){
            throw new ArgumentException(
                "Configuration must be Debug or Release.",nameof(Configuration));
        }
    }
    private sealed record InputDefinition(string Role,string Path);
}
