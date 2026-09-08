using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class RenderingPerformanceProbeTests {
    [Fact]
    public void ValidRawReportRequiresBothThirtySampleWorkloads(){
        RenderingRawPerformanceReport Report=CreateValidReport();

        Assert.Empty(RenderingPerformanceProbe.ValidateRawReport(Report));
    }

    [Fact]
    public void MissingDuplicateOrUnmeasurableSamplesFailClosed(){
        RenderingRawPerformanceReport Missing=CreateValidReport() with {
            Workloads=[
                CreateWorkload("rendering.frame-1280x720",29),
                CreateWorkload("rendering.visibility-100k",30),
            ],
        };
        RenderingRawPerformanceReport Duplicate=CreateValidReport() with {
            Workloads=[
                CreateWorkload("rendering.frame-1280x720",30,[0,0]),
                CreateWorkload("rendering.visibility-100k",30),
            ],
        };
        RenderingRawPerformanceReport Unmeasurable=CreateValidReport() with {
            Workloads=[
                CreateWorkload("rendering.frame-1280x720",30,ElapsedOverride:0),
                CreateWorkload("rendering.visibility-100k",30),
            ],
        };

        Assert.Contains(RenderingPerformanceProbe.ValidateRawReport(Missing),Error=>Error.Contains("30",StringComparison.Ordinal));
        Assert.Contains(RenderingPerformanceProbe.ValidateRawReport(Duplicate),Error=>Error.Contains("Duplicate",StringComparison.Ordinal));
        Assert.Contains(RenderingPerformanceProbe.ValidateRawReport(Unmeasurable),Error=>Error.Contains("measurable",StringComparison.Ordinal));
    }

    [Fact]
    public void DebugErrorsPixelFailureWrongAdapterAndAllocationFailClosed(){
        RenderingRawPerformanceReport Broken=CreateValidReport() with {
            Adapter="hardware",
            ShaderArtifactSha256="invalid",
            DebugErrorCount=1,
            PixelVerified=false,
            Workloads=[
                CreateWorkload("rendering.frame-1280x720",30,AllocatedOverride:1_000_001),
                CreateWorkload("rendering.visibility-100k",30),
            ],
        };

        IReadOnlyList<string>Errors=RenderingPerformanceProbe.ValidateRawReport(Broken);

        Assert.Contains(Errors,Error=>Error.Contains("adapter",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains("shader",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains("debug",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains("pixel",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains("allocation",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RuntimeModeAndRequestedConfigurationFailClosed(){
        RenderingRawPerformanceReport Broken=CreateValidReport() with {
            Configuration="Debug",
            RuntimeMode="runtime-default",
        };

        IReadOnlyList<string>Errors=RenderingPerformanceProbe.ValidateRawReport(
            Broken,"warp","Release");

        Assert.Contains(Errors,Error=>Error.Contains("configuration",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains("tiered",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReleaseVisibilityPlanEnforcesTheFixedTickCpuBudget(){
        RenderingRawPerformanceReport Report=CreateValidReport() with {Configuration="Release"};
        RenderingRawWorkload Workload=Report.Workloads.Single(
            static Workload=>Workload.Id=="rendering.visibility-100k");
        PerformanceMeasurementPlan Plan=RenderingPerformanceProbe.CreatePlan(Report,Workload,"raw-sha");

        Assert.Equal(6_000_000,Plan.AbsoluteBudget!.MaximumMedianNanoseconds);
        Assert.Equal(8_330_000,Plan.AbsoluteBudget.MaximumP95Nanoseconds);
        Assert.Equal(0,Plan.AbsoluteBudget.MaximumP95AllocatedBytes);
        Assert.False(PerformanceMeasurementFramework.Analyze(
            Plan,CreateWorkload("rendering.visibility-100k",30,ElapsedOverride:8_500_000).Samples).Passed);
    }

    private static RenderingRawPerformanceReport CreateValidReport()=>new(
        RenderingPerformanceProbe.RawSchema,"Debug","warp","Microsoft Basic Render Driver",
        "tiered-compilation-disabled",new string('a',64),1280,720,0,true,
        [
            CreateWorkload("rendering.frame-1280x720",30),
            CreateWorkload("rendering.visibility-100k",30),
        ]);

    private static RenderingRawWorkload CreateWorkload(
        string Id,int Count,IReadOnlyList<int>?Indices=null,
        long?ElapsedOverride=null,long?AllocatedOverride=null){
        int[]ResolvedIndices=Indices is null?[.. Enumerable.Range(0,Count)]:[.. Indices,.. Enumerable.Range(Indices.Count,Math.Max(0,Count-Indices.Count))];
        return new RenderingRawWorkload(
            Id,5,
            [.. ResolvedIndices.Select(Index=>new PerformanceSample(
                Index,ElapsedOverride??100_000+Index,AllocatedOverride??0))]);
    }
}
