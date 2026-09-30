using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Platform.Windows;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;
namespace CSharpGameEngine.Platform.Windows.Tests;
/// <summary>Mouse-look capture changes the system-wide cursor clip only while its window owns focus.</summary>
public sealed class Win32WindowMouseCaptureTests {
    private static unsafe RECT CurrentClip(){
        RECT Clip;
        Assert.True(GetClipCursor(&Clip));
        return Clip;
    }
    [WindowsOnlyFact]
    public unsafe void SetMouseCaptured_OnAnUnfocusedWindow_LeavesTheCursorClipAlone(){
        var Before=CurrentClip();
        try {
            using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Mouse Capture Test",Width=64,Height=64,Visible=false });
            Window.SetMouseCaptured(true);
            Assert.True(Window.IsMouseCaptured);
            var During=CurrentClip();
            Assert.Equal((Before.left,Before.top,Before.right,Before.bottom),(During.left,During.top,During.right,During.bottom));
            Window.SetMouseCaptured(false);
            Assert.False(Window.IsMouseCaptured);
        }
        finally {
            // A failure must not leave the developer's cursor trapped.
            _=ClipCursor(null);
        }
    }
}
