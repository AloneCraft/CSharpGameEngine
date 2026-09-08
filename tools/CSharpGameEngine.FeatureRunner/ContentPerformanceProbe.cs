using System.Text;
using CSharpGameEngine.Content;
using CSharpGameEngine.ContentCompiler;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;

namespace CSharpGameEngine.FeatureStages;

internal static class ContentPerformanceProbe {
    private const int NodeCount=32;
    private static readonly StableId NamespaceId=StableId.Parse(
        "ad178cda-54bb-52a2-a21c-4992bf315c88");

    public static async Task<int>RunAsync(
        string RepositoryRoot,string Configuration,TextWriter Output,TextWriter Error,
        CancellationToken CancellationToken){
        string WorkRoot=Path.Combine(
            Path.GetTempPath(),"CgeContentPerformance",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(WorkRoot);
        try {
            var ColdPlan=Plan(
                "content.cold-compile",Configuration,"cold",NodeCount,
                new PerformanceAbsoluteBudget(500_000_000,1_000_000_000,8*1024*1024));
            (IReadOnlyList<PerformanceSample>ColdSamples,PerformanceAnalysis ColdAnalysis)=
                PerformanceMeasurementFramework.Measure(
                    ColdPlan,Index=>RunCold(WorkRoot,Index,CancellationToken));

            using IncrementalWorkload Incremental=new(
                Path.Combine(WorkRoot,"incremental"),CancellationToken);
            var IncrementalPlan=Plan(
                "content.one-change",Configuration,"one-leaf-change",NodeCount+1,
                new PerformanceAbsoluteBudget(100_000_000,300_000_000,4*1024*1024));
            (IReadOnlyList<PerformanceSample>IncrementalSamples,
                PerformanceAnalysis IncrementalAnalysis)=PerformanceMeasurementFramework.Measure(
                    IncrementalPlan,Index=>Incremental.Run(Index,CancellationToken));

            (string Commit,bool IsDirty)Provenance;
            try {
                Provenance=await GitChangedPathProvider.ReadProvenanceAsync(
                    RepositoryRoot,CancellationToken).ConfigureAwait(false);
            } catch(Exception Ex)when(Ex is not OperationCanceledException){
                Provenance=("unknown",true);
                await Error.WriteLineAsync(
                    $"Warning: git provenance is unavailable: {Ex.Message}").ConfigureAwait(false);
            }
            string ColdReport=await SaveAsync(
                RepositoryRoot,ColdPlan,ColdSamples,ColdAnalysis,Provenance,CancellationToken)
                .ConfigureAwait(false);
            string IncrementalReport=await SaveAsync(
                RepositoryRoot,IncrementalPlan,IncrementalSamples,IncrementalAnalysis,
                Provenance,CancellationToken).ConfigureAwait(false);
            var Errors=PerformanceReportStore.Validate(ColdReport)
                .Concat(PerformanceReportStore.Validate(IncrementalReport)).ToArray();
            await WriteSummary(Output,ColdPlan,ColdAnalysis,ColdReport).ConfigureAwait(false);
            await WriteSummary(Output,IncrementalPlan,IncrementalAnalysis,IncrementalReport)
                .ConfigureAwait(false);
            foreach(string ValidationError in Errors){
                await Error.WriteLineAsync(ValidationError).ConfigureAwait(false);
            }
            return ColdAnalysis.Passed&&IncrementalAnalysis.Passed&&Errors.Length==0?0:1;
        } finally {
            if(Directory.Exists(WorkRoot)){ Directory.Delete(WorkRoot,true); }
        }
    }

    private static PerformanceMeasurementPlan Plan(
        string Id,string Configuration,string Mode,int Nodes,PerformanceAbsoluteBudget Budget)=>
        new(Id,Configuration,"nanoseconds",
            new SortedDictionary<string,string>(StringComparer.Ordinal){
                ["mode"]=Mode,["nodes"]=Nodes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["maximumConcurrency"]="4",["payloadBytes"]="64",
            },PerformanceMeasurementFramework.RequiredWarmupCount,
            PerformanceMeasurementFramework.RequiredSampleCount,Budget,null);

    private static void RunCold(string Root,int Index,CancellationToken CancellationToken){
        string SampleRoot=Path.Combine(Root,"cold-"+Index.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        try {
            var Store=new ContentAddressedStore(Path.Combine(SampleRoot,"cas"));
            var Compiler=new IncrementalContentCompiler(
                Store,Path.Combine(SampleRoot,"state","content-state.json"));
            ContentCompileNode[]Nodes=[.. Enumerable.Range(0,NodeCount).Select(
                NodeIndex=>CreateNode("cold-"+NodeIndex,"v1",[]))];
            ContentCompileResult Result=Compiler.CompileAsync(
                Nodes,Nodes.Select(static Node=>Node.Key),4,CancellationToken)
                .AsTask().GetAwaiter().GetResult();
            if(!Result.Succeeded||Result.Rebuilt.Length!=NodeCount||Result.Reused.Length!=0){
                throw new InvalidOperationException("Cold content performance workload produced an invalid compile result.");
            }
        } finally {
            if(Directory.Exists(SampleRoot)){ Directory.Delete(SampleRoot,true); }
        }
    }

    private static ContentCompileNode CreateNode(
        string Name,string Revision,IEnumerable<AssetKey>Dependencies){
        var Key=new AssetKey(new AssetId(StableId.FromName(NamespaceId,Name)),null);
        string SourceUri="asset://performance/"+Name;
        Hash256 SourceHash=Hash256.Compute(Encoding.UTF8.GetBytes(Name+":"+Revision));
        Hash256 OptionsHash=Hash256.Compute(Encoding.UTF8.GetBytes("options:"+Name));
        return new ContentCompileNode(
            Key,SourceUri,SourceHash,"content-performance/1","fixture",OptionsHash,
            Dependencies,(Context,_)=>{
                byte[]Payload=new byte[64];
                Encoding.UTF8.GetBytes(Name+":"+Revision).AsSpan().CopyTo(Payload);
                var Artifact=new ContentArtifact(
                    ContentArtifactKind.Scene,
                    new ArtifactProvenance(
                        "content-performance/1",SourceUri,SourceHash,"fixture",OptionsHash),
                    Context.Dependencies.Values.Select(static Value=>Value.Reference),Payload);
                return ValueTask.FromResult<ReadOnlyMemory<byte>>(
                    ContentArtifactCodec.Write(Artifact));
            });
    }

    private static async Task<string>SaveAsync(
        string RepositoryRoot,PerformanceMeasurementPlan Plan,
        IReadOnlyList<PerformanceSample>Samples,PerformanceAnalysis Analysis,
        (string Commit,bool IsDirty)Provenance,CancellationToken CancellationToken){
        var Payload=new PerformanceReportPayload(
            PerformanceFrameworkProbe.ReportSchema,DateTimeOffset.UtcNow,Plan,Samples,Analysis,
            PerformanceMeasurementFramework.CaptureMachineFingerprint(),
            Provenance.Commit,Provenance.IsDirty);
        return await PerformanceReportStore.SaveAsync(
            RepositoryRoot,Payload,CancellationToken).ConfigureAwait(false);
    }

    private static Task WriteSummary(
        TextWriter Output,PerformanceMeasurementPlan Plan,PerformanceAnalysis Analysis,
        string ReportPath)=>Output.WriteLineAsync(
            $"{Plan.Id} samples={Plan.SampleCount} medianNs={Analysis.ElapsedNanoseconds.Median} "
            +$"p95Ns={Analysis.ElapsedNanoseconds.P95} madNs={Analysis.ElapsedNanoseconds.Mad} "
            +$"allocationP95={Analysis.AllocatedBytes.P95} passed={Analysis.Passed} report={ReportPath}");

    private sealed class IncrementalWorkload:IDisposable {
        private readonly string Root;
        private readonly IncrementalContentCompiler Compiler;
        private readonly int[]Revisions=new int[NodeCount];

        public IncrementalWorkload(string Root,CancellationToken CancellationToken){
            this.Root=Root;
            var Store=new ContentAddressedStore(Path.Combine(Root,"cas"));
            this.Compiler=new IncrementalContentCompiler(
                Store,Path.Combine(Root,"state","content-state.json"));
            ContentCompileResult Initial=this.Compile(CancellationToken);
            if(!Initial.Succeeded||Initial.Rebuilt.Length!=NodeCount+1){
                throw new InvalidOperationException("Incremental content performance setup failed.");
            }
        }

        public void Run(int Index,CancellationToken CancellationToken){
            this.Revisions[Index%NodeCount]++;
            ContentCompileResult Result=this.Compile(CancellationToken);
            if(!Result.Succeeded||Result.Rebuilt.Length!=2||Result.Reused.Length!=NodeCount-1){
                throw new InvalidOperationException(
                    "Incremental content performance workload rebuilt the wrong closure.");
            }
        }

        private ContentCompileResult Compile(CancellationToken CancellationToken){
            ContentCompileNode[]Leaves=[.. Enumerable.Range(0,NodeCount).Select(Index=>
                CreateNode("leaf-"+Index,"v"+this.Revisions[Index],[]))];
            ContentCompileNode RootNode=CreateNode(
                "root","v1",Leaves.Select(static Node=>Node.Key));
            return this.Compiler.CompileAsync(
                [.. Leaves,RootNode],[RootNode.Key],4,CancellationToken)
                .AsTask().GetAwaiter().GetResult();
        }

        public void Dispose(){
            if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,true); }
        }
    }
}
