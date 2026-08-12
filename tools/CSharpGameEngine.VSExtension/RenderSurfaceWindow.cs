using System;
using System.Runtime.InteropServices;
using static CSharpGameEngine.VSExtension.NativeMethods;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// A single lightweight native <c>WS_CHILD</c> window used purely as the D3D12 swap-chain target
/// - the "slot" that <see cref="ViewerHwndHost"/> returns from <c>BuildWindowCore</c>. Forwards
/// <c>WM_SIZE</c> so the caller can resize the swap chain to match (ADR-0008).
/// </summary>
internal sealed class RenderSurfaceWindow:IDisposable {
    private const string ClassName="CSharpGameEngine.VSExtension.RenderSurface";

    private static readonly object RegistrationLock=new();
    private static readonly WndProcDelegate WndProcDelegate=WndProc;
    private static bool ClassRegistered;

    private static readonly System.Collections.Generic.Dictionary<IntPtr,RenderSurfaceWindow>Instances=[];

    private IntPtr Hwnd;
    private readonly IntPtr ParentHwnd;
    private bool Disposed;

    public event Action<int,int>?Resized;

    public RenderSurfaceWindow(IntPtr ParentHwnd,int Width,int Height,bool InitiallyVisible=true){
        EnsureClassRegistered();
        this.ParentHwnd=ParentHwnd;

        Hwnd=CreateWindowEx(
            WS_EX_NOREDIRECTIONBITMAP,
            ClassName,
            string.Empty,
            WS_CHILD|(InitiallyVisible?WS_VISIBLE:0),
            0,
            0,
            Width,
            Height,
            ParentHwnd,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if(Hwnd==IntPtr.Zero){
            var Error=Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"CreateWindowEx failed with Win32 error 0x{Error:X8}.");
        }

        lock(Instances){
            Instances[Hwnd]=this;
        }
    }

    public IntPtr Handle=>Hwnd;

    /// <summary>
    /// 未描画の <c>WS_EX_NOREDIRECTIONBITMAP</c> 子HWNDは、ドッキング先に以前表示されていた
    /// ToolWindowを背面から露出させることがある。RendererがPresent可能な間だけ表示する。
    /// </summary>
    public void SetVisible(bool Visible){
        if(!Disposed&&Hwnd!=IntPtr.Zero){
            _=ShowWindow(Hwnd,Visible?SW_SHOWNA:SW_HIDE);
        }
    }

    public void Dispose(){
        if(Disposed){
            return;
        }

        Disposed=true;

        lock(Instances){
            Instances.Remove(Hwnd);
        }

        if(Hwnd!=IntPtr.Zero){
            // If this native child window still owns real Win32 keyboard focus when destroyed,
            // Windows leaves focus dangling on a handle that no longer exists and WPF's own focus
            // tracking (Keyboard.FocusedElement) desyncs from the real OS focus state for the rest
            // of the VS session - Tab/Backspace/Delete stop reaching the text editor even after this
            // pane is closed, while plain character input (routed differently) keeps working. Hand
            // focus back to the parent HWND before tearing down the native window.
            if(GetFocus()==Hwnd){
                SetFocus(ParentHwnd);
            }

            _=DestroyWindow(Hwnd);
            Hwnd=IntPtr.Zero;
        }
    }

    private static void EnsureClassRegistered(){
        if(ClassRegistered){
            return;
        }

        lock(RegistrationLock){
            if(ClassRegistered){
                return;
            }

            WNDCLASSEX Wc=new(){
                CbSize=(uint)Marshal.SizeOf<WNDCLASSEX>(),
                Style=0,
                LpfnWndProc=WndProcDelegate,
                HInstance=GetModuleHandle(null),
                HCursor=IntPtr.Zero,
                LpszClassName=ClassName,
            };

            if(RegisterClassEx(ref Wc)==0){
                var Error=Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"RegisterClassEx failed with Win32 error 0x{Error:X8}.");
            }

            ClassRegistered=true;
        }
    }

    private static IntPtr WndProc(IntPtr HWnd,uint Msg,IntPtr WParam,IntPtr LParam){
        if(Msg==WM_SIZE){
            RenderSurfaceWindow?Instance;
            lock(Instances){
                Instances.TryGetValue(HWnd,out Instance);
            }

            if(Instance is not null){
                var Lp=LParam.ToInt64();
                var Width=(int)(Lp&0xFFFF);
                var Height=(int)((Lp >> 16)&0xFFFF);
                Instance.Resized?.Invoke(Width,Height);
            }

            return IntPtr.Zero;
        }

        if(Msg==WM_SETFOCUS){
            // This window is a pure render target with no keyboard interaction of its own. It never
            // implements WM_SETFOCUS/WM_KILLFOCUS beyond DefWindowProc's no-op, so WPF's HwndHost/
            // HwndSource keyboard-input-sink machinery is never notified when real Win32 focus
            // actually lands here (e.g. a mouse click into the 3D preview) - Keyboard.FocusedElement
            // then desyncs from the real OS focus state for the rest of the VS session, breaking
            // Tab/Backspace/Delete everywhere (not just in this pane) while plain character input
            // (routed differently) keeps working. Refuse focus outright by bouncing it straight back
            // to the parent HWND instead of ever actually holding it.
            RenderSurfaceWindow?FocusedInstance;
            lock(Instances){
                Instances.TryGetValue(HWnd,out FocusedInstance);
            }

            if(FocusedInstance is not null){
                SetFocus(FocusedInstance.ParentHwnd);
            }

            return IntPtr.Zero;
        }

        return DefWindowProc(HWnd,Msg,WParam,LParam);
    }
}
