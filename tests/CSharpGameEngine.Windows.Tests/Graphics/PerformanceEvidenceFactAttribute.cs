namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>
/// 必要時に性能または精度の証拠を記録するWindows GPUテスト。<c>CGE_PERFORMANCE_EVIDENCE=1</c>の場合だけ実行し、
/// それ以外はSkippedとして報告する。通常のテスト実行を速く保つため。
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
