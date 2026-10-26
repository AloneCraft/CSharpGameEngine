using System.Runtime.InteropServices;
using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Platform.Windows;
namespace CSharpGameEngine.Platform.Windows.Tests;
/// <summary>
/// ゲームウィンドウはテキストを扱わず、入力メソッドも持たない。IME（ひらがな入力モードの日本語IMEなど）が有効だと、IMEが処理するキーは
/// 元のキーではなくVK_PROCESSKEYとしてウィンドウへ届き、W／A／S／Dでもキャラクターが動かなかった。
/// </summary>
public sealed partial class Win32WindowImeTests {
    [LibraryImport("imm32.dll")]
    private static partial nint ImmGetContext(nint Hwnd);
    [LibraryImport("imm32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static partial bool ImmReleaseContext(nint Hwnd,nint Context);

    [WindowsOnlyFact]
    public void GameWindow_HasNoInputMethodContext(){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine IME Test",Width=64,Height=64,Visible=false });

        var Context=ImmGetContext(Window.Handle);
        if(Context!=0){ _=ImmReleaseContext(Window.Handle,Context); }

        Assert.Equal((nint)0,Context);
    }
}
