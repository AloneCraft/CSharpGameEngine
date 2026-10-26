using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Runtime.Windows;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;
namespace CSharpGameEngine.Runtime.Windows.Tests;
[Collection(D3D12GpuCollection.Name)]
public sealed unsafe class RuntimeAutomationWindowTests {
    [WindowsOnlyFact]
    public void Automation_CreatesAHiddenWindowAtTheRequestedSize(){
        var Variables=new[] { RuntimeAutomation.FramesVariable,RuntimeAutomation.InputVariable,RuntimeAutomation.CaptureVariable,RuntimeAutomation.ReportVariable };
        var Previous=Variables.Select(Environment.GetEnvironmentVariable).ToArray();
        try {
            foreach(var Variable in Variables){ Environment.SetEnvironmentVariable(Variable,null); }
            Environment.SetEnvironmentVariable(RuntimeAutomation.FramesVariable,"1");
            using var Host=new EngineHost();
            Assert.NotNull(Host.Automation);
            var Window=Host.EnsureWindow(new AppHost { Title="CGE hidden automation test",Width=160,Height=90 });
            Assert.Equal(160,Window.ClientWidth);
            Assert.Equal(90,Window.ClientHeight);
            Assert.False(IsWindowVisible((HWND)Window.Handle));
        } finally {
            for(var Index=0;Index<Variables.Length;Index++){
                Environment.SetEnvironmentVariable(Variables[Index],Previous[Index]);
            }
        }
    }
}
