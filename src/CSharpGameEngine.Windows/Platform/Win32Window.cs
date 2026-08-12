using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.GWLP;
using static TerraFX.Interop.Windows.PM;
using static TerraFX.Interop.Windows.SW;
using static TerraFX.Interop.Windows.Windows;
using static TerraFX.Interop.Windows.WM;
using static TerraFX.Interop.Windows.WS;

namespace CSharpGameEngine.Platform.Windows;

/// <summary>
/// Owns exactly one raw Win32 <c>HWND</c>: creates it, pumps its message queue, and destroys it.
/// Not thread-safe; must be created, pumped, and disposed from the same thread.
/// </summary>
public sealed unsafe class Win32Window:IDisposable {
    private const string ClassName="CSharpGameEngine.Win32Window";
    private static readonly char*IDC_ARROW=(char*)0x7F00;

    private static readonly object RegistrationLock=new();
    private static bool ClassRegistered;

    private GCHandle SelfHandle;
    private HWND Hwnd;
    private bool Destroyed;
    private bool Disposed;

    private readonly bool[]KeyDown=new bool[256];
    private bool MouseCaptured;
    private POINT CapturedCenterScreenPoint;
    private bool SkipNextMouseDelta;

    public event Action<int,int>?Resized;

    public Win32Window(Win32WindowOptions Options){
        ArgumentNullException.ThrowIfNull(Options);

        EnsureDpiAwareness();
        EnsureClassRegistered();

        SelfHandle=GCHandle.Alloc(this,GCHandleType.Normal);

        const uint Style=WS_OVERLAPPEDWINDOW;

        RECT WindowRect=new(){ left=0,top=0,right=Options.Width,bottom=Options.Height };
        _=AdjustWindowRect(&WindowRect,Style,FALSE);
        var WindowWidth=WindowRect.right-WindowRect.left;
        var WindowHeight=WindowRect.bottom-WindowRect.top;

        HWND Hwnd;
        fixed(char*ClassName= Win32Window.ClassName)
        fixed(char*Title= Options.Title){
            Hwnd= CreateWindowExW(
                0,
                ClassName,
                Title,
                Style,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                WindowWidth,
                WindowHeight,
                HWND.NULL,
                HMENU.NULL,
                GetModuleHandleForCurrentProcess(),
                (void*)GCHandle.ToIntPtr(SelfHandle));
        }

        if(Hwnd==HWND.NULL){
            SelfHandle.Free();
            var Error=Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"CreateWindowExW failed with Win32 error 0x{Error:X8}.");
        }

        this.Hwnd=Hwnd;
        RefreshClientSize();

        if(Options.Visible){
            _=ShowWindow(Hwnd,SW_SHOW);
            _=UpdateWindow(Hwnd);
        }
    }

    public nint Handle=>(nint)Hwnd.Value;

    public int ClientWidth { get;private set;}

    public int ClientHeight { get;private set;}

    public bool IsClosed { get;private set;}

    public bool IsMinimized { get;private set;}

    public void SetTitle(string Title){
        ObjectDisposedException.ThrowIf(Disposed,this);

        fixed(char*PTitle=Title){
            _=SetWindowTextW(Hwnd,PTitle);
        }
    }

    /// <summary>Processes all messages currently queued for this thread without blocking.</summary>
    public void PumpEvents(){
        ObjectDisposedException.ThrowIf(Disposed,this);

        MSG Msg;
        while(PeekMessageW(&Msg,HWND.NULL,0,0,PM_REMOVE)){
            _=TranslateMessage(&Msg);
            _=DispatchMessageW(&Msg);
        }
    }

    /// <summary>True while the given virtual-key code (see <c>VK_*</c>) is held down. Cleared automatically on focus loss.</summary>
    public bool IsKeyDown(int VirtualKeyCode){
        return VirtualKeyCode is>=0 and <256&&KeyDown[VirtualKeyCode];
    }

    public bool IsMouseCaptured=>MouseCaptured;

    /// <summary>
    /// Hides the cursor and clips it to this window (mouse-look style capture), or releases it.
    /// Implemented by re-centering the cursor every <see cref="ConsumeMouseDelta"/> call rather
    /// than raw input (<c>WM_INPUT</c>) - simpler and precise enough for an orbit camera, at the
    /// cost of being bounded by the window's client area.
    /// </summary>
    public void SetMouseCaptured(bool Captured){
        ObjectDisposedException.ThrowIf(Disposed,this);

        if(Captured==MouseCaptured){
            return;
        }

        MouseCaptured=Captured;

        if(Captured){
            _=ShowCursor(FALSE);
            ClipCursorToClientArea();
            RecenterCursor();
            SkipNextMouseDelta=true;
        } else {
            _=ShowCursor(TRUE);
            _=ClipCursor(null);
        }
    }

    /// <summary>
    /// Returns the cursor movement since the last call (0,0 when not captured), then re-centers
    /// the cursor so the next call measures only new movement. Call at most once per frame.
    /// </summary>
    /// <remarks>
    /// The very first call after (re)gaining capture - either from <see cref="SetMouseCaptured"/>
    /// or from <c>WM_SETFOCUS</c> re-clipping after a focus loss - always reports (0,0) instead of
    /// the raw delta. <c>ClipCursor</c> is silently released by the OS whenever this process loses
    /// the foreground, so the cursor can end up anywhere on the desktop while unclipped; without
    /// this guard, the next real cursor position read back against the stale pre-loss center would
    /// be reported as one huge, spurious look-delta (observed in practice: it can snap a first- or
    /// third-person camera's pitch straight to its clamp on Alt-Tab back into the window).
    /// </remarks>
    public(float DeltaX,float DeltaY)ConsumeMouseDelta(){
        ObjectDisposedException.ThrowIf(Disposed,this);

        if(!MouseCaptured){
            return(0f,0f);
        }

        POINT Current;
        if(!GetCursorPos(&Current)){
            return(0f,0f);
        }

        float DeltaX=Current.x-CapturedCenterScreenPoint.x;
        float DeltaY=Current.y-CapturedCenterScreenPoint.y;

        RecenterCursor();

        if(SkipNextMouseDelta){
            SkipNextMouseDelta=false;
            return(0f,0f);
        }

        return(DeltaX,DeltaY);
    }

    private void RecenterCursor(){
        RECT ClientRect;
        if(!GetClientRect(Hwnd,&ClientRect)){
            return;
        }

        POINT Center=new(){ x=(ClientRect.left+ClientRect.right)/2,y=(ClientRect.top+ClientRect.bottom)/2 };
        _=ClientToScreen(Hwnd,&Center);
        _=SetCursorPos(Center.x,Center.y);
        CapturedCenterScreenPoint=Center;
    }

    private void ClipCursorToClientArea(){
        RECT ClientRect;
        if(!GetClientRect(Hwnd,&ClientRect)){
            return;
        }

        POINT TopLeft=new(){ x=ClientRect.left,y=ClientRect.top };
        POINT BottomRight=new(){ x=ClientRect.right,y=ClientRect.bottom };
        _=ClientToScreen(Hwnd,&TopLeft);
        _=ClientToScreen(Hwnd,&BottomRight);

        RECT ScreenRect=new(){ left=TopLeft.x,top=TopLeft.y,right=BottomRight.x,bottom=BottomRight.y };
        _=ClipCursor(&ScreenRect);
    }

    private void RefreshClientSize(){
        RECT ClientRect;
        if(GetClientRect(Hwnd,&ClientRect)){
            ClientWidth=ClientRect.right-ClientRect.left;
            ClientHeight=ClientRect.bottom-ClientRect.top;
        }
    }

    private LRESULT HandleMessage(HWND Hwnd,uint Msg,WPARAM WParam,LPARAM LParam){
        switch(Msg){
            case WM_CLOSE:
                _=DestroyWindow(Hwnd);
                return 0;

            case WM_DESTROY:
                Destroyed=true;
                IsClosed=true;
                return 0;

            case WM_SIZE:{
                    IsMinimized=(uint)WParam.Value.ToUInt64()==SIZE_MINIMIZED;

                    var Lp=LParam.Value.ToInt64();
                    var Width=(int)(Lp&0xFFFF);
                    var Height=(int)((Lp >> 16)&0xFFFF);

                    ClientWidth=Width;
                    ClientHeight=Height;

                    if(!IsMinimized){
                        Resized?.Invoke(Width,Height);
                    }

                    return 0;
                }

            case WM_NCDESTROY:{
                    LRESULT Result=DefWindowProcW(Hwnd,Msg,WParam,LParam);
                    if(SelfHandle.IsAllocated){
                        SelfHandle.Free();
                    }
                    return Result;
                }

            case WM_KEYDOWN:
            case WM_SYSKEYDOWN:{
                    var VirtualKeyCode=(int)WParam.Value.ToUInt64();
                    if(VirtualKeyCode is>=0 and <256){
                        KeyDown[VirtualKeyCode]=true;
                    }
                    // SYSKEY must still reach DefWindowProcW (Alt+F4, the system menu, etc.); plain
                    // WM_KEYDOWN is fully consumed since this is a game window with no default text
                    // input/accelerator behavior to preserve.
                    return Msg==WM_SYSKEYDOWN?DefWindowProcW(Hwnd,Msg,WParam,LParam):0;
                }

            case WM_KEYUP:
            case WM_SYSKEYUP:{
                    var VirtualKeyCode=(int)WParam.Value.ToUInt64();
                    if(VirtualKeyCode is>=0 and <256){
                        KeyDown[VirtualKeyCode]=false;
                    }
                    return Msg==WM_SYSKEYUP?DefWindowProcW(Hwnd,Msg,WParam,LParam):0;
                }

            case WM_KILLFOCUS:
                // Keys can't be reliably tracked once another window owns input focus - clear all
                // state so a key held down during an Alt-Tab doesn't appear "stuck" on return.
                Array.Clear(KeyDown);
                if(MouseCaptured){
                    _=ShowCursor(TRUE);
                    _=ClipCursor(null);
                }
                return DefWindowProcW(Hwnd,Msg,WParam,LParam);

            case WM_SETFOCUS:
                if(MouseCaptured){
                    _=ShowCursor(FALSE);
                    ClipCursorToClientArea();
                    RecenterCursor();
                    SkipNextMouseDelta=true;
                }
                return DefWindowProcW(Hwnd,Msg,WParam,LParam);

            default:
                return DefWindowProcW(Hwnd,Msg,WParam,LParam);
        }
    }

    public void Dispose(){
        if(Disposed){
            return;
        }

        Disposed=true;

        if(MouseCaptured){
            _=ShowCursor(TRUE);
            _=ClipCursor(null);
            MouseCaptured=false;
        }

        if(!Destroyed&&Hwnd!=HWND.NULL){
            _=DestroyWindow(Hwnd);
        }

        Hwnd=HWND.NULL;
    }

    private static void EnsureDpiAwareness(){
        const int PerMonitorAwareV2=-4;
        _=SetProcessDpiAwarenessContext((DPI_AWARENESS_CONTEXT)(nint)PerMonitorAwareV2);
    }

    private static void EnsureClassRegistered(){
        if(ClassRegistered){
            return;
        }

        lock(RegistrationLock){
            if(ClassRegistered){
                return;
            }

            fixed(char*ClassName= Win32Window.ClassName){
                WNDCLASSEXW Wc =new(){
                    cbSize=(uint)sizeof(WNDCLASSEXW),
                    style=0,
                    lpfnWndProc=&WndProc,
                    hInstance= GetModuleHandleForCurrentProcess(),
                    hCursor= LoadCursorW(HINSTANCE.NULL, IDC_ARROW),
                    lpszClassName= ClassName,
                };

                if(RegisterClassExW(&Wc) ==0){
                    var Error= Marshal.GetLastWin32Error();
                    throw new InvalidOperationException($"RegisterClassExW failed with Win32 error 0x{Error:X8}.");
                }
            }

            ClassRegistered=true;
        }
    }

    private static HINSTANCE GetModuleHandleForCurrentProcess()=>(HINSTANCE)(void*)GetModuleHandleW(null);

    [UnmanagedCallersOnly]
    private static LRESULT WndProc(HWND Hwnd,uint Msg,WPARAM WParam,LPARAM LParam){
        if(Msg==WM_NCCREATE){
            CREATESTRUCTW*CreateStruct=(CREATESTRUCTW*)LParam.Value;
            var SelfHandle=(nint)CreateStruct->lpCreateParams;
            _=SetWindowLongPtrW(Hwnd,GWLP_USERDATA,SelfHandle);
            return DefWindowProcW(Hwnd,Msg,WParam,LParam);
        }

        var UserData=GetWindowLongPtrW(Hwnd,GWLP_USERDATA);
        if(UserData!=0){
            GCHandle Handle=GCHandle.FromIntPtr(UserData);
            if(Handle.Target is Win32Window Window){
                try {
                    return Window.HandleMessage(Hwnd,Msg,WParam,LParam);
                } catch {
                    return 0;
                }
            }
        }

        return DefWindowProcW(Hwnd,Msg,WParam,LParam);
    }
}
