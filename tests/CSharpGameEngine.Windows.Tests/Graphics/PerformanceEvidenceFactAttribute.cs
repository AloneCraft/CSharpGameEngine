namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>
/// A Windows GPU fact that records performance evidence on demand: it runs only when
/// <c>CGE_PERFORMANCE_EVIDENCE=1</c> and is reported as Skipped otherwise, so ordinary test runs stay fast.
/// </summary>
public sealed class PerformanceEvidenceFactAttribute:FactAttribute {
    public const string EnvironmentVariable="CGE_PERFORMANCE_EVIDENCE";
    public PerformanceEvidenceFactAttribute(){
        if(!OperatingSystem.IsWindows()){
            this.Skip="Requires Windows (D3D12/DXGI).";
        } else if(Environment.GetEnvironmentVariable(EnvironmentVariable)!="1"){
            this.Skip=$"Performance evidence runs only with {EnvironmentVariable}=1.";
        }
    }
}
