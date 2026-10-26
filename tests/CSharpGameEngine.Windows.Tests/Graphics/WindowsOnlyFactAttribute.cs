namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>
/// Windows以外では明示的にSkippedとして報告し、成功扱いにしない<see cref="FactAttribute"/>。
/// Windowsでは常に実行する。ここでは失敗するGPUテストを自動スキップしない。
/// </summary>
public sealed class WindowsOnlyFactAttribute:FactAttribute {
    public WindowsOnlyFactAttribute(){
        if(!OperatingSystem.IsWindows()){
            this.Skip="Requires Windows (D3D12/DXGI).";
        }
    }
}
