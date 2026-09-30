using System.Runtime.InteropServices;
using CSharpGameEngine.Core;
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
    private const uint RidInput=0x10000003;
    private const uint RidevDevNotify=0x00002000;
    private static readonly char*IDC_ARROW=(char*)0x7F00;

    private static readonly object RegistrationLock=new();
    private static bool ClassRegistered;

    private GCHandle SelfHandle;
    private HWND Hwnd;
    private bool Destroyed;
    private bool Disposed;

    private readonly Win32InputEventAdapter InputAdapter;
    private bool MouseCaptured;
    private bool CaptureApplied;
    private readonly bool IgnoreDeviceInput;

    public event Action<int,int>?Resized;

    public Win32Window(Win32WindowOptions Options){
        ArgumentNullException.ThrowIfNull(Options);
        this.InputAdapter=new Win32InputEventAdapter(Options.WindowId.IsValid?Options.WindowId:WindowId.Primary);
        this.IgnoreDeviceInput=Options.IgnoreDeviceInput;

        EnsureDpiAwareness();
        EnsureClassRegistered();

        this.SelfHandle=GCHandle.Alloc(this,GCHandleType.Normal);

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
                (void*)GCHandle.ToIntPtr(this.SelfHandle));
        }

        if(Hwnd==HWND.NULL){
            this.SelfHandle.Free();
            var Error=Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"CreateWindowExW failed with Win32 error 0x{Error:X8}.");
        }

        this.Hwnd=Hwnd;
        this.RegisterRawInput();
        this.RefreshClientSize();

        if(Options.Visible){
            _=ShowWindow(Hwnd,SW_SHOW);
            _=UpdateWindow(Hwnd);
        }
    }

    public nint Handle=>(nint)this.Hwnd.Value;

    public int ClientWidth { get;private set;}

    public int ClientHeight { get;private set;}

    public bool IsClosed { get;private set;}

    public bool IsMinimized { get;private set;}

    public WindowId WindowId=>this.InputAdapter.WindowId;

    public long InputTimestampFrequency=>Win32InputEventAdapter.TimestampFrequency;

    /// <summary>Polls the gamepad, then returns and clears every input event recorded since the last call.</summary>
    public IReadOnlyList<InputEvent>DrainInputEvents(){
        if(!this.IgnoreDeviceInput){
            this.InputAdapter.RecordGamepad(XInputGamepad.Poll());
        }
        return this.InputAdapter.Drain();
    }

    public void SetTitle(string Title){
        ObjectDisposedException.ThrowIf(this.Disposed,this);

        fixed(char*PTitle=Title){
            _=SetWindowTextW(this.Hwnd,PTitle);
        }
    }

    /// <summary>Processes all messages currently queued for this thread without blocking.</summary>
    public void PumpEvents(){
        ObjectDisposedException.ThrowIf(this.Disposed,this);

        MSG Msg;
        while(PeekMessageW(&Msg,HWND.NULL,0,0,PM_REMOVE)){
            _=TranslateMessage(&Msg);
            _=DispatchMessageW(&Msg);
        }
    }

    public bool IsMouseCaptured=> this.MouseCaptured;

    /// <summary>Records a keyboard key as if this window had received it (scripted automation input).</summary>
    public void InjectKey(string Code,bool Pressed){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        this.InputAdapter.RecordKeyCode(Code,Pressed);
    }

    /// <summary>
    /// Hides the cursor and clips it to this window (mouse-look style capture), or releases it.
    /// Relative motion is delivered through timestamped input events while capture owns the cursor.
    /// </summary>
    public void SetMouseCaptured(bool Captured){
        ObjectDisposedException.ThrowIf(this.Disposed,this);

        if(Captured== this.MouseCaptured){
            return;
        }

        this.MouseCaptured=Captured;

        if(Captured){
            // The cursor clip is system-wide: an unfocused window applies it on WM_SETFOCUS instead.
            this.ApplyCapture(GetFocus()==this.Hwnd);
        } else {
            this.ApplyCapture(false);
            this.InputAdapter.RecordCaptureReleased();
        }
    }

    /// <summary>Hides and clips the cursor, or restores it, keeping ShowCursor calls balanced.</summary>
    private void ApplyCapture(bool Apply){
        if(Apply==this.CaptureApplied){
            return;
        }
        this.CaptureApplied=Apply;
        if(Apply){
            _=ShowCursor(FALSE);
            this.ClipCursorToClientArea();
        } else {
            _=ShowCursor(TRUE);
            _=ClipCursor(null);
        }
    }

    private void ClipCursorToClientArea(){
        RECT ClientRect;
        if(!GetClientRect(this.Hwnd,&ClientRect)){
            return;
        }

        POINT TopLeft=new(){ x=ClientRect.left,y=ClientRect.top };
        POINT BottomRight=new(){ x=ClientRect.right,y=ClientRect.bottom };
        _=ClientToScreen(this.Hwnd,&TopLeft);
        _=ClientToScreen(this.Hwnd,&BottomRight);

        RECT ScreenRect=new(){ left=TopLeft.x,top=TopLeft.y,right=BottomRight.x,bottom=BottomRight.y };
        _=ClipCursor(&ScreenRect);
    }

    private void RefreshClientSize(){
        RECT ClientRect;
        if(GetClientRect(this.Hwnd,&ClientRect)){
            this.ClientWidth=ClientRect.right-ClientRect.left;
            this.ClientHeight=ClientRect.bottom-ClientRect.top;
        }
    }

    private void RegisterRawInput(){
        NativeRawInputDevice*Devices=stackalloc NativeRawInputDevice[2];
        Devices[0]=new NativeRawInputDevice { UsagePage=1,Usage=2,Flags=RidevDevNotify,Target=(nint)this.Hwnd.Value };
        Devices[1]=new NativeRawInputDevice { UsagePage=1,Usage=6,Flags=RidevDevNotify,Target=(nint)this.Hwnd.Value };
        if(!NativeRegisterRawInputDevices(Devices,2,(uint)sizeof(NativeRawInputDevice))){
            var Error=Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"RegisterRawInputDevices failed with Win32 error 0x{Error:X8}.");
        }
    }

    private void HandleRawInput(nint RawInputHandle){
        if(this.IgnoreDeviceInput){
            return;
        }
        uint Size=0;
        if(NativeGetRawInputData(RawInputHandle,RidInput,null,&Size,(uint)sizeof(NativeRawInputHeader))==uint.MaxValue||Size<(uint)sizeof(NativeRawInputHeader)){ return; }
        var Buffer=stackalloc byte[(int)Size];
        if(NativeGetRawInputData(RawInputHandle,RidInput,Buffer,&Size,(uint)sizeof(NativeRawInputHeader))==uint.MaxValue){ return; }
        var Header=(NativeRawInputHeader*)Buffer;
        if(Header->Type!=0||Size<(uint)(sizeof(NativeRawInputHeader)+sizeof(NativeRawMouse))){ return; }
        var Mouse=(NativeRawMouse*)(Buffer+sizeof(NativeRawInputHeader));
        this.InputAdapter.RecordMouseDelta(Mouse->LastX,Mouse->LastY);
        var Flags=Mouse->ButtonFlags;
        this.RecordRawMouseButton(Flags,0x0001,0x0002,"Left");
        this.RecordRawMouseButton(Flags,0x0004,0x0008,"Right");
        this.RecordRawMouseButton(Flags,0x0010,0x0020,"Middle");
        this.RecordRawMouseButton(Flags,0x0040,0x0080,"X1");
        this.RecordRawMouseButton(Flags,0x0100,0x0200,"X2");
        if((Flags&0x0400)!=0){ this.InputAdapter.RecordMouseWheel((short)Mouse->ButtonData/120f); }
    }

    private void RecordRawMouseButton(ushort Flags,ushort Down,ushort Up,string Code){
        if((Flags&Down)!=0){ this.InputAdapter.RecordMouseButton(Code,true); }
        if((Flags&Up)!=0){ this.InputAdapter.RecordMouseButton(Code,false); }
    }

    private LRESULT HandleMessage(HWND Hwnd,uint Msg,WPARAM WParam,LPARAM LParam){
        switch(Msg){
            case WM_CLOSE:
                _=DestroyWindow(Hwnd);
                return 0;

            case WM_DESTROY:
                this.InputAdapter.RecordWindowDestroyed();
                this.Destroyed=true;
                this.IsClosed=true;
                return 0;

            case WM_SIZE:{
                this.IsMinimized=(uint)WParam.Value.ToUInt64()==SIZE_MINIMIZED;

                    var Lp=LParam.Value.ToInt64();
                    var Width=(int)(Lp&0xFFFF);
                    var Height=(int)((Lp >> 16)&0xFFFF);

                    this.ClientWidth=Width;
                    this.ClientHeight=Height;

                    if(!this.IsMinimized){
                        this.Resized?.Invoke(Width,Height);
                    }

                    return 0;
                }

            case WM_NCDESTROY:{
                    var Result=DefWindowProcW(Hwnd,Msg,WParam,LParam);
                    if(this.SelfHandle.IsAllocated){
                        this.SelfHandle.Free();
                    }
                    return Result;
                }

            case WM_KEYDOWN:
            case WM_SYSKEYDOWN:{
                    var VirtualKeyCode=(int)WParam.Value.ToUInt64();
                    if(!this.IgnoreDeviceInput){
                        this.InputAdapter.RecordKey(VirtualKeyCode,true);
                    }
                    // SYSKEY must still reach DefWindowProcW (Alt+F4, the system menu, etc.); plain
                    // WM_KEYDOWN is fully consumed since this is a game window with no default text
                    // input/accelerator behavior to preserve.
                    return Msg==WM_SYSKEYDOWN?DefWindowProcW(Hwnd,Msg,WParam,LParam):0;
                }

            case WM_KEYUP:
            case WM_SYSKEYUP:{
                    var VirtualKeyCode=(int)WParam.Value.ToUInt64();
                    if(!this.IgnoreDeviceInput){
                        this.InputAdapter.RecordKey(VirtualKeyCode,false);
                    }
                    return Msg==WM_SYSKEYUP?DefWindowProcW(Hwnd,Msg,WParam,LParam):0;
                }

            case WM_KILLFOCUS:
                // Keys can't be reliably tracked once another window owns input focus - clear all
                // state so a key held down during an Alt-Tab doesn't appear "stuck" on return.
                // Injected keys do not belong to a physical device, so focus changes leave them held.
                if(!this.IgnoreDeviceInput){
                    this.InputAdapter.RecordFocusLost();
                }
                this.ApplyCapture(false);
                return DefWindowProcW(Hwnd,Msg,WParam,LParam);

            case WM_SETFOCUS:
                if(this.MouseCaptured){
                    this.ApplyCapture(true);
                }
                return DefWindowProcW(Hwnd,Msg,WParam,LParam);

            case 0x00FF:
                this.HandleRawInput(LParam.Value);
                return DefWindowProcW(Hwnd,Msg,WParam,LParam);

            case 0x00FE:
                if(WParam.Value.ToUInt64()==2){ this.InputAdapter.RecordDeviceRemoved(); }
                return DefWindowProcW(Hwnd,Msg,WParam,LParam);

            default:
                return DefWindowProcW(Hwnd,Msg,WParam,LParam);
        }
    }

    public void Dispose(){
        if(this.Disposed){
            return;
        }

        this.Disposed=true;

        this.ApplyCapture(false);
        this.MouseCaptured=false;

        if(!this.Destroyed&& this.Hwnd!=HWND.NULL){
            _=DestroyWindow(this.Hwnd);
        }

        this.Hwnd=HWND.NULL;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRawInputDevice { public ushort UsagePage;public ushort Usage;public uint Flags;public nint Target; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRawInputHeader { public uint Type;public uint Size;public nint Device;public nint WParam; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRawMouse { public ushort Flags;public ushort ButtonFlags;public ushort ButtonData;public uint RawButtons;public int LastX;public int LastY;public uint ExtraInformation; }
    [DllImport("user32.dll",EntryPoint="RegisterRawInputDevices",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeRegisterRawInputDevices(NativeRawInputDevice*Devices,uint DeviceCount,uint StructureSize);
    [DllImport("user32.dll",EntryPoint="GetRawInputData",SetLastError=true)]
    private static extern uint NativeGetRawInputData(nint RawInput,uint Command,void*Data,uint*Size,uint HeaderSize);

    [UnmanagedCallersOnly]
    private static LRESULT WndProc(HWND Hwnd,uint Msg,WPARAM WParam,LPARAM LParam){
        if(Msg==WM_NCCREATE){
            var CreateStruct=(CREATESTRUCTW*)LParam.Value;
            var SelfHandle=(nint)CreateStruct->lpCreateParams;
            _=SetWindowLongPtrW(Hwnd,GWLP_USERDATA,SelfHandle);
            return DefWindowProcW(Hwnd,Msg,WParam,LParam);
        }

        var UserData=GetWindowLongPtrW(Hwnd,GWLP_USERDATA);
        if(UserData!=0){
            var Handle=GCHandle.FromIntPtr(UserData);
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
