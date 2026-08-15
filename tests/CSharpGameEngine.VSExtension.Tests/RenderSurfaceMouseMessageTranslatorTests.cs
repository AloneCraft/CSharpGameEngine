using System;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// <see cref="RenderSurfaceMouseMessageTranslator"/>のWin32メッセージ→<see cref="PointerEvent"/>変換を
/// 実VS・実HWND無しに固定する。
/// </summary>
public sealed class RenderSurfaceMouseMessageTranslatorTests {
    private static readonly RawInputModifiers NoModifiers=new(Shift:false,Ctrl:false,Alt:false);

    [Fact]
    public void Translate_LButtonDown_ProducesLeftDownWithDecodedCoordinates(){
        IntPtr LParam=MakeLParam(120,80);

        PointerEvent?Result=RenderSurfaceMouseMessageTranslator.Translate(
            NativeMethods.WM_LBUTTONDOWN,LParam,NoModifiers);

        Assert.NotNull(Result);
        Assert.Equal(PointerEventKind.Down,Result!.Value.Kind);
        Assert.Equal(PointerButton.Left,Result.Value.Button);
        Assert.Equal(120,Result.Value.X);
        Assert.Equal(80,Result.Value.Y);
    }

    [Fact]
    public void Translate_RButtonUp_ProducesRightUp(){
        PointerEvent?Result=RenderSurfaceMouseMessageTranslator.Translate(
            NativeMethods.WM_RBUTTONUP,MakeLParam(1,2),NoModifiers);

        Assert.NotNull(Result);
        Assert.Equal(PointerEventKind.Up,Result!.Value.Kind);
        Assert.Equal(PointerButton.Right,Result.Value.Button);
    }

    [Fact]
    public void Translate_MButtonDown_ProducesMiddleDown(){
        PointerEvent?Result=RenderSurfaceMouseMessageTranslator.Translate(
            NativeMethods.WM_MBUTTONDOWN,MakeLParam(1,2),NoModifiers);

        Assert.NotNull(Result);
        Assert.Equal(PointerEventKind.Down,Result!.Value.Kind);
        Assert.Equal(PointerButton.Middle,Result.Value.Button);
    }

    [Fact]
    public void Translate_MouseMove_CarriesModifiers(){
        RawInputModifiers Modifiers=new(Shift:true,Ctrl:false,Alt:true);

        PointerEvent?Result=RenderSurfaceMouseMessageTranslator.Translate(
            NativeMethods.WM_MOUSEMOVE,MakeLParam(5,6),Modifiers);

        Assert.NotNull(Result);
        Assert.Equal(PointerEventKind.Move,Result!.Value.Kind);
        Assert.Equal(PointerButton.None,Result.Value.Button);
        Assert.True(Result.Value.Modifiers.Shift);
        Assert.False(Result.Value.Modifiers.Ctrl);
        Assert.True(Result.Value.Modifiers.Alt);
    }

    [Fact]
    public void Translate_CaptureChanged_ProducesCaptureLostEvent(){
        PointerEvent?Result=RenderSurfaceMouseMessageTranslator.Translate(
            NativeMethods.WM_CAPTURECHANGED,IntPtr.Zero,NoModifiers);

        Assert.NotNull(Result);
        Assert.Equal(PointerEventKind.CaptureLost,Result!.Value.Kind);
    }

    /// <summary>
    /// 自分がキャプチャを取り直しただけのWM_CAPTURECHANGEDは無視する。これをCaptureLostにすると
    /// SceneInputStateMachineがIdleへ戻り、「LMB押下中にRMBでナビゲーションへプリエンプト」が
    /// 不発になる(実機で観測。RenderSurfaceWindow.SetPointerCapture参照)。
    /// </summary>
    [Fact]
    public void Translate_CaptureChangedToSelf_IsIgnored(){
        IntPtr SelfHwnd=new(0x1234);

        PointerEvent?Result=RenderSurfaceMouseMessageTranslator.Translate(
            NativeMethods.WM_CAPTURECHANGED,SelfHwnd,NoModifiers,SelfHwnd);

        Assert.Null(Result);
    }

    [Fact]
    public void Translate_CaptureChangedToAnotherWindow_StillReportsCaptureLost(){
        PointerEvent?Result=RenderSurfaceMouseMessageTranslator.Translate(
            NativeMethods.WM_CAPTURECHANGED,new IntPtr(0x9999),NoModifiers,new IntPtr(0x1234));

        Assert.NotNull(Result);
        Assert.Equal(PointerEventKind.CaptureLost,Result!.Value.Kind);
    }

    /// <summary>
    /// LParamがゼロは「どのウィンドウもキャプチャを取得しない」= 完全な解放なので、自己ハンドルが
    /// 有効でもCaptureLostとして扱わなければならない(自己一致判定に巻き込まれてはいけない)。
    /// </summary>
    [Fact]
    public void Translate_CaptureFullyReleased_StillReportsCaptureLost(){
        PointerEvent?Result=RenderSurfaceMouseMessageTranslator.Translate(
            NativeMethods.WM_CAPTURECHANGED,IntPtr.Zero,NoModifiers,new IntPtr(0x1234));

        Assert.NotNull(Result);
        Assert.Equal(PointerEventKind.CaptureLost,Result!.Value.Kind);
    }

    [Fact]
    public void Translate_UnrelatedMessage_ReturnsNull(){
        PointerEvent?Result=RenderSurfaceMouseMessageTranslator.Translate(
            NativeMethods.WM_SETFOCUS,IntPtr.Zero,NoModifiers);

        Assert.Null(Result);
    }

    [Fact]
    public void DecodeXY_NegativeCoordinates_DecodeAsSignedNotUnsigned(){
        // WM_SIZEの幅・高さは非負なので無符号抽出で正しいが、マウス座標はキャプチャ中の
        // ウィンドウ外ドラッグで負になりうる。(short)キャストを省くと-5が65531として化ける。
        IntPtr LParam=MakeLParam(-5,-10);

        Assert.Equal(-5,RenderSurfaceMouseMessageTranslator.DecodeX(LParam));
        Assert.Equal(-10,RenderSurfaceMouseMessageTranslator.DecodeY(LParam));
    }

    [Fact]
    public void DecodeXY_LargePositiveCoordinates_RoundTrip(){
        IntPtr LParam=MakeLParam(1920,1080);

        Assert.Equal(1920,RenderSurfaceMouseMessageTranslator.DecodeX(LParam));
        Assert.Equal(1080,RenderSurfaceMouseMessageTranslator.DecodeY(LParam));
    }

    [Fact]
    public void DecodeWheelDelta_ForwardNotch_ReturnsPositiveWheelDelta()=>
        Assert.Equal(120,RenderSurfaceMouseMessageTranslator.DecodeWheelDelta(MakeWheelWParam(120)));

    [Fact]
    public void DecodeWheelDelta_BackwardNotch_ReturnsNegativeWheelDelta()=>
        Assert.Equal(-120,RenderSurfaceMouseMessageTranslator.DecodeWheelDelta(MakeWheelWParam(-120)));

    private static IntPtr MakeLParam(int X,int Y)=>
        new(unchecked(((short)Y<<16)|((short)X&0xFFFF)));

    private static IntPtr MakeWheelWParam(short Delta)=>new(unchecked(Delta<<16));
}
