using System;
using static CSharpGameEngine.VSExtension.NativeMethods;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class RenderSurfaceHostWindowTests {
    [Fact]
    public void RecreateSurface_KeepsContainerAndReplacesSwapChainTarget(){
        IntPtr Parent=CreateWindowEx(
            0,"STATIC",string.Empty,0,
            0,0,800,600,IntPtr.Zero,IntPtr.Zero,GetModuleHandle(null),IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero,Parent);

        try {
            using RenderSurfaceHostWindow Host=new(Parent,800,600,InitiallyVisible:false);
            IntPtr Container=Host.Handle;
            IntPtr FirstSurface=Host.Surface.Handle;

            Assert.True(IsWindow(Container));
            Assert.True(IsWindow(FirstSurface));
            Assert.Equal<ulong>(1,Host.Generation);

            Assert.True(MoveWindow(Container,0,0,320,240,Repaint:false));
            Assert.Equal(320,Host.Width);
            Assert.Equal(240,Host.Height);

            RenderSurfaceWindow Replacement=Host.RecreateSurface(InitiallyVisible:false);

            Assert.Equal(Container,Host.Handle);
            Assert.NotEqual(FirstSurface,Replacement.Handle);
            Assert.False(IsWindow(FirstSurface));
            Assert.True(IsWindow(Replacement.Handle));
            Assert.Equal<ulong>(2,Host.Generation);
        } finally {
            _=DestroyWindow(Parent);
        }
    }

    [Fact]
    public void Dispose_DestroysContainerAndCurrentSurface(){
        IntPtr Parent=CreateWindowEx(
            0,"STATIC",string.Empty,0,
            0,0,800,600,IntPtr.Zero,IntPtr.Zero,GetModuleHandle(null),IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero,Parent);

        try {
            RenderSurfaceHostWindow Host=new(Parent,800,600,InitiallyVisible:false);
            IntPtr Container=Host.Handle;
            IntPtr Surface=Host.Surface.Handle;

            Host.Dispose();

            Assert.False(IsWindow(Surface));
            Assert.False(IsWindow(Container));
        } finally {
            _=DestroyWindow(Parent);
        }
    }
}
