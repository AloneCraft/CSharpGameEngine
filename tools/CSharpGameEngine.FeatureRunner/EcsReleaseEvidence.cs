using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CSharpGameEngine.FeatureStages;

public sealed record EcsReleaseInput(
    string Role,string Path,string Sha256,long ByteLength);
public sealed record EcsReleaseStageEvidence(
    string StageId,string ReportPath,string FileSha256,string ResultArtifactSha256,
    DateTimeOffset StartedUtc,DateTimeOffset FinishedUtc,string Profile,string Adapter);
public sealed record EcsReleaseRawEvidence(
    int ComponentTypeCount,string ReportPath,string FileSha256,string Schema,
    string RuntimeMode,string QueryComponent,int EntityCount,int WorkerCount,
    string GeneratedSourceSha256,string StateSha256,int MaskWordCount,
    bool GeneratedRuntimeContainsHighWord);
public sealed record EcsReleasePerformanceEvidence(
    int ComponentTypeCount,string PlanId,string ReportPath,string FileSha256,
    string ReportSha256,string RawReportSha256,string GeneratedSourceSha256,
    string StateSha256,int MaskWordCount,long MedianNanoseconds,
    long P95Nanoseconds,long AllocationP95Bytes);
public sealed record EcsReleasePayload(
    string Schema,DateTimeOffset CreatedUtc,string Configuration,string Commit,
    bool IsDirty,string TargetOs,string MinimumOsVersion,string Architecture,
    PerformanceMachineFingerprint Machine,
    IReadOnlyList<EcsReleaseStageEvidence>Stages,
    IReadOnlyList<EcsReleaseRawEvidence>RawReports,
    IReadOnlyList<EcsReleasePerformanceEvidence>PerformanceReports,
    IReadOnlyList<EcsReleaseInput>Inputs);
public sealed record EcsReleaseReport(
    string Schema,string ReportSha256,DateTimeOffset CreatedUtc,
    string Configuration,string Commit,bool IsDirty,string TargetOs,
    string MinimumOsVersion,string Architecture,
    PerformanceMachineFingerprint Machine,
    IReadOnlyList<EcsReleaseStageEvidence>Stages,
    IReadOnlyList<EcsReleaseRawEvidence>RawReports,
    IReadOnlyList<EcsReleasePerformanceEvidence>PerformanceReports,
    IReadOnlyList<EcsReleaseInput>Inputs);

/// <summary>
/// Binds ECS schema, schedule, deterministic replay, and performance truths into one
/// track-scoped release input. It is not a package or product release manifest.
/// </summary>
public static class EcsReleaseEvidence {
    public const string ReportSchema="csharp-game-engine-ecs-release-v1";
    public const string ReportFileName="ecs-release.json";
    public const string ReplayProfile="ecs-release-replay";
    public const string ScheduleProfile="ecs-release-schedule";
    public const string SnapshotProfile="ecs-release-snapshot";
    public const string PerformanceProfile="ecs-release-performance";
    private static readonly TimeSpan MaximumEvidenceAge=TimeSpan.FromHours(2);
    private static readonly int[]RequiredComponentTypeCounts=[1,64,65,128];
    private static readonly StageRequirement[]StageRequirements=[
        new("foundation.deterministic-jobs",ReplayProfile),
        new("ecs.schedule-diagnostics",ScheduleProfile),
        new("ecs.snapshot",SnapshotProfile),
        new("ecs.performance",PerformanceProfile),
    ];
    private static readonly InputDefinition[]InputDefinitions=[
        new("ecs.specification","Docs/Specifications/Ecs-Runtime.md"),
        new("release.specification","Docs/Specifications/Testing-CI-Release.md"),
        new("toolchain","global.json"),
        new("entity.runtime","src/CSharpGameEngine.Entities/Runtime.cs"),
        new("entity.jobs","src/CSharpGameEngine.Entities/DeterministicJobs.cs"),
        new("entity.schedule-contract","src/CSharpGameEngine.Entities/SimulationSystems.cs"),
        new("entity.snapshot","src/CSharpGameEngine.Entities/Snapshot.cs"),
        new("entity.snapshot-transient","src/CSharpGameEngine.Entities/EcsSnapshotTransientAttribute.cs"),
        new("generator.project","src/CSharpGameEngine.CodeAnalysis/CSharpGameEngine.CodeAnalysis.csproj"),
        new("generator.index","src/CSharpGameEngine.CodeAnalysis/GameSystemIndexAssigner.cs"),
        new("generator.types","src/CSharpGameEngine.CodeAnalysis/GameSystemTypesEmitter.cs"),
        new("generator.world","src/CSharpGameEngine.CodeAnalysis/GameSystemWorldEmitter.cs"),
        new("generator.loop","src/CSharpGameEngine.CodeAnalysis/GameSystemLoopEmitter.cs"),
        new("generator.schedule","src/CSharpGameEngine.CodeAnalysis/GameSystemScheduleAnalyzer.cs"),
        new("generator.snapshot","src/CSharpGameEngine.CodeAnalysis/GameSystemSnapshotEmitter.cs"),
        new("benchmark.project","tools/CSharpGameEngine.Ecs.Benchmarks/CSharpGameEngine.Ecs.Benchmarks.csproj"),
        new("benchmark.capture","tools/CSharpGameEngine.Ecs.Benchmarks/Program.cs"),
        new("benchmark.components","tools/CSharpGameEngine.Ecs.Benchmarks/EcsBenchmarkComponents.cs"),
        new("performance.probe","tools/CSharpGameEngine.FeatureRunner/EcsPerformanceProbe.cs"),
        new("performance.framework","tools/CSharpGameEngine.FeatureRunner/PerformanceMeasurementFramework.cs"),
        new("release.evidence","tools/CSharpGameEngine.FeatureRunner/EcsReleaseEvidence.cs"),
        new("stage.catalog","tools/CSharpGameEngine.FeatureRunner/FeatureStageCatalog.cs"),
    ];
    private static readonly JsonSerializerOptions JsonOptions=
        FeatureRunResultStore.CreateJsonOptions();
    private static readonly Regex RawPathPattern=new(
        @"ECS performance raw report: count=(?<count>\d+) path=(?<path>[^\r\n]+?\.raw\.json)",
        RegexOptions.Compiled|RegexOptions.CultureInvariant);
    private static readonly Regex PerformancePathPattern=new(
        @"report=(?<path>[^\r\n]+?\.performance\.json)",
        RegexOptions.Compiled|RegexOptions.CultureInvariant);

    public static IEnumerable<string>RequiredRepositoryInputPaths=>
        InputDefinitions.Select(static Definition=>Definition.Path);

    public static async Task<int>RunAsync(
        string RepositoryRoot,string Configuration,TextWriter Output,
        TextWriter Error,CancellationToken CancellationToken){
        string Root=Path.GetFullPath(RepositoryRoot);
        ValidateConfiguration(Configuration);
        DateTimeOffset CreatedUtc=DateTimeOffset.UtcNow;
        (string Commit,bool IsDirty)Provenance;
        try {
            Provenance=await GitChangedPathProvider.ReadProvenanceAsync(
                Root,CancellationToken).ConfigureAwait(false);
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Provenance=("unknown",true);
            await Error.WriteLineAsync(
                $"Warning: git provenance is unavailable: {Ex.Message}")
                .ConfigureAwait(false);
        }
        string EvidenceRoot=Path.Combine(
            Root,".artifacts","EcsRelease",
            $"{CreatedUtc:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        string ReportPath=Path.Combine(EvidenceRoot,ReportFileName);
        bool Completed=false;
        try {
            FeatureStageCatalog Catalog=FeatureStageCatalog.CreateDefault();
            EcsReleaseStageEvidence[]Stages=[.. StageRequirements.Select(
                Requirement=>FindStageEvidence(
                    Root,Requirement,Configuration,Provenance,CreatedUtc,Catalog))];
            EcsReleaseStageEvidence PerformanceStage=
                Stages.Single(static Stage=>Stage.StageId=="ecs.performance");
            EcsReleaseRawEvidence[]RawReports=ReadRawEvidence(
                Root,PerformanceStage,Configuration,true);
            EcsReleasePerformanceEvidence[]PerformanceReports=
                ReadPerformanceEvidence(
                    Root,PerformanceStage,RawReports,Configuration,Provenance);
            EcsReleaseInput[]Inputs=ReadInputs(Root);
            PerformanceMachineFingerprint Machine=
                PerformanceMeasurementFramework.CaptureMachineFingerprint();
            var Payload=new EcsReleasePayload(
                ReportSchema,CreatedUtc,Configuration,Provenance.Commit,
                Provenance.IsDirty,"Windows","10.0.19041.0","x64",Machine,
                Stages,RawReports,PerformanceReports,Inputs);
            string Hash=Sha256(
                JsonSerializer.SerializeToUtf8Bytes(Payload,JsonOptions));
            var Report=new EcsReleaseReport(
                Payload.Schema,Hash,Payload.CreatedUtc,Payload.Configuration,
                Payload.Commit,Payload.IsDirty,Payload.TargetOs,
                Payload.MinimumOsVersion,Payload.Architecture,Payload.Machine,
                Payload.Stages,Payload.RawReports,Payload.PerformanceReports,
                Payload.Inputs);
            Directory.CreateDirectory(EvidenceRoot);
            await WriteCanonicalAsync(ReportPath,Report,CancellationToken)
                .ConfigureAwait(false);
            IReadOnlyList<string>Errors=await ValidateAsync(
                Root,ReportPath,CancellationToken).ConfigureAwait(false);
            foreach(string ValidationError in Errors){
                await Error.WriteLineAsync(ValidationError).ConfigureAwait(false);
            }
            if(Errors.Count!=0){ return 1; }
            Completed=true;
            await Output.WriteLineAsync(
                $"ecsRelease stages={Stages.Length} rawReports={RawReports.Length} "
                +$"performanceReports={PerformanceReports.Length} inputs={Inputs.Length} "
                +$"machine={Machine.FingerprintSha256}").ConfigureAwait(false);
            await Output.WriteLineAsync(ReportPath).ConfigureAwait(false);
            return 0;
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            await Error.WriteLineAsync(
                $"ECS release verification failed: {Ex.Message}")
                .ConfigureAwait(false);
            return 1;
        } finally {
            if(!Completed&&Directory.Exists(EvidenceRoot)){
                Directory.Delete(EvidenceRoot,true);
            }
        }
    }

    public static async Task<IReadOnlyList<string>>ValidateAsync(
        string RepositoryRoot,string ReportPath,
        CancellationToken CancellationToken=default){
        var Errors=new List<string>();
        string Root;
        string FullReportPath;
        byte[]Bytes;
        EcsReleaseReport?Report;
        try {
            Root=Path.GetFullPath(RepositoryRoot);
            FullReportPath=Path.GetFullPath(ReportPath);
            if(!IsContained(
                Path.Combine(Root,".artifacts","EcsRelease"),FullReportPath)){
                return ["ECS release report must be inside .artifacts/EcsRelease."];
            }
            EnsureNoReparsePoints(Root,FullReportPath);
            Bytes=await File.ReadAllBytesAsync(
                FullReportPath,CancellationToken).ConfigureAwait(false);
            Report=JsonSerializer.Deserialize<EcsReleaseReport>(
                Bytes,JsonOptions);
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            return [$"ECS release report cannot be read: {Ex.Message}"];
        }
        if(Report is null){ return ["ECS release report is empty."]; }
        try {
            ValidateConfiguration(Report.Configuration);
            if(!Bytes.AsSpan().SequenceEqual(CanonicalBytes(Report))){
                Errors.Add("ECS release report is not canonically encoded.");
            }
            if(Report.Schema!=ReportSchema){
                Errors.Add($"ECS release report schema must be {ReportSchema}.");
            }
            var Payload=new EcsReleasePayload(
                Report.Schema,Report.CreatedUtc,Report.Configuration,
                Report.Commit,Report.IsDirty,Report.TargetOs,
                Report.MinimumOsVersion,Report.Architecture,Report.Machine,
                Report.Stages,Report.RawReports,Report.PerformanceReports,
                Report.Inputs);
            if(!FixedHashEquals(
                Sha256(JsonSerializer.SerializeToUtf8Bytes(Payload,JsonOptions)),
                Report.ReportSha256)){
                Errors.Add("ECS release report SHA-256 mismatch.");
            }
            if(Report.TargetOs!="Windows"
                ||Report.MinimumOsVersion!="10.0.19041.0"
                ||Report.Architecture!="x64"){
                Errors.Add(
                    "ECS release target must be Windows 10.0.19041.0+ x64.");
            }
            if(!FixedHashEquals(
                PerformanceMeasurementFramework.HashFingerprint(Report.Machine),
                Report.Machine.FingerprintSha256)){
                Errors.Add("ECS release machine fingerprint mismatch.");
            }
            if(Report.Machine.OsArchitecture!="X64"
                ||Report.Machine.ProcessArchitecture!="X64"){
                Errors.Add(
                    "ECS release evidence must be captured by an x64 OS and process.");
            }
            ValidateInputs(Root,Report.Inputs,Errors);
            ValidateStages(Root,Report,Errors);
            ValidateRawReports(Root,Report,Errors);
            ValidatePerformanceReports(Root,Report,Errors);
            string[]Files=Directory.EnumerateFiles(
                Path.GetDirectoryName(FullReportPath)!,"*",
                SearchOption.AllDirectories).ToArray();
            if(Files.Length!=1||!string.Equals(
                Path.GetFullPath(Files[0]),FullReportPath,
                StringComparison.OrdinalIgnoreCase)){
                Errors.Add(
                    "ECS release evidence directory contains missing or extra files.");
            }
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Errors.Add($"ECS release evidence validation failed: {Ex.Message}");
        }
        return Errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static EcsReleaseStageEvidence FindStageEvidence(
        string Root,StageRequirement Requirement,string Configuration,
        (string Commit,bool IsDirty)Provenance,DateTimeOffset CreatedUtc,
        FeatureStageCatalog Catalog){
        string StageRoot=Path.Combine(Root,".artifacts","FeatureStages");
        if(!Directory.Exists(StageRoot)){
            throw new InvalidDataException(
                $"ECS release requires a recent passing {Requirement.StageId} Stage result.");
        }
        var Candidates=new List<(string Path,FeatureStageRunResult Result)>();
        foreach(string PathValue in Directory.EnumerateFiles(
            StageRoot,"*.run-result.json",SearchOption.TopDirectoryOnly)){
            try {
                EnsureNoReparsePoints(Root,PathValue);
                FeatureRunResultDocument?Document=
                    JsonSerializer.Deserialize<FeatureRunResultDocument>(
                        File.ReadAllBytes(PathValue),JsonOptions);
                if(Document?.SchemaVersion!=
                    FeatureRunResultDocument.CurrentSchemaVersion){
                    continue;
                }
                Candidates.AddRange(Document.Results.Where(Result=>
                    Result.StageId==Requirement.StageId
                    &&Result.Configuration==Configuration
                    &&Result.Profile==Requirement.Profile
                    &&Result.Status==FeatureStageRunStatus.Passed
                    &&Result.Adapter=="Warp"
                    &&Result.GitCommit==Provenance.Commit
                    &&Result.IsDirty==Provenance.IsDirty
                    &&Result.FinishedUtc<=CreatedUtc
                    &&CreatedUtc-Result.FinishedUtc<=MaximumEvidenceAge
                    &&ValidateStageResult(
                        Result,Requirement.StageId,Catalog).Count==0)
                    .Select(Result=>(PathValue,Result)));
            } catch(Exception Ex)when(
                Ex is not OperationCanceledException
                    &&Ex is not OutOfMemoryException
                    &&Ex is not StackOverflowException){ }
        }
        (string Path,FeatureStageRunResult Result)Match=Candidates
            .OrderByDescending(static Candidate=>Candidate.Result.FinishedUtc)
            .ThenByDescending(
                static Candidate=>Candidate.Path,StringComparer.Ordinal)
            .FirstOrDefault();
        if(Match.Result is null){
            throw new InvalidDataException(
                $"ECS release requires a recent passing {Requirement.StageId} result "
                +$"for {Configuration} with {Requirement.Profile} profile, "
                +"matching Git provenance, and Warp adapter.");
        }
        return new EcsReleaseStageEvidence(
            Requirement.StageId,Relative(Root,Match.Path),
            Sha256(File.ReadAllBytes(Match.Path)),Match.Result.ArtifactSha256,
            Match.Result.StartedUtc,Match.Result.FinishedUtc,
            Match.Result.Profile,Match.Result.Adapter);
    }

    private static void ValidateStages(
        string Root,EcsReleaseReport Report,List<string>Errors){
        if(Report.Stages is null
            ||Report.Stages.Count!=StageRequirements.Length){
            Errors.Add("ECS release Stage closure is incomplete.");return;
        }
        for(int Index=0;Index<StageRequirements.Length;Index++){
            StageRequirement Requirement=StageRequirements[Index];
            EcsReleaseStageEvidence Evidence=Report.Stages[Index];
            if(Evidence.StageId!=Requirement.StageId){
                Errors.Add($"ECS release Stage {Index} is not canonical.");
                continue;
            }
            try {
                string PathValue=ResolveRelative(Root,Evidence.ReportPath);
                if(!IsContained(
                    Path.Combine(Root,".artifacts","FeatureStages"),PathValue)){
                    throw new InvalidDataException(
                        $"{Requirement.StageId} result escaped .artifacts/FeatureStages.");
                }
                EnsureNoReparsePoints(Root,PathValue);
                byte[]StageBytes=File.ReadAllBytes(PathValue);
                if(!FixedHashEquals(
                    Sha256(StageBytes),Evidence.FileSha256)){
                    Errors.Add(
                        $"ECS {Requirement.StageId} result file SHA-256 mismatch.");
                    continue;
                }
                FeatureStageRunResult Result=ReadBoundStageResult(Root,Evidence);
                Errors.AddRange(ValidateStageResult(
                    Result,Requirement.StageId,
                    FeatureStageCatalog.CreateDefault()));
                if(Result.Configuration!=Report.Configuration
                    ||Result.GitCommit!=Report.Commit
                    ||Result.IsDirty!=Report.IsDirty
                    ||Result.StartedUtc!=Evidence.StartedUtc
                    ||Result.FinishedUtc!=Evidence.FinishedUtc
                    ||Result.Profile!=Requirement.Profile
                    ||Result.Profile!=Evidence.Profile
                    ||Result.Adapter!="Warp"
                    ||Result.Adapter!=Evidence.Adapter
                    ||Result.FinishedUtc>Report.CreatedUtc
                    ||Report.CreatedUtc-Result.FinishedUtc>MaximumEvidenceAge){
                    Errors.Add(
                        $"ECS {Requirement.StageId} result provenance does not match release report.");
                }
            } catch(Exception Ex)when(Ex is not OperationCanceledException){
                Errors.Add(
                    $"ECS {Requirement.StageId} result cannot be validated: {Ex.Message}");
            }
        }
    }

    private static EcsReleaseRawEvidence[]ReadRawEvidence(
        string Root,EcsReleaseStageEvidence Stage,string Configuration,
        bool ValidateGeneratedOutput){
        FeatureStageRunResult Result=ReadBoundStageResult(Root,Stage);
        Match[]Matches=[.. RawPathPattern.Matches(Result.StandardOutput)
            .Cast<Match>()];
        if(Matches.Length!=RequiredComponentTypeCounts.Length){
            throw new InvalidDataException(
                "ECS performance Stage must publish exactly four raw reports.");
        }
        var Reports=new List<(
            int DeclaredCount,string FullPath,byte[]Bytes,
            EcsRawPerformanceReport Report)>();
        foreach(Match Match in Matches){
            int DeclaredCount=int.Parse(
                Match.Groups["count"].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            string FullPath=Path.GetFullPath(
                Match.Groups["path"].Value.Trim());
            if(!IsContained(
                Path.Combine(Root,".artifacts","EcsPerformance"),FullPath)){
                throw new InvalidDataException(
                    "ECS raw report escaped .artifacts/EcsPerformance.");
            }
            EnsureNoReparsePoints(Root,FullPath);
            byte[]RawBytes=File.ReadAllBytes(FullPath);
            EcsRawPerformanceReport Raw=
                JsonSerializer.Deserialize<EcsRawPerformanceReport>(
                    RawBytes,JsonOptions)
                ??throw new InvalidDataException("ECS raw report is empty.");
            if(DeclaredCount!=Raw.ComponentTypeCount){
                throw new InvalidDataException(
                    "ECS raw report boundary disagrees with Stage output.");
            }
            Reports.Add((DeclaredCount,FullPath,RawBytes,Raw));
        }
        IReadOnlyList<string>ValidationErrors=
            EcsPerformanceProbe.ValidateRawReports(
                Reports.Select(static Entry=>Entry.Report).ToArray(),
                Configuration);
        if(ValidationErrors.Count!=0){
            throw new InvalidDataException(
                "ECS raw report failed validation: "
                +string.Join(" ",ValidationErrors));
        }
        var Evidence=new List<EcsReleaseRawEvidence>();
        foreach(var Entry in Reports.OrderBy(
            static Entry=>Entry.Report.ComponentTypeCount)){
            if(ValidateGeneratedOutput){
                (string Sha256,bool ContainsHighWord)Source=
                    EcsPerformanceProbe.ReadGeneratedSourceEvidence(
                        Root,Configuration,Entry.Report.ComponentTypeCount);
                if(Source.Sha256!=Entry.Report.GeneratedSourceSha256
                    ||Source.ContainsHighWord!=
                        Entry.Report.GeneratedRuntimeContainsHighWord){
                    throw new InvalidDataException(
                        $"ECS {Entry.Report.ComponentTypeCount}-type generated source disagrees with raw evidence.");
                }
            }
            Evidence.Add(new EcsReleaseRawEvidence(
                Entry.Report.ComponentTypeCount,Relative(Root,Entry.FullPath),
                Sha256(Entry.Bytes),Entry.Report.Schema,
                Entry.Report.RuntimeMode,Entry.Report.QueryComponent,
                Entry.Report.EntityCount,Entry.Report.WorkerCount,
                Entry.Report.GeneratedSourceSha256,Entry.Report.StateSha256,
                Entry.Report.MaskWordCount,
                Entry.Report.GeneratedRuntimeContainsHighWord));
        }
        return [.. Evidence];
    }

    private static EcsReleasePerformanceEvidence[]ReadPerformanceEvidence(
        string Root,EcsReleaseStageEvidence Stage,
        IReadOnlyList<EcsReleaseRawEvidence>RawReports,string Configuration,
        (string Commit,bool IsDirty)Provenance){
        FeatureStageRunResult StageResult=ReadBoundStageResult(Root,Stage);
        string[]Paths=[.. PerformancePathPattern.Matches(
            StageResult.StandardOutput).Cast<Match>()
            .Select(static Match=>Match.Groups["path"].Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        if(Paths.Length!=RequiredComponentTypeCounts.Length){
            throw new InvalidDataException(
                "ECS performance Stage must publish exactly four common reports.");
        }
        EcsReleasePerformanceEvidence[]Evidence=[.. Paths.Select(Path=>
            ReadPerformanceReport(
                Root,Path,StageResult,RawReports,Configuration,Provenance))
            .OrderBy(static Entry=>Entry.ComponentTypeCount)];
        if(!Evidence.Select(static Entry=>Entry.ComponentTypeCount)
            .SequenceEqual(RequiredComponentTypeCounts)){
            throw new InvalidDataException(
                "ECS performance report boundary closure is incomplete.");
        }
        return Evidence;
    }

    private static EcsReleasePerformanceEvidence ReadPerformanceReport(
        string Root,string PathValue,FeatureStageRunResult Stage,
        IReadOnlyList<EcsReleaseRawEvidence>RawReports,string Configuration,
        (string Commit,bool IsDirty)Provenance){
        string FullPath=Path.GetFullPath(PathValue);
        if(!IsContained(
            Path.Combine(Root,".artifacts","Performance"),FullPath)){
            throw new InvalidDataException(
                "ECS performance report escaped .artifacts/Performance.");
        }
        EnsureNoReparsePoints(Root,FullPath);
        IReadOnlyList<string>IntegrityErrors=
            PerformanceReportStore.Validate(FullPath);
        if(IntegrityErrors.Count!=0){
            throw new InvalidDataException(
                "ECS performance report failed integrity validation: "
                +string.Join(" ",IntegrityErrors));
        }
        byte[]Bytes=File.ReadAllBytes(FullPath);
        PerformanceReport Report=JsonSerializer.Deserialize<PerformanceReport>(
            Bytes,JsonOptions)
            ??throw new InvalidDataException(
                "ECS performance report is empty.");
        if(Report.Plan.Configuration!=Configuration
            ||Report.Commit!=Provenance.Commit
            ||Report.IsDirty!=Provenance.IsDirty
            ||!Report.Analysis.Passed
            ||Report.CreatedUtc<Stage.StartedUtc
            ||Report.CreatedUtc>Stage.FinishedUtc){
            throw new InvalidDataException(
                "ECS performance report provenance or verdict does not match release.");
        }
        IReadOnlyDictionary<string,string>Metadata=Report.Plan.Workload;
        if(!int.TryParse(
                Metadata.GetValueOrDefault("componentTypeCount"),
                out int ComponentTypeCount)
            ||!RequiredComponentTypeCounts.Contains(ComponentTypeCount)
            ||Report.Plan.Id!=$"ecs.generated-query-{ComponentTypeCount}"
            ||Metadata.GetValueOrDefault("activeEntities")!="100000"
            ||Metadata.GetValueOrDefault("workerCount")!="1"
            ||Metadata.GetValueOrDefault("runtimeMode")!=
                EcsPerformanceProbe.RequiredRuntimeMode
            ||Metadata.GetValueOrDefault("queryShape")!=
                $"write:C{ComponentTypeCount-1}"
            ||!IsSha256(Metadata.GetValueOrDefault("rawReportSha256"))
            ||!IsSha256(Metadata.GetValueOrDefault("generatedSourceSha256"))
            ||!IsSha256(Metadata.GetValueOrDefault("stateSha256"))){
            throw new InvalidDataException(
                "ECS performance workload metadata is incomplete or non-canonical.");
        }
        EcsReleaseRawEvidence Raw=RawReports.Single(
            Entry=>Entry.ComponentTypeCount==ComponentTypeCount);
        if(Metadata["rawReportSha256"]!=Raw.FileSha256
            ||Metadata["generatedSourceSha256"]!=Raw.GeneratedSourceSha256
            ||Metadata["stateSha256"]!=Raw.StateSha256
            ||Metadata.GetValueOrDefault("maskWordCount")!=
                Raw.MaskWordCount.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
            ||Metadata.GetValueOrDefault(
                "generatedRuntimeContainsHighWord")!=
                (Raw.GeneratedRuntimeContainsHighWord?"true":"false")){
            throw new InvalidDataException(
                "ECS performance report does not match its bound raw report.");
        }
        if(Configuration=="Release"&&ComponentTypeCount==64
            &&Report.Analysis.BaselineDelta.Status!=
                PerformanceGateStatus.Passed){
            throw new InvalidDataException(
                "ECS 64-type performance report lacks its passing one-type baseline gate.");
        }
        if((Configuration=="Debug"||ComponentTypeCount!=64)
            &&Report.Analysis.BaselineDelta.Status!=
                PerformanceGateStatus.NotEvaluated){
            throw new InvalidDataException(
                "Only the Release ECS 64-type report may own the one-type baseline gate.");
        }
        return new EcsReleasePerformanceEvidence(
            ComponentTypeCount,Report.Plan.Id,Relative(Root,FullPath),
            Sha256(Bytes),Report.ReportSha256,Raw.FileSha256,
            Raw.GeneratedSourceSha256,Raw.StateSha256,Raw.MaskWordCount,
            Report.Analysis.ElapsedNanoseconds.Median,
            Report.Analysis.ElapsedNanoseconds.P95,
            Report.Analysis.AllocatedBytes.P95);
    }

    private static void ValidateRawReports(
        string Root,EcsReleaseReport Report,List<string>Errors){
        if(Report.RawReports is null
            ||Report.RawReports.Count!=RequiredComponentTypeCounts.Length){
            Errors.Add("ECS release raw report closure is incomplete.");return;
        }
        EcsReleaseStageEvidence?PerformanceStage=Report.Stages?
            .FirstOrDefault(static Stage=>Stage.StageId=="ecs.performance");
        if(PerformanceStage is null){
            Errors.Add("ECS release performance Stage evidence is missing.");return;
        }
        try {
            EcsReleaseRawEvidence[]Expected=ReadRawEvidence(
                Root,PerformanceStage,Report.Configuration,false);
            EcsReleaseRawEvidence[]Actual=[.. Report.RawReports.OrderBy(
                static Entry=>Entry.ComponentTypeCount)];
            for(int Index=0;Index<Expected.Length;Index++){
                if(Actual[Index].ReportPath!=Expected[Index].ReportPath
                    ||!FixedHashEquals(
                        Actual[Index].FileSha256,Expected[Index].FileSha256)){
                    Errors.Add(
                        $"ECS raw report file SHA-256 mismatch: {Actual[Index].ComponentTypeCount}.");
                } else if(Actual[Index]!=Expected[Index]){
                    Errors.Add(
                        $"ECS raw report evidence changed: {Actual[Index].ComponentTypeCount}.");
                }
            }
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Errors.Add($"ECS raw reports cannot be validated: {Ex.Message}");
        }
    }

    private static void ValidatePerformanceReports(
        string Root,EcsReleaseReport Report,List<string>Errors){
        if(Report.PerformanceReports is null
            ||Report.PerformanceReports.Count!=RequiredComponentTypeCounts.Length){
            Errors.Add(
                "ECS release performance report closure is incomplete.");return;
        }
        EcsReleaseStageEvidence?PerformanceStage=Report.Stages?
            .FirstOrDefault(static Stage=>Stage.StageId=="ecs.performance");
        if(PerformanceStage is null){ return; }
        try {
            EcsReleasePerformanceEvidence[]Expected=ReadPerformanceEvidence(
                Root,PerformanceStage,Report.RawReports,Report.Configuration,
                (Report.Commit,Report.IsDirty));
            EcsReleasePerformanceEvidence[]Actual=[..
                Report.PerformanceReports.OrderBy(
                    static Entry=>Entry.ComponentTypeCount)];
            for(int Index=0;Index<Expected.Length;Index++){
                if(Actual[Index].ReportPath!=Expected[Index].ReportPath
                    ||!FixedHashEquals(
                        Actual[Index].FileSha256,Expected[Index].FileSha256)){
                    Errors.Add(
                        $"ECS performance report file SHA-256 mismatch: {Actual[Index].ComponentTypeCount}.");
                } else if(Actual[Index]!=Expected[Index]){
                    Errors.Add(
                        $"ECS performance report evidence changed: {Actual[Index].ComponentTypeCount}.");
                }
                PerformanceReport Performance=
                    JsonSerializer.Deserialize<PerformanceReport>(
                        File.ReadAllBytes(ResolveRelative(
                            Root,Expected[Index].ReportPath)),JsonOptions)
                    ??throw new InvalidDataException(
                        "ECS performance report is empty.");
                if(Performance.Machine.FingerprintSha256!=
                    Report.Machine.FingerprintSha256){
                    Errors.Add(
                        $"ECS performance machine does not match release: {Expected[Index].ComponentTypeCount}.");
                }
            }
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Errors.Add(
                $"ECS performance reports cannot be validated: {Ex.Message}");
        }
    }

    private static FeatureStageRunResult ReadBoundStageResult(
        string Root,EcsReleaseStageEvidence Evidence){
        string PathValue=ResolveRelative(Root,Evidence.ReportPath);
        FeatureRunResultDocument?Document=
            JsonSerializer.Deserialize<FeatureRunResultDocument>(
                File.ReadAllBytes(PathValue),JsonOptions);
        if(Document?.SchemaVersion!=
            FeatureRunResultDocument.CurrentSchemaVersion){
            throw new InvalidDataException("ECS Stage result schema mismatch.");
        }
        return Document.Results.FirstOrDefault(Result=>
            Result.StageId==Evidence.StageId
            &&Result.ArtifactSha256==Evidence.ResultArtifactSha256)
            ??throw new InvalidDataException(
                $"Bound {Evidence.StageId} result is missing.");
    }

    private static IReadOnlyList<string>ValidateStageResult(
        FeatureStageRunResult Result,string StageId,
        FeatureStageCatalog Catalog){
        var Errors=new List<string>();
        FeatureStageDefinition Stage=Catalog.GetStage(StageId);
        if(Result.Status!=FeatureStageRunStatus.Passed
            ||Result.Pack!=Stage.Pack.ToString()
            ||Result.Adapter!="Warp"
            ||!Result.FeatureIds.SequenceEqual(
                Stage.FeatureIds,StringComparer.Ordinal)
            ||Result.Errors.Count!=0
            ||Result.Assertions.Count!=Stage.Commands.Count
            ||Result.Assertions.Any(static Assertion=>!Assertion.Passed)){
            Errors.Add(
                $"Bound {StageId} result is not a complete passing Warp verdict.");
        }
        if(!FixedHashEquals(
            ComputeStageArtifactHash(Result),Result.ArtifactSha256)){
            Errors.Add(
                $"Bound {StageId} result artifact SHA-256 mismatch.");
        }
        if(Result.FinishedUtc<Result.StartedUtc
            ||Result.DurationMilliseconds<0){
            Errors.Add($"Bound {StageId} result has invalid timing.");
        }
        return Errors;
    }

    private static EcsReleaseInput[]ReadInputs(string Root)=>[
        .. InputDefinitions.Select(Definition=>{
            string PathValue=ResolveRelative(Root,Definition.Path);
            EnsureNoReparsePoints(Root,PathValue);
            byte[]Bytes=File.ReadAllBytes(PathValue);
            return new EcsReleaseInput(
                Definition.Role,Definition.Path,Sha256(Bytes),
                Bytes.LongLength);
        })];

    private static void ValidateInputs(
        string Root,IReadOnlyList<EcsReleaseInput>?Inputs,
        List<string>Errors){
        if(Inputs is null||Inputs.Count!=InputDefinitions.Length){
            Errors.Add("ECS release input closure is incomplete.");return;
        }
        for(int Index=0;Index<InputDefinitions.Length;Index++){
            InputDefinition Definition=InputDefinitions[Index];
            EcsReleaseInput Entry=Inputs[Index];
            if(Entry.Role!=Definition.Role||Entry.Path!=Definition.Path){
                Errors.Add(
                    $"ECS release input {Index} is not canonical.");
                continue;
            }
            try {
                string PathValue=ResolveRelative(Root,Entry.Path);
                EnsureNoReparsePoints(Root,PathValue);
                byte[]Bytes=File.ReadAllBytes(PathValue);
                if(Entry.ByteLength!=Bytes.LongLength
                    ||!FixedHashEquals(Sha256(Bytes),Entry.Sha256)){
                    Errors.Add(
                        $"ECS release input changed: {Entry.Path}.");
                }
            } catch(Exception Ex)when(Ex is not OperationCanceledException){
                Errors.Add(
                    $"ECS release input cannot be validated: {Entry.Path}: {Ex.Message}");
            }
        }
    }

    private static async Task WriteCanonicalAsync(
        string PathValue,EcsReleaseReport Report,
        CancellationToken CancellationToken){
        string Temporary=PathValue+".tmp";
        try {
            await File.WriteAllBytesAsync(
                Temporary,CanonicalBytes(Report),CancellationToken)
                .ConfigureAwait(false);
            File.Move(Temporary,PathValue,true);
        } finally {
            if(File.Exists(Temporary)){ File.Delete(Temporary); }
        }
    }

    private static byte[]CanonicalBytes(EcsReleaseReport Report){
        byte[]Json=JsonSerializer.SerializeToUtf8Bytes(Report,JsonOptions);
        byte[]Result=new byte[Json.Length+1];
        Json.CopyTo(Result,0);
        Result[^1]=(byte)'\n';
        return Result;
    }

    private static string ComputeStageArtifactHash(
        FeatureStageRunResult Result){
        string Evidence=string.Join("\n",[
            Result.StageId,Result.Status.ToString(),Result.Summary,
            Result.StateHash??string.Empty,Result.EventHash??string.Empty,
            Result.StandardOutput,Result.StandardError,
            .. Result.Assertions.Select(static Assertion=>
                $"{Assertion.Name}:{Assertion.Passed}:{Assertion.Details}"),
            .. Result.Errors]);
        return Sha256(Encoding.UTF8.GetBytes(Evidence));
    }

    private static string ResolveRelative(
        string Root,string RelativePath){
        if(string.IsNullOrWhiteSpace(RelativePath)
            ||Path.IsPathRooted(RelativePath)
            ||RelativePath.Contains('\\')){
            throw new InvalidDataException(
                "Evidence path must be a canonical forward-slash relative path.");
        }
        string[]Segments=RelativePath.Split('/');
        if(Segments.Any(static Segment=>
            Segment.Length==0||Segment is "." or "..")){
            throw new InvalidDataException(
                "Evidence path contains an invalid segment.");
        }
        string FullPath=Path.GetFullPath(Path.Combine(
            Path.GetFullPath(Root),
            RelativePath.Replace('/',Path.DirectorySeparatorChar)));
        if(!IsContained(Root,FullPath)){
            throw new InvalidDataException(
                "Evidence path escaped its root.");
        }
        return FullPath;
    }

    private static bool IsContained(string Root,string Candidate){
        string FullRoot=Path.GetFullPath(Root)
            .TrimEnd(Path.DirectorySeparatorChar)
            +Path.DirectorySeparatorChar;
        return Path.GetFullPath(Candidate).StartsWith(
            FullRoot,StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureNoReparsePoints(
        string Root,string PathValue){
        string FullRoot=Path.GetFullPath(Root)
            .TrimEnd(Path.DirectorySeparatorChar);
        string Current=Path.GetFullPath(PathValue);
        if(!IsContained(FullRoot,Current)
            &&!string.Equals(
                FullRoot,Current,StringComparison.OrdinalIgnoreCase)){
            throw new InvalidDataException(
                "Evidence path escaped its validation root.");
        }
        while(Current.Length>=FullRoot.Length){
            if((File.Exists(Current)||Directory.Exists(Current))
                &&(File.GetAttributes(Current)&FileAttributes.ReparsePoint)!=0){
                throw new InvalidDataException(
                    "Evidence paths cannot contain filesystem reparse points.");
            }
            if(string.Equals(
                Current,FullRoot,StringComparison.OrdinalIgnoreCase)){
                break;
            }
            Current=Path.GetDirectoryName(Current)
                ??throw new InvalidDataException(
                    "Evidence path has no validation parent.");
        }
    }

    private static string Relative(
        string Root,string PathValue)=>Path.GetRelativePath(
            Path.GetFullPath(Root),Path.GetFullPath(PathValue))
            .Replace('\\','/');

    private static string Sha256(byte[]Bytes)=>Convert.ToHexString(
        SHA256.HashData(Bytes)).ToLowerInvariant();

    private static bool IsSha256(string?Value)=>
        Value is {Length:64}&&Value.All(static Character=>
            Character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool FixedHashEquals(string?Left,string?Right){
        if(Left is null||Right is null
            ||Left.Length!=64||Right.Length!=64){
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Left.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(Right.ToLowerInvariant()));
    }

    private static void ValidateConfiguration(string Configuration){
        if(Configuration is not("Debug" or "Release")){
            throw new ArgumentException(
                "Configuration must be Debug or Release.",
                nameof(Configuration));
        }
    }

    private sealed record InputDefinition(string Role,string Path);
    private sealed record StageRequirement(string StageId,string Profile);
}
