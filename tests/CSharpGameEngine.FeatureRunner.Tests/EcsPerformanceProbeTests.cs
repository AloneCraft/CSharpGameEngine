using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class EcsPerformanceProbeTests {
    [Fact]
    public void CanonicalBoundaryReportsAreAcceptedAndProduceBoundPlans(){
        EcsRawPerformanceReport[]Reports=new[]{1,64,65,128}
            .Select(CreateReport).ToArray();

        Assert.Empty(EcsPerformanceProbe.ValidateRawReports(Reports,"Release"));
        foreach(EcsRawPerformanceReport Report in Reports){
            PerformanceMeasurementPlan Plan=EcsPerformanceProbe.CreatePlan(
                Report,new string('f',64));
            Assert.Equal($"ecs.generated-query-{Report.ComponentTypeCount}",Plan.Id);
            Assert.Equal("100000",Plan.Workload["activeEntities"]);
            Assert.Equal(Report.GeneratedSourceSha256,Plan.Workload["generatedSourceSha256"]);
            Assert.Equal(Report.StateSha256,Plan.Workload["stateSha256"]);
            Assert.Equal(Report.ComponentTypeCount.ToString(),Plan.Workload["componentTypeCount"]);
            Assert.Equal($"write:C{Report.ComponentTypeCount-1}",Plan.Workload["queryShape"]);
            Assert.Equal(0,Plan.AbsoluteBudget!.MaximumP95AllocatedBytes);
        }
    }

    [Fact]
    public void MissingDuplicateExtraAndWrongBoundaryReportsFailClosed(){
        EcsRawPerformanceReport[]Canonical=new[]{1,64,65,128}
            .Select(CreateReport).ToArray();
        EcsRawPerformanceReport Duplicate=CreateReport(64);
        EcsRawPerformanceReport Extra=CreateReport(127);
        EcsRawPerformanceReport WrongBoundary=CreateReport(65) with {
            QueryComponent="C63",
            Workload=CreateReport(65).Workload with {Id="ecs.generated-query-64"},
        };

        Assert.Contains(EcsPerformanceProbe.ValidateRawReports(
            [.. Canonical.Where(static Report=>Report.ComponentTypeCount!=128)]),
            Error=>Error.Contains("128",StringComparison.Ordinal));
        Assert.Contains(EcsPerformanceProbe.ValidateRawReports(
            [.. Canonical,Duplicate]),
            Error=>Error.Contains("Duplicate",StringComparison.Ordinal));
        Assert.Contains(EcsPerformanceProbe.ValidateRawReports(
            [.. Canonical,Extra]),
            Error=>Error.Contains("Unregistered",StringComparison.Ordinal));
        Assert.Contains(EcsPerformanceProbe.ValidateRawReports(
            [Canonical[0],Canonical[1],WrongBoundary,Canonical[3]]),
            Error=>Error.Contains("query",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TamperedRuntimeSourceStateAndSamplesFailClosed(){
        EcsRawPerformanceReport Canonical=CreateReport(128);
        EcsRawPerformanceReport Broken=Canonical with {
            RuntimeMode="runtime-default",
            GeneratedSourceSha256="bad",
            StateSha256=new string('A',64),
            Workload=Canonical.Workload with {
                WarmupCount=4,
                Samples=[
                    .. Canonical.Workload.Samples.Take(29),
                    new PerformanceSample(28,0,1),
                ],
            },
        };

        IReadOnlyList<string>Errors=EcsPerformanceProbe.ValidateRawReports(
            [CreateReport(1),CreateReport(64),CreateReport(65),Broken],"Release");
        Assert.Contains(Errors,Error=>Error.Contains("Tiered",StringComparison.Ordinal));
        Assert.Contains(Errors,Error=>Error.Contains("generated",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains("state",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains("warm-up",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains("Duplicate",StringComparison.Ordinal));
        Assert.Contains(Errors,Error=>Error.Contains("measurable",StringComparison.Ordinal));
        Assert.Contains(Errors,Error=>Error.Contains("allocation",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DebugRecordsTimingButReleaseOwnsTheShippingBudget(){
        PerformanceMeasurementPlan DebugPlan=EcsPerformanceProbe.CreatePlan(
            CreateReport(64) with {Configuration="Debug"},new string('d',64),
            new PerformanceBaseline(1,1,0,0));
        PerformanceMeasurementPlan ReleasePlan=EcsPerformanceProbe.CreatePlan(
            CreateReport(64),new string('e',64));

        Assert.Equal(long.MaxValue,DebugPlan.AbsoluteBudget!.MaximumMedianNanoseconds);
        Assert.Equal(long.MaxValue,DebugPlan.AbsoluteBudget.MaximumP95Nanoseconds);
        Assert.True(ReleasePlan.AbsoluteBudget!.MaximumMedianNanoseconds<long.MaxValue);
        Assert.True(ReleasePlan.AbsoluteBudget.MaximumP95Nanoseconds<long.MaxValue);
        Assert.Equal(0,DebugPlan.AbsoluteBudget.MaximumP95AllocatedBytes);
        Assert.Equal(0,ReleasePlan.AbsoluteBudget.MaximumP95AllocatedBytes);
        Assert.Null(DebugPlan.Baseline);
    }

    private static EcsRawPerformanceReport CreateReport(int ComponentTypeCount){
        string QueryComponent="C"+(ComponentTypeCount-1);
        char SourceDigit=ComponentTypeCount switch {1=>'1',64=>'4',65=>'5',128=>'8',_=>'e'};
        string StateSha256=ComponentTypeCount is 1 or 64 or 65 or 128
            ?EcsPerformanceProbe.ComputeExpectedStateSha256(ComponentTypeCount)
            :new string('f',64);
        return new EcsRawPerformanceReport(
            EcsPerformanceProbe.RawSchema,"Release",
            EcsPerformanceProbe.RequiredRuntimeMode,ComponentTypeCount,
            QueryComponent,100_000,1,new string(SourceDigit,64),
            StateSha256,
            ComponentTypeCount<=64?1:2,ComponentTypeCount>64,
            new EcsRawWorkload(
                $"ecs.generated-query-{ComponentTypeCount}",5,
                [.. Enumerable.Range(0,30).Select(
                    static Index=>new PerformanceSample(Index,1_000_000+Index,0))]));
    }
}
