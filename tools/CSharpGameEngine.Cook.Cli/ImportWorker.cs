using System.Collections.Immutable;
using System.Text.Json;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Cooking;
using CSharpGameEngine.Interchange;
namespace CSharpGameEngine.Cook.Cli;
internal sealed record ImportWorkerRequest(string ImporterIdentity,string AssetId,string SourcePath,string ProjectDirectory,string ScratchDirectory,string OutputPath,IReadOnlyDictionary<string,string>Options);
internal sealed record ImportWorkerResult(bool Success,string?OutputPath,ImmutableArray<string>ReadSet,ImmutableArray<ImportDiagnostic>Diagnostics);
internal static class ImportWorker {
    private const int MaximumRequestBytes=1024*1024;
    internal static string Describe(string ModulePath){
        using var Host=ImporterHost.Load(ModulePath);
        return JsonSerializer.Serialize(new { Schema="cge-import-module/1",Importers=Host.Importers.Select(Importer=>Importer.Descriptor),Processors=Host.Processors.Select(Processor=>Processor.Descriptor) });
    }
    internal static async Task<int>RunAsync(string ModulePath,string RequestPath,string ResultPath,CancellationToken CancellationToken){
        var Diagnostics=new List<ImportDiagnostic>();
        ImportFileContext?Context=null;
        string?OutputPath=null;
        try {
            var Info=new FileInfo(RequestPath);
            if(Info.Length<1||Info.Length>MaximumRequestBytes){ throw new InvalidDataException("IMP803: invalid worker request size."); }
            var Bytes=await File.ReadAllBytesAsync(RequestPath,CancellationToken);
            if(Bytes.Length>MaximumRequestBytes){ throw new InvalidDataException("IMP803: worker request changed size."); }
            var Request=JsonSerializer.Deserialize<ImportWorkerRequest>(Bytes)??throw new InvalidDataException("IMP002: missing worker request.");
            if(Request.Options is null||Request.Options.Count>1024){ throw new InvalidDataException("IMP004: options are missing or exceed their limit."); }
            if(!AssetId.TryParse(Request.AssetId,out var Asset)||!Asset.IsValid){ throw new InvalidDataException("IMP002: invalid worker AssetId."); }
            Context=new ImportFileContext(Request.ProjectDirectory,Request.Options,Diagnostics);
            var Source=Context.Resolve(Request.SourcePath);
            if(string.Equals(Path.GetFullPath(ResultPath),Source,StringComparison.OrdinalIgnoreCase)){
                Console.Error.WriteLine("IMP006: worker result cannot replace its source.");
                return 1;
            }
            var SourceInfo=new FileInfo(Source);
            if(!SourceInfo.Exists){ throw new FileNotFoundException("IMP005: source does not exist.",Source); }
            if(SourceInfo.Length>2L*1024*1024*1024){ throw new InvalidDataException("IMP803: source exceeds 2 GiB."); }
            var Scratch=Path.GetFullPath(Request.ScratchDirectory);
            var Output=Path.GetFullPath(Request.OutputPath);
            if(!Output.StartsWith(Scratch.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){ throw new InvalidDataException("IMP006: output must stay in scratch directory."); }
            Directory.CreateDirectory(Scratch);
            if(string.Equals(Output,Source,StringComparison.OrdinalIgnoreCase)){ throw new InvalidDataException("IMP006: worker output cannot replace its source."); }
            ImportFileContext.EnsureNoReparse(Output);
            Context.RecordDependency(Request.SourcePath);
            using var Host=ImporterHost.Load(ModulePath);
            var Importer=Host.Select(Request.ImporterIdentity,Path.GetExtension(Source));
            var ResolvedOptions=ImportOptions.Resolve(Importer.Descriptor,Host.Processors.Select(Processor=>Processor.Descriptor),Request.Options);
            Context.SetOptions(ResolvedOptions);
            if(Importer.Descriptor.Tools.Length>0||Importer.Descriptor.NestedImporters.Length>0){ throw new InvalidDataException("IMP501: this direct-import worker does not provide DCC or nested-import services yet."); }
            var SourceUri=Path.GetRelativePath(Path.GetFullPath(Request.ProjectDirectory),Source).Replace('\\','/');
            var Document=await Host.ImportAsync(Request.ImporterIdentity,new(Asset,SourceUri,Source,Path.GetFullPath(Request.ProjectDirectory),ResolvedOptions,Scratch),Context,CancellationToken);
            CancellationToken.ThrowIfCancellationRequested();
            if(Document is not null&&!Diagnostics.Any(D=>D.Severity==ImportSeverity.Error)){
                var Data=InterchangeCodec.Write(Document with { Provenance=Document.Provenance with { SourceUri=SourceUri,ImporterIdentity=Request.ImporterIdentity,ReadSet=[..Context.ReadSet.Order(StringComparer.Ordinal)] } });
                Directory.CreateDirectory(Path.GetDirectoryName(Output)!);
                await WriteAtomicAsync(Output,Data,CancellationToken);
                OutputPath=Output;
            }
            else if(!Diagnostics.Any(D=>D.Severity==ImportSeverity.Error)){
                AddDiagnostic(Diagnostics,new(ImportSeverity.Error,"IMP101",Request.ImporterIdentity,ImportPhase.Read,"Importer returned no document.",new(SourceUri)));
            }
        } catch(OperationCanceledException){
            AddDiagnostic(Diagnostics,new(ImportSeverity.Error,"IMP802","cge.worker@1",ImportPhase.Worker,"Import cancelled.",new(RequestPath)));
        } catch(Exception Error) when(Error is not OutOfMemoryException){
            var Code=Error.Message.StartsWith("IMP",StringComparison.Ordinal)&&Error.Message.Length>=6&&Error.Message.AsSpan(3,3).ToArray().All(char.IsAsciiDigit)?Error.Message[..6]:"IMP801";
            AddDiagnostic(Diagnostics,new(ImportSeverity.Error,Code,"cge.worker@1",ImportPhase.Worker,Error.Message,new(RequestPath)));
        }
        var Result=new ImportWorkerResult(OutputPath is not null,OutputPath,Context is null?[]:[..Context.ReadSet.Order(StringComparer.Ordinal)],[..Diagnostics.Take(256)]);
        await WriteAtomicAsync(ResultPath,JsonSerializer.SerializeToUtf8Bytes(Result),CancellationToken.None);
        return Result.Success?0:1;
    }
    internal static void AddDiagnostic(List<ImportDiagnostic>Diagnostics,ImportDiagnostic Diagnostic){
        if(Diagnostics.Count<256){ Diagnostics.Add(Diagnostic); }
        else if(Diagnostic.Severity==ImportSeverity.Error){
            var Replace=Diagnostics.FindLastIndex(Existing=>Existing.Severity!=ImportSeverity.Error);
            if(Replace>=0){ Diagnostics[Replace]=Diagnostic; }
        }
    }
    private static async Task WriteAtomicAsync(string PathValue,byte[]Bytes,CancellationToken CancellationToken){
        var Full=Path.GetFullPath(PathValue);
        ImportFileContext.EnsureNoReparse(Full);
        Directory.CreateDirectory(Path.GetDirectoryName(Full)!);
        var Temporary=Full+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            await File.WriteAllBytesAsync(Temporary,Bytes,CancellationToken);
            CancellationToken.ThrowIfCancellationRequested();
            File.Move(Temporary,Full,true);
        } finally { if(File.Exists(Temporary)){ File.Delete(Temporary); } }
    }
}
/// <summary>M0114 の直接アダプター向けの読み取りセットとパス制限。ツール/入れ子のサービスは明示的に利用できない。</summary>
internal sealed class ImportFileContext:IImportContext {
    private readonly string Root;
    private readonly List<ImportDiagnostic>Diagnostics;
    internal HashSet<string>ReadSet{get;}=new(StringComparer.Ordinal);
    internal ImportFileContext(string ProjectDirectory,IReadOnlyDictionary<string,string>Options,List<ImportDiagnostic>Diagnostics){
        this.Root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(ProjectDirectory));
        this.Options=Options;
        this.Diagnostics=Diagnostics;
    }
    public IReadOnlyDictionary<string,string>Options{get;private set;}
    internal void SetOptions(IReadOnlyDictionary<string,string>Options)=>this.Options=Options;
    public IProgress<ImportProgress>Progress{get;}=new NullProgress();
    public IReadOnlyList<HumanoidProfile>HumanoidProfiles=>[];
    public IReadOnlyList<ExporterProfile>ExporterProfiles=>[];
    public void Report(ImportDiagnostic Diagnostic)=>ImportWorker.AddDiagnostic(this.Diagnostics,Diagnostic);
    internal string Resolve(string ProjectRelativePath){
        var Full=Path.GetFullPath(Path.IsPathRooted(ProjectRelativePath)?ProjectRelativePath:Path.Combine(this.Root,ProjectRelativePath));
        var Prefix=this.Root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!Full.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase)){ throw new InvalidDataException("IMP006: input escapes project directory."); }
        EnsureNoReparse(Full);
        return Full;
    }
    internal static void EnsureNoReparse(string Full){
        for(var Entry=Full;Entry is not null;Entry=Path.GetDirectoryName(Entry)){
            if((File.Exists(Entry)||Directory.Exists(Entry))&&(File.GetAttributes(Entry)&FileAttributes.ReparsePoint)!=0){ throw new InvalidDataException("IMP006: reparse-point inputs are not allowed."); }
        }
    }
    public Stream OpenRead(string ProjectRelativePath){
        var Full=this.Resolve(ProjectRelativePath);
        this.RecordDependency(ProjectRelativePath);
        return new FileStream(Full,FileMode.Open,FileAccess.Read,FileShare.Read);
    }
    public bool TryOpenRead(string ProjectRelativePath,out Stream?Stream){
        this.RecordDependency(ProjectRelativePath);
        if(!File.Exists(this.Resolve(ProjectRelativePath))){ Stream=null;return false; }
        Stream=this.OpenRead(ProjectRelativePath);
        return true;
    }
    public void RecordDependency(string ProjectRelativePath)=>this.ReadSet.Add(Path.GetRelativePath(this.Root,this.Resolve(ProjectRelativePath)).Replace('\\','/'));
    public string StageArchive(string ProjectRelativePath,ArchiveLimits Limits)=>throw new InvalidDataException("IMP002: archive import is not available in the direct-import worker.");
    public ExternalTool?ResolveTool(string ToolId,string VersionRange)=>null;
    public ValueTask<ExternalProcessResult>RunToolAsync(ExternalProcessRequest Request,CancellationToken CancellationToken)=>throw new InvalidDataException("IMP501: external tools are not available in the direct-import worker.");
    public ValueTask<InterchangeDocument>ImportNestedAsync(string ScratchRelativePath,string ImporterIdentity,IReadOnlyDictionary<string,string>Options,CancellationToken CancellationToken)=>throw new InvalidDataException("IMP002: nested import is not available in the direct-import worker.");
    public ValueTask<InterchangeDocument>ImportNestedAsync(string ScratchRelativePath,string ImporterIdentity,IReadOnlyDictionary<string,string>Options,InterchangeSpace?SpaceOverride,CancellationToken CancellationToken)=>this.ImportNestedAsync(ScratchRelativePath,ImporterIdentity,Options,CancellationToken);
    private sealed class NullProgress:IProgress<ImportProgress> {
        public void Report(ImportProgress Value){ }
    }
}
