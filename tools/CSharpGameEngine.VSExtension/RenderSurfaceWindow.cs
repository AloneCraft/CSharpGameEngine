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
    private IntPtr LastExternalFocusHwnd;
    private bool Disposed;

    public event Action<int,int>?Resized;

    /// <summary>マウスのボタン・移動・キャプチャ喪失(M0020 Phase 1、ADR-0041 決定事項2)。座標は物理ピクセル。</summary>
    public event Action<PointerEvent>?PointerInput;

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

    /// <summary>ドラッグ操作(Look/Fly/Pan/Orbit)開始時にSceneInputStateMachineの指示で呼ぶ。</summary>
    public void SetPointerCapture(){
        if(!Disposed&&Hwnd!=IntPtr.Zero){
            _=SetCapture(Hwnd);
        }
    }

    /// <summary>自分がキャプチャを保持している場合のみ解放する(既に奪われている場合は何もしない)。</summary>
    public void ReleasePointerCapture(){
        if(!Disposed&&Hwnd!=IntPtr.Zero&&GetCapture()==Hwnd){
            _=ReleaseCapture();
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
            // focus back to the last valid external owner (or the parent as a fallback) before
            // tearing down the native window.
            IntPtr SurfaceHandle=Hwnd;
            IntPtr CurrentFocus=GetFocus();
            bool PreviousIsWindow=LastExternalFocusHwnd!=IntPtr.Zero&&IsWindow(LastExternalFocusHwnd);
            bool ParentIsWindow=ParentHwnd!=IntPtr.Zero&&IsWindow(ParentHwnd);
            IntPtr ReturnTarget=RenderSurfaceFocusPolicy.SelectReturnTarget(
                LastExternalFocusHwnd,
                PreviousIsWindow,
                SurfaceHandle,
                ParentHwnd,
                ParentIsWindow);
            if(RenderSurfaceFocusPolicy.ShouldRestoreFocus(
                CurrentFocus,SurfaceHandle,ReturnTarget)){
                SetFocus(ReturnTarget);
            }

            _=DestroyWindow(SurfaceHandle);
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

        // M0020 Phase 1(ADR-0041 決定事項2)。MA_NOACTIVATEでもマウスメッセージ自体は
        // ヒットテストで届くため、ここでSceneInputStateMachine向けに翻訳して転送する。
        // GetKeyStateで修飾キーを読むのは、マウスwParamのMKフラグにAltが存在しないため
        // (Ctrl/Shiftも経路を1本に揃える目的で同じくGetKeyStateへ統一する)。
        RawInputModifiers Modifiers=new(
            Shift:(GetKeyState(VK_SHIFT)&0x8000)!=0,
            Ctrl:(GetKeyState(VK_CONTROL)&0x8000)!=0,
            Alt:(GetKeyState(VK_MENU)&0x8000)!=0);

        if(RenderSurfaceMouseMessageTranslator.Translate(Msg,LParam,Modifiers)is{ }PointerEvent){
            RenderSurfaceWindow?PointerInstance;
            lock(Instances){
                Instances.TryGetValue(HWnd,out PointerInstance);
            }

            PointerInstance?.PointerInput?.Invoke(PointerEvent);
            return IntPtr.Zero;
        }

        RenderSurfaceFocusAction FocusAction=RenderSurfaceFocusPolicy.GetAction(Msg);

        if(FocusAction==RenderSurfaceFocusAction.MouseWithoutActivation){
            // MA_NOACTIVATEはマウスイベント自体を捨てず、Surfaceをアクティブ化しない。
            // 3Dビューの将来のPicking入力は維持しつつ、VSのキーボードフォーカスは奪わない。
            return new IntPtr(MA_NOACTIVATE);
        }

        // これは a162478 以来のネイティブD3D12子HWNDにおけるWin32/WPFフォーカス不整合の防御です。
        // Packageの起動方法や初期ToolWindowの非アクティブ表示とは別層で維持します。
        if(FocusAction==RenderSurfaceFocusAction.ReturnFocus){
            // Surfaceは純粋な描画先でキーボード入力を所有しない。ここで実フォーカスを保持すると
            // WPFのHwndHost/HwndSourceとWin32のフォーカスがずれ、VS全体でTab、Backspace、
            // Deleteなどがコードエディタへ届かなくなる（通常の文字入力だけ動く場合がある）。
            // a162478では親HWNDへ戻していたが、現在の親はScene Designer ToolWindowである。
            // WM_SETFOCUSのwParam（直前の所有者）を優先しないと、次のTabがコード編集ではなく
            // VSシェルのフォーカス巡回としてナビゲーションバーへ移動してしまう。
            RenderSurfaceWindow?FocusedInstance;
            lock(Instances){
                Instances.TryGetValue(HWnd,out FocusedInstance);
            }

            if(FocusedInstance is not null){
                bool PreviousIsWindow=WParam!=IntPtr.Zero&&IsWindow(WParam);
                bool ParentIsWindow=FocusedInstance.ParentHwnd!=IntPtr.Zero
                    &&IsWindow(FocusedInstance.ParentHwnd);
                IntPtr ReturnTarget=RenderSurfaceFocusPolicy.SelectReturnTarget(
                    WParam,
                    PreviousIsWindow,
                    HWnd,
                    FocusedInstance.ParentHwnd,
                    ParentIsWindow);
                if(ReturnTarget!=IntPtr.Zero){
                    FocusedInstance.LastExternalFocusHwnd=ReturnTarget;
                    SetFocus(ReturnTarget);
                }
            }

            return IntPtr.Zero;
        }

        return DefWindowProc(HWnd,Msg,WParam,LParam);
    }
}
