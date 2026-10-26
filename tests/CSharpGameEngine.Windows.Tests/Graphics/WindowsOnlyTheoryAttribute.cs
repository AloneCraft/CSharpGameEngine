namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>
/// <see cref="WindowsOnlyFactAttribute"/>に対応する<see cref="TheoryAttribute"/>。Windows以外では明示的にSkipped、
/// Windowsでは常に実行する。
/// </summary>
public sealed class WindowsOnlyTheoryAttribute:TheoryAttribute {
    public WindowsOnlyTheoryAttribute(){
        if(!OperatingSystem.IsWindows()){
            this.Skip="Requires Windows (D3D12/DXGI).";
        }
    }
}
