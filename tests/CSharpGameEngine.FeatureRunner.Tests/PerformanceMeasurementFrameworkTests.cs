using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class PerformanceMeasurementFrameworkTests {
    [Fact]
    public void ContractRequiresFiveWarmupsAndThirtySamples(){
        PerformanceMeasurementPlan Invalid=CreatePlan(Warmups:4,Samples:30);

        Assert.Throws<ArgumentException>(()=>PerformanceMeasurementFramework.Measure(Invalid,static _=>Thread.SpinWait(100)));
    }

    [Fact]
    public void StatisticsUseDeterministicMedianNearestRankP95AndMad(){
        PerformanceMeasurementPlan Plan=CreatePlan();
        PerformanceSample[]Samples=[.. Enumerable.Range(0,30).Select(Index=>new PerformanceSample(Index,Index==29?100:10,Index==29?64:0))];

        PerformanceAnalysis Analysis=PerformanceMeasurementFramework.Analyze(Plan,Samples);

        Assert.True(Analysis.Passed);
        Assert.Equal(10,Analysis.ElapsedNanoseconds.Median);
        Assert.Equal(10,Analysis.ElapsedNanoseconds.P95);
        Assert.Equal(0,Analysis.ElapsedNanoseconds.Mad);
        Assert.Equal(100,Analysis.ElapsedNanoseconds.Maximum);
        Assert.Equal(0,Analysis.AllocatedBytes.P95);
    }

    [Fact]
    public void AbsoluteBudgetAndBaselineDeltaAreIndependentVerdicts(){
        var Plan=CreatePlan(
            Absolute:new PerformanceAbsoluteBudget(200,200,20),
            Baseline:new PerformanceBaseline(100,100,10,1000));
        PerformanceSample[]Samples=[.. Enumerable.Range(0,30).Select(Index=>new PerformanceSample(Index,120,10))];

        PerformanceAnalysis Analysis=PerformanceMeasurementFramework.Analyze(Plan,Samples);

        Assert.Equal(PerformanceGateStatus.Passed,Analysis.AbsoluteBudget.Status);
        Assert.Equal(PerformanceGateStatus.Failed,Analysis.BaselineDelta.Status);
        Assert.False(Analysis.Passed);
    }

    [Fact]
    public void BaselineWithoutP95ComparesTheMedianOnly(){
        // 30 サンプルの最近順位法による p95 は 1 サンプル分。別プロセスのベースラインは対象から外す。
        PerformanceSample[]TailHeavy=[.. Enumerable.Range(0,30).Select(Index=>new PerformanceSample(Index,Index>=27?500:100,0))];
        PerformanceSample[]Slower=[.. Enumerable.Range(0,30).Select(Index=>new PerformanceSample(Index,130,0))];

        PerformanceAnalysis Tail=PerformanceMeasurementFramework.Analyze(
            CreatePlan(Baseline:new PerformanceBaseline(100,100,0,2000)),TailHeavy);
        PerformanceAnalysis MedianOnlyTail=PerformanceMeasurementFramework.Analyze(
            CreatePlan(Baseline:new PerformanceBaseline(100,null,0,2000)),TailHeavy);
        PerformanceAnalysis MedianOnlySlower=PerformanceMeasurementFramework.Analyze(
            CreatePlan(Baseline:new PerformanceBaseline(100,null,0,2000)),Slower);

        Assert.Equal(PerformanceGateStatus.Failed,Tail.BaselineDelta.Status);
        Assert.Equal(PerformanceGateStatus.Passed,MedianOnlyTail.BaselineDelta.Status);
        Assert.Equal(PerformanceGateStatus.Failed,MedianOnlySlower.BaselineDelta.Status);
        Assert.Contains("median",MedianOnlySlower.BaselineDelta.Detail,StringComparison.Ordinal);
    }

    [Fact]
    public void MissingOrUnmeasurableSamplesNeverPass(){
        PerformanceMeasurementPlan Plan=CreatePlan();
        PerformanceSample[]Samples=[.. Enumerable.Range(0,29).Select(Index=>new PerformanceSample(Index,10,0))];

        PerformanceAnalysis Analysis=PerformanceMeasurementFramework.Analyze(Plan,Samples);

        Assert.Equal(PerformanceGateStatus.Unavailable,Analysis.Measurement.Status);
        Assert.False(Analysis.Passed);
    }

    [Fact]
    public void MeasuredWorkloadExecutesExactlyFiveWarmupsAndThirtySamples(){
        PerformanceMeasurementPlan Plan=CreatePlan(Absolute:new PerformanceAbsoluteBudget(10_000_000_000,10_000_000_000,1_000_000));
        int Executions=0;

        (IReadOnlyList<PerformanceSample>Samples,PerformanceAnalysis Analysis)=PerformanceMeasurementFramework.Measure(Plan,_=>{ Executions++;Thread.SpinWait(10000); });

        Assert.Equal(35,Executions);
        Assert.Equal(30,Samples.Count);
        Assert.All(Samples,Sample=>Assert.True(Sample.ElapsedNanoseconds>0));
        Assert.True(Analysis.Passed,Analysis.AbsoluteBudget.Detail);
    }

    [Fact]
    public async Task MeasurementExcludesAllocationsFromOtherThreads(){
        PerformanceMeasurementPlan Plan=CreatePlan(
            Absolute:new PerformanceAbsoluteBudget(
                10_000_000_000,10_000_000_000,64*1024));
        using var Started=new ManualResetEventSlim();
        using var Stop=new CancellationTokenSource();
        Task Allocator=Task.Run(()=>{
            Started.Set();
            while(!Stop.IsCancellationRequested){
                _=new byte[256*1024];
            }
        });
        Started.Wait();

        try {
            (_,PerformanceAnalysis Analysis)=PerformanceMeasurementFramework.Measure(
                Plan,static _=>Thread.SpinWait(10000));

            Assert.True(Analysis.Passed,Analysis.AbsoluteBudget.Detail);
            Assert.True(Analysis.AllocatedBytes.P95<=64*1024);
        } finally {
            Stop.Cancel();
            await Allocator.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public void MachineFingerprintExcludesMachineNameAndPinsRuntimeShape(){
        PerformanceMachineFingerprint Fingerprint=PerformanceMeasurementFramework.CaptureMachineFingerprint();

        Assert.Equal(64,Fingerprint.FingerprintSha256.Length);
        Assert.True(Fingerprint.ProcessorCount>0);
        Assert.True(Fingerprint.AvailableMemoryBytes>0);
        Assert.False(string.IsNullOrWhiteSpace(Fingerprint.ProcessorIdentifier));
        Assert.True(Fingerprint.StopwatchFrequency>0);
        Assert.False(string.IsNullOrWhiteSpace(Fingerprint.RuntimeDescription));
        Assert.DoesNotContain(Environment.MachineName,Fingerprint.FingerprintSha256,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VersionedReportRejectsPayloadOrStatisticsTampering(){
        string Root=Path.Combine(Path.GetTempPath(),"cge-performance-report-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        try {
            PerformanceMeasurementPlan Plan=CreatePlan();
            PerformanceSample[]Samples=[.. Enumerable.Range(0,30).Select(Index=>new PerformanceSample(Index,10,0))];
            PerformanceAnalysis Analysis=PerformanceMeasurementFramework.Analyze(Plan,Samples);
            var Payload=new PerformanceReportPayload(PerformanceFrameworkProbe.ReportSchema,DateTimeOffset.Parse("2026-08-31T00:00:00Z"),Plan,Samples,Analysis,PerformanceMeasurementFramework.CaptureMachineFingerprint(),"contract",true);
            string ReportPath=await PerformanceReportStore.SaveAsync(Root,Payload,CancellationToken.None);

            Assert.Empty(PerformanceReportStore.Validate(ReportPath));
            string Json=await File.ReadAllTextAsync(ReportPath);
            await File.WriteAllTextAsync(ReportPath,Json.Replace("\"elapsedNanoseconds\": 10","\"elapsedNanoseconds\": 11",StringComparison.Ordinal));

            IReadOnlyList<string>Errors=PerformanceReportStore.Validate(ReportPath);
            Assert.Contains(Errors,Error=>Error.Contains("report SHA-256 mismatch",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("statistics or gate verdict",StringComparison.Ordinal));
        } finally { Directory.Delete(Root,true); }
    }

    private static PerformanceMeasurementPlan CreatePlan(int Warmups=5,int Samples=30,PerformanceAbsoluteBudget?Absolute=null,PerformanceBaseline?Baseline=null)=>new(
        "contract", "Release","nanoseconds",new Dictionary<string,string>{{"workload","synthetic"}},Warmups,Samples,Absolute,Baseline);
}
