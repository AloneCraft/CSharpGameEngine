using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CSharpGameEngine.FeatureStages;

public sealed record ReleaseCandidateScope(
    int ReachedLevel,IReadOnlyList<string>Tracks,
    IReadOnlyList<string>RequiredFeatureIds,IReadOnlyList<string>RequiredStageIds);
public sealed record ReleaseCandidateFeature(
    string FeatureId,string Track,int DependencyLevel,string LifecycleStatus,
    IReadOnlyList<string>DependsOnFeatureIds);
public sealed record ReleaseCandidateStageEvidence(
    string StageId,string ResultPath,string ResultFileSha256,string ArtifactSha256,
    DateTimeOffset FinishedUtc,IReadOnlyList<string>FeatureIds);
public sealed record ReleaseCandidateInput(
    string Role,string Path,string Sha256,long ByteLength);
public sealed record ReleaseCandidateReportPayload(
    string Schema,DateTimeOffset CreatedUtc,string Configuration,string Adapter,
    string Commit,bool IsDirty,int ReachedLevel,IReadOnlyList<string>Tracks,
    IReadOnlyList<ReleaseCandidateFeature>Features,
    IReadOnlyList<ReleaseCandidateStageEvidence>Stages,
    IReadOnlyList<ReleaseCandidateInput>Inputs);
public sealed record ReleaseCandidateReport(
    string Schema,string ReportSha256,DateTimeOffset CreatedUtc,string Configuration,
    string Adapter,string Commit,bool IsDirty,int ReachedLevel,
    IReadOnlyList<string>Tracks,IReadOnlyList<ReleaseCandidateFeature>Features,
    IReadOnlyList<ReleaseCandidateStageEvidence>Stages,
    IReadOnlyList<ReleaseCandidateInput>Inputs);

/// <summary>
/// Derives one RC prerequisite closure from the Catalog and binds recent Release/WARP Stage
/// verdicts. Stage results remain the execution truth; this report is only their immutable,
/// Level-gated selection and provenance closure.
/// </summary>
public static class ReleaseCandidateEvidence {
    public const string ReportSchema="csharp-game-engine-release-candidate-v1";
    public const string ReportFileName="release-candidate.json";
    public const string PrerequisiteProfile="release-candidate-prerequisite";
    public static readonly TimeSpan MaximumEvidenceAge=TimeSpan.FromHours(24);
    public static IReadOnlyList<FeatureRoadmapTrack>ProductTracks{get;}=[
        FeatureRoadmapTrack.Established,
        FeatureRoadmapTrack.Rendering,
        FeatureRoadmapTrack.AssetContent,
        FeatureRoadmapTrack.EditorVsix,
        FeatureRoadmapTrack.EcsRuntime,
        FeatureRoadmapTrack.WorldScale,
        FeatureRoadmapTrack.AudioInputPlatform,
        FeatureRoadmapTrack.TestingCiRelease,
    ];
    private static readonly JsonSerializerOptions JsonOptions=
        FeatureRunResultStore.CreateJsonOptions();
    private static readonly HashSet<string>ControlFeatureIds=
        new(["F-L7-047","F-L7-048"],StringComparer.Ordinal);
    private static readonly InputDefinition[]InputDefinitions=[
        new("release.specification","Docs/Specifications/Testing-CI-Release.md"),
        new("release.roadmap","Docs/Roadmap.md"),
        new("release.catalog","tools/CSharpGameEngine.FeatureRunner/FeatureStageCatalog.cs"),
        new("release.compatibility","Docs/Verification/Release-Compatibility-Matrix.json"),
        new("release.candidate","tools/CSharpGameEngine.FeatureRunner/ReleaseCandidateEvidence.cs"),
    ];

    public static IEnumerable<string>RequiredRepositoryInputPaths=>
        InputDefinitions.Select(static Definition=>Definition.Path);

    public static ReleaseCandidateScope SelectScope(
        FeatureStageCatalog Catalog,int ReachedLevel,
        IReadOnlyList<FeatureRoadmapTrack>Tracks){
        if(ReachedLevel<0||ReachedLevel>7){
            throw new ArgumentOutOfRangeException(
                nameof(ReachedLevel),"Reached Level must be between zero and seven.");
        }
        FeatureRoadmapTrack[]Selected=[.. Tracks.Distinct().OrderBy(
            static Track=>Track.ToString(),StringComparer.Ordinal)];
        if(Selected.Length==0){
            throw new ArgumentException("At least one release track is required.",nameof(Tracks));
        }
        var SelectedSet=Selected.ToHashSet();
        Dictionary<string,FeatureDefinition>Features=Catalog.Features.ToDictionary(
            static Feature=>Feature.Id,StringComparer.Ordinal);
        var Required=new HashSet<string>(StringComparer.Ordinal);
        var Pending=new Stack<FeatureDefinition>(Catalog.Features.Where(Feature=>
            !ControlFeatureIds.Contains(Feature.Id)
            &&
            SelectedSet.Contains(Feature.Track)
            &&Feature.DependencyLevel<=ReachedLevel
            &&IsExecutableLifecycle(Feature.Status)));
        while(Pending.Count!=0){
            FeatureDefinition Feature=Pending.Pop();
            if(!Required.Add(Feature.Id)){ continue; }
            foreach(string DependencyId in Feature.DependsOnFeatureIds){
                if(!Features.TryGetValue(DependencyId,out FeatureDefinition?Dependency)){
                    throw new InvalidDataException(
                        $"Release feature {Feature.Id} depends on unknown feature {DependencyId}.");
                }
                if(Dependency.DependencyLevel>ReachedLevel){
                    throw new InvalidDataException(
                        $"Release feature {Feature.Id} depends on unreached feature {DependencyId}.");
                }
                if(!IsExecutableLifecycle(Dependency.Status)){
                    throw new InvalidDataException(
                        $"Release feature {Feature.Id} depends on non-executable feature {DependencyId}.");
                }
                Pending.Push(Dependency);
            }
        }
        string[]FeatureIds=[.. Required.Order(StringComparer.Ordinal)];
        string[]StageIds=[.. Catalog.Stages.Where(Stage=>
                Stage.Availability==FeatureStageAvailability.Executable
                &&Stage.Id is not("release.candidate" or "release.manifest")
                &&Stage.FeatureIds.Any(Required.Contains))
            .Select(static Stage=>Stage.Id).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
        foreach(string FeatureId in FeatureIds){
            if(!StageIds.Any(StageId=>Catalog.GetStage(StageId).FeatureIds.Contains(
                FeatureId,StringComparer.Ordinal))){
                throw new InvalidDataException(
                    $"Release feature {FeatureId} has no executable prerequisite Stage.");
            }
        }
        return new ReleaseCandidateScope(
            ReachedLevel,
            [.. Selected.Select(static Track=>Track.ToString())],
            FeatureIds,StageIds);
    }

    public static async Task<int>RunAsync(
        string RepositoryRoot,FeatureStageCatalog Catalog,int ReachedLevel,
        IReadOnlyList<FeatureRoadmapTrack>Tracks,TextWriter Output,TextWriter Error,
        CancellationToken CancellationToken){
        string Root=Path.GetFullPath(RepositoryRoot);
        DateTimeOffset CreatedUtc=DateTimeOffset.UtcNow;
        string EvidenceRoot=Path.Combine(
            Root,".artifacts","ReleaseCandidates",
            $"{CreatedUtc:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        bool Completed=false;
        try {
            ReleaseCandidateScope Scope=SelectScope(Catalog,ReachedLevel,Tracks);
            (string Commit,bool IsDirty)Provenance;
            try {
                Provenance=await GitChangedPathProvider.ReadProvenanceAsync(
                    Root,CancellationToken).ConfigureAwait(false);
            } catch(Exception Ex)when(Ex is not OperationCanceledException){
                Provenance=("unknown",true);
                await Error.WriteLineAsync(
                    $"Warning: git provenance is unavailable: {Ex.Message}").ConfigureAwait(false);
            }
            ReleaseCandidateStageEvidence[]Stages=ReadStageEvidence(
                Root,Catalog,Scope,Provenance,CreatedUtc);
            ReleaseCandidateFeature[]Features=[.. Scope.RequiredFeatureIds.Select(Id=>{
                FeatureDefinition Feature=Catalog.GetFeature(Id);
                return new ReleaseCandidateFeature(
                    Feature.Id,Feature.Track.ToString(),Feature.DependencyLevel,
                    Feature.Status.ToString(),
                    [.. Feature.DependsOnFeatureIds.Order(StringComparer.Ordinal)]);
            })];
            ReleaseCandidateInput[]Inputs=ReadInputs(Root);
            var Payload=new ReleaseCandidateReportPayload(
                ReportSchema,CreatedUtc,"Release","Warp",Provenance.Commit,
                Provenance.IsDirty,Scope.ReachedLevel,Scope.Tracks,Features,Stages,Inputs);
            string ReportHash=Sha256(JsonSerializer.SerializeToUtf8Bytes(Payload,JsonOptions));
            var Report=new ReleaseCandidateReport(
                Payload.Schema,ReportHash,Payload.CreatedUtc,Payload.Configuration,
                Payload.Adapter,Payload.Commit,Payload.IsDirty,Payload.ReachedLevel,
                Payload.Tracks,Payload.Features,Payload.Stages,Payload.Inputs);
            Directory.CreateDirectory(EvidenceRoot);
            string ReportPath=Path.Combine(EvidenceRoot,ReportFileName);
            await WriteCanonicalAsync(ReportPath,Report,CancellationToken).ConfigureAwait(false);
            IReadOnlyList<string>Errors=await ValidateAsync(
                Root,ReportPath,Catalog,CancellationToken).ConfigureAwait(false);
            foreach(string ValidationError in Errors){
                await Error.WriteLineAsync(ValidationError).ConfigureAwait(false);
            }
            if(Errors.Count!=0){ return 1; }
            Completed=true;
            await Output.WriteLineAsync(
                $"releaseCandidate level={ReachedLevel} tracks={Scope.Tracks.Count} "
                +$"features={Features.Length} stages={Stages.Length} commit={Provenance.Commit}")
                .ConfigureAwait(false);
            await Output.WriteLineAsync(ReportPath).ConfigureAwait(false);
            return 0;
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            await Error.WriteLineAsync(
                $"Release candidate verification failed: {Ex.Message}").ConfigureAwait(false);
            return 1;
        } finally {
            if(!Completed&&Directory.Exists(EvidenceRoot)){
                Directory.Delete(EvidenceRoot,true);
                DeleteEmptyParent(Path.GetDirectoryName(EvidenceRoot)!);
            }
        }
    }

    public static async Task<IReadOnlyList<string>>ValidateAsync(
        string RepositoryRoot,string ReportPath,FeatureStageCatalog Catalog,
        CancellationToken CancellationToken=default){
        var Errors=new List<string>();
        string Root=Path.GetFullPath(RepositoryRoot);
        string FullReportPath=Path.GetFullPath(ReportPath);
        ReleaseCandidateReport?Report;
        byte[]ReportBytes;
        try {
            if(!IsContained(Path.Combine(Root,".artifacts","ReleaseCandidates"),FullReportPath)){
                return ["Release candidate report must be inside .artifacts/ReleaseCandidates."];
            }
            EnsureNoReparsePoints(Root,FullReportPath);
            ReportBytes=await File.ReadAllBytesAsync(
                FullReportPath,CancellationToken).ConfigureAwait(false);
            Report=JsonSerializer.Deserialize<ReleaseCandidateReport>(ReportBytes,JsonOptions);
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            return [$"Release candidate report cannot be read: {Ex.Message}"];
        }
        if(Report is null){ return ["Release candidate report is empty."]; }
        try {
            if(Report.Schema!=ReportSchema){
                Errors.Add($"Release candidate report schema must be {ReportSchema}.");
            }
            if(Report.Configuration!="Release"||Report.Adapter!="Warp"){
                Errors.Add("Release candidate report must bind Release and Warp.");
            }
            if(!ReportBytes.AsSpan().SequenceEqual(CanonicalBytes(Report))){
                Errors.Add("Release candidate report is not canonically encoded.");
            }
            var Payload=new ReleaseCandidateReportPayload(
                Report.Schema,Report.CreatedUtc,Report.Configuration,Report.Adapter,
                Report.Commit,Report.IsDirty,Report.ReachedLevel,Report.Tracks,
                Report.Features,Report.Stages,Report.Inputs);
            if(!FixedHashEquals(
                Sha256(JsonSerializer.SerializeToUtf8Bytes(Payload,JsonOptions)),
                Report.ReportSha256)){
                Errors.Add("Release candidate report SHA-256 mismatch.");
            }
            FeatureRoadmapTrack[]Tracks=ParseTracks(Report.Tracks,Errors);
            if(Tracks.Length!=0){
                ReleaseCandidateScope Scope=SelectScope(
                    Catalog,Report.ReachedLevel,Tracks);
                ValidateScope(Catalog,Scope,Report,Errors);
            }
            ValidateInputs(Root,Report.Inputs,Errors);
            ValidateStages(Root,Catalog,Report,Errors);
            string EvidenceDirectory=Path.GetDirectoryName(FullReportPath)!;
            string[]ActualFiles=[.. Directory.EnumerateFiles(
                EvidenceDirectory,"*",SearchOption.AllDirectories)
                .Select(Path.GetFullPath).Order(StringComparer.Ordinal)];
            string[]ExpectedFiles=[FullReportPath];
            if(!ActualFiles.SequenceEqual(ExpectedFiles,StringComparer.Ordinal)){
                Errors.Add("Release candidate evidence has missing or extra files.");
            }
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Errors.Add($"Release candidate report cannot be validated: {Ex.Message}");
        }
        return Errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static ReleaseCandidateStageEvidence[]ReadStageEvidence(
        string Root,FeatureStageCatalog Catalog,ReleaseCandidateScope Scope,
        (string Commit,bool IsDirty)Provenance,DateTimeOffset CreatedUtc){
        string StageRoot=Path.Combine(Root,".artifacts","FeatureStages");
        var Candidates=new Dictionary<string,List<(string Path,FeatureStageRunResult Result)>>(
            StringComparer.Ordinal);
        foreach(string StageId in Scope.RequiredStageIds){
            Candidates.Add(StageId,[]);
        }
        if(Directory.Exists(StageRoot)){
            foreach(string PathValue in Directory.EnumerateFiles(
                StageRoot,"*.run-result.json",SearchOption.TopDirectoryOnly)){
                try {
                    FeatureRunResultDocument?Document=JsonSerializer.Deserialize<
                        FeatureRunResultDocument>(File.ReadAllBytes(PathValue),JsonOptions);
                    if(Document?.SchemaVersion!=FeatureRunResultDocument.CurrentSchemaVersion){
                        continue;
                    }
                    foreach(FeatureStageRunResult Result in Document.Results){
                        if(Candidates.TryGetValue(Result.StageId,out var StageCandidates)){
                            StageCandidates.Add((PathValue,Result));
                        }
                    }
                } catch(JsonException){ }
            }
        }
        var Errors=new List<string>();
        var Evidence=new List<ReleaseCandidateStageEvidence>(Scope.RequiredStageIds.Count);
        foreach(string StageId in Scope.RequiredStageIds){
            FeatureStageDefinition Stage=Catalog.GetStage(StageId);
            (string Path,FeatureStageRunResult Result)?Match=Candidates[StageId]
                .Where(Candidate=>ValidateStageResult(
                    Candidate.Result,Stage,Provenance,CreatedUtc).Count==0)
                .OrderByDescending(static Candidate=>Candidate.Result.FinishedUtc)
                .Cast<(string Path,FeatureStageRunResult Result)?>().FirstOrDefault();
            if(Match is null){
                Errors.Add(
                    $"{StageId}: no current passing {PrerequisiteProfile} Release/Warp result "
                    +"matches the current Git provenance.");
                continue;
            }
            byte[]Bytes=File.ReadAllBytes(Match.Value.Path);
            Evidence.Add(new ReleaseCandidateStageEvidence(
                StageId,Relative(Root,Match.Value.Path),Sha256(Bytes),
                Match.Value.Result.ArtifactSha256,Match.Value.Result.FinishedUtc,
                [.. Match.Value.Result.FeatureIds]));
        }
        if(Errors.Count!=0){ throw new InvalidDataException(string.Join(Environment.NewLine,Errors)); }
        return [.. Evidence];
    }

    private static void ValidateScope(
        FeatureStageCatalog Catalog,ReleaseCandidateScope Scope,
        ReleaseCandidateReport Report,List<string>Errors){
        if(!Scope.Tracks.SequenceEqual(Report.Tracks,StringComparer.Ordinal)){
            Errors.Add("Release candidate track scope changed.");
        }
        if(!Scope.RequiredFeatureIds.SequenceEqual(
            Report.Features.Select(static Feature=>Feature.FeatureId),StringComparer.Ordinal)){
            Errors.Add("Release candidate feature closure changed.");
        }
        if(!Scope.RequiredStageIds.SequenceEqual(
            Report.Stages.Select(static Stage=>Stage.StageId),StringComparer.Ordinal)){
            Errors.Add("Release candidate Stage closure changed.");
        }
        foreach(ReleaseCandidateFeature Captured in Report.Features){
            FeatureDefinition Current=Catalog.GetFeature(Captured.FeatureId);
            var Expected=new ReleaseCandidateFeature(
                Current.Id,Current.Track.ToString(),Current.DependencyLevel,
                Current.Status.ToString(),
                [.. Current.DependsOnFeatureIds.Order(StringComparer.Ordinal)]);
            if(Captured.FeatureId!=Expected.FeatureId
                ||Captured.Track!=Expected.Track
                ||Captured.DependencyLevel!=Expected.DependencyLevel
                ||Captured.LifecycleStatus!=Expected.LifecycleStatus
                ||!Captured.DependsOnFeatureIds.SequenceEqual(
                    Expected.DependsOnFeatureIds,StringComparer.Ordinal)){
                Errors.Add($"Release candidate feature changed: {Captured.FeatureId}.");
            }
        }
    }

    private static void ValidateStages(
        string Root,FeatureStageCatalog Catalog,ReleaseCandidateReport Report,
        List<string>Errors){
        if(Report.Stages.Select(static Stage=>Stage.StageId).Distinct(
            StringComparer.Ordinal).Count()!=Report.Stages.Count){
            Errors.Add("Release candidate Stage evidence contains duplicates.");
        }
        foreach(ReleaseCandidateStageEvidence Evidence in Report.Stages){
            try {
                string PathValue=ResolveRelative(Root,Evidence.ResultPath);
                if(!IsContained(Path.Combine(Root,".artifacts","FeatureStages"),PathValue)){
                    throw new InvalidDataException(
                        $"{Evidence.StageId} result escaped .artifacts/FeatureStages.");
                }
                EnsureNoReparsePoints(Root,PathValue);
                byte[]Bytes=File.ReadAllBytes(PathValue);
                if(!FixedHashEquals(Sha256(Bytes),Evidence.ResultFileSha256)){
                    Errors.Add($"{Evidence.StageId} result file SHA-256 mismatch.");
                    continue;
                }
                FeatureRunResultDocument?Document=JsonSerializer.Deserialize<
                    FeatureRunResultDocument>(Bytes,JsonOptions);
                FeatureStageRunResult?Result=Document?.Results.SingleOrDefault(
                    Item=>Item.StageId==Evidence.StageId
                        &&FixedHashEquals(Item.ArtifactSha256,Evidence.ArtifactSha256));
                if(Document?.SchemaVersion!=FeatureRunResultDocument.CurrentSchemaVersion
                    ||Result is null){
                    Errors.Add($"{Evidence.StageId} result file does not contain its bound result.");
                    continue;
                }
                FeatureStageDefinition Stage=Catalog.GetStage(Evidence.StageId);
                foreach(string Error in ValidateStageResult(
                    Result,Stage,(Report.Commit,Report.IsDirty),Report.CreatedUtc)){
                    Errors.Add($"{Evidence.StageId}: {Error}");
                }
                if(Result.FinishedUtc!=Evidence.FinishedUtc
                    ||!Result.FeatureIds.SequenceEqual(Evidence.FeatureIds,StringComparer.Ordinal)){
                    Errors.Add($"{Evidence.StageId} captured result identity changed.");
                }
            } catch(Exception Ex)when(Ex is not OperationCanceledException){
                Errors.Add($"{Evidence.StageId} result file cannot be validated: {Ex.Message}");
            }
        }
    }

    private static IReadOnlyList<string>ValidateStageResult(
        FeatureStageRunResult Result,FeatureStageDefinition Stage,
        (string Commit,bool IsDirty)Provenance,DateTimeOffset CreatedUtc){
        var Errors=new List<string>();
        if(Result.StageId!=Stage.Id
            ||!Result.FeatureIds.SequenceEqual(Stage.FeatureIds,StringComparer.Ordinal)
            ||Result.Pack!=Stage.Pack.ToString()){
            Errors.Add("Stage identity does not match the Catalog.");
        }
        if(Result.Configuration!="Release"||Result.Profile!=PrerequisiteProfile
            ||Result.Adapter!="Warp"||Result.Seed!=Stage.Seed||Result.Ticks!=Stage.Ticks){
            Errors.Add("Stage execution options are not the RC prerequisite contract.");
        }
        if(Result.GitCommit!=Provenance.Commit||Result.IsDirty!=Provenance.IsDirty){
            Errors.Add("Stage Git provenance does not match the RC.");
        }
        if(Result.Status!=FeatureStageRunStatus.Passed||Result.Errors.Count!=0
            ||Result.Assertions.Count<Stage.Commands.Count
            ||Result.Assertions.Any(static Assertion=>!Assertion.Passed)){
            Errors.Add("Stage is not a complete passing verdict.");
        }
        if(Result.FinishedUtc<Result.StartedUtc||Result.DurationMilliseconds<0
            ||Result.FinishedUtc>CreatedUtc
            ||CreatedUtc-Result.FinishedUtc>MaximumEvidenceAge){
            Errors.Add("Stage result timing is invalid or stale.");
        }
        if(!FixedHashEquals(ComputeStageArtifactHash(Result),Result.ArtifactSha256)){
            Errors.Add("Stage artifact SHA-256 mismatch.");
        }
        return Errors;
    }

    private static ReleaseCandidateInput[]ReadInputs(string Root)=>
        [.. InputDefinitions.Select(Definition=>{
            string FullPath=ResolveRelative(Root,Definition.Path);
            EnsureNoReparsePoints(Root,FullPath);
            byte[]Bytes=File.ReadAllBytes(FullPath);
            return new ReleaseCandidateInput(
                Definition.Role,Definition.Path,Sha256(Bytes),Bytes.LongLength);
        })];

    private static void ValidateInputs(
        string Root,IReadOnlyList<ReleaseCandidateInput>Inputs,List<string>Errors){
        if(Inputs.Count!=InputDefinitions.Length){
            Errors.Add("Release candidate input closure is incomplete.");
            return;
        }
        for(int Index=0;Index<InputDefinitions.Length;Index++){
            InputDefinition Expected=InputDefinitions[Index];
            ReleaseCandidateInput Input=Inputs[Index];
            if(Input.Role!=Expected.Role||Input.Path!=Expected.Path){
                Errors.Add($"Release candidate input identity changed at index {Index}.");
                continue;
            }
            try {
                string PathValue=ResolveRelative(Root,Input.Path);
                EnsureNoReparsePoints(Root,PathValue);
                byte[]Bytes=File.ReadAllBytes(PathValue);
                if(Bytes.LongLength!=Input.ByteLength
                    ||!FixedHashEquals(Sha256(Bytes),Input.Sha256)){
                    Errors.Add($"Release candidate input changed: {Input.Path}.");
                }
            } catch(Exception Ex)when(Ex is not OperationCanceledException){
                Errors.Add($"Release candidate input cannot be validated: {Input.Path}: {Ex.Message}");
            }
        }
    }

    private static FeatureRoadmapTrack[]ParseTracks(
        IReadOnlyList<string>Values,List<string>Errors){
        var Tracks=new List<FeatureRoadmapTrack>();
        foreach(string Value in Values){
            if(!Enum.TryParse(Value,false,out FeatureRoadmapTrack Track)
                ||!Enum.IsDefined(Track)){
                Errors.Add($"Unknown release candidate track: {Value}.");
            } else { Tracks.Add(Track); }
        }
        if(Tracks.Count!=Values.Count
            ||Tracks.Distinct().Count()!=Tracks.Count
            ||!Values.SequenceEqual(Values.Order(StringComparer.Ordinal),StringComparer.Ordinal)){
            Errors.Add("Release candidate tracks must be unique and ordinally sorted.");
        }
        return [.. Tracks];
    }

    private static bool IsExecutableLifecycle(FeatureLifecycleStatus Status)=>
        Status is FeatureLifecycleStatus.Implemented
            or FeatureLifecycleStatus.PartiallyImplemented
            or FeatureLifecycleStatus.Implementing;

    internal static string ComputeStageArtifactHash(FeatureStageRunResult Result){
        string Evidence=string.Join("\n",[
            Result.StageId,Result.Status.ToString(),Result.Summary,
            Result.StateHash??string.Empty,Result.EventHash??string.Empty,
            Result.StandardOutput,Result.StandardError,
            .. Result.Assertions.Select(static Assertion=>
                $"{Assertion.Name}:{Assertion.Passed}:{Assertion.Details}"),
            .. Result.Errors,
        ]);
        return Sha256(Encoding.UTF8.GetBytes(Evidence));
    }

    private static byte[]CanonicalBytes<T>(T Value){
        byte[]Json=JsonSerializer.SerializeToUtf8Bytes(Value,JsonOptions);
        byte[]Canonical=new byte[Json.Length+1];
        Json.CopyTo(Canonical,0);
        Canonical[^1]=(byte)'\n';
        return Canonical;
    }

    private static async Task WriteCanonicalAsync<T>(
        string PathValue,T Value,CancellationToken CancellationToken){
        string Temporary=PathValue+".tmp-"+Guid.NewGuid().ToString("N");
        await File.WriteAllBytesAsync(
            Temporary,CanonicalBytes(Value),CancellationToken).ConfigureAwait(false);
        File.Move(Temporary,PathValue,false);
    }

    private static string ResolveRelative(string Root,string RelativePath){
        string FullPath=Path.GetFullPath(Path.Combine(
            Root,RelativePath.Replace('/',Path.DirectorySeparatorChar)));
        if(!IsContained(Root,FullPath)){
            throw new InvalidDataException($"Path escaped the repository: {RelativePath}.");
        }
        return FullPath;
    }

    private static string Relative(string Root,string PathValue)=>
        Path.GetRelativePath(Root,PathValue).Replace(Path.DirectorySeparatorChar,'/');

    private static bool IsContained(string Parent,string Candidate){
        string Root=Path.GetFullPath(Parent).TrimEnd(
            Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)
            +Path.DirectorySeparatorChar;
        string Full=Path.GetFullPath(Candidate);
        return Full.StartsWith(Root,StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureNoReparsePoints(string Root,string PathValue){
        string FullRoot=Path.GetFullPath(Root);
        string Current=Path.GetFullPath(PathValue);
        while(IsContained(FullRoot,Current)){
            if(File.Exists(Current)||Directory.Exists(Current)){
                if((File.GetAttributes(Current)&FileAttributes.ReparsePoint)!=0){
                    throw new InvalidDataException($"Path contains a reparse point: {Current}.");
                }
            }
            string?Parent=Path.GetDirectoryName(Current);
            if(Parent is null||Current.Equals(FullRoot,StringComparison.OrdinalIgnoreCase)){ break; }
            Current=Parent;
        }
    }

    private static bool FixedHashEquals(string Left,string Right){
        if(Left.Length!=64||Right.Length!=64){ return false; }
        try {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(Left),Convert.FromHexString(Right));
        } catch(FormatException){ return false; }
    }

    private static string Sha256(ReadOnlySpan<byte>Bytes)=>
        Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();

    private static void DeleteEmptyParent(string Parent){
        if(Directory.Exists(Parent)&&!Directory.EnumerateFileSystemEntries(Parent).Any()){
            Directory.Delete(Parent);
        }
    }

    private sealed record InputDefinition(string Role,string Path);
}
