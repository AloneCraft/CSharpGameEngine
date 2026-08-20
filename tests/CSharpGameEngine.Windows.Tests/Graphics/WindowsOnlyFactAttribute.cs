namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that reports as explicitly Skipped (not silently passed) on
/// non-Windows platforms. On Windows it always runs - never auto-skip a failing GPU test here.
/// </summary>
public sealed class WindowsOnlyFactAttribute:FactAttribute {
    public WindowsOnlyFactAttribute(){
        if(!OperatingSystem.IsWindows()){
            this.Skip="Requires Windows (D3D12/DXGI).";
        }
    }
}
