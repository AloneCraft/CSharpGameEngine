using System.Collections.Immutable;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Interchange;
namespace CSharpGameEngine.Cooking;
public interface IAssetImporter {
    ImporterDescriptor Descriptor{get;}
    ImportSniffResult Sniff(ImportSniffRequest Request);
    ValueTask<ImportResult> ImportAsync(ImportRequest Request,IImportContext Context,CancellationToken CancellationToken);
}
public interface IModelProcessor {
    ModelProcessorDescriptor Descriptor{get;}
    bool AppliesTo(InterchangeDocument Document,IProcessContext Context);
    ValueTask<InterchangeDocument> ProcessAsync(InterchangeDocument Document,IProcessContext Context,CancellationToken CancellationToken);
}
public enum ImporterIsolation:byte { Shared,Serialized,Dedicated,PerImport }
public enum ImporterDeterminism:byte { Deterministic,ExternalToolCached }
[Flags] public enum ImportCapabilities:ushort { None=0,Mesh=1,Scene=2,Materials=4,Skin=8,Animation=16,Morph=32,Camera=64,Light=128,Humanoid=256 }
public enum ImportSniffResult:byte { Rejected,Possible,Definite }
public sealed record ImportSniffRequest(string SourcePath,ReadOnlyMemory<byte>Prefix);
public sealed record ImportOptionDescriptor(string Name,string DefaultValue,string Description,ImmutableArray<string>AllowedValues);
public sealed record ExternalToolRequirement(string Identity,string VersionRange,bool RequiresLicense);
public sealed record ImporterDescriptor(string Identity,int Version,string DisplayName,ImmutableArray<string>Extensions,int Priority,string Provider,ImporterIsolation Isolation,ImporterDeterminism Determinism,ImportCapabilities Capabilities,ImmutableArray<ImportOptionDescriptor>Options,ImmutableArray<string>NestedImporters,ImmutableArray<ExternalToolRequirement>Tools);
public sealed record ImportRequest(AssetId AssetId,string SourceUri,string SourcePath,string ProjectDirectory,IReadOnlyDictionary<string,string>Options,string ScratchDirectory);
public sealed record ImportResult(InterchangeDocument?Document);
public enum ModelProcessorStage:byte { SourceSpace,AfterNormalize,BeforeWrite }
public sealed record ModelProcessorDescriptor(string Identity,int Version,ModelProcessorStage Stage,int Order,ImporterIsolation Isolation,ImmutableArray<ImportOptionDescriptor>Options);
public enum ImportSeverity:byte { Info,Warning,Error }
public enum ImportPhase:byte { Discovery,Read,Normalize,Validate,Write,Worker }
public readonly record struct ImportSourceLocation(string Path,int Line=0,int Column=0);
public sealed record ImportDiagnostic(ImportSeverity Severity,string Code,string Component,ImportPhase Phase,string Message,ImportSourceLocation Location) {
    public override string ToString()=>$"{this.Location.Path}({this.Location.Line},{this.Location.Column}): {this.Severity.ToString().ToLowerInvariant()} {this.Code}: {this.Message} [component={this.Component} phase={this.Phase}]";
}
public readonly record struct ImportProgress(string Phase,int Completed,int Total);
public sealed record ArchiveLimits(int MaximumEntries,long MaximumExpandedBytes,int MaximumPathLength);
public sealed record ExternalTool(string Identity,string Path,string Version);
public sealed record ExternalProcessRequest(ExternalTool Tool,ImmutableArray<string>Arguments,string WorkingDirectory,int TimeoutSeconds);
public sealed record ExternalProcessResult(int ExitCode,string StandardOutput,string StandardError);
public sealed record HumanoidProfile(string Identity,ImmutableSortedDictionary<HumanoidBone,string>BoneNames,ImmutableArray<InterchangeTransform>ReferencePose);
public sealed record ExporterProfile(string Identity,string Provider,ImmutableSortedDictionary<string,string>Options);
public interface IProcessContext {
    IReadOnlyDictionary<string,string> Options{get;}
    void Report(ImportDiagnostic Diagnostic);
}
public interface IImportContext:IProcessContext {
    Stream OpenRead(string ProjectRelativePath);
    bool TryOpenRead(string ProjectRelativePath,out Stream?Stream);
    void RecordDependency(string ProjectRelativePath);
    string StageArchive(string ProjectRelativePath,ArchiveLimits Limits);
    IProgress<ImportProgress> Progress{get;}
    ValueTask<InterchangeDocument> ImportNestedAsync(string ScratchRelativePath,string ImporterIdentity,IReadOnlyDictionary<string,string>Options,CancellationToken CancellationToken);
    ValueTask<InterchangeDocument> ImportNestedAsync(string ScratchRelativePath,string ImporterIdentity,IReadOnlyDictionary<string,string>Options,InterchangeSpace?SpaceOverride,CancellationToken CancellationToken);
    ExternalTool?ResolveTool(string ToolId,string VersionRange);
    ValueTask<ExternalProcessResult> RunToolAsync(ExternalProcessRequest Request,CancellationToken CancellationToken);
    IReadOnlyList<HumanoidProfile> HumanoidProfiles{get;}
    IReadOnlyList<ExporterProfile> ExporterProfiles{get;}
}
