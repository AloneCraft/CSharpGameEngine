using System.Runtime.InteropServices;
using static CSharpGameEngine.VSExtension.NativeMethods;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class AssetEditorFloatingOwnerTests {
    [Fact]
    public void FloatingContentOwnsItsRootToTheIdeWithoutActivatingOrBecomingTopmost(){
        var Main=Create();
        var Floating=Create();
        var Child=CreateWindowEx(0,"STATIC",string.Empty,0x40000000,0,0,10,10,Floating,IntPtr.Zero,GetModuleHandle(null),IntPtr.Zero);
        try {
            Assert.NotEqual(IntPtr.Zero,Child);
            var Foreground=GetForegroundWindow();
            var Style=GetWindowLong(Floating,-20);
            Assert.Equal(IntPtr.Zero,GetWindow(Floating,4));
            Assert.True(AssetEditorFloatingOwner.Apply(Child,Main));
            Assert.Equal(Main,GetWindow(Floating,4));
            Assert.Equal(Foreground,GetForegroundWindow());
            Assert.Equal(Style,GetWindowLong(Floating,-20));
            Assert.Equal(Floating,GetAncestor(Child,2));
            Assert.True(AssetEditorFloatingOwner.Apply(Child,Main));
            Assert.Equal(Main,GetWindow(Floating,4));
        } finally { DestroyWindow(Floating);DestroyWindow(Main); }
    }
    [Fact]
    public void DockedContentAndMissingHandlesDoNotChangeTheIdeWindow(){
        var Main=Create();
        var Child=CreateWindowEx(0,"STATIC",string.Empty,0x40000000,0,0,10,10,Main,IntPtr.Zero,GetModuleHandle(null),IntPtr.Zero);
        try {
            var Owner=GetWindow(Main,4);
            Assert.False(AssetEditorFloatingOwner.Apply(Child,Main));
            Assert.Equal(Owner,GetWindow(Main,4));
            Assert.False(AssetEditorFloatingOwner.Apply(IntPtr.Zero,Main));
            Assert.False(AssetEditorFloatingOwner.Apply(Child,IntPtr.Zero));
        } finally { DestroyWindow(Main); }
    }
    [Fact]
    public void ClosingOneFloatingWindowDoesNotAffectAnother(){
        var Main=Create();
        var First=Create();
        var Second=Create();
        try {
            Assert.True(AssetEditorFloatingOwner.Apply(First,Main));
            Assert.True(AssetEditorFloatingOwner.Apply(Second,Main));
            DestroyWindow(First);
            Assert.True(IsWindow(Main));
            Assert.True(IsWindow(Second));
            Assert.Equal(Main,GetWindow(Second,4));
        } finally { if(IsWindow(First)){ DestroyWindow(First); }DestroyWindow(Second);DestroyWindow(Main); }
    }
    private static IntPtr Create(){
        var Window=CreateWindowEx(0,"STATIC",string.Empty,0,0,0,10,10,IntPtr.Zero,IntPtr.Zero,GetModuleHandle(null),IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero,Window);
        return Window;
    }
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr Window,uint Command);
    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr Window,uint Flags);
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr Window,int Index);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
