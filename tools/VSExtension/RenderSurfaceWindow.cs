using System;
using System.Runtime.InteropServices;
using static CSharpGameEngine.VSExtension.NativeMethods;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// D3D12 swap-chain target 専用の軽量なネイティブ <c>WS_CHILD</c> ウィンドウ。<see cref="ViewerHwndHost"/> が <c>BuildWindowCore</c> から返す「スロット」です。一致するように swap chain をリサイズできるよう、<c>WM_SIZE</c> を転送します（ADR-0008）。
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

    /// <summary>マウスホイール(WHEEL_DELTA=120単位、符号付き)。実機検証でWPF側PreviewMouseWheelが
    /// 発火しないことが判明したため、他のマウスメッセージと同じくここで受ける(M0020 Phase 1訂正)。</summary>
    public event Action<int>?WheelInput;

    public RenderSurfaceWindow(IntPtr ParentHwnd,int Width,int Height,bool InitiallyVisible=true){
        EnsureClassRegistered();
        this.ParentHwnd=ParentHwnd;

        this.Hwnd=CreateWindowEx(
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

        if(this.Hwnd==IntPtr.Zero){
            var Error=Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"CreateWindowEx failed with Win32 error 0x{Error:X8}.");
        }

        lock(Instances){
            Instances[this.Hwnd]=this;
        }
    }

    public IntPtr Handle=> this.Hwnd;

    /// <summary>
    /// 未描画の <c>WS_EX_NOREDIRECTIONBITMAP</c> 子HWNDは、ドッキング先に以前表示されていた
    /// ToolWindowを背面から露出させることがある。RendererがPresent可能な間だけ表示する。
    /// </summary>
    public void SetVisible(bool Visible){
        if(!this.Disposed&& this.Hwnd!=IntPtr.Zero){
            _=ShowWindow(this.Hwnd,Visible?SW_SHOWNA:SW_HIDE);
        }
    }

    /// <summary>
    /// ドラッグ操作(Look/Fly/Pan/Orbit)開始時にSceneInputStateMachineの指示で呼ぶ。
    ///
    /// <para>
    /// <b>既に自分が保持している場合は再取得しない。</b>Win32のSetCaptureはキャプチャを失う側へ
    /// WM_CAPTURECHANGEDを送るため、自分がキャプチャ中に再度呼ぶとそれが自分自身へ届き、
    /// WndProcがCaptureLostへ翻訳してSceneInputStateMachineが「奪われた」と解釈してIdleへ戻って
    /// しまう。実機で「LMB押下中(PendingSelect)にRMBを押してもナビゲーションに切り替わらない」
    /// (仕様§12の優先順位によるプリエンプトが不発)として観測された。PendingSelectで既に
    /// キャプチャしている状態からFlying/Panningへ遷移する経路が該当する。
    /// </para>
    /// </summary>
    public void SetPointerCapture(){
        if(!this.Disposed&& this.Hwnd!=IntPtr.Zero&&GetCapture()!= this.Hwnd){
            _=SetCapture(this.Hwnd);
        }
    }

    /// <summary>自分がキャプチャを保持している場合のみ解放する(既に奪われている場合は何もしない)。</summary>
    public void ReleasePointerCapture(){
        if(!this.Disposed&& this.Hwnd!=IntPtr.Zero&&GetCapture()== this.Hwnd){
            _=ReleaseCapture();
        }
    }

    public void Dispose(){
        if(this.Disposed){
            return;
        }

        this.Disposed=true;

        lock(Instances){
            Instances.Remove(this.Hwnd);
        }

        if(this.Hwnd!=IntPtr.Zero){
            // このネイティブ子ウィンドウの破棄時に、実際の Win32 キーボードフォーカスがまだ残っていると、
            // Windows は存在しないハンドルにフォーカスを残し、WPF 自身のフォーカス追跡
            // （Keyboard.FocusedElement）も、その後の VS セッション中ずっと実際の OS のフォーカス状態から
            // ずれてしまいます。このペインを閉じた後も Tab/Backspace/Delete がテキストエディターに届かなくなり、
            // 異なる経路でルーティングされる通常の文字入力だけが動き続けます。
            // ネイティブウィンドウを破棄する前に、フォーカスを最後の有効な外部 owner（なければ親）へ
            // 戻してください。
            var SurfaceHandle= this.Hwnd;
            var CurrentFocus=GetFocus();
            var PreviousIsWindow= this.LastExternalFocusHwnd!=IntPtr.Zero&&IsWindow(this.LastExternalFocusHwnd);
            var ParentIsWindow= this.ParentHwnd!=IntPtr.Zero&&IsWindow(this.ParentHwnd);
            var ReturnTarget=RenderSurfaceFocusPolicy.SelectReturnTarget(this.LastExternalFocusHwnd,
                                                                         PreviousIsWindow,
                                                                         SurfaceHandle, this.ParentHwnd,
                                                                         ParentIsWindow);
            if(RenderSurfaceFocusPolicy.ShouldRestoreFocus(
                CurrentFocus,SurfaceHandle,ReturnTarget)){
                SetFocus(ReturnTarget);
            }

            _=DestroyWindow(SurfaceHandle);
            this.Hwnd=IntPtr.Zero;
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
                // HCursorが未設定(NULL)だとDefWindowProcはWM_SETCURSORで何もしない仕様のため、
                // 直前に別の場所(テキストのIビーム等)で表示されていたカーソル形状がこのSurface上
                // でも固着し続けていた。標準の矢印を明示することでDefWindowProcの既定処理が
                // 毎回正しくリセットする(ドラッグキャプチャ中もWM_SETCURSORは届き続けるため
                // 同様に効く)。
                HCursor=LoadCursor(IntPtr.Zero,IDC_ARROW),
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

        if(Msg==WM_MOUSEWHEEL){
            RenderSurfaceWindow?WheelInstance;
            lock(Instances){
                Instances.TryGetValue(HWnd,out WheelInstance);
            }

            WheelInstance?.WheelInput?.Invoke(RenderSurfaceMouseMessageTranslator.DecodeWheelDelta(WParam));
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

        if(RenderSurfaceMouseMessageTranslator.Translate(Msg,LParam,Modifiers,HWnd)is{ }PointerEvent){
            RenderSurfaceWindow?PointerInstance;
            lock(Instances){
                Instances.TryGetValue(HWnd,out PointerInstance);
            }

            PointerInstance?.PointerInput?.Invoke(PointerEvent);
            return IntPtr.Zero;
        }

        var FocusAction=RenderSurfaceFocusPolicy.GetAction(Msg);

        if(FocusAction==RenderSurfaceFocusAction.MouseWithoutActivation){
            // MA_NOACTIVATEはマウスイベント自体を捨てず、Surfaceをアクティブ化しない。
            // 3Dビューの将来のPicking入力は維持しつつ、VSのキーボードフォーカスは奪わない。
            return new IntPtr(MA_NOACTIVATE);
        }

        // これは 840d5c1 以来のネイティブD3D12子HWNDにおけるWin32/WPFフォーカス不整合の防御です。
        // Packageの起動方法や初期ToolWindowの非アクティブ表示とは別層で維持します。
        if(FocusAction==RenderSurfaceFocusAction.ReturnFocus){
            // Surfaceは純粋な描画先でキーボード入力を所有しない。ここで実フォーカスを保持すると
            // WPFのHwndHost/HwndSourceとWin32のフォーカスがずれ、VS全体でTab、Backspace、
            // Deleteなどがコードエディタへ届かなくなる（通常の文字入力だけ動く場合がある）。
            // 840d5c1では親HWNDへ戻していたが、現在の親はScene Designer ToolWindowである。
            // WM_SETFOCUSのwParam（直前の所有者）を優先しないと、次のTabがコード編集ではなく
            // VSシェルのフォーカス巡回としてナビゲーションバーへ移動してしまう。
            RenderSurfaceWindow?FocusedInstance;
            lock(Instances){
                Instances.TryGetValue(HWnd,out FocusedInstance);
            }

            if(FocusedInstance is not null){
                var PreviousIsWindow=WParam!=IntPtr.Zero&&IsWindow(WParam);
                var ParentIsWindow=FocusedInstance.ParentHwnd!=IntPtr.Zero
                                 &&IsWindow(FocusedInstance.ParentHwnd);
                var ReturnTarget=RenderSurfaceFocusPolicy.SelectReturnTarget(
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
