using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// 所有者が WPF keyboard focus を持つ間だけ、Visual Studio のコマンドルーティングより先に Escape を捕捉します。ネイティブ hook は HwndHost viewport 経路を扱い、PreviewKeyDown は通常の WPF コントロール用の fallback です。同じキーの繰り返し key-down message は消費しますが、意味的な操作は繰り返しません。
/// </summary>
internal sealed class SceneEditorEscapeRouter:IDisposable {
    private readonly FrameworkElement Owner;
    private readonly Action EscapeRequested;
    private HwndSource?Source;
    private bool EscapePressed;
    private bool Disposed;
    internal SceneEditorEscapeRouter(FrameworkElement Owner,Action EscapeRequested){
        this.Owner=Owner??throw new ArgumentNullException(nameof(Owner));
        this.EscapeRequested=EscapeRequested??throw new ArgumentNullException(nameof(EscapeRequested));
        this.Owner.Loaded+=this.OnLoaded;
        this.Owner.Unloaded+=this.OnUnloaded;
        this.Owner.LostKeyboardFocus+=this.OnLostKeyboardFocus;
        InputManager.Current.PreProcessInput+=this.OnPreProcessInput;
        this.Owner.AddHandler(Keyboard.PreviewKeyDownEvent,new KeyEventHandler(this.OnPreviewKeyDown),true);
        this.Owner.AddHandler(Keyboard.PreviewKeyUpEvent,new KeyEventHandler(this.OnPreviewKeyUp),true);
    }
    private void OnLoaded(object Sender,RoutedEventArgs Args)=>this.AttachSource();
    private void OnUnloaded(object Sender,RoutedEventArgs Args){
        this.EscapePressed=false;
        this.DetachSource();
    }
    private void OnLostKeyboardFocus(object Sender,KeyboardFocusChangedEventArgs Args)=>this.EscapePressed=false;
    private void OnPreProcessInput(object Sender,PreProcessInputEventArgs Args){
        if(Args.StagingItem.Input is not KeyEventArgs KeyArgs){
            return;
        }
        if(KeyArgs.RoutedEvent==Keyboard.PreviewKeyDownEvent
            &&SceneEditorEscapeRoutingPolicy.ShouldConsume(
                this.Owner.IsKeyboardFocusWithin,KeyArgs.Key==Key.Escape)){
            this.HandleInitialEscape();
            KeyArgs.Handled=true;
        } else if(KeyArgs.RoutedEvent==Keyboard.PreviewKeyUpEvent
            &&SceneEditorEscapeRoutingPolicy.ShouldConsume(
                this.Owner.IsKeyboardFocusWithin,KeyArgs.Key==Key.Escape)){
            this.EscapePressed=false;
            KeyArgs.Handled=true;
        }
    }
    private void AttachSource(){
        if(this.Disposed||this.Source is not null){
            return;
        }
        this.Source=PresentationSource.FromVisual(this.Owner)as HwndSource;
        this.Source?.AddHook(this.WndProc);
    }
    private void DetachSource(){
        if(this.Source is null){
            return;
        }
        this.Source.RemoveHook(this.WndProc);
        this.Source=null;
    }
    private void OnPreviewKeyDown(object Sender,KeyEventArgs Args){
        if(!SceneEditorEscapeRoutingPolicy.ShouldConsume(this.Owner.IsKeyboardFocusWithin,Args.Key==Key.Escape)){
            return;
        }
        this.HandleInitialEscape();
        Args.Handled=true;
    }
    private void OnPreviewKeyUp(object Sender,KeyEventArgs Args){
        if(!SceneEditorEscapeRoutingPolicy.ShouldConsume(this.Owner.IsKeyboardFocusWithin,Args.Key==Key.Escape)){
            return;
        }
        this.EscapePressed=false;
        Args.Handled=true;
    }
    private IntPtr WndProc(IntPtr Hwnd,int Msg,IntPtr WParam,IntPtr LParam,ref bool Handled){
        if(!SceneEditorEscapeRoutingPolicy.ShouldConsume(this.Owner.IsKeyboardFocusWithin,WParam.ToInt64()==NativeMethods.VK_ESCAPE)){
            return IntPtr.Zero;
        }
        if(Msg==NativeMethods.WM_KEYDOWN){
            this.HandleInitialEscape();
            Handled=true;
        } else if(Msg==NativeMethods.WM_KEYUP){
            this.EscapePressed=false;
            Handled=true;
        }
        return IntPtr.Zero;
    }
    private void HandleInitialEscape(){
        if(!SceneEditorEscapeRoutingPolicy.ShouldInvoke(this.EscapePressed)){
            return;
        }
        this.EscapePressed=true;
        this.EscapeRequested();
    }
    public void Dispose(){
        if(this.Disposed){
            return;
        }
        this.Disposed=true;
        this.EscapePressed=false;
        this.DetachSource();
        this.Owner.Loaded-=this.OnLoaded;
        this.Owner.Unloaded-=this.OnUnloaded;
        this.Owner.LostKeyboardFocus-=this.OnLostKeyboardFocus;
        InputManager.Current.PreProcessInput-=this.OnPreProcessInput;
        this.Owner.RemoveHandler(Keyboard.PreviewKeyDownEvent,new KeyEventHandler(this.OnPreviewKeyDown));
        this.Owner.RemoveHandler(Keyboard.PreviewKeyUpEvent,new KeyEventHandler(this.OnPreviewKeyUp));
    }
}
