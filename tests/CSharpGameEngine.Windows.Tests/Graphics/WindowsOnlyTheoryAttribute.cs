namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>
/// The <see cref="TheoryAttribute"/> counterpart of <see cref="WindowsOnlyFactAttribute"/>: explicitly Skipped on
/// non-Windows platforms and always run on Windows.
/// </summary>
public sealed class WindowsOnlyTheoryAttribute:TheoryAttribute {
    public WindowsOnlyTheoryAttribute(){
        if(!OperatingSystem.IsWindows()){
            this.Skip="Requires Windows (D3D12/DXGI).";
        }
    }
}
