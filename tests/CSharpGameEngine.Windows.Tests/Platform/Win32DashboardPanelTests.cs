using System.Runtime.InteropServices;
using System.Text;
using CSharpGameEngine.Platform.Windows;
namespace CSharpGameEngine.Windows.Tests.Platform;
public sealed class Win32DashboardPanelTests {
    [Fact]
    public void Panel_UpdatesMachineStatusAndVisibility(){
        using var Window=new Win32Window(new Win32WindowOptions { Width=800,Height=600,Visible=false });
        using var Panel=new Win32DashboardPanel(Window.Handle);
        var Document=new DashboardDocument("C# Game Engine","ECS",[new DashboardLine("FixedTick","42")],[new DashboardBar("Features",54,149)],"1-4 pages","CGE|page=ecs|tick=42");
        Panel.Update(Document);
        var Buffer=new StringBuilder(256);
        _=GetWindowText(Panel.Handle,Buffer,Buffer.Capacity);
        Assert.StartsWith(Document.MachineStatus+"|paint=",Buffer.ToString(),StringComparison.Ordinal);
        Assert.InRange(Panel.PixelWidth,1,360);
        Assert.True(Panel.PixelHeight>0);
        Panel.SetVisible(false);Assert.False(Panel.Visible);
        Panel.SetVisible(true);Assert.True(Panel.Visible);
        var Snapshot=Path.Combine(AppContext.BaseDirectory,"DashboardTests",Guid.NewGuid()+".bmp");
        try {
            Panel.SaveSnapshot(Snapshot);
            var Bytes=File.ReadAllBytes(Snapshot);
            Assert.True(Bytes.Length>54);
            HashSet<int>Colors=[];
            for(var Offset=54;Offset+2<Bytes.Length;Offset+=64){ Colors.Add(Bytes[Offset]|(Bytes[Offset+1]<<8)|(Bytes[Offset+2]<<16)); }
            Assert.True(Colors.Count>=8,$"Expected dashboard text and bar pixels, got {Colors.Count} sampled colors.");
        } finally { if(File.Exists(Snapshot)){ File.Delete(Snapshot); } }
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="GetWindowTextW")]
    private static extern int GetWindowText(nint Hwnd,StringBuilder Text,int MaximumCount);
}
