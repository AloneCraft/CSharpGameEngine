using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static CSharpGameEngine.VSExtension.NativeMethods;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Stable child HWND returned to WPF while the actual DXGI target is a replaceable child
/// generation. A crashed out-of-process host must never attach a new swap chain to its prior HWND.
/// </summary>
internal sealed class RenderSurfaceHostWindow:IDisposable {
    private const string ClassName="CSharpGameEngine.VSExtension.RenderSurfaceHost";
    private static readonly object RegistrationLock=new();
    private static readonly WndProcDelegate WindowProcedure=WndProc;
    private static readonly Dictionary<IntPtr,RenderSurfaceHostWindow>Instances=[];
    private static bool ClassRegistered;
    private IntPtr Hwnd;
    private RenderSurfaceWindow?CurrentSurface;
    private int LastPositiveWidth;
    private int LastPositiveHeight;
    private bool Disposed;

    internal RenderSurfaceHostWindow(
        IntPtr ParentHwnd,int Width,int Height,bool InitiallyVisible=true){
        if(ParentHwnd==IntPtr.Zero){ throw new ArgumentException("A parent HWND is required.",nameof(ParentHwnd)); }
        if(Width<=0){ throw new ArgumentOutOfRangeException(nameof(Width)); }
        if(Height<=0){ throw new ArgumentOutOfRangeException(nameof(Height)); }
        EnsureClassRegistered();
        this.LastPositiveWidth=Width;
        this.LastPositiveHeight=Height;
        this.Hwnd=CreateWindowEx(
            0,ClassName,string.Empty,WS_CHILD|WS_VISIBLE,
            0,0,Width,Height,ParentHwnd,IntPtr.Zero,GetModuleHandle(null),IntPtr.Zero);
        if(this.Hwnd==IntPtr.Zero){
            int Error=Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"CreateWindowEx failed for the render surface host with Win32 error 0x{Error:X8}.");
        }

        lock(Instances){
            Instances[this.Hwnd]=this;
        }

        try {
            this.CurrentSurface=new RenderSurfaceWindow(
                this.Hwnd,Width,Height,InitiallyVisible);
            this.Generation=1;
        } catch {
            lock(Instances){
                Instances.Remove(this.Hwnd);
            }
            _=DestroyWindow(this.Hwnd);
            this.Hwnd=IntPtr.Zero;
            throw;
        }
    }

    internal IntPtr Handle=>this.Hwnd;
    internal int Width=>this.LastPositiveWidth;
    internal int Height=>this.LastPositiveHeight;
    internal ulong Generation{get;private set;}
    internal RenderSurfaceWindow Surface=>this.CurrentSurface
        ??throw new ObjectDisposedException(nameof(RenderSurfaceHostWindow));

    internal RenderSurfaceWindow RecreateSurface(bool InitiallyVisible){
        if(this.Disposed){
            throw new ObjectDisposedException(nameof(RenderSurfaceHostWindow));
        }
        this.CurrentSurface?.Dispose();
        this.CurrentSurface=null;
        RenderSurfaceWindow Replacement=new(
            this.Hwnd,this.LastPositiveWidth,this.LastPositiveHeight,InitiallyVisible);
        this.CurrentSurface=Replacement;
        this.Generation=checked(this.Generation+1);
        return Replacement;
    }

    public void Dispose(){
        if(this.Disposed){ return; }
        this.Disposed=true;
        this.CurrentSurface?.Dispose();
        this.CurrentSurface=null;
        lock(Instances){
            Instances.Remove(this.Hwnd);
        }
        if(this.Hwnd!=IntPtr.Zero){
            _=DestroyWindow(this.Hwnd);
            this.Hwnd=IntPtr.Zero;
        }
    }

    private void ResizeSurface(int Width,int Height){
        if(Width>0&&Height>0){
            this.LastPositiveWidth=Width;
            this.LastPositiveHeight=Height;
        }
        if(this.CurrentSurface is { }Surface){
            _=MoveWindow(Surface.Handle,0,0,Width,Height,Repaint:false);
        }
    }

    private static void EnsureClassRegistered(){
        if(ClassRegistered){ return; }
        lock(RegistrationLock){
            if(ClassRegistered){ return; }
            WNDCLASSEX WindowClass=new(){
                CbSize=(uint)Marshal.SizeOf<WNDCLASSEX>(),
                LpfnWndProc=WindowProcedure,
                HInstance=GetModuleHandle(null),
                HCursor=LoadCursor(IntPtr.Zero,IDC_ARROW),
                LpszClassName=ClassName,
            };
            if(RegisterClassEx(ref WindowClass)==0){
                int Error=Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"RegisterClassEx failed for the render surface host with Win32 error 0x{Error:X8}.");
            }
            ClassRegistered=true;
        }
    }

    private static IntPtr WndProc(
        IntPtr HWnd,uint Message,IntPtr WParam,IntPtr LParam){
        if(Message==WM_SIZE){
            RenderSurfaceHostWindow?Instance;
            lock(Instances){
                Instances.TryGetValue(HWnd,out Instance);
            }
            if(Instance is not null){
                long Size=LParam.ToInt64();
                int Width=(int)(Size&0xFFFF);
                int Height=(int)((Size>>16)&0xFFFF);
                Instance.ResizeSurface(Width,Height);
            }
            return IntPtr.Zero;
        }
        return DefWindowProc(HWnd,Message,WParam,LParam);
    }
}
